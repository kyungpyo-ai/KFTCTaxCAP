using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using KFTCOneCAP.Wpf.Protocol.Pos;
using KFTCOneCAP.Wpf.Protocol.Pos.Schemas;
using KFTCOneCAP.Wpf.Services.Diagnostics;
using KFTCOneCAP.Wpf.Services.Pos;
using KFTCOneCAP.Wpf.Services.Receipt;
using KFTCOneCAP.Wpf.Services.Settings;

namespace KFTCOneCAP.Wpf.ViewModels.Payment;

/// <summary>
/// Phase 29 P29-6(docs/payment_relay/development_plan.md "P29-6", PRD.md §12) — 국세 결제 화면
/// (Views/PaymentScreenWindow.xaml)의 최상위 ViewModel. 3전문(501008/800000/902614) 탭을 담는다.
///
/// 홈 화면 "결제" 카드(P29-7)에서 연다. <c>--payment-screen-test</c> 진단 인자로 Owner 없이 단독
/// 생성/표시할 수도 있다(App.xaml.cs).
/// </summary>
public sealed partial class PaymentScreenViewModel : ObservableObject
{
    public PaymentScreenViewModel() : this(new ReceiptPrintService())
    {
    }

    /// <summary>Phase 34 CP1 M-1 — self 검증용 주입점(Fake 프린터를 쓴 <see cref="ReceiptPrintService"/>).
    /// 운영 경로는 위 기본 생성자만 쓴다.</summary>
    internal PaymentScreenViewModel(ReceiptPrintService receiptPrintService)
    {
        _receiptPrintService = receiptPrintService ?? throw new ArgumentNullException(nameof(receiptPrintService));
        var shopSettingsService = new ShopSettingsService();

        // 탭 라벨에서 거래 구분 코드(501008/800000/902614) 숫자를 뺐다(2026-09-18 사용자 지시 — 균등폭
        // 탭에서 말줄임이 나던 것을 한글 문구만 남겨 줄여서 해결). 코드값 자체는 각 탭의
        // TransactionTypeCode 프로퍼티(PaymentTelegramTabViewModel)로 여전히 확인 가능하다.
        // 2026-09-28 P30-7 실기 검증 — 902614 #42(키오스크 고유번호)를 설정값으로 채우기 위한 공용
        // provider(PaymentTelegramTabViewModel._kioskIdProvider 클래스 주석 참고). 501008/800000 탭은
        // 이 값을 쓰지 않지만(스키마상 #42 자체가 없다), 생성자 시그니처를 셋 다 통일해 둔다.
        Func<string> kioskIdProvider = () => shopSettingsService.Load().KioskId;

        Tabs = new ObservableCollection<PaymentTelegramTabViewModel>
        {
            new("국고 상세 고지내역 조회", NoticeInquirySchema.Create(), () => PosClient.DefaultResponseTimeout, kioskIdProvider),
            new("카드 정보 조회", CardInfoInquirySchema.Create(), () => PosClient.DefaultResponseTimeout, kioskIdProvider),
            new("국고 신용카드 승인요청", CardApprovalSchema.Create(),
                () => PosClient.ComputeCardApprovalResponseTimeout(shopSettingsService.Load()), kioskIdProvider),
        };

        // 체크포인트 2 M-2 수정(2026-09-21, 사용자 확정 "통신중일때는 다른 걸 못하게 하는게 맞아") —
        // 탭별 IsSending은 자기 탭의 전송/재생성 버튼만 막고 다른 탭에서 또 전송 버튼을 누르는 것은
        // 막지 않았다. 그래서 한 탭(예: 902614, 카드 대기 최대 120초)이 전송 중일 때 다른 탭으로 넘어가
        // 그 탭의 전송을 또 누를 수 있었다 — TransactionQueue가 순차 처리하므로 안전(리더기/VAN 동시
        // 접근)에는 문제가 없지만, 두 번째 요청이 응답 타임아웃에 먼저 걸려 화면엔 "실패"로 보이는데
        // 실제로는 서버에서 정상 처리되는 상황이 생겨 사용자가 오인 재전송할 위험이 있었다.
        //
        // 최초 구현(TabControl 자체를 IsEnabled=false로 비활성화)은 탭 전환 자체가 막혀 버려 "다른 두
        // 탭이 아예 안 눌리는" 부작용이 났다(2026-09-21 사용자 실측 발견) — TabControl.IsEnabled=false는
        // 자식 전체(헤더 포함)의 입력을 막는데, 그 시각적 신호가 없어(PaymentTabItemStyle에 비활성 트
        // 리거가 없음) 고장처럼 보였다. 탭 전환 자체는 무해하므로(구경만 하는 것) 항상 열어 두고, 각
        // 탭의 전송/재생성 버튼만 개별적으로 막는 방식(PaymentTelegramTabViewModel.IsBlockedByOtherTab)
        // 으로 바꿨다.
        foreach (PaymentTelegramTabViewModel tab in Tabs)
            tab.PropertyChanged += OnTabPropertyChanged;
    }

    private void OnTabPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PaymentTelegramTabViewModel.IsSending))
            RecomputeBlockedFlags();
        else if (e.PropertyName == nameof(PaymentTelegramTabViewModel.HasResponse)
                 && sender is PaymentTelegramTabViewModel { HasResponse: true } sourceTab)
        {
            ApplyChainMappingsFrom(sourceTab);

            // Phase 34 P34-3(docs/receipt_print/PRD.md §4.1) — 응답 처리(연쇄 적용) **뒤**에 자동 출력을
            // 시도한다. 902614 탭의 false→true 전이만 대상이다(SendAsync가 전송 직전 HasResponse를 false로
            // 떨어뜨려 응답마다 전이가 보장된다).
            if (sourceTab.TransactionTypeCode == CardApprovalTransactionTypeCode)
                PrintReceiptIfApproved(sourceTab);
        }
    }

    private const string CardApprovalTransactionTypeCode = "902614";
    private const string NoticeInquiryTransactionTypeCode = "501008";
    private const string CardInfoInquiryTransactionTypeCode = "800000";

    /// <summary>Phase 34 P34-3 — 납부확인증 출력 서비스. 설정은 이 서비스가 매 호출마다 다시 읽는다(캐시
    /// 금지, PRD §7.2). 상태가 없으므로 창 수명 동안 인스턴스 하나를 재사용한다. 생성자 주입은 self 검증
    /// (<c>NationalTaxReceiptAssemblerSelfTest</c>)이 Fake 프린터를 넣기 위한 것뿐이다.</summary>
    private readonly ReceiptPrintService _receiptPrintService;

    /// <summary>
    /// Phase 34 P34-3(PRD §5) — 자동 출력이 실패했을 때 View에 경고 문구를 알린다. ViewModel은 MessageBox를
    /// 모른다(계층 규칙, <c>ShopSetupViewModel.ResultMessageReady</c>와 같은 패턴). Skipped/Printed는 알리지
    /// 않는다. 출력은 창과 무관하게 끝까지 진행되므로 창이 닫힌 뒤에 발생할 수 있다 — View는 닫힐 때
    /// 구독을 해제한다.
    /// </summary>
    public event EventHandler<string>? ReceiptPrintWarningRequested;

    /// <summary>
    /// <c>HasResponse</c> PropertyChanged 핸들러에서 부르는 진입점. <c>async void</c>는 이벤트 핸들러 경로라서이며,
    /// 예외는 <see cref="PrintReceiptIfApprovedAsync"/>가 전부 잡는다. 이 메서드는 조립까지만 동기로 하고 곧바로
    /// 반환한다 — 출력과 경고 이벤트는 항상 나중(아래 메서드 주석)에 일어난다.
    /// </summary>
    private async void PrintReceiptIfApproved(PaymentTelegramTabViewModel approvalTab) =>
        await PrintReceiptIfApprovedAsync(approvalTab.TryReadResponseField);

    /// <summary>
    /// 승인(PRD §2.3)이면 세 탭 값으로 영수증을 조립한 뒤 출력한다. 실패면 <see cref="ReceiptPrintWarningRequested"/>.
    ///
    /// <b>실행 순서(CP1 M-1)</b>: 이 메서드는 <c>PaymentTelegramTabViewModel.SendAsync</c>의 <c>HasResponse = true</c>
    /// setter 안(= 그 메서드의 <c>finally { IsSending = false; }</c>보다 먼저)에서 동기로 불린다.
    /// <list type="number">
    /// <item>승인 판정·조립은 <b>동기</b>로 한다 — 탭 응답 캐시를 바로 이 시점의 값으로 읽어야 한다.</item>
    /// <item><c>await Task.Yield()</c> — 여기서 호출자에게 반드시 제어를 돌려준다. 그래서 출력 서비스가 동기로 즉시
    /// 끝나는 실패(포트 빈 값/범위 밖 InvalidPort, 조립 예외, 설정 로드 예외)라도 경고 이벤트가 이 setter 안에서
    /// 올라갈 수 없다. UI 스레드에서는 나머지가 디스패처 큐 뒤로 가므로 SendAsync의 남은 동기 구간(IsSending=false,
    /// 배지 갱신)이 먼저 끝난다.</item>
    /// <item><c>Task.Run</c>으로 출력 서비스를 스레드 풀에서 시작한다 — 설정 로드(레지스트리)와 포트 I/O가 UI 스레드에서
    /// 빠진다.</item>
    /// <item>결과를 받은 continuation은 원래 컨텍스트(UI 스레드)로 돌아와 이벤트를 올린다(View는 UI 스레드 밖에서 올 때만 BeginInvoke로 넘긴다).</item>
    /// </list>
    /// 창이 닫혀도 출력은 끝까지 진행한다(development_plan.md Phase 34 위험 #3) — 취소 토큰을 쓰지 않는다. 전송 버튼
    /// 잠금과 무관하다(확정 사항 1 — 출력 중 다음 전송 허용). 예외는 밖으로 내보내지 않는다.
    /// </summary>
    internal async Task PrintReceiptIfApprovedAsync(Func<int, string?> read902614)
    {
        try
        {
            if (!NationalTaxReceiptAssembler.IsApproved(read902614))
                return;

            NationalTaxReceipt receipt = NationalTaxReceiptAssembler.Assemble(
                FindTab(NoticeInquiryTransactionTypeCode) is { } noticeTab ? noticeTab.TryReadResponseField : null,
                FindTab(CardInfoInquiryTransactionTypeCode) is { } cardInfoTab ? cardInfoTab.TryReadResponseField : null,
                read902614);

            await Task.Yield();

            ReceiptPrintOutcome outcome = await Task.Run(() => _receiptPrintService.PrintAsync(receipt, CancellationToken.None));

            if (outcome.Status == ReceiptPrintStatus.Failed)
                ReceiptPrintWarningRequested?.Invoke(this, BuildReceiptPrintWarningMessage(outcome.Failure));
        }
        catch (Exception ex)
        {
            // 조립/출력 서비스 모두 예외를 던지지 않는 계약이지만, async void 경로라 최후 방어선을 둔다.
            // 영수증 값은 로그에 남기지 않는다(예외 타입·메시지만).
            FileLogger.Error(LogCategory.Printer,
                $"[PaymentScreenViewModel] 납부확인증 자동 출력 처리 중 예외: {ex.GetType().Name}: {ex.Message}",
                code: null, transactionId: null);
        }
    }

    /// <summary>
    /// PRD §5 표의 결제창 경고 문구. 재출력 안내 문구(<c>가맹점 설정의 '직전거래 전표출력'…</c>)는 Phase 35부터
    /// 붙이므로 여기서는 붙이지 않는다(development_plan.md Phase 34 확정 사항 8). PRD 표에 행이 없는 사유
    /// (<see cref="ReceiptPrintFailureReason.PrinterError"/>/<see cref="ReceiptPrintFailureReason.CompositionFailed"/>/
    /// <see cref="ReceiptPrintFailureReason.Unexpected"/> 등)는 괄호 사유 없이 공통 문장만 보여 준다.
    /// </summary>
    internal static string BuildReceiptPrintWarningMessage(ReceiptPrintFailureReason reason)
    {
        const string prefix = "전표 출력에 실패했습니다.";
        string? detail = reason switch
        {
            ReceiptPrintFailureReason.InvalidPort => "프린터 포트 설정 확인",
            ReceiptPrintFailureReason.PortOpenFailed => "프린터 포트를 열 수 없습니다",
            ReceiptPrintFailureReason.NoResponse => "프린터 응답 없음",
            ReceiptPrintFailureReason.PaperEnd => "용지 없음",
            ReceiptPrintFailureReason.CoverOpen => "프린터 덮개 열림",
            ReceiptPrintFailureReason.WriteFailed => "전송 오류",
            _ => null,
        };
        return detail is null ? prefix : $"{prefix} ({detail})";
    }

    /// <summary>Phase 30 P30-4(PRD §13.3 "501008/800000 전송 성공 → 뒤 전문 탭 갱신") — <paramref
    /// name="sourceTab"/>에서 다른 전문(자기 자신 제외)으로 가는 연쇄 항목만 처리한다. 자기참조 항목
    /// (902614 #29 = #27+#28)은 <see cref="PaymentTelegramTabViewModel.RecomputeSelfReferencingChainTargets"/>
    /// 가 그 탭 내부에서 자동으로 처리하므로 여기서 건드릴 필요가 없다.
    ///
    /// <b>값을 직접 고친 뒤 앞 전문을 재전송하면 덮어쓴다</b>(2026-09-22 확정, PRD §13.3) — 연쇄가 최신
    /// 응답을 반영하는 것이 이 기능의 목적이라 의도된 동작이다.</summary>
    private void ApplyChainMappingsFrom(PaymentTelegramTabViewModel sourceTab)
    {
        foreach (TelegramFieldChainMap.ChainEntry entry in TelegramFieldChainMap.Entries)
        {
            if (entry.SourceTelegram != sourceTab.TransactionTypeCode || entry.SourceTelegram == entry.TargetTelegram)
                continue;

            PaymentTelegramTabViewModel? targetTab = FindTab(entry.TargetTelegram);
            if (targetTab is null)
                continue;

            var sourceValues = new List<string>(entry.SourceFieldNumbers.Count);
            bool allAvailable = true;
            foreach (int sourceFieldNumber in entry.SourceFieldNumbers)
            {
                string? value = sourceTab.TryReadResponseField(sourceFieldNumber);
                if (value is null) { allAvailable = false; break; }
                sourceValues.Add(value);
            }
            if (!allAvailable)
                continue;

            // TelegramFieldChainConverter.Convert(값 계산)는 ApplyChainedValue 호출보다 먼저 일어나므로,
            // 합산 자리수 초과 등으로 던지는 PosProtocolException이 ApplyChainedValue 내부의 기존
            // try/catch(OnRequestRowValueChanged)를 거치지 못하고 이 메서드 밖으로 그대로 전파될 수
            // 있다 — 이벤트 핸들러(OnTabPropertyChanged) 안에서 잡히지 않으면 창이 죽으므로 여기서
            // 직접 감싼다. 실패한 필드는 건너뛰고 다음 항목을 계속 처리한다.
            string computed;
            try
            {
                computed = TelegramFieldChainConverter.Convert(
                    entry.Conversion, sourceValues, targetTab.GetFieldLength(entry.TargetFieldNumber), entry.FixedValue);
            }
            catch (PosProtocolException)
            {
                continue;
            }

            targetTab.ApplyChainedValue(entry.TargetFieldNumber, computed);
        }
    }

    private PaymentTelegramTabViewModel? FindTab(string transactionTypeCode)
    {
        foreach (PaymentTelegramTabViewModel tab in Tabs)
            if (tab.TransactionTypeCode == transactionTypeCode)
                return tab;
        return null;
    }

    private void RecomputeBlockedFlags()
    {
        bool anySending = false;
        foreach (PaymentTelegramTabViewModel tab in Tabs)
        {
            if (tab.IsSending)
            {
                anySending = true;
                break;
            }
        }

        foreach (PaymentTelegramTabViewModel tab in Tabs)
            tab.IsBlockedByOtherTab = anySending && !tab.IsSending;
    }

    /// <summary>탭 3개(501008/800000/902614) 고정 목록 — 전문 종류가 늘어날 계획이 없어(PRD §12) 동적
    /// 등록 구조를 두지 않는다.</summary>
    public ObservableCollection<PaymentTelegramTabViewModel> Tabs { get; }

    [ObservableProperty]
    private int selectedTabIndex;
}
