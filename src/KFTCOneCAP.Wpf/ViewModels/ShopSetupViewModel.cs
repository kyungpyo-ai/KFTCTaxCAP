using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KFTCOneCAP.Wpf.Services.Diagnostics;
using KFTCOneCAP.Wpf.Services.Settings;

namespace KFTCOneCAP.Wpf.ViewModels;

/// <summary>
/// 가맹점 설정 화면(Views/ShopSetupWindow.xaml)의 ViewModel.
/// Phase 23(docs/operations/development_plan.md P23-3) — <see cref="ReaderSetupViewModel"/>과 같은
/// 역할 분담을 따른다: 레지스트리 로드/저장·검증은 이 클래스가 맡고, <c>Window.Close()</c>/
/// <c>DialogResult</c>/<c>MessageBox</c>는 <c>Views/ShopSetupWindow.xaml.cs</c>에 남는다(계층 규칙,
/// ViewModels → Services → Protocol → Interop 단방향, PRD.md §0.2). 이 클래스는 WPF 타입을 알지
/// 못한다 — 검증 실패/저장 실패를 <see cref="ResultMessageReady"/> 이벤트로만 알린다.
/// </summary>
public sealed partial class ShopSetupViewModel : ObservableObject
{
    // PRD.md §2.2 — 표시 문구만 원본 MFC에서 가져오고 저장값은 FNAISCRDVAN의 Mode 인자다.
    // "R" 같은 저장값 리터럴이 XAML에 등장하지 않도록 이 클래스 안에서만 표시 문구 ↔ 저장값을 매핑한다.
    private const string VanModeProductionDisplay = "운영 서버";
    private const string VanModeExternalTestDisplay = "테스트 서버";
    private const string VanModeInternalTestDisplay = "테스트 서버(내부용)";

    private const int MinimumConfigurableTimeoutSeconds = 30;

    // PRD §2.8.4 — 확인 시 저장을 막는 포트번호 범위(1~255). "bps" 표시 접미사(PRD §2.8.2)도 여기서만 다룬다.
    private const string PrinterSpeedDisplaySuffix = "bps";
    private const int MinimumPrinterPort = 1;
    private const int MaximumPrinterPort = 255;
    private const int DefaultPrinterSpeedForDisplay = 57600;

    private readonly ShopSettingsService _settingsService = new();

    /// <summary>ComboBox ItemsSource. 표시 문구만 담는다(저장값은 <see cref="ToStorageValue"/>가 매핑).</summary>
    public ObservableCollection<string> VanModeOptions { get; } = new()
    {
        VanModeProductionDisplay,
        VanModeExternalTestDisplay,
        VanModeInternalTestDisplay,
    };

    [ObservableProperty]
    private string vanModeSelection = VanModeProductionDisplay;

    [ObservableProperty]
    private string kioskId = string.Empty;

    // TextBox 바인딩이라 문자열로 들고 있는다 — 검증(숫자만·0 또는 30 이상)은 TryConfirm에서 수행한다.
    [ObservableProperty]
    private string cardReadTimeoutSecondsText = string.Empty;

    [ObservableProperty]
    private bool autoReboot = true;

    [ObservableProperty]
    private bool autoUpdate;

    [ObservableProperty]
    private bool keyinDim;

    // Phase 31(P31-2) — 전표 설정 섹션(PRD §2.8). SlipPrintEnabled가 바뀌면 IsPrinterPortMissing도
    // 다시 계산해야 하므로 [NotifyPropertyChangedFor]로 연결한다(둘 다 값이 하나라도 바뀌면 "입력
    // 필요" 표시 여부가 바뀔 수 있다).
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPrinterPortMissing))]
    private bool slipPrintEnabled;

    /// <summary>ComboBox ItemsSource. <see cref="ShopSettingsService.AllowedPrinterSpeeds"/>로 생성한
    /// "9600bps" 등 표시 문구만 담는다 — "bps"를 붙이고 떼는 곳은 이 클래스뿐이다(서비스·모델은
    /// int만 다룬다, PRD §2.8.2).</summary>
    public ObservableCollection<string> PrinterSpeedOptions { get; } =
        new(ShopSettingsService.AllowedPrinterSpeeds.Select(ToSpeedDisplay));

    [ObservableProperty]
    private string printerSpeedSelection = ToSpeedDisplay(DefaultPrinterSpeedForDisplay);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPrinterPortMissing))]
    private string printerPort = string.Empty;

    /// <summary>"입력 필요" 표시의 유일한 근거(PRD §2.8.4) — 토글 ON이고 포트번호가 비어 있을 때만
    /// true. <see cref="SlipPrintEnabled"/>/<see cref="PrinterPort"/> 변경 시 자동으로 다시 계산된다.</summary>
    public bool IsPrinterPortMissing => SlipPrintEnabled && string.IsNullOrWhiteSpace(PrinterPort);

    /// <summary>P23-3 — 검증 실패(타임아웃 범위 위반) 또는 저장 실패(레지스트리 권한 등)를 View에
    /// 알린다. ReaderSetupViewModel.ResultMessageReady와 같은 이유로 이벤트로만 알리고 MessageBox를
    /// 직접 호출하지 않는다.</summary>
    public event EventHandler<string>? ResultMessageReady;

    /// <summary>Phase 31(P31-2) — 정보(안내) 아이콘으로 띄워야 하는 메시지 전용 이벤트. 기존
    /// <see cref="ResultMessageReady"/>는 경고 전용 계약이 이미 굳어 있어(View가 항상
    /// MessageBoxImage.Warning으로 처리) 시그니처를 바꾸지 않고 새 이벤트를 추가했다.</summary>
    public event EventHandler<string>? InfoMessageReady;

    /// <summary>Phase 31(P31-2) — 포트 검증 실패 시 View가 포트 입력칸에 포커스를 주도록 요청한다.
    /// ViewModel은 WPF 컨트롤(TextBox)을 몰라야 하므로 포커스 자체는 View 책임이다(계층 규칙).</summary>
    public event EventHandler? FocusPrinterPortRequested;

    // 2026-09-02 Opus 리뷰(CP1) 개선권장 9 — ReaderSetupViewModel의 dirty-check 스냅샷 패턴
    // (_snapshotReader1Port 등)과 동일하게, Load() 시점 값을 6개 필드 모두 따로 들고 있다가
    // IsDirty()에서 현재 값과 비교한다.
    private string _snapshotVanModeSelection = string.Empty;
    private string _snapshotKioskId = string.Empty;
    private string _snapshotCardReadTimeoutSecondsText = string.Empty;
    private bool _snapshotAutoReboot;
    private bool _snapshotAutoUpdate;
    private bool _snapshotKeyinDim;

    // Phase 31(P31-2) — 전표 설정 섹션 3개.
    private bool _snapshotSlipPrintEnabled;
    private string _snapshotPrinterSpeedSelection = string.Empty;
    private string _snapshotPrinterPort = string.Empty;

    internal ShopSetupViewModel()
    {
        Load();
    }

    private void Load()
    {
        var settings = _settingsService.Load();

        VanModeSelection = ToDisplayValue(settings.VanMode);
        KioskId = settings.KioskId;
        CardReadTimeoutSecondsText = settings.CardReadTimeoutSeconds.ToString();
        AutoReboot = settings.AutoReboot;
        AutoUpdate = settings.AutoUpdate;
        KeyinDim = settings.KeyinDim;
        SlipPrintEnabled = settings.SlipPrintEnabled;
        PrinterSpeedSelection = ToSpeedDisplay(settings.PrinterSpeed);
        PrinterPort = settings.PrinterPort;

        _snapshotVanModeSelection = VanModeSelection;
        _snapshotKioskId = KioskId;
        _snapshotCardReadTimeoutSecondsText = CardReadTimeoutSecondsText;
        _snapshotAutoReboot = AutoReboot;
        _snapshotAutoUpdate = AutoUpdate;
        _snapshotKeyinDim = KeyinDim;
        _snapshotSlipPrintEnabled = SlipPrintEnabled;
        _snapshotPrinterSpeedSelection = PrinterSpeedSelection;
        _snapshotPrinterPort = PrinterPort;
    }

    /// <summary>
    /// 2026-09-02 Opus 리뷰(CP1) 개선권장 9(사용자 확정) — 취소/창 닫기(X, Alt+F4) 흐름에서
    /// <c>ReaderSetupWindow</c>와 동일하게 "변경사항을 버리시겠습니까" 확인창을 띄우기 위해 필요하다.
    /// 9개 필드 중 하나라도 <see cref="Load"/> 시점 스냅샷과 다르면 dirty(Phase 31, P31-2 — 전표
    /// 설정 3개 추가).
    /// </summary>
    public bool IsDirty() =>
        VanModeSelection != _snapshotVanModeSelection ||
        KioskId != _snapshotKioskId ||
        CardReadTimeoutSecondsText != _snapshotCardReadTimeoutSecondsText ||
        AutoReboot != _snapshotAutoReboot ||
        AutoUpdate != _snapshotAutoUpdate ||
        KeyinDim != _snapshotKeyinDim ||
        SlipPrintEnabled != _snapshotSlipPrintEnabled ||
        PrinterSpeedSelection != _snapshotPrinterSpeedSelection ||
        PrinterPort != _snapshotPrinterPort;

    private static string ToDisplayValue(string vanMode) => vanMode switch
    {
        "OT" => VanModeExternalTestDisplay,
        "IT" => VanModeInternalTestDisplay,
        _ => VanModeProductionDisplay,
    };

    private static string ToStorageValue(string display) => display switch
    {
        VanModeExternalTestDisplay => "OT",
        VanModeInternalTestDisplay => "IT",
        _ => "R",
    };

    /// <summary>PRD §2.8.2 — 화면 표시 문구("9600bps" 등)를 만든다. 서비스·모델은 이 접미사를 모른다.</summary>
    private static string ToSpeedDisplay(int speed) => $"{speed}{PrinterSpeedDisplaySuffix}";

    /// <summary>PRD §2.8.2 — 화면 표시 문구에서 "bps"를 떼고 숫자로 되돌린다. 목록에 없는 값이
    /// 들어올 일은 콤보 바인딩상 없지만, 방어적으로 파싱 실패 시 57600으로 폴백한다.</summary>
    private static int ToSpeedStorage(string display)
    {
        var numericPart = display.EndsWith(PrinterSpeedDisplaySuffix, StringComparison.Ordinal)
            ? display.Substring(0, display.Length - PrinterSpeedDisplaySuffix.Length)
            : display;

        return int.TryParse(numericPart, out int speed) ? speed : DefaultPrinterSpeedForDisplay;
    }

    /// <summary>
    /// PRD.md §2.4 — 카드입력 타임아웃은 숫자만, 0 또는 30 이상만 허용한다. 음수나 비숫자 입력도
    /// 이 조건으로 함께 걸러진다(파싱 실패 시 통과 조건을 만족할 수 없음).
    /// </summary>
    private static bool IsValidTimeout(string text, out int seconds)
    {
        if (!int.TryParse(text, out seconds))
            return false;

        return seconds == 0 || seconds >= MinimumConfigurableTimeoutSeconds;
    }

    /// <summary>
    /// 확인 버튼(PRD.md §2.6) — 검증 후 저장한다. 검증 실패 시 저장하지 않고 false를 반환한다(View는
    /// 이 경우 창을 닫지 않는다). 저장은 <c>확인</c>을 눌렀을 때만 일어난다(취소는 아무것도 저장하지
    /// 않으므로 이 메서드를 호출하지 않는다).
    /// </summary>
    internal bool TryConfirm()
    {
        if (!IsValidTimeout(CardReadTimeoutSecondsText, out int timeoutSeconds))
        {
            ResultMessageReady?.Invoke(this, "30초 이상 입력");
            return false;
        }

        // PRD §2.8.4 — 토글 ON이고 포트번호가 비어 있거나 1~255 밖이면 저장을 막는다. 타임아웃
        // 검증 다음에 둬서 기존 검증 순서(계획서 지시)를 바꾸지 않는다. 토글 OFF면 검증하지 않는다.
        if (SlipPrintEnabled && !IsValidPrinterPort(PrinterPort, out _))
        {
            ResultMessageReady?.Invoke(this, "입력값을 확인해주세요.");
            FocusPrinterPortRequested?.Invoke(this, EventArgs.Empty);
            return false;
        }

        var settings = new ShopSettings
        {
            VanMode = ToStorageValue(VanModeSelection),
            KioskId = KioskId,
            CardReadTimeoutSeconds = timeoutSeconds,
            AutoReboot = AutoReboot,
            AutoUpdate = AutoUpdate,
            KeyinDim = KeyinDim,
            SlipPrintEnabled = SlipPrintEnabled,
            PrinterSpeed = ToSpeedStorage(PrinterSpeedSelection),
            PrinterPort = PrinterPort,
        };

        try
        {
            _settingsService.Save(settings);
        }
        catch (Exception ex)
        {
            // PRD.md §2.6 — 저장 실패는 조용히 넘기지 않는다(사용자가 방금 한 조작이 반영되지
            // 않았음을 알아야 한다). View는 이 메시지를 받으면 창을 닫지 않는다.
            ResultMessageReady?.Invoke(this, $"설정을 저장하지 못했습니다.\n{ex.Message}");
            return false;
        }

        // 2026-09-02 Opus 리뷰(CP1) 개선권장 4 — PRD.md §1.5가 "설정 저장"을 로그 경계로 명시한다.
        // 여기 적는 값들(VAN Mode/타임아웃/키오스크ID/토글 3종)은 카드·PIN이 아니라 이미 §1.6.1이
        // 장애 봉투에 싣기로 확정한 값들이라(VAN Mode, KIOSK_ID) 그대로 로그에 남겨도 된다. 거래ID는
        // 이 맥락에 없으므로 null.
        FileLogger.Info(
            LogCategory.Settings,
            $"가맹점 설정 저장 — VAN_MODE={settings.VanMode}, KIOSK_ID='{settings.KioskId}', " +
            $"TIMEOUT={settings.CardReadTimeoutSeconds}, AUTO_REBOOT={settings.AutoReboot}, " +
            $"AUTO_UPDATE={settings.AutoUpdate}, KEYIN_DIM={settings.KeyinDim}, " +
            $"PRINTER_CHECK={settings.SlipPrintEnabled}, PRINTER_SPEED={settings.PrinterSpeed}, " +
            $"PRINTER='{settings.PrinterPort}'",
            code: null,
            transactionId: null);

        return true;
    }

    /// <summary>
    /// PRD §2.8.4 — 숫자만, 1~255 범위인지 검사한다. 빈 값/공백/비숫자/범위 밖 전부 실패.
    /// 2026-09-28 CP1 리뷰 L-1 — <c>int.TryParse</c>는 부호(<c>"+3"</c>)나 공백이 섞인 문자열도
    /// 통과시킨다. 입력칸이 ASCII 숫자만 받도록 이미 막혀 있어(<c>ShopSetupWindow.xaml.cs</c>) 정상
    /// 경로로는 그런 값이 들어올 수 없지만, 이 메서드 자체를 방어적으로 엄격하게 만들어 둔다 —
    /// 문자열이 전부 ASCII 0~9인지 먼저 확인한 뒤에만 파싱한다(<c>ShopSettingsService.ResolvePrinterPort</c>와
    /// 같은 판정 기준).
    /// </summary>
    private static bool IsValidPrinterPort(string text, out int port)
    {
        port = 0;
        if (string.IsNullOrEmpty(text) || !text.All(c => c >= '0' && c <= '9'))
            return false;

        if (!int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out port))
            return false;

        return port >= MinimumPrinterPort && port <= MaximumPrinterPort;
    }

    /// <summary>
    /// 직전거래 전표출력 버튼(PRD §2.8.5) — 실제 출력은 이번 범위 밖이다. 판단 기준은 레지스트리
    /// 저장값이 아니라 <b>지금 화면 입력칸의 값</b>이다(사용자 확정) — 저장 전이라도 입력칸에 값이
    /// 있으면 안내창을 띄운다. 토글 ON/OFF와 무관하다. 아무것도 저장하지 않는다.
    /// </summary>
    [RelayCommand]
    private void RequestLastSlipPrint()
    {
        if (string.IsNullOrWhiteSpace(PrinterPort))
        {
            ResultMessageReady?.Invoke(this, "프린터 포트번호를 입력해주세요.");
            return;
        }

        FileLogger.Info(LogCategory.Settings, "직전거래 전표출력 요청 — 출력 미구현, 안내만", code: null, transactionId: null);
        InfoMessageReady?.Invoke(this, "직전거래 전표를 출력합니다.");
    }
}
