using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KFTCTaxCAP.Protocol.Printer;
using KFTCTaxCAP.Services.Printer;
using KFTCTaxCAP.Services.Receipt;
using KFTCTaxCAP.Services.Settings;

namespace KFTCTaxCAP.Services.Diagnostics;

/// <summary>
/// Phase 34 P34-1/P34-2 완료 조건 전용 셀프테스트(docs/receipt_print/development_plan.md) —
/// <see cref="ReceiptValueFormatter"/>/<see cref="NationalTaxReceiptComposer"/>/<see cref="ReceiptPrintService"/>
/// 를 하드웨어·설정 화면 없이 검증한다. <see cref="ReceiptPrintSelfTest.RunAll"/>이 이 클래스의
/// <see cref="RunAll"/>을 함께 호출한다(<c>--receipt-print-test self</c> 한 번으로 Phase 33+34 전부 확인).
/// </summary>
internal static class NationalTaxReceiptSelfTest
{
    internal static bool RunAll()
    {
        bool formatterOk = RunFormatterCases();
        bool composerFullOk = RunComposerFullSampleCase();
        bool composerNoRateOk = RunComposerNoRateCase();
        bool composerReprintOk = RunComposerReprintCase();
        bool controlCharOk = RunControlCharacterInjectionCase();
        bool serviceOk = RunServiceCases();

        bool allPassed = formatterOk && composerFullOk && composerNoRateOk && composerReprintOk
            && controlCharOk && serviceOk;

        FileLogger.Info(
            "[receipt-print-test self] [Phase34 조립] 완료 — " +
            $"변환함수={(formatterOk ? "통과" : "실패")}, 전체조립={(composerFullOk ? "통과" : "실패")}, " +
            $"요율없음={(composerNoRateOk ? "통과" : "실패")}, 재출력={(composerReprintOk ? "통과" : "실패")}, " +
            $"제어문자={(controlCharOk ? "통과" : "실패")}, 서비스={(serviceOk ? "통과" : "실패")}, " +
            $"종합={(allPassed ? "통과" : "실패")}");

        return allPassed;
    }

    // ------------------------------------------------------------------
    // ReceiptValueFormatter — 변환 함수마다 정상·공백·비정상 케이스.
    // ------------------------------------------------------------------

    private static bool RunFormatterCases()
    {
        bool ok = true;

        // Amount — 정상/전부 0(빈 값 아님)/공백(빈 값)/비정상(파싱 불가 원문 그대로)/null.
        ok &= Check("Amount 정상", ReceiptValueFormatter.Amount("000000000100000"), "100,000");
        ok &= Check("Amount 전부 0", ReceiptValueFormatter.Amount("000000000000000"), "0");
        ok &= Check("Amount 공백", ReceiptValueFormatter.Amount("               "), string.Empty);
        ok &= Check("Amount 비정상", ReceiptValueFormatter.Amount("12A45"), "12A45");
        ok &= Check("Amount null", ReceiptValueFormatter.Amount(null), string.Empty);

        // Date — 정상/공백/비정상(형식 아님, 원문 그대로)/null.
        ok &= Check("Date 정상", ReceiptValueFormatter.Date("20260831"), "2026.08.31");
        ok &= Check("Date 공백", ReceiptValueFormatter.Date("        "), string.Empty);
        ok &= Check("Date 비정상", ReceiptValueFormatter.Date("2026-08-31"), "2026-08-31");
        ok &= Check("Date null", ReceiptValueFormatter.Date(null), string.Empty);

        // Installment — 00/01→일시불, 숫자→N개월, 공백, 비정상, null.
        ok &= Check("Installment 01", ReceiptValueFormatter.Installment("01"), "일시불");
        ok &= Check("Installment 00", ReceiptValueFormatter.Installment("00"), "일시불");
        ok &= Check("Installment 03", ReceiptValueFormatter.Installment("03"), "3개월");
        ok &= Check("Installment 공백", ReceiptValueFormatter.Installment("  "), string.Empty);
        ok &= Check("Installment 비정상", ReceiptValueFormatter.Installment("XY"), "XY");
        ok &= Check("Installment null", ReceiptValueFormatter.Installment(null), string.Empty);

        // MaskTaxpayerNumber — 13자리/6자 이하(그대로)/정확히 6자/공백/null.
        ok &= Check("MaskTaxpayerNumber 13자리", ReceiptValueFormatter.MaskTaxpayerNumber("8001011234567"), "800101*******");
        ok &= Check("MaskTaxpayerNumber 5자(짧음)", ReceiptValueFormatter.MaskTaxpayerNumber("12345"), "12345");
        ok &= Check("MaskTaxpayerNumber 정확히6자", ReceiptValueFormatter.MaskTaxpayerNumber("123456"), "123456");
        ok &= Check("MaskTaxpayerNumber 공백", ReceiptValueFormatter.MaskTaxpayerNumber("   "), string.Empty);
        ok &= Check("MaskTaxpayerNumber null", ReceiptValueFormatter.MaskTaxpayerNumber(null), string.Empty);

        // PrepaidBalance — 공백→0(다른 항목과 다른 예외)/정상(천단위 쉼표)/전부0/null.
        ok &= Check("PrepaidBalance 공백", ReceiptValueFormatter.PrepaidBalance("            "), "0");
        ok &= Check("PrepaidBalance 정상", ReceiptValueFormatter.PrepaidBalance("000000001234"), "1,234");
        ok &= Check("PrepaidBalance 전부0", ReceiptValueFormatter.PrepaidBalance("000000000000"), "0");
        ok &= Check("PrepaidBalance null", ReceiptValueFormatter.PrepaidBalance(null), "0");

        // FeeRatePercent — 0050→0.5, 0075→0.75, 1000→10, 0005→0.05, 공백/비정상/null→null(요율 없음).
        ok &= CheckNullable("FeeRatePercent 0050", ReceiptValueFormatter.FeeRatePercent("0050"), "0.5");
        ok &= CheckNullable("FeeRatePercent 0075", ReceiptValueFormatter.FeeRatePercent("0075"), "0.75");
        ok &= CheckNullable("FeeRatePercent 1000", ReceiptValueFormatter.FeeRatePercent("1000"), "10");
        ok &= CheckNullable("FeeRatePercent 0005", ReceiptValueFormatter.FeeRatePercent("0005"), "0.05");
        ok &= CheckNullable("FeeRatePercent 공백", ReceiptValueFormatter.FeeRatePercent("    "), null);
        ok &= CheckNullable("FeeRatePercent 비정상", ReceiptValueFormatter.FeeRatePercent("abcd"), null);
        ok &= CheckNullable("FeeRatePercent null", ReceiptValueFormatter.FeeRatePercent(null), null);

        // CardNumber — 정상(4자리마다 -)/공백/null. (값은 800000 응답 #14 — 결제창 조립기 self ⑥에서 조립 경로까지 검증)
        ok &= Check("CardNumber 정상", ReceiptValueFormatter.CardNumber("1234567890123456"), "1234-5678-9012-3456");
        ok &= Check("CardNumber 공백", ReceiptValueFormatter.CardNumber("   "), string.Empty);
        ok &= Check("CardNumber null", ReceiptValueFormatter.CardNumber(null), string.Empty);

        // Sanitize — 제어문자(ESC 0x1B, DEL 0x7F) 치환 + 뒤 공백 트림, 공백뿐, null.
        ok &= Check("Sanitize ESC+뒤공백", ReceiptValueFormatter.Sanitize("ab\u001Bcd "), "ab cd");
        ok &= Check("Sanitize DEL", ReceiptValueFormatter.Sanitize("ab\u007Fcd"), "ab cd");
        ok &= Check("Sanitize 공백뿐", ReceiptValueFormatter.Sanitize("   "), string.Empty);
        ok &= Check("Sanitize null", ReceiptValueFormatter.Sanitize(null), string.Empty);

        return ok;
    }

    // ------------------------------------------------------------------
    // NationalTaxReceiptComposer — PRD §3.2 그림과 글자 단위 대조.
    // ------------------------------------------------------------------

    /// <summary>
    /// PRD §3.2 그림·§3.3(요율 0080=0.8%)과 글자 단위로 같은 더미 값으로 <see cref="NationalTaxReceipt"/>를
    /// 채운다. <b>카드번호만 예외</b> — 카드번호는 800000 응답 <c>#14</c>(마스킹 카드번호, SPEC 20260930)에서
    /// 오고 800000 응답이 없으면 <c>null</c>(빈칸, 라벨만)이다(PRD §2.2 #16, §2.4). 이 샘플은 그 "없음" 경로를
    /// 검증하려고 <c>null</c>로 둔다 — 값이 있을 때의 줄(<c>1111-2222-****-444*</c>)은
    /// <c>NationalTaxReceiptAssemblerSelfTest</c> ⑥이 확인한다.
    /// </summary>
    private static NationalTaxReceipt BuildFullSampleReceipt() => new()
    {
        ElectronicPaymentNumber = "0000000000000000000",
        TaxpayerName = "홍길동",
        TaxpayerNumber = "8001011234567",
        CollectingAgency = "국세청 서울지방국세청 강서세무서 징수관",
        AccountNumber = "012027",
        DueDateWithinPeriod = "20260831",
        AmountWithinPeriod = "000000000100000",
        DueDateAfterPeriod = "20260831",
        AmountAfterPeriod = "000000000100000",
        TaxAmount = "000000000100000",
        Fee = "000000000000700",
        TotalAmount = "000000000100700",
        PrepaidCardBalance = "000000000000",
        CardCompanyName = "신한카드",
        InstallmentMonths = "01",
        CardNumber = null,
        PaymentDate = "20260819",
        FeeRatePercentRaw = "0080",
        IsReprint = false,
    };

    private static readonly (string Text, PrintAlignment Alignment, PrintSize Size)[] ExpectedFullSampleLines =
    {
        ("종합소득세 납부확인증", PrintAlignment.Center, PrintSize.DoubleSize),
        (Sep(), PrintAlignment.Left, PrintSize.Normal),
        ("전자납부번호 : 0000000000000000000", PrintAlignment.Left, PrintSize.Normal),
        ("납세자명 : 홍길동", PrintAlignment.Left, PrintSize.Normal),
        ("납세자번호 : 800101*******", PrintAlignment.Left, PrintSize.Normal),
        ("세입징수관서 : 국세청 서울지방국세청 강서", PrintAlignment.Left, PrintSize.Normal),
        ("세무서 징수관", PrintAlignment.Left, PrintSize.Normal),
        ("세입징수관계좌번호 : 012027", PrintAlignment.Left, PrintSize.Normal),
        (Sep(), PrintAlignment.Left, PrintSize.Normal),
        ("납기내기한 : 2026.08.31", PrintAlignment.Left, PrintSize.Normal),
        ("납기내금액(원) : 100,000", PrintAlignment.Left, PrintSize.Normal),
        ("납기후기한 : 2026.08.31", PrintAlignment.Left, PrintSize.Normal),
        ("납기후금액(원) : 100,000", PrintAlignment.Left, PrintSize.Normal),
        (Sep(), PrintAlignment.Left, PrintSize.Normal),
        ("납부세액(원) : 100,000", PrintAlignment.Left, PrintSize.Normal),
        ("납부대행수수료(원) : 700", PrintAlignment.Left, PrintSize.Normal),
        ("총 납부금액(원) : 100,700", PrintAlignment.Left, PrintSize.Normal),
        ("선불카드잔액(원) : 0", PrintAlignment.Left, PrintSize.Normal),
        (Sep(), PrintAlignment.Left, PrintSize.Normal),
        ("납부카드 : 신한카드", PrintAlignment.Left, PrintSize.Normal),
        ("할부개월수 : 일시불", PrintAlignment.Left, PrintSize.Normal),
        ("카드번호 :", PrintAlignment.Left, PrintSize.Normal), // 값 빈칸(§2.2 #16, §9 #11) — LabelValue의 트림 결과.
        ("납부일자 : 2026.08.19", PrintAlignment.Left, PrintSize.Normal),
        (Sep(), PrintAlignment.Left, PrintSize.Normal),
        (string.Empty, PrintAlignment.Left, PrintSize.Normal),
        ("1.신용카드로 납부한 국세는 할부거래계약서", PrintAlignment.Left, PrintSize.Normal),
        ("및 약관 등에도 불구하고 납부를 취소할 수", PrintAlignment.Left, PrintSize.Normal),
        ("없습니다.", PrintAlignment.Left, PrintSize.Normal),
        (string.Empty, PrintAlignment.Left, PrintSize.Normal),
        ("2.신용카드로 국세를 납부할 경우 납부세액의", PrintAlignment.Left, PrintSize.Normal),
        (" 0.8%인 납부대행수수료는 납세자가 부담합니", PrintAlignment.Left, PrintSize.Normal),
        ("다.", PrintAlignment.Left, PrintSize.Normal),
    };

    private static string Sep() => new string('-', PrinterProfile.LineWidth);

    private static bool RunComposerFullSampleCase()
    {
        PrintDocument document = NationalTaxReceiptComposer.Compose(BuildFullSampleReceipt());
        return CheckLines("전체 조립(PRD §3.2, 요율 0.8%)", document.Lines, ExpectedFullSampleLines);
    }

    /// <summary>완료 조건: 요율 없음 → 2번 문구가 요율 없는 원문의 Wrap 결과와 같다(PRD §3.3).</summary>
    private static bool RunComposerNoRateCase()
    {
        NationalTaxReceipt receipt = BuildFullSampleReceipt();
        receipt.FeeRatePercentRaw = null;

        PrintDocument document = NationalTaxReceiptComposer.Compose(receipt);

        const string expectedNoticeText = "2.신용카드로 국세를 납부할 경우 납부대행수수료는 납세자가 부담합니다.";
        IReadOnlyList<string> expectedNoticeLines = ReceiptTextLayout.Wrap(expectedNoticeText, PrinterProfile.LineWidth);

        var actualTail = document.Lines.Skip(document.Lines.Count - expectedNoticeLines.Count)
            .Select(l => l.Text)
            .ToList();

        bool ok = actualTail.SequenceEqual(expectedNoticeLines);

        if (ok)
        {
            FileLogger.Info(
                "[receipt-print-test self] [요율 없음 2번 문구] 통과 — " +
                string.Join(" | ", actualTail.Select(l => $"\"{l}\"")));
        }
        else
        {
            FileLogger.Error(LogCategory.App,
                "[receipt-print-test self] ★ [요율 없음 2번 문구] 불일치 — 기대: " +
                string.Join(" | ", expectedNoticeLines.Select(l => $"\"{l}\"")) + " / 실제: " +
                string.Join(" | ", actualTail.Select(l => $"\"{l}\"")));
        }

        return ok;
    }

    /// <summary>완료 조건: IsReprint → 첫 줄 "(재출력)"(가운데, 보통 크기), 나머지는 그대로 한 줄씩 밀림.</summary>
    private static bool RunComposerReprintCase()
    {
        NationalTaxReceipt receipt = BuildFullSampleReceipt();
        receipt.IsReprint = true;

        PrintDocument document = NationalTaxReceiptComposer.Compose(receipt);

        bool ok = document.Lines.Count == ExpectedFullSampleLines.Length + 1
            && document.Lines[0].Text == "(재출력)"
            && document.Lines[0].Alignment == PrintAlignment.Center
            && document.Lines[0].Size == PrintSize.Normal;

        // 나머지 줄은 원래 목록과 인덱스가 하나씩 밀려 그대로 일치해야 한다.
        if (ok)
        {
            for (int i = 0; i < ExpectedFullSampleLines.Length; i++)
            {
                PrintLine actual = document.Lines[i + 1];
                (string text, PrintAlignment alignment, PrintSize size) = ExpectedFullSampleLines[i];
                if (actual.Text != text || actual.Alignment != alignment || actual.Size != size)
                {
                    ok = false;
                    break;
                }
            }
        }

        if (ok)
        {
            FileLogger.Info("[receipt-print-test self] [재출력] 통과 — 첫 줄=\"(재출력)\"(가운데/보통), 나머지 줄 그대로 밀림");
        }
        else
        {
            FileLogger.Error(LogCategory.App,
                $"[receipt-print-test self] ★ [재출력] 불일치 — 총 {document.Lines.Count}줄, 첫 줄=\"{document.Lines[0].Text}\"");
        }

        return ok;
    }

    /// <summary>
    /// 완료 조건: 제어문자 포함 값(납세자명에 ESC(0x1B)+'@' 삽입, "ESC @" 명령 흉내) → 인코딩 결과의
    /// 값 영역에 <c>1B 40</c>이 추가로 나타나지 않는다(문서 맨 앞의 정상 초기화 명령 1개만 있어야 한다).
    /// </summary>
    private static bool RunControlCharacterInjectionCase()
    {
        NationalTaxReceipt receipt = BuildFullSampleReceipt();
        receipt.TaxpayerName = "홍\u001B@길동"; // ESC(0x1B) + '@'(0x40) — "ESC @" 명령 바이트 흉내.

        PrintDocument document = NationalTaxReceiptComposer.Compose(receipt);
        byte[] bytes = EscPosDocumentEncoder.Encode(document);

        int occurrences = CountSequence(bytes, new byte[] { 0x1B, 0x40 });
        bool ok = occurrences == 1; // 문서 맨 앞 ESC @ 초기화 명령 1개만 있어야 한다.

        if (ok)
        {
            FileLogger.Info($"[receipt-print-test self] [제어문자 방어] 통과 — 1B 40 시퀀스 {occurrences}회(초기화 명령 1개만)");
        }
        else
        {
            FileLogger.Error(LogCategory.App,
                $"[receipt-print-test self] ★ [제어문자 방어] 1B 40 시퀀스가 {occurrences}회 나타남(기대 1회)");
        }

        return ok;
    }

    // ------------------------------------------------------------------
    // ReceiptPrintService — 설정 확인·출력·예외 미전파.
    // ------------------------------------------------------------------

    private static bool RunServiceCases()
    {
        bool ok = true;

        ok &= RunServiceSkippedWhenDisabledCase();
        ok &= RunServiceFailedOnPrinterFailureCase();
        ok &= RunServicePayloadMatchesComposerCase();
        ok &= RunServiceExceptionSafetyCases();

        return ok;
    }

    private static ShopSettings BuildSettings(bool slipPrintEnabled, string printerPort = "5", int printerSpeed = 115200) => new()
    {
        SlipPrintEnabled = slipPrintEnabled,
        PrinterPort = printerPort,
        PrinterSpeed = printerSpeed,
    };

    /// <summary>완료 조건: 설정 OFF → 프린터 호출 0회, Skipped.</summary>
    private static bool RunServiceSkippedWhenDisabledCase()
    {
        var fakePrinter = new FakeReceiptPrinter(PrintResult.Success());
        var service = new ReceiptPrintService(() => BuildSettings(slipPrintEnabled: false), fakePrinter);

        ReceiptPrintOutcome outcome = service.PrintAsync(BuildFullSampleReceipt(), CancellationToken.None)
            .GetAwaiter().GetResult();

        bool ok = outcome.Status == ReceiptPrintStatus.Skipped && fakePrinter.CallCount == 0;

        LogServiceCheck("설정 OFF → Skipped", ok,
            $"Status={outcome.Status}, CallCount={fakePrinter.CallCount}");

        return ok;
    }

    /// <summary>완료 조건: Fake 프린터 실패 → Failed(사유 매핑). S12 로그는 별도로 파일에서 확인한다
    /// (다른 코드(E42/E43 등)와 같은 방식 — 이 self 검증은 렌더링된 로그 텍스트까지 재현하지 않는다).</summary>
    private static bool RunServiceFailedOnPrinterFailureCase()
    {
        var fakePrinter = new FakeReceiptPrinter(PrintResult.Fail(PrintFailureReason.PortOpenFailed));
        var service = new ReceiptPrintService(() => BuildSettings(slipPrintEnabled: true), fakePrinter);

        ReceiptPrintOutcome outcome = service.PrintAsync(BuildFullSampleReceipt(), CancellationToken.None)
            .GetAwaiter().GetResult();

        bool ok = outcome.Status == ReceiptPrintStatus.Failed
            && outcome.Failure == ReceiptPrintFailureReason.PortOpenFailed
            && fakePrinter.CallCount == 1;

        LogServiceCheck("Fake 실패 → Failed(PortOpenFailed)", ok,
            $"Status={outcome.Status}, Failure={outcome.Failure}, CallCount={fakePrinter.CallCount}");

        return ok;
    }

    /// <summary>완료 조건: 설정 ON + Fake 성공 → payload가 P34-1 조립 결과의 인코딩과 같다.</summary>
    private static bool RunServicePayloadMatchesComposerCase()
    {
        var fakePrinter = new FakeReceiptPrinter(PrintResult.Success());
        var service = new ReceiptPrintService(() => BuildSettings(slipPrintEnabled: true, printerPort: "7", printerSpeed: 9600), fakePrinter);

        NationalTaxReceipt receipt = BuildFullSampleReceipt();
        ReceiptPrintOutcome outcome = service.PrintAsync(receipt, CancellationToken.None).GetAwaiter().GetResult();

        byte[] expectedPayload = EscPosDocumentEncoder.Encode(NationalTaxReceiptComposer.Compose(receipt));

        bool ok = outcome.Status == ReceiptPrintStatus.Printed
            && fakePrinter.LastPayload is not null
            && fakePrinter.LastPayload.SequenceEqual(expectedPayload)
            && fakePrinter.LastConnection?.PortNumberText == "7"
            && fakePrinter.LastConnection?.BaudRate == 9600;

        LogServiceCheck("payload == 조립 결과 인코딩", ok,
            $"Status={outcome.Status}, payload길이={fakePrinter.LastPayload?.Length}, 기대길이={expectedPayload.Length}, " +
            $"포트={fakePrinter.LastConnection?.PortNumberText}, 속도={fakePrinter.LastConnection?.BaudRate}");

        return ok;
    }

    /// <summary>완료 조건: 예외 미전파(설정 로드·조립·출력 어디서 던져도).</summary>
    private static bool RunServiceExceptionSafetyCases()
    {
        bool ok = true;

        // 설정 로드가 예외를 던짐.
        var serviceSettingsThrows = new ReceiptPrintService(
            () => throw new InvalidOperationException("설정 로드 실패(self 검증용)"),
            new FakeReceiptPrinter(PrintResult.Success()));
        ok &= RunNoThrowCase("설정 로드 예외", () =>
            serviceSettingsThrows.PrintAsync(BuildFullSampleReceipt(), CancellationToken.None).GetAwaiter().GetResult());

        // 조립이 예외를 던짐 — NationalTaxReceiptComposer.Compose(null)이 ArgumentNullException을 던진다.
        var serviceCompositionThrows = new ReceiptPrintService(
            () => BuildSettings(slipPrintEnabled: true), new FakeReceiptPrinter(PrintResult.Success()));
        ok &= RunNoThrowCase("조립 예외(null 영수증)", () =>
            serviceCompositionThrows.PrintAsync(null!, CancellationToken.None).GetAwaiter().GetResult());

        // 출력이 예외를 던짐.
        var serviceOutputThrows = new ReceiptPrintService(
            () => BuildSettings(slipPrintEnabled: true), new ThrowingReceiptPrinter());
        ok &= RunNoThrowCase("출력 예외", () =>
            serviceOutputThrows.PrintAsync(BuildFullSampleReceipt(), CancellationToken.None).GetAwaiter().GetResult());

        return ok;
    }

    private static bool RunNoThrowCase(string caseName, Func<ReceiptPrintOutcome> action)
    {
        try
        {
            ReceiptPrintOutcome outcome = action();
            bool ok = outcome.Status == ReceiptPrintStatus.Failed;
            LogServiceCheck($"예외 미전파 — {caseName}", ok, $"Status={outcome.Status}, Failure={outcome.Failure}");
            return ok;
        }
        catch (Exception ex)
        {
            FileLogger.Error(LogCategory.App,
                $"[receipt-print-test self] ★ [예외 미전파 — {caseName}] 예외가 밖으로 전파됨: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static void LogServiceCheck(string caseName, bool ok, string detail)
    {
        if (ok)
        {
            FileLogger.Info($"[receipt-print-test self] [서비스 — {caseName}] 통과 — {detail}");
        }
        else
        {
            FileLogger.Error(LogCategory.App, $"[receipt-print-test self] ★ [서비스 — {caseName}] 불일치 — {detail}");
        }
    }

    /// <summary>출력 단계에서 예외를 던지는 테스트 전용 <see cref="IReceiptPrinter"/>.</summary>
    private sealed class ThrowingReceiptPrinter : IReceiptPrinter
    {
        public Task<PrintResult> PrintAsync(byte[] payload, PrinterConnection connection, CancellationToken cancellationToken)
            => throw new InvalidOperationException("출력 실패(self 검증용)");
    }

    // ------------------------------------------------------------------
    // 공통 Check 헬퍼.
    // ------------------------------------------------------------------

    private static bool Check(string caseName, string actual, string expected)
    {
        bool ok = actual == expected;
        if (ok)
        {
            FileLogger.Info($"[receipt-print-test self] [{caseName}] 통과 — \"{actual}\"");
        }
        else
        {
            FileLogger.Error(LogCategory.App,
                $"[receipt-print-test self] ★ [{caseName}] 불일치 — 기대=\"{expected}\", 실제=\"{actual}\"");
        }

        return ok;
    }

    private static bool CheckNullable(string caseName, string? actual, string? expected)
    {
        bool ok = actual == expected;
        if (ok)
        {
            FileLogger.Info($"[receipt-print-test self] [{caseName}] 통과 — {(actual is null ? "null" : $"\"{actual}\"")}");
        }
        else
        {
            FileLogger.Error(LogCategory.App,
                $"[receipt-print-test self] ★ [{caseName}] 불일치 — 기대={(expected is null ? "null" : $"\"{expected}\"")}, " +
                $"실제={(actual is null ? "null" : $"\"{actual}\"")}");
        }

        return ok;
    }

    private static bool CheckLines(
        string caseName,
        IReadOnlyList<PrintLine> actual,
        (string Text, PrintAlignment Alignment, PrintSize Size)[] expected)
    {
        bool ok = actual.Count == expected.Length;
        if (ok)
        {
            for (int i = 0; i < expected.Length; i++)
            {
                PrintLine line = actual[i];
                (string text, PrintAlignment alignment, PrintSize size) = expected[i];
                if (line.Text != text || line.Alignment != alignment || line.Size != size)
                {
                    ok = false;
                    FileLogger.Error(LogCategory.App,
                        $"[receipt-print-test self] ★ [{caseName}] {i}번째 줄 불일치 — 기대=(\"{text}\",{alignment},{size}), " +
                        $"실제=(\"{line.Text}\",{line.Alignment},{line.Size})");
                    break;
                }
            }
        }
        else
        {
            FileLogger.Error(LogCategory.App,
                $"[receipt-print-test self] ★ [{caseName}] 줄 수 불일치 — 기대={expected.Length}, 실제={actual.Count}");
        }

        if (ok)
        {
            FileLogger.Info($"[receipt-print-test self] [{caseName}] 통과 — {actual.Count}줄 전부 글자 단위 일치");
        }

        return ok;
    }

    private static int CountSequence(byte[] haystack, byte[] needle)
    {
        int count = 0;
        for (int i = 0; i <= haystack.Length - needle.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
                count++;
        }

        return count;
    }
}
