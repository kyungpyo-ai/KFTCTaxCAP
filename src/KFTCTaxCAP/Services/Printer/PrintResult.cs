namespace KFTCTaxCAP.Services.Printer;

/// <summary>
/// <see cref="IReceiptPrinter.PrintAsync"/> 실패 사유(docs/receipt_print/PRD.md §5, development_plan.md
/// P33-3). 값 이름은 PRD §5 표의 행과 1:1로 대응한다.
/// </summary>
public enum PrintFailureReason
{
    /// <summary>성공(실패 없음).</summary>
    None,

    /// <summary>
    /// <b>InvalidPort = 포트·속도 설정 오류</b>(2026-09-30 CP1 리뷰 L-5) — 포트 번호가 숫자만으로
    /// 이뤄지지 않았거나(공백·부호·문자 혼입) 1~255 범위 밖, 또는 속도(BaudRate)가 0 이하. 어느
    /// 경우든 <see cref="System.IO.Ports.SerialPort"/>를 만들지 않는다.
    /// </summary>
    InvalidPort,

    /// <summary><c>SerialPort.Open()</c> 예외(없는 포트, 다른 프로그램이 점유 중 등).</summary>
    PortOpenFailed,

    /// <summary>상태 조회(<c>DLE EOT</c>) 응답 타임아웃, 또는 고정 비트가 맞지 않는 "알 수 없는 응답"
    /// (속도 불일치 시 흔함).</summary>
    NoResponse,

    /// <summary>용지 없음(<c>DLE EOT 4</c> 용지 끝 비트).</summary>
    PaperEnd,

    /// <summary>커버(덮개) 열림(<c>DLE EOT 2</c> 커버 비트).</summary>
    CoverOpen,

    /// <summary>프린터 자체 오류(<c>DLE EOT 2</c> 오류 비트).</summary>
    PrinterError,

    /// <summary>쓰기 타임아웃/예외, 또는 취소 토큰 취소(P33-3 — 별도 값을 두지 않고 이 값으로 합쳤다,
    /// <see cref="SerialReceiptPrinter"/> 클래스 요약 참고).</summary>
    WriteFailed,
}

/// <summary>
/// <see cref="IReceiptPrinter.PrintAsync"/>의 결과(docs/receipt_print/PRD.md §4.3, §5, development_plan.md
/// P33-3). 공개 메서드가 예외 대신 돌려주는 값 — 실패해도 호출자(결제 Flow)가 크래시하지 않는다.
/// </summary>
public sealed class PrintResult
{
    public bool IsSuccess { get; }

    public PrintFailureReason Failure { get; }

    /// <summary>성공했지만 용지가 거의 끝났다(near-end, PRD §5 — 실패가 아니라 WARN 대상).</summary>
    public bool PaperNearEnd { get; }

    private PrintResult(bool isSuccess, PrintFailureReason failure, bool paperNearEnd)
    {
        IsSuccess = isSuccess;
        Failure = failure;
        PaperNearEnd = paperNearEnd;
    }

    public static PrintResult Success(bool paperNearEnd = false) => new(true, PrintFailureReason.None, paperNearEnd);

    public static PrintResult Fail(PrintFailureReason failure) => new(false, failure, paperNearEnd: false);
}
