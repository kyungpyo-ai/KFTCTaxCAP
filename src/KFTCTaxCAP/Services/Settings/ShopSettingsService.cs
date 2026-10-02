using System;
using System.Collections.Generic;
using Microsoft.Win32;
using KFTCTaxCAP.Services.Diagnostics;

namespace KFTCTaxCAP.Services.Settings;

/// <summary>
/// 가맹점 설정(HKCU\Software\KFTC_VAN\KFTCTaxCAP\TCP, ...\SERIALPORT) 접근 계층.
/// Phase 23(docs/operations/development_plan.md P23-1) — <see cref="ReaderSettingsService"/>와 같은
/// 패턴이다: 읽기 실패는 예외를 던지지 않고 기본값으로 폴백하고(PRD §0.3), 반전 인코딩은 이 클래스
/// 안에서만 다룬다.
///
/// <b>저장 위치가 두 하위 키에 걸쳐 있다</b>(2026-09-02 최종 확정) — <c>VAN_MODE</c>/<c>KIOSK_ID</c>는
/// <c>TCP</c>, <c>TIMEOUT</c>/<c>AUTO_REBOOT</c>/<c>AUTO_UPDATE</c>/<c>KEYIN_DIM</c>은
/// <c>SERIALPORT</c>다. <c>KIOSK_ID</c>만 원본 MFC에 없던 신규 항목이라 VAN Mode와 같은 `TCP`로
/// 모았다 — 코드 수정 시 이 매핑을 추측하지 말고 PRD.md §2.2~§2.5 표를 확인한다.
///
/// <b>이 클래스 밖으로 새어 나가면 안 되는 것 4가지</b>(PRD §2.2/§2.4/§2.5/§2.8.2):
/// <list type="bullet">
/// <item>AUTO_REBOOT/AUTO_UPDATE/KEYIN_DIM의 반전 인코딩(ON→"0", OFF→"1"). <b>단, 같은
/// SERIALPORT 키의 PRINTER_CHECK(전표 인쇄 사용, Phase 31)는 반전이 아니다</b> — ON="1"/OFF="0"이
/// 원본(SlipSetupDlg.cpp:517,563)부터 그렇다. 옆의 세 값을 복사해 반전으로 구현하면 틀린다.</item>
/// <item><b>카드입력 타임아웃의 "0=미설정" 규칙</b> — 레지스트리 값이 <c>0</c>이거나 아예 없으면
/// <see cref="Load"/>가 <c>120</c>을 돌려준다(2026-09-01 사용자 확정, PRD §2.4). 무제한이 아니다.
/// <c>Save</c>는 이 변환을 하지 않는다 — 사용자가 입력한 값을 그대로 쓴다(다시 열었을 때 방금 입력한
/// <c>0</c>이 그대로 보여야 한다).</item>
/// <item>레지스트리에 손으로 써넣은 이상값 처리 — <c>TIMEOUT</c>이 숫자가 아니거나 1~29(화면 검증이
/// 막는 범위), <c>VAN_MODE</c>가 R/OT/IT 외의 값, <c>KIOSK_ID</c>가 20자 초과인 경우 각각 안전한
/// 기본값으로 폴백하고 <c>WARN</c> 로그를 남긴다(이 계획서 P23-1 판단 — PRD에 명시 없음). Phase 31의
/// PRINTER_SPEED/PRINTER도 같은 원칙(허용 목록/범위 밖 → 폴백 + WARN).</item>
/// <item><b>프린터 속도의 "bps" 표시 접미사</b> — 레지스트리에는 숫자만 저장한다(PRD §2.8.2). "bps"를
/// 붙이고 떼는 것은 <c>ShopSetupViewModel</c>의 책임이고, 이 서비스와 <see cref="ShopSettings"/>는
/// <c>int</c>만 다룬다.</item>
/// </list>
///
/// WPF 타입에 의존하지 않는다(ViewModels → Services → Protocol → Interop 계층 규칙).
///
/// <para><b>반복 WARN 로그 억제(2026-09-02 CP2 — Opus 리뷰(CP1) 개선권장 8 해결)</b> — <c>PaymentOrchestrator</c>가
/// 거래마다(902614 한 건당 최대 3회) <see cref="Load"/>를 호출하게 되면서(P23-6/P23-7, 설정값 캐시
/// 금지 PRD §2.6) 이상값이 바뀌지 않았는데도 같은 WARN이 거래마다 반복 출력될 위험이 실제로 생겼다.
/// <see cref="PaymentOrchestrator"/>/<see cref="Van.VanService"/> 둘 다 생성자에서 <c>new
/// ShopSettingsService().Load</c>를 메서드 그룹으로 한 번만 바인딩해 앱 수명 동안 <b>같은
/// <see cref="ShopSettingsService"/> 인스턴스를 재사용</b>한다(그 두 클래스 자체가 앱 수명 싱글턴으로
/// 한 번만 생성됨, <c>App.xaml.cs</c>) — 따라서 인스턴스 필드에 "마지막으로 로깅한 이상값(raw)"을
/// 3개(VAN_MODE/KIOSK_ID/TIMEOUT 각각) 기억해뒀다가, 같은 이상값이면 WARN을 다시 찍지 않고 값이
/// 바뀔 때만(정상값으로 복구된 뒤 다시 같은 이상값이 나타나는 경우도 포함) 다시 찍는다.</para>
/// </summary>
public sealed class ShopSettingsService
{
    private const string TcpKeyPath = @"Software\KFTC_VAN\KFTCTaxCAP\TCP";
    private const string SerialPortKeyPath = @"Software\KFTC_VAN\KFTCTaxCAP\SERIALPORT";

    private const int DefaultCardReadTimeoutSeconds = 120;
    private const int MinimumConfigurableTimeoutSeconds = 30;

    private const int DefaultPrinterSpeed = 57600;
    // 2026-09-28 CP1 리뷰 L-2 — 범위(1~255) 검사는 이 서비스에서 하지 않기로 해(ResolvePrinterPort
    // 주석 참고) MinimumPrinterPort/MaximumPrinterPort 상수를 제거했다. 범위 검사는
    // ShopSetupViewModel의 동명 상수만 쓴다(토글 ON 저장 시점 전용).

    /// <summary>PRD §2.8.2 — 프린터 속도 허용 목록 4개. 서비스에 한 번만 정의하고
    /// <c>ShopSetupViewModel</c>은 이 목록을 그대로 써서 콤보 항목을 만든다(원본 SlipSetupDlg.cpp:209-212와
    /// 동일 순서).</summary>
    public static IReadOnlyList<int> AllowedPrinterSpeeds { get; } = new[] { 9600, 38400, 57600, 115200 };

    // 개선권장 2(CP2 Opus 리뷰) — 직전에 WARN으로 남긴 이상값(raw) 3종. null이면 "아직 이 이상값으로
    // 경고한 적 없음"을 뜻한다. 정상값으로 돌아오면 각 Resolve*가 이 필드를 다시 null로 되돌려, 같은
    // 이상값이 나중에 재발해도 다시 한번 WARN이 찍히게 한다(완전 무음이 되지 않도록).
    private string? _lastWarnedVanModeRaw;
    private string? _lastWarnedKioskIdRaw;
    private string? _lastWarnedTimeoutRaw;

    // Phase 31(P31-1) — 전표 설정 이상값 2종(PRINTER_SPEED/PRINTER)도 같은 원칙으로 반복 WARN을 억제한다.
    // PRINTER_CHECK는 원본(:517)과 동일하게 "1"만 ON, 그 외는 전부 OFF로 조용히 처리하므로 WARN이 없다.
    private string? _lastWarnedPrinterSpeedRaw;
    private string? _lastWarnedPrinterPortRaw;

    public ShopSettings Load()
    {
        string? vanMode = null;
        string? kioskId = null;
        string? timeoutText = null;
        string? autoReboot = null;
        string? autoUpdate = null;
        string? keyinDim = null;
        string? printerCheck = null;
        string? printerSpeedText = null;
        string? printerPortText = null;

        try
        {
            using var tcpKey = Registry.CurrentUser.OpenSubKey(TcpKeyPath);
            if (tcpKey != null)
            {
                vanMode = tcpKey.GetValue("VAN_MODE") as string;
                kioskId = tcpKey.GetValue("KIOSK_ID") as string;
            }

            using var serialKey = Registry.CurrentUser.OpenSubKey(SerialPortKeyPath);
            if (serialKey != null)
            {
                timeoutText = serialKey.GetValue("TIMEOUT") as string;
                autoReboot = serialKey.GetValue("AUTO_REBOOT") as string;
                autoUpdate = serialKey.GetValue("AUTO_UPDATE") as string;
                keyinDim = serialKey.GetValue("KEYIN_DIM") as string;
                printerCheck = serialKey.GetValue("PRINTER_CHECK") as string;
                printerSpeedText = serialKey.GetValue("PRINTER_SPEED") as string;
                printerPortText = serialKey.GetValue("PRINTER") as string;
            }
        }
        catch
        {
            // 레지스트리 접근 실패(권한 등) — 아래 기본값 폴백으로 조용히 무시한다(ReaderSettingsService와 동일 원칙).
        }

        return new ShopSettings
        {
            VanMode = ResolveVanMode(vanMode),
            KioskId = ResolveKioskId(kioskId),
            CardReadTimeoutSeconds = ResolveCardReadTimeoutSeconds(timeoutText),
            AutoReboot = autoReboot != "1",
            AutoUpdate = autoUpdate == "0",
            KeyinDim = keyinDim == "0",
            // PRD §2.8.2 — "1"만 ON, 그 외(값 없음 포함)는 전부 OFF. 원본(:517)과 동일하게 WARN 없음.
            SlipPrintEnabled = printerCheck == "1",
            PrinterSpeed = ResolvePrinterSpeed(printerSpeedText),
            PrinterPort = ResolvePrinterPort(printerPortText),
        };
    }

    /// <summary>
    /// 2026-09-02 Opus 리뷰(CP1) 개선권장 6 — <c>CreateSubKey</c>가 <c>null</c>을 반환하는 극단적
    /// 권한 문제 상황에서 예전엔 조용히 <c>return</c>했다. <c>TCP</c> 키는 성공하고
    /// <c>SERIALPORT</c> 키만 실패하면 절반만 저장된 채 호출부(<c>ShopSetupViewModel.TryConfirm</c>)가
    /// 성공으로 착각해 창을 닫을 위험이 있었다(PRD.md §2.6 "저장 실패는 조용히 넘기지 않는다" 위반).
    /// <see cref="ReaderSettingsService.Save"/>와 계약을 맞춰 예외를 던진다 — 호출부는 이미
    /// <c>try/catch</c>로 저장 실패를 사용자에게 알리고 있다(<see cref="ShopSetupViewModel.TryConfirm"/>).
    /// </summary>
    public void Save(ShopSettings settings)
    {
        using var tcpKey = Registry.CurrentUser.CreateSubKey(TcpKeyPath);
        if (tcpKey is null)
            throw new InvalidOperationException($@"레지스트리 키를 생성하지 못했습니다: HKCU\{TcpKeyPath}");

        tcpKey.SetValue("VAN_MODE", settings.VanMode, RegistryValueKind.String);
        tcpKey.SetValue("KIOSK_ID", settings.KioskId, RegistryValueKind.String);

        using var serialKey = Registry.CurrentUser.CreateSubKey(SerialPortKeyPath);
        if (serialKey is null)
            throw new InvalidOperationException($@"레지스트리 키를 생성하지 못했습니다: HKCU\{SerialPortKeyPath}");

        // 입력값을 그대로 쓴다 — 0은 0으로 저장한다(Load()에서만 120으로 해석한다).
        serialKey.SetValue("TIMEOUT", settings.CardReadTimeoutSeconds.ToString(), RegistryValueKind.String);
        serialKey.SetValue("AUTO_REBOOT", settings.AutoReboot ? "0" : "1", RegistryValueKind.String);
        serialKey.SetValue("AUTO_UPDATE", settings.AutoUpdate ? "0" : "1", RegistryValueKind.String);
        serialKey.SetValue("KEYIN_DIM", settings.KeyinDim ? "0" : "1", RegistryValueKind.String);

        // PRD §2.8.2 — PRINTER_CHECK는 ON="1"/OFF="0"(반전 아님, 위 세 값과 다름). 검증(범위 등)은
        // ShopSetupViewModel 책임이고, 이 서비스는 받은 값을 그대로 쓴다(KioskId와 같은 분담).
        serialKey.SetValue("PRINTER_CHECK", settings.SlipPrintEnabled ? "1" : "0", RegistryValueKind.String);
        serialKey.SetValue("PRINTER_SPEED", settings.PrinterSpeed.ToString(), RegistryValueKind.String);
        serialKey.SetValue("PRINTER", settings.PrinterPort, RegistryValueKind.String);
    }

    /// <summary>PRD §2.2 이상값 폴백 — 알 수 없는 Mode는 운영("R")로 폴백한다. 테스트 서버로 조용히
    /// 붙는 것보다, 설정이 깨졌을 때 운영으로 가는 편이 "결제가 되긴 한다"는 관점에서 안전하다.
    /// 개선권장 2(CP2) — 같은 이상값이면 WARN을 반복하지 않는다(클래스 요약 참고).</summary>
    private string ResolveVanMode(string? raw)
    {
        if (raw is "R" or "OT" or "IT")
        {
            _lastWarnedVanModeRaw = null; // 정상값으로 복구 — 다음 이상값은 새로 경고한다.
            return raw;
        }

        if (!string.IsNullOrEmpty(raw) && raw != _lastWarnedVanModeRaw)
        {
            FileLogger.Warn(LogCategory.Settings, $"[ShopSettingsService] VAN_MODE 이상값 '{raw}' — 운영(R)으로 폴백", code: null, transactionId: null);
            _lastWarnedVanModeRaw = raw;
        }

        return "R";
    }

    /// <summary>
    /// 2026-09-02 Opus 리뷰(CP1) 개선권장 5 — PRD.md §2.3.2가 "설정값이 비어 있어도 거부(E06)"로
    /// 재확정되면서, 이 폴백의 실제 효과가 바뀌었다. 20자를 넘는 값을 그대로 써도 AN(20) SPEC과 맞을
    /// 수 없어 어차피 결제는 거부된다 — 그 점은 폴백 이전과 동일하다. 이 폴백이 막는 것은 더 이상
    /// "거부 자체"가 아니라 "거부 이유가 불명확해지는 것"이다: 폴백이 없으면 21자짜리 값이 그대로
    /// 저장되고, 매 거래마다 그 이상한 값과 정상적인 `#42` 수신값이 계속 불일치로 거부되는데 현장에서
    /// 그 원인이 "레지스트리에 잘못된 값이 들어있다"는 사실과 바로 연결되지 않는다. 빈 값으로
    /// 취급하면 §2.3.2의 "빈 값은 거부" 규칙 하나로 통일되고, 관리자가 20자 넘는 값을 실수로
    /// 넣었다는 사실 자체는 아래 WARN 로그로 남으므로 현장에서 원인 추적은 여전히 가능하다.
    /// (동작 자체는 그대로다 — 이 주석만 최신 정책에 맞춰 정정했다.)
    /// </summary>
    private string ResolveKioskId(string? raw)
    {
        // net48 BCL의 string.IsNullOrEmpty에는 NotNullWhen 어노테이션이 없어 null 가능성 분석이
        // 이어지지 않는다 — is null 패턴으로 직접 좁혀 CS8602 경고를 없앤다(LogLineRenderer와 동일 패턴).
        if (raw is null || raw.Length == 0)
        {
            _lastWarnedKioskIdRaw = null; // 정상값(빈 값)으로 복구.
            return string.Empty;
        }

        if (raw.Length > 20)
        {
            if (raw != _lastWarnedKioskIdRaw)
            {
                FileLogger.Warn(LogCategory.Settings, $"[ShopSettingsService] KIOSK_ID 길이 초과({raw.Length}자) — 빈 값으로 취급(검증 미수행)", code: null, transactionId: null);
                _lastWarnedKioskIdRaw = raw;
            }

            return string.Empty;
        }

        _lastWarnedKioskIdRaw = null; // 정상값으로 복구.
        return raw;
    }

    /// <summary>PRD §2.4 — "0" 또는 값 없음은 120초로 해석한다(무제한 아님, 2026-09-01 확정).
    /// 숫자가 아니거나 화면 검증이 막는 1~29 범위의 이상값도 120으로 폴백한다. 개선권장 2(CP2) —
    /// 두 이상값 분기(비숫자/음수, 1~29 범위)가 같은 억제 필드(<see cref="_lastWarnedTimeoutRaw"/>)를
    /// 공유한다 — raw 텍스트가 같으면 같은 이상값으로 취급한다.</summary>
    private int ResolveCardReadTimeoutSeconds(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            _lastWarnedTimeoutRaw = null; // 정상값(미설정)으로 복구.
            return DefaultCardReadTimeoutSeconds;
        }

        if (!int.TryParse(raw, out int seconds) || seconds < 0)
        {
            if (raw != _lastWarnedTimeoutRaw)
            {
                FileLogger.Warn(LogCategory.Settings, $"[ShopSettingsService] TIMEOUT 이상값 '{raw}' — {DefaultCardReadTimeoutSeconds}초로 폴백", code: null, transactionId: null);
                _lastWarnedTimeoutRaw = raw;
            }

            return DefaultCardReadTimeoutSeconds;
        }

        if (seconds == 0)
        {
            _lastWarnedTimeoutRaw = null; // 정상값(0=미설정)으로 복구.
            return DefaultCardReadTimeoutSeconds;
        }

        if (seconds < MinimumConfigurableTimeoutSeconds)
        {
            if (raw != _lastWarnedTimeoutRaw)
            {
                FileLogger.Warn(LogCategory.Settings, $"[ShopSettingsService] TIMEOUT 이상값 {seconds}초(30 미만) — {DefaultCardReadTimeoutSeconds}초로 폴백", code: null, transactionId: null);
                _lastWarnedTimeoutRaw = raw;
            }

            return DefaultCardReadTimeoutSeconds;
        }

        _lastWarnedTimeoutRaw = null; // 정상값으로 복구.
        return seconds;
    }

    /// <summary>PRD §2.8.2 — 허용 목록 4개 중 하나가 아니면 57600으로 폴백한다(원본 SlipSetupDlg.cpp:544
    /// 와 동일 기본값). 값이 없을 때도 조용히(WARN 없이) 57600을 쓴다 — 이상값(값이 있는데 목록에
    /// 없는 경우)만 WARN 대상이다.</summary>
    private int ResolvePrinterSpeed(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            _lastWarnedPrinterSpeedRaw = null; // 정상값(미설정)으로 복구.
            return DefaultPrinterSpeed;
        }

        foreach (var allowed in AllowedPrinterSpeeds)
        {
            if (raw == allowed.ToString())
            {
                _lastWarnedPrinterSpeedRaw = null; // 정상값으로 복구.
                return allowed;
            }
        }

        if (raw != _lastWarnedPrinterSpeedRaw)
        {
            FileLogger.Warn(LogCategory.Settings, $"[ShopSettingsService] PRINTER_SPEED 이상값 '{raw}' — {DefaultPrinterSpeed}로 폴백", code: null, transactionId: null);
            _lastWarnedPrinterSpeedRaw = raw;
        }

        return DefaultPrinterSpeed;
    }

    /// <summary>
    /// PRD §2.8.2 — 값이 숫자로만 이뤄졌는지만 검사한다("COM3" 같은 비숫자는 빈 값으로 폴백). **1~255
    /// 범위는 여기서 검사하지 않는다**(2026-09-28 CP1 리뷰 L-2, 사용자 결정 "포트는 사용자가 적는
    /// 것"). 토글 OFF 상태에서는 §2.8.3에 따라 <see cref="ShopSetupViewModel.TryConfirm"/>이 검증 없이
    /// 그대로 저장하므로, `999`나 `0`처럼 범위 밖인데도 정상적으로 저장된 값이 있을 수 있다 — 그
    /// 값을 여기서 "이상값"으로 오인해 지우면 사용자가 방금 저장한 값이 다음에 열었을 때 사라지는
    /// 모순이 생긴다(CP1 L-2). 범위 검사는 토글 ON 상태에서 저장할 때만
    /// <see cref="ShopSetupViewModel"/>의 <c>IsValidPrinterPort</c>가 한다 — 이 메서드는 그 대칭이
    /// 아니라 "레지스트리 값이 숫자 문자열인가"만 보는 순수성 검사다.
    ///
    /// 숫자 판정은 <c>int.TryParse</c>가 아니라 **ASCII 0~9로만 이뤄졌는지 직접** 확인한다
    /// (2026-09-28 CP1 리뷰 L-1) — <c>int.TryParse</c>는 부호(<c>"+3"</c>)나 공백(<c>" 3"</c>)이
    /// 섞인 문자열도 통과시켜, 입력 단계에서는 절대 나올 수 없는 형태가 화면에 그대로 노출되는
    /// 문제가 있었다. 값이 원래 없던 경우(빈 값)는 WARN 없이 조용히 처리한다(다른 이상값 폴백과
    /// 동일 원칙).
    /// </summary>
    private string ResolvePrinterPort(string? raw)
    {
        // net48 BCL의 string.IsNullOrEmpty에는 NotNullWhen 어노테이션이 없어 null 가능성 분석이
        // 이어지지 않는다 — is null 패턴으로 직접 좁힌다(ResolveKioskId와 동일 패턴, CS8603 방지).
        if (raw is null || raw.Length == 0)
        {
            _lastWarnedPrinterPortRaw = null; // 정상값(미설정)으로 복구.
            return string.Empty;
        }

        bool isAllAsciiDigits = true;
        foreach (char c in raw)
        {
            if (c < '0' || c > '9')
            {
                isAllAsciiDigits = false;
                break;
            }
        }

        if (isAllAsciiDigits)
        {
            _lastWarnedPrinterPortRaw = null; // 정상값으로 복구.
            return raw;
        }

        if (raw != _lastWarnedPrinterPortRaw)
        {
            FileLogger.Warn(LogCategory.Settings, $"[ShopSettingsService] PRINTER 이상값 '{raw}' — 빈 값으로 폴백", code: null, transactionId: null);
            _lastWarnedPrinterPortRaw = raw;
        }

        return string.Empty;
    }
}
