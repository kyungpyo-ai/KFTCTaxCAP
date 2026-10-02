namespace KFTCTaxCAP.Protocol.Printer;

/// <summary>
/// <c>DLE EOT n</c> 실시간 상태 응답 1바이트 해석 결과(docs/receipt_print/escpos_reference.md §2).
/// <see cref="PrinterStatusParser.ParseOfflineCause"/>(n=2)와 <see cref="PrinterStatusParser.ParsePaperSensor"/>
/// (n=4)가 공통으로 쓰는 구조체다 — 각 메서드는 자신이 다루지 않는 필드를 기본값(false)으로 둔다(예:
/// <see cref="ParseOfflineCause"/> 결과의 <see cref="PaperEnd"/>/<see cref="PaperNearEnd"/>는 항상 false).
/// </summary>
public readonly struct PrinterStatus
{
    /// <summary>고정 비트 검사 통과 여부(<c>(b &amp; 0x93) == 0x12</c>). false면 "알 수 없는 응답"이다 — 나머지 필드는 신뢰할 수 없다.</summary>
    public bool IsValid { get; }

    /// <summary>커버(덮개) 열림(n=2, bit2).</summary>
    public bool CoverOpen { get; }

    /// <summary>프린터 오류 발생(n=2, bit6).</summary>
    public bool Error { get; }

    /// <summary>용지 끝(n=4, bit5·bit6).</summary>
    public bool PaperEnd { get; }

    /// <summary>용지 거의 끝, near-end(n=4, bit2·bit3).</summary>
    public bool PaperNearEnd { get; }

    public PrinterStatus(bool isValid, bool coverOpen, bool error, bool paperEnd, bool paperNearEnd)
    {
        IsValid = isValid;
        CoverOpen = coverOpen;
        Error = error;
        PaperEnd = paperEnd;
        PaperNearEnd = paperNearEnd;
    }
}

/// <summary>
/// <c>DLE EOT n</c> 응답 1바이트 파서(docs/receipt_print/escpos_reference.md §2). 순수 함수 — 포트 I/O는
/// <c>Services/Printer/</c>(P33-3)의 몫이다.
/// </summary>
public static class PrinterStatusParser
{
    /// <summary>고정 비트 마스크: bit0=0, bit1=1, bit4=1, bit7=0.</summary>
    private const byte FixedBitsMask = 0x93;

    /// <summary>고정 비트 기대값(마스크 적용 후).</summary>
    private const byte FixedBitsExpected = 0x12;

    private const byte CoverOpenBit = 0x04;   // bit2
    private const byte ErrorBit = 0x40;       // bit6
    private const byte PaperEndBits = 0x60;   // bit5, bit6
    private const byte PaperNearEndBits = 0x0C; // bit2, bit3

    private static bool IsValidStatusByte(byte b) => (b & FixedBitsMask) == FixedBitsExpected;

    /// <summary><c>DLE EOT 2</c>(오프라인 원인) 응답 해석 — 커버 열림·오류 비트만 쓴다.</summary>
    public static PrinterStatus ParseOfflineCause(byte statusByte)
    {
        bool isValid = IsValidStatusByte(statusByte);
        return new PrinterStatus(
            isValid: isValid,
            coverOpen: isValid && (statusByte & CoverOpenBit) != 0,
            error: isValid && (statusByte & ErrorBit) != 0,
            paperEnd: false,
            paperNearEnd: false);
    }

    /// <summary><c>DLE EOT 4</c>(롤 용지 센서) 응답 해석 — 용지 끝·거의 끝 비트만 쓴다.</summary>
    public static PrinterStatus ParsePaperSensor(byte statusByte)
    {
        bool isValid = IsValidStatusByte(statusByte);
        return new PrinterStatus(
            isValid: isValid,
            coverOpen: false,
            error: false,
            paperEnd: isValid && (statusByte & PaperEndBits) != 0,
            paperNearEnd: isValid && (statusByte & PaperNearEndBits) != 0);
    }
}
