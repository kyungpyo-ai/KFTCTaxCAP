using System.Threading;
using System.Threading.Tasks;

namespace KFTCOneCAP.Wpf.Services.Printer;

/// <summary>
/// 영수증 출력 서비스의 공개 계약(docs/receipt_print/PRD.md §7, development_plan.md P33-3). 구현체는
/// 예외를 던지지 않고 <see cref="PrintResult"/>로 성공/실패를 돌려준다 — 출력 실패가 결제 Flow로
/// 번지지 않게 하기 위한 계약이다(PRD §5 "거래 결과는 바뀌지 않는다").
/// </summary>
public interface IReceiptPrinter
{
    /// <summary>
    /// <paramref name="payload"/>(<c>EscPosDocumentEncoder.Encode</c> 결과)를 <paramref name="connection"/>이
    /// 가리키는 포트로 보낸다. 포트 열기~상태 조회~송신~닫기 전체를 백그라운드에서 수행한다(UI 스레드
    /// 비차단). 같은 프린터로 향하는 동시 호출은 구현체 내부에서 직렬화한다(PRD §4.3).
    /// </summary>
    Task<PrintResult> PrintAsync(byte[] payload, PrinterConnection connection, CancellationToken cancellationToken);
}
