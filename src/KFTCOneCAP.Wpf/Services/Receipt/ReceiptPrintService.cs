using System;
using System.Threading;
using System.Threading.Tasks;
using KFTCOneCAP.Wpf.Protocol.Printer;
using KFTCOneCAP.Wpf.Services.Diagnostics;
using KFTCOneCAP.Wpf.Services.Printer;
using KFTCOneCAP.Wpf.Services.Settings;

namespace KFTCOneCAP.Wpf.Services.Receipt;

/// <summary>
/// 설정 확인 → 조립 → 인코딩 → 출력 → 실패 로그(S12)까지 묶는 진입점(docs/receipt_print/PRD.md §4.1,
/// §5, development_plan.md Phase 34 P34-2). <b>공개 메서드는 예외를 던지지 않는다</b> — 결제창(호출자)이
/// 이 호출 때문에 죽으면 안 된다(PRD §0 "출력 성패는 거래 결과에 영향을 주지 않는다").
///
/// 설정 로더(<see cref="ShopSettingsService.Load"/>)와 프린터(<see cref="IReceiptPrinter"/>)는 생성자
/// 주입이다 — 기본 생성자는 실제 구현을 쓰고, self 검증은 <see cref="FakeReceiptPrinter"/>와 가짜
/// 로더로 교체한다. <b>설정은 필드에 보관하지 않고 매 호출마다 다시 읽는다</b>(캐시 금지, PRD §7.2·
/// 운영 PRD §2.6) — 자동 출력 도중에도 가맹점 설정 화면에서 값을 바꾸면 다음 호출부터 바로 반영돼야
/// 한다.
/// </summary>
public sealed class ReceiptPrintService
{
    private readonly Func<ShopSettings> _loadSettings;
    private readonly IReceiptPrinter _printer;

    public ReceiptPrintService() : this(new ShopSettingsService().Load, new SerialReceiptPrinter())
    {
    }

    internal ReceiptPrintService(Func<ShopSettings> loadSettings, IReceiptPrinter printer)
    {
        _loadSettings = loadSettings;
        _printer = printer;
    }

    public async Task<ReceiptPrintOutcome> PrintAsync(NationalTaxReceipt receipt, CancellationToken cancellationToken)
    {
        const string label = "전표 출력";
        try
        {
            // PRD §7.2 — 설정값은 매 출력 직전에 읽는다(캐시 금지). 이 메서드 밖(필드)에 저장하지 않는다.
            ShopSettings settings = _loadSettings();

            if (!settings.SlipPrintEnabled)
            {
                FileLogger.Info(LogCategory.Printer, "[ReceiptPrintService] 전표 인쇄 사용 안 함 — 출력 생략");
                return ReceiptPrintOutcome.Skipped();
            }

            var connection = new PrinterConnection(settings.PrinterPort, settings.PrinterSpeed);
            return await ComposeAndPrintAsync(receipt, connection, label, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 설정 로드·그 밖의 예상치 못한 경로 — 공개 메서드는 예외를 던지지 않는다는 계약을 끝까지 지킨다.
            return LogUnexpected(label, ex);
        }
    }

    /// <summary>
    /// Phase 35 P35-1(PRD §4.2) — 직전거래 전표출력. <b>전표 인쇄 사용 설정을 읽지 않는다</b>(사용자가
    /// 명시적으로 요청한 출력) — 포트·속도는 호출자(가맹점 설정 화면의 현재 입력값)가 넘긴 <paramref
    /// name="connection"/>을 그대로 쓴다. 넘겨받은 <paramref name="receipt"/>는 변경하지 않고 복사본에
    /// <see cref="NationalTaxReceipt.IsReprint"/>=<c>true</c>를 세워 제목 위 <c>(재출력)</c> 줄을 붙인다.
    /// 실패 로그는 <see cref="PrintAsync"/>와 같은 S12(InvalidPort WARN, 그 외 ERROR)이며 문구에 "재출력"을
    /// 표시한다. 예외를 던지지 않는다.
    /// </summary>
    public async Task<ReceiptPrintOutcome> ReprintAsync(
        NationalTaxReceipt receipt, PrinterConnection connection, CancellationToken cancellationToken)
    {
        const string label = "전표 재출력";
        try
        {
            NationalTaxReceipt reprint = CopyAsReprint(receipt);
            return await ComposeAndPrintAsync(reprint, connection, label, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return LogUnexpected(label, ex);
        }
    }

    /// <summary>자동 출력·재출력 공통부 — 조립 → 인코딩 → 출력 → 실패 로그(S12). 로그에는 결과·사유만 남긴다.</summary>
    private async Task<ReceiptPrintOutcome> ComposeAndPrintAsync(
        NationalTaxReceipt receipt, PrinterConnection connection, string label, CancellationToken cancellationToken)
    {
        byte[] payload;
        try
        {
            PrintDocument document = NationalTaxReceiptComposer.Compose(receipt);
            payload = EscPosDocumentEncoder.Encode(document);
        }
        catch (Exception ex)
        {
            // 영수증 값(납세자명 등)은 로그에 남기지 않는다 — 예외 타입·메시지만.
            FileLogger.Error(
                LogCategory.Printer,
                $"[ReceiptPrintService] {label} 실패 — 사유=양식 조립 오류({ex.GetType().Name}: {ex.Message})",
                code: InternalFaultCodes.ReceiptPrintFailure,
                transactionId: null);
            return ReceiptPrintOutcome.Failed(ReceiptPrintFailureReason.CompositionFailed);
        }

        PrintResult result = await _printer.PrintAsync(payload, connection, cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            // PRD §5 표 — 포트 번호 없음·잘못됨(설정 문제)은 WARN, 그 외 실패는 ERROR. 코드는 둘 다 S12.
            string message = $"[ReceiptPrintService] {label} 실패 — 사유={result.Failure}";
            if (result.Failure == PrintFailureReason.InvalidPort)
                FileLogger.Warn(LogCategory.Printer, message, code: InternalFaultCodes.ReceiptPrintFailure, transactionId: null);
            else
                FileLogger.Error(LogCategory.Printer, message, code: InternalFaultCodes.ReceiptPrintFailure, transactionId: null);
            return ReceiptPrintOutcome.Failed(MapFailure(result.Failure));
        }

        FileLogger.Info(LogCategory.Printer, $"[ReceiptPrintService] {label} 완료(용지부족={result.PaperNearEnd})");
        return ReceiptPrintOutcome.Printed(result.PaperNearEnd);
    }

    private static ReceiptPrintOutcome LogUnexpected(string label, Exception ex)
    {
        FileLogger.Error(
            LogCategory.Printer,
            $"[ReceiptPrintService] {label} 실패 — 사유=예상치 못한 예외({ex.GetType().Name}: {ex.Message})",
            code: InternalFaultCodes.ReceiptPrintFailure,
            transactionId: null);
        return ReceiptPrintOutcome.Failed(ReceiptPrintFailureReason.Unexpected);
    }

    /// <summary>재출력용 복사본 — 호출자의 모델(저장소에서 읽은 값)을 건드리지 않는다.</summary>
    private static NationalTaxReceipt CopyAsReprint(NationalTaxReceipt source) => new()
    {
        ElectronicPaymentNumber = source.ElectronicPaymentNumber,
        TaxpayerName = source.TaxpayerName,
        TaxpayerNumber = source.TaxpayerNumber,
        CollectingAgency = source.CollectingAgency,
        AccountNumber = source.AccountNumber,
        DueDateWithinPeriod = source.DueDateWithinPeriod,
        AmountWithinPeriod = source.AmountWithinPeriod,
        DueDateAfterPeriod = source.DueDateAfterPeriod,
        AmountAfterPeriod = source.AmountAfterPeriod,
        TaxAmount = source.TaxAmount,
        Fee = source.Fee,
        TotalAmount = source.TotalAmount,
        PrepaidCardBalance = source.PrepaidCardBalance,
        CardCompanyName = source.CardCompanyName,
        InstallmentMonths = source.InstallmentMonths,
        CardNumber = source.CardNumber,
        PaymentDate = source.PaymentDate,
        FeeRatePercentRaw = source.FeeRatePercentRaw,
        IsReprint = true,
    };

    private static ReceiptPrintFailureReason MapFailure(PrintFailureReason reason) => reason switch
    {
        PrintFailureReason.InvalidPort => ReceiptPrintFailureReason.InvalidPort,
        PrintFailureReason.PortOpenFailed => ReceiptPrintFailureReason.PortOpenFailed,
        PrintFailureReason.NoResponse => ReceiptPrintFailureReason.NoResponse,
        PrintFailureReason.PaperEnd => ReceiptPrintFailureReason.PaperEnd,
        PrintFailureReason.CoverOpen => ReceiptPrintFailureReason.CoverOpen,
        PrintFailureReason.PrinterError => ReceiptPrintFailureReason.PrinterError,
        PrintFailureReason.WriteFailed => ReceiptPrintFailureReason.WriteFailed,
        _ => ReceiptPrintFailureReason.Unexpected,
    };
}
