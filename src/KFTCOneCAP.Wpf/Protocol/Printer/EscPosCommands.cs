namespace KFTCOneCAP.Wpf.Protocol.Printer;

/// <summary>줄 정렬(<c>ESC a n</c>의 n).</summary>
public enum PrintAlignment
{
    Left = 0,
    Center = 1,
    Right = 2,
}

/// <summary>
/// <c>docs/receipt_print/escpos_reference.md</c> §1에 등재된 명령만 바이트로 만든다. 여기 없는 명령을
/// 새로 쓰려면 그 문서에 먼저 추가한다(receipt-printer-developer 원칙). 순수 함수 — I/O·상태 없음.
/// </summary>
public static class EscPosCommands
{
    /// <summary>초기화 <c>ESC @</c>(<c>1B 40</c>). 매 출력 시작 시 1회 — 이전 서식(배율·정렬) 초기화.</summary>
    public static byte[] Initialize() => new byte[] { 0x1B, 0x40 };

    /// <summary>정렬 <c>ESC a n</c>(<c>1B 61 n</c>). 줄 맨 앞에서만 유효.</summary>
    public static byte[] Align(PrintAlignment alignment) => new byte[] { 0x1B, 0x61, (byte)alignment };

    /// <summary>
    /// 문자 크기 <c>GS ! n</c>(<c>1D 21 n</c>). 보통 <c>00</c>, 가로·세로 2배는 <c>11</c>
    /// (상위 4비트 가로 배율-1, 하위 4비트 세로 배율-1).
    /// </summary>
    public static byte[] CharacterSize(bool doubleSize) => new byte[] { 0x1D, 0x21, (byte)(doubleSize ? 0x11 : 0x00) };

    /// <summary>굵게 <c>ESC E n</c>(<c>1B 45 n</c>). 이번 양식에서는 쓰지 않으나 예비로 둔다.</summary>
    public static byte[] Bold(bool on) => new byte[] { 0x1B, 0x45, (byte)(on ? 1 : 0) };

    /// <summary>2바이트 문자(한글) 모드 켜기 <c>FS &amp;</c>(<c>1C 26</c>).</summary>
    public static byte[] TwoByteCharacterModeOn() => new byte[] { 0x1C, 0x26 };

    /// <summary>2바이트 문자(한글) 모드 끄기 <c>FS .</c>(<c>1C 2E</c>).</summary>
    public static byte[] TwoByteCharacterModeOff() => new byte[] { 0x1C, 0x2E };

    /// <summary>
    /// 왼쪽 여백 <c>GS L nL nH</c>(<c>1D 4C nL nH</c>). 여백 = (nL + nH×256) × 가로 이동 단위. 줄 맨 앞에서만
    /// 유효. 값은 <see cref="PrinterProfile.LeftMarginDots"/>(escpos_reference.md §1, 2026-09-30 실기 확인).
    /// </summary>
    public static byte[] LeftMargin(ushort units) => new byte[] { 0x1D, 0x4C, (byte)(units & 0xFF), (byte)(units >> 8) };

    /// <summary>줄바꿈 <c>LF</c>(<c>0A</c>). 버퍼 인쇄 + 한 줄 이송.</summary>
    public const byte LineFeed = 0x0A;

    /// <summary>n줄 이송 <c>ESC d n</c>(<c>1B 64 n</c>). 커팅 전 여백.</summary>
    public static byte[] FeedLines(byte n) => new byte[] { 0x1B, 0x64, n };

    /// <summary>
    /// 이송 후 부분 커팅 <c>GS V m n</c>(<c>1D 56 42 n</c>, m=66(0x42) 고정). n 도트만큼 이송 후 부분
    /// 커팅. 커터 없는 모델이면 프린터가 무시한다.
    /// </summary>
    public static byte[] PartialCut(byte feedDots) => new byte[] { 0x1D, 0x56, 0x42, feedDots };

    /// <summary>실시간 상태 조회 <c>DLE EOT n</c>(<c>10 04 n</c>). 응답 1바이트(<see cref="PrinterStatusParser"/>).</summary>
    public static byte[] RealtimeStatus(byte n) => new byte[] { 0x10, 0x04, n };
}
