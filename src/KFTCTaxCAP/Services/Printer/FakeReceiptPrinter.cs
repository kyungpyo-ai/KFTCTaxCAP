using System.Threading;
using System.Threading.Tasks;

namespace KFTCTaxCAP.Services.Printer;

/// <summary>
/// 테스트용 <see cref="IReceiptPrinter"/> 구현 — 받은 payload/연결 정보를 보관하고 미리 지정한
/// <see cref="PrintResult"/>를 그대로 돌려준다(docs/receipt_print/development_plan.md P33-3). 실제
/// 포트를 열지 않으므로 하드웨어 없이 상위 계층(Phase 34 이후 영수증 조립·경고창 Flow)을 검증할 때
/// 쓴다. 파일 위치는 다른 <c>Fake*</c> 클래스가 모인 <c>Services/Diagnostics/</c>가 아니라
/// <c>Services/Printer/</c>에 뒀다 — development_plan.md P33-3 표가 이 경로로 명시했고, 이 타입이
/// 구현하는 <see cref="IReceiptPrinter"/>와 같은 폴더에 있는 편이 찾기 쉽다.
/// </summary>
public sealed class FakeReceiptPrinter : IReceiptPrinter
{
    private readonly PrintResult _result;

    /// <summary>가장 최근 호출에서 받은 payload(참조 그대로, 복사하지 않음).</summary>
    public byte[]? LastPayload { get; private set; }

    /// <summary>가장 최근 호출에서 받은 연결 정보.</summary>
    public PrinterConnection? LastConnection { get; private set; }

    /// <summary><see cref="PrintAsync"/> 호출 횟수.</summary>
    public int CallCount { get; private set; }

    public FakeReceiptPrinter(PrintResult result)
    {
        _result = result;
    }

    public Task<PrintResult> PrintAsync(byte[] payload, PrinterConnection connection, CancellationToken cancellationToken)
    {
        LastPayload = payload;
        LastConnection = connection;
        CallCount++;
        return Task.FromResult(_result);
    }
}
