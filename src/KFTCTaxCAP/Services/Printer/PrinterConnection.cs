namespace KFTCTaxCAP.Services.Printer;

/// <summary>
/// 출력 대상 포트·속도(docs/receipt_print/PRD.md §4.3, development_plan.md P33-3). <b>설정을 직접 읽지
/// 않고 호출자가 넘긴다</b> — 자동 출력(저장된 설정, <c>ShopSettingsService.Load()</c>)과 재출력(화면
/// 입력값, PRD §4.2)이 서로 다른 값을 쓸 수 있기 때문이다(설정 캐시 금지 원칙, PRD §7.2와는 별개로
/// "누가 언제 읽는가"의 문제).
/// </summary>
public sealed class PrinterConnection
{
    /// <summary>포트 번호(숫자 문자열, 예: "5"). <c>"COM"</c> 접두는 붙이지 않는다 — 그건
    /// <see cref="SerialReceiptPrinter"/>가 조립한다.</summary>
    public string PortNumberText { get; }

    /// <summary>통신 속도(bps). 9600/38400/57600/115200 등(운영 PRD §2.8.2).</summary>
    public int BaudRate { get; }

    public PrinterConnection(string portNumberText, int baudRate)
    {
        PortNumberText = portNumberText ?? string.Empty;
        BaudRate = baudRate;
    }
}
