using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using KFTCTaxCAP.Protocol.Pos;
using KFTCTaxCAP.Services.Printer;
using KFTCTaxCAP.Services.Receipt;
using KFTCTaxCAP.Services.Settings;
using KFTCTaxCAP.Services.Storage;
using KFTCTaxCAP.ViewModels.Payment;

namespace KFTCTaxCAP.Services.Diagnostics;

/// <summary>
/// Phase 45 P45-4 개발용 시연 모드(<c>--response-band-demo</c>, 최종 산출물 아님) — 결제 화면을 전송 함수만 바꿔 끼운
/// ViewModel로 열어, 전송할 때마다 탭별 시나리오를 순환하며 응답코드 해설띠·배지를 눈으로 확인하게 한다.
/// 실제 소켓·리더기·VAN·프린터를 쓰지 않는다(영수증 출력은 가짜 프린터, 직전 영수증 DB는 임시 파일).
/// </summary>
internal static class ResponseBandDemo
{
    private const string Tag = "[response-band-demo]";

    /// <summary>탭(거래 구분 코드)별 순환 시나리오 — (#7, #27). <c>#27</c>은 800000에서만 쓰인다.</summary>
    private static readonly Dictionary<string, (string Code, string Detail)[]> Scenarios = new()
    {
        ["501008"] = new[] { ("000", ""), ("311", ""), ("030", ""), ("X12", "") },
        ["800000"] = new[]
        {
            ("000", ""), ("M01", "PAY502"), ("M01", "M2207"), ("M01", ""), ("M01", "Z999"), ("N01", ""),
        },
        ["902614"] = new[]
        {
            ("000", ""), ("V51", ""), ("VA8", ""), ("VZZ", ""), ("E03", ""), ("R02", ""), ("D02", ""),
        },
    };

    private static readonly Dictionary<string, int> Counters = new();

    /// <summary>시연용 ViewModel을 만든다(전송 함수 주입 + 가짜 프린터 + 임시 DB).</summary>
    public static PaymentScreenViewModel CreateViewModel()
    {
        var printer = new FakeReceiptPrinter(PrintResult.Success());
        var service = new ReceiptPrintService(
            () => new ShopSettings { SlipPrintEnabled = true, PrinterPort = "5", PrinterSpeed = 115200 }, printer);
        string dbPath = Path.Combine(Path.GetTempPath(), $"p45-demo-{Guid.NewGuid():N}.db");
        return new PaymentScreenViewModel(service, new LastReceiptStore(dbPath), Transport);
    }

    private static Task<byte[]> Transport(byte[] body, TimeSpan timeout)
    {
        PosRequestParseOutcome parsed = PosRequestTelegram.Parse(body);
        if (!parsed.IsSuccess || parsed.Telegram is null)
            throw new InvalidOperationException($"시연 전송이 요청을 파싱하지 못함: {parsed.ErrorCode}");

        string txType = parsed.Telegram.TransactionTypeCode;
        (string Code, string Detail)[] list = Scenarios[txType];
        Counters.TryGetValue(txType, out int index);
        Counters[txType] = index + 1;
        (string code, string detail) = list[index % list.Length];

        PosTelegram response = parsed.Telegram.Telegram.Clone();
        response.Write(3, "0210");
        response.Write(6, "C");
        response.Write(7, code);
        response.Write(8, DateTime.Now.ToString("yyMMddHHmmss"));
        if (txType == "800000")
            response.Write(PosResponseCodeCatalog.CardInfoDetailFieldNumber, detail);

        FileLogger.Info($"{Tag} {txType} 시나리오 {index % list.Length + 1}/{list.Length}: #7=\"{code}\"" +
            (txType == "800000" ? $", #27=\"{detail}\"" : string.Empty));
        return Task.FromResult(response.ToBody());
    }
}
