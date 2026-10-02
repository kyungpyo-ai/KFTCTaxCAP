namespace KFTCTaxCAP.Services.Receipt;

/// <summary><see cref="ReceiptPrintService.PrintAsync"/>가 실제로 무엇을 했는지(docs/receipt_print/PRD.md §4.1, §5).</summary>
public enum ReceiptPrintStatus
{
    /// <summary>전표 인쇄 사용(설정)이 꺼져 있어 아무것도 하지 않았다.</summary>
    Skipped,

    /// <summary>출력 성공.</summary>
    Printed,

    /// <summary>조립·인코딩·출력 중 어딘가에서 실패했다 — 사유는 <see cref="ReceiptPrintOutcome.Failure"/>.</summary>
    Failed,
}

/// <summary>
/// <see cref="ReceiptPrintService.PrintAsync"/> 실패 사유(docs/receipt_print/PRD.md §5). 값 대부분은
/// <see cref="Printer.PrintFailureReason"/>과 1:1로 대응하고(그 값 이름을 그대로 옮김), 이 계층에서만
/// 생기는 실패 2가지(<see cref="CompositionFailed"/>/<see cref="Unexpected"/>)를 추가했다.
/// </summary>
public enum ReceiptPrintFailureReason
{
    None,
    InvalidPort,
    PortOpenFailed,
    NoResponse,
    PaperEnd,
    CoverOpen,
    PrinterError,
    WriteFailed,

    /// <summary>양식 조립(<see cref="NationalTaxReceiptComposer.Compose"/>) 또는 ESC/POS 인코딩 중 예외.</summary>
    CompositionFailed,

    /// <summary>위 어디에도 속하지 않는 예상치 못한 예외(공개 메서드가 예외를 던지지 않기 위한 최후 방어선).</summary>
    Unexpected,
}

/// <summary>
/// <see cref="ReceiptPrintService.PrintAsync"/>의 결과. 공개 메서드가 예외 대신 돌려주는 값 —
/// 실패해도 호출자(결제창 ViewModel)가 크래시하지 않는다(docs/receipt_print/PRD.md §5).
/// </summary>
public sealed class ReceiptPrintOutcome
{
    public ReceiptPrintStatus Status { get; }

    public ReceiptPrintFailureReason Failure { get; }

    /// <summary>성공했지만 용지가 거의 끝났다(near-end, PRD §5 — 실패가 아니라 WARN 대상).</summary>
    public bool PaperNearEnd { get; }

    private ReceiptPrintOutcome(ReceiptPrintStatus status, ReceiptPrintFailureReason failure, bool paperNearEnd)
    {
        Status = status;
        Failure = failure;
        PaperNearEnd = paperNearEnd;
    }

    public static ReceiptPrintOutcome Skipped() => new(ReceiptPrintStatus.Skipped, ReceiptPrintFailureReason.None, paperNearEnd: false);

    public static ReceiptPrintOutcome Printed(bool paperNearEnd) => new(ReceiptPrintStatus.Printed, ReceiptPrintFailureReason.None, paperNearEnd);

    public static ReceiptPrintOutcome Failed(ReceiptPrintFailureReason reason) => new(ReceiptPrintStatus.Failed, reason, paperNearEnd: false);
}
