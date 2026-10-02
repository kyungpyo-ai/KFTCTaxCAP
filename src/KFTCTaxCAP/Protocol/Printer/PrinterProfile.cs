namespace KFTCTaxCAP.Protocol.Printer;

/// <summary>
/// Epson 호환 영수증 프린터 기종마다 달라질 수 있는 값 4가지를 한 곳에 모은다
/// (docs/receipt_print/development_plan.md Phase 33 착수 전 확정 사항 6). 실기(P33-0/P33-5) 결과가
/// 아래 가정과 다르면 <b>이 클래스만</b> 고치면 된다 — <c>Protocol/Printer/</c>의 다른 코드는 명령
/// 바이트 값을 직접 하드코딩하지 않고 이 상수만 참조한다.
///
/// 근거는 <c>docs/receipt_print/escpos_reference.md</c>다. 실기 확인 전까지는 (E) 등급(Epson ESC/POS
/// 공개 명령 체계의 일반 정의) 가정값이다.
/// </summary>
public static class PrinterProfile
{
    /// <summary>
    /// 한 줄 칸 수(CP949 바이트 기준, Font A). PRD §3.1·escpos_reference.md §4 — 샘플 역산 42.
    /// 2배 크기 줄의 폭은 이 값의 절반(<see cref="ReceiptTextLayout"/>이 계산).
    /// </summary>
    public const int LineWidth = 42;

    // 아래 5개는 P33-3(체크포인트 지적)에서 static readonly로 바꿨다 — const bool을 false로 바꾸면
    // 그 값을 참조하는 if 분기가 컴파일 타임에 항상 거짓으로 확정돼 반대쪽 분기가 CS0162(도달할 수
    // 없는 코드) 경고를 낸다. "이 한 곳만 고치면 된다"는 이 클래스의 존재 이유와 어긋나므로, 조건 분기에
    // 쓰이는 값은 모두 static readonly로 둔다. LineWidth(위)는 조건이 아니라 산술(폭 나눗셈)에만 쓰여
    // 이 문제가 없고, <see cref="ReceiptTextLayout.LineWidth"/>가 이 값을 const로 그대로 옮겨 쓰고
    // 있어(컴파일 타임 상수 전파) const로 유지한다.

    /// <summary>
    /// 초기화(<c>ESC @</c>) 직후 2바이트 문자 모드(<c>FS &amp;</c>, <c>1C 26</c>)를 명시적으로 켤지 여부.
    /// 한국어 펌웨어 모델은 기본 켜짐일 수 있으나(escpos_reference.md §1), 켜져 있어도 다시 켜는 것은
    /// 무해하므로 기본 true. 실기에서 이 명령이 오히려 오동작을 일으키면 false로 바꾼다.
    /// </summary>
    public static readonly bool SendTwoByteCharacterModeOn = true;

    /// <summary>
    /// 이송 후 부분 커팅(<c>GS V 66 n</c>) 명령 사용 여부. 커터가 없는 모델이면 false로 바꿔 문서 끝에
    /// 이송·커팅 명령 자체를 생략한다(<see cref="EscPosDocumentEncoder"/>가 참조).
    /// </summary>
    public static readonly bool UseCutting = true;

    /// <summary>
    /// 커팅 전 줄 이송 수(<c>ESC d n</c>의 n). 본문 마지막 줄과 커팅 위치 사이 여백. 실기에서 너무 짧아
    /// 절취선이 문구를 침범하면 이 값을 늘린다.
    /// </summary>
    public static readonly byte FeedLinesBeforeCut = 3;

    /// <summary>
    /// 부분 커팅 직전 추가 이송 도트 수(<c>GS V 66 n</c>의 n). <see cref="FeedLinesBeforeCut"/>로 이미
    /// 줄 단위 여백을 뒀으므로 기본 0(도트 단위 미세 조정이 필요하면 실기에서 조정).
    /// </summary>
    public static readonly byte CutFeedDots = 0;

    /// <summary>
    /// 왼쪽 여백(<c>GS L</c>의 값, 프린터 가로 이동 단위). 42칸(약 63mm) 인쇄 영역이 80mm 용지의 왼쪽에 붙어
    /// 오른쪽만 비어 보이던 것을 가운데로 옮긴다. <b>2026-09-30 실기(COM6)에서 0/24/36/48/72를 찍어 비교 —
    /// 36이 양쪽 여백이 가장 고르고 42칸이 한 줄에 그대로 들어감(사용자 확인).</b> 0이면 명령을 보내지 않는다.
    /// 기종이 바뀌면 이 값만 다시 맞춘다.
    /// </summary>
    public static readonly ushort LeftMarginDots = 36;

    /// <summary>
    /// 실시간 상태 조회(<c>DLE EOT n</c>) 사용 여부. 일부 호환 기종은 이 명령을 지원하지 않는다
    /// (escpos_reference.md §2) — 미지원이면 false로 바꿔 Services/Printer의 시리얼 출력 서비스가
    /// 상태 조회를 건너뛰고 바로 송신하게 한다(P33-3).
    ///
    /// <b>2026-09-30 실기(P33-0) 결과 false</b> — 개발 PC의 프린터(COM6, 115200bps)는 출력은 정상이나
    /// <c>DLE EOT 1/2/4</c>에 응답이 전혀 없고 CTS/DSR/CD도 모두 꺼져 있다(속도 5종·DTR/RTS 조합 전부 동일).
    /// 켜 두면 모든 출력이 NoResponse로 실패하므로 사용자 결정으로 끈다. 코드는 지원 기종용으로 남긴다.
    /// </summary>
    public static readonly bool UseStatusQuery = false;
}
