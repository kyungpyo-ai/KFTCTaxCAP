namespace KFTCTaxCAP.Services.Settings;

/// <summary>
/// 가맹점 설정 화면(Phase 23, docs/operations/development_plan.md P23-1)이 다루는 옵션 9개
/// (Phase 31, P31-1에서 전표 설정 3개 추가). 레지스트리의 반전 인코딩(AUTO_REBOOT/AUTO_UPDATE/
/// KEYIN_DIM: ON→"0", OFF→"1")과 "0=미설정" 규칙(<see cref="CardReadTimeoutSeconds"/>)은
/// <see cref="ShopSettingsService"/> 안에서만 다루고, 이 모델은 화면에 그대로 보여줄 값만 담는다
/// (docs/payment_relay/development_plan.md P7-1의 ReaderSettings와 동일한 패턴).
/// </summary>
public sealed class ShopSettings
{
    /// <summary>금융결제원 서버 — <c>FNAISCRDVAN</c>의 Mode 인자(PRD §2.2). "R"/"OT"/"IT" 중 하나.</summary>
    public string VanMode { get; set; } = "R";

    /// <summary>키오스크 고유번호(PRD §2.3). AN 20, 빈 값 허용.</summary>
    public string KioskId { get; set; } = string.Empty;

    /// <summary>
    /// 카드입력 타임아웃(초, PRD §2.4). <b>레지스트리 원본이 <c>0</c>이거나 값이 없으면 이미
    /// <c>120</c>으로 변환된 값</b> — 화면과 <c>PaymentOrchestrator</c>는 "0=미설정" 규칙을
    /// 모른다(<see cref="ShopSettingsService"/>에서만 처리).
    /// </summary>
    public int CardReadTimeoutSeconds { get; set; } = 120;

    /// <summary>자동 리부팅(PRD §2.5). 값 저장만 하고 실동작은 이번 범위 밖.</summary>
    public bool AutoReboot { get; set; } = true;

    /// <summary>자동 업데이트(PRD §2.5). 값 저장만.</summary>
    public bool AutoUpdate { get; set; }

    /// <summary>결제 화면 잠금(PRD §2.5). 값 저장만.</summary>
    public bool KeyinDim { get; set; }

    /// <summary>전표 인쇄 사용(PRD §2.8.2). ON="1"/OFF="0" — 같은 SERIALPORT 키의 AutoReboot 등과
    /// 달리 반전 인코딩이 아니다(인코딩은 <see cref="ShopSettingsService"/> 안에서만 다룬다).</summary>
    public bool SlipPrintEnabled { get; set; }

    /// <summary>프린터 속도(PRD §2.8.2, bps 단위). 저장값은 숫자만(<c>9600</c>/<c>38400</c>/
    /// <c>57600</c>/<c>115200</c>) — 화면 표시용 "bps" 접미사는 ViewModel 안에서만 붙이고 뗀다.</summary>
    public int PrinterSpeed { get; set; } = 57600;

    /// <summary>프린터 포트번호(PRD §2.8.2). 숫자 문자열(예: <c>"3"</c>) 또는 빈 값. <c>COM3</c> 같은
    /// 접두사 없이 숫자만 저장한다(2026-09-28 사용자 확정).</summary>
    public string PrinterPort { get; set; } = string.Empty;
}
