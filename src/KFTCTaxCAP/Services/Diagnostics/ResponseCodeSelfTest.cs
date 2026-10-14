using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using KFTCTaxCAP.Protocol.Pos;
using KFTCTaxCAP.Services.Printer;
using KFTCTaxCAP.Services.Receipt;
using KFTCTaxCAP.Services.Settings;
using KFTCTaxCAP.Services.Storage;
using KFTCTaxCAP.ViewModels.Payment;
using Microsoft.Data.Sqlite;

namespace KFTCTaxCAP.Services.Diagnostics;

/// <summary>
/// Phase 45 P45-6(docs/payment_relay/development_plan.md "Phase 45 실행계획서") 자가진단 — 응답코드 해설 표
/// (<see cref="PosResponseCodeCatalog"/>), 로그 설명 줄(<see cref="LogCodeCatalog"/>/<see cref="FileLogSink.BuildCodeNoteBytes"/>),
/// 결제창 해설띠·배지(<see cref="PaymentTelegramTabViewModel"/>)를 확인한다. 소켓·리더기·VAN·UI 없이 돈다.
/// <c>App.xaml.cs</c>가 <c>--response-code-test</c> 인자로 실행될 때만 <see cref="RunAll"/>을 호출한다.
/// </summary>
internal static class ResponseCodeSelfTest
{
    private const string Tag = "[response-code-test]";

    private static int _pass;
    private static int _fail;

    public static bool RunAll()
    {
        _pass = 0;
        _fail = 0;
        try
        {
            FileLogger.Info($"{Tag} 시작");
            RunCounts();
            RunSamples();
            RunClassification();
            RunSuccessAndDetail();
            RunLog();
            RunViewModel();
        }
        catch (Exception ex)
        {
            _fail++;
            FileLogger.Error($"{Tag} 예외로 중단: {ex}");
        }

        bool ok = _fail == 0;
        FileLogger.Info($"{Tag} 완료 — 통과 {_pass} / 전체 {_pass + _fail}, 실패 {_fail}, 종합={(ok ? "통과" : "실패")}");
        return ok;
    }

    private static void Check(string name, bool ok, string? detail = null)
    {
        if (ok)
        {
            _pass++;
            FileLogger.Info($"{Tag} [PASS] {name}");
        }
        else
        {
            _fail++;
            FileLogger.Error(LogCategory.App, $"{Tag} [FAIL] {name}{(detail is null ? string.Empty : " — " + detail)}");
        }
    }

    private static void CheckEq(string name, string? expected, string? actual) =>
        Check(name, string.Equals(expected, actual, StringComparison.Ordinal), $"기대=\"{expected}\" 실제=\"{actual}\"");

    // ------------------------------------------------------------------
    // 1. 개수
    // ------------------------------------------------------------------
    private static void RunCounts()
    {
        Check("개수 공통 8", PosResponseCodeCatalog.CommonCount == 8, PosResponseCodeCatalog.CommonCount.ToString());
        // SPEC 3장 1) 표(p.23~24) 인터넷지로 코드 32개: 311~315, 321, 324, 325, 331, 332, 334~345, 360~369.
        Check("개수 인터넷지로 32", PosResponseCodeCatalog.InternetGiroCount == 32, PosResponseCodeCatalog.InternetGiroCount.ToString());
        Check("개수 중계 3", PosResponseCodeCatalog.KftcVanRelayCount == 3, PosResponseCodeCatalog.KftcVanRelayCount.ToString());
        Check("개수 원캡 E 12", PosResponseCodeCatalog.OneCapCount == 12, PosResponseCodeCatalog.OneCapCount.ToString());
        Check("개수 원캡 R 33", PosResponseCodeCatalog.OneCapReaderCount == 33, PosResponseCodeCatalog.OneCapReaderCount.ToString());
        Check("개수 원캡 D 2", PosResponseCodeCatalog.OneCapVanDllCount == 2, PosResponseCodeCatalog.OneCapVanDllCount.ToString());
        Check("개수 #27 고정 20", PosResponseCodeCatalog.CardInfoDetailFixedCount == 20, PosResponseCodeCatalog.CardInfoDetailFixedCount.ToString());

        bool allReader = Enumerable.Range(0, 33).All(i => PosResponseCodeCatalog.Describe("R" + i.ToString("00")).IsKnown);
        Check("R00~R32 전부 IsKnown", allReader);
        Check("#27 접두 M22+2자·M23+2자",
            PosResponseCodeCatalog.DescribeCardInfoDetail("M22AB") != null && PosResponseCodeCatalog.DescribeCardInfoDetail("M23AB") != null);
        Check("별첨1 Count == 474 == Entries.Count",
            KftcVanApprovalCodeTable.Count == 474 && KftcVanApprovalCodeTable.Entries.Count == 474,
            $"Count={KftcVanApprovalCodeTable.Count}, Entries={KftcVanApprovalCodeTable.Entries.Count}");
    }

    // ------------------------------------------------------------------
    // 2. 표본 문구
    // ------------------------------------------------------------------
    private static void RunSamples()
    {
        CheckEq("000 정상", "정상", PosResponseCodeCatalog.Describe("000").Description);
        CheckEq("311", "고지내역 없음", PosResponseCodeCatalog.Describe("311").Description);
        CheckEq("364", "‘납부 기한’ 경과로 수납 불가", PosResponseCodeCatalog.Describe("364").Description);
        CheckEq("369", "입력 내역 불충분으로 수납 불가(연대 납부)", PosResponseCodeCatalog.Describe("369").Description);
        CheckEq("N02", "납부 불가 카드사", PosResponseCodeCatalog.Describe("N02").Description);
        CheckEq("V51", "주민(사업자)번호 상이", PosResponseCodeCatalog.Describe("V51").Description);
        CheckEq("VEN", "무통장거래횟수초과(1588-4500)", PosResponseCodeCatalog.Describe("VEN").Description);
        CheckEq("VJ1", "현장할인무료제공이용불가가맹점및카드", PosResponseCodeCatalog.Describe("VJ1").Description);
        CheckEq("VC2", "인증세선 불일치", PosResponseCodeCatalog.Describe("VC2").Description);
        CheckEq("VS6", "빠른주유 포인트+신용 승인실패", PosResponseCodeCatalog.Describe("VS6").Description);
        CheckEq("E03", "설정 화면 사용 중", PosResponseCodeCatalog.Describe("E03").Description);
        CheckEq("R02", "Reader Error(카드 리딩 도중 제거)", PosResponseCodeCatalog.Describe("R02").Description);
        CheckEq("R32", "리더기 실패 원인을 특정할 수 없는 경우", PosResponseCodeCatalog.Describe("R32").Description);
        CheckEq("D02", "VAN DLL 호출 자체 실패(서버 거절과는 다른, 통신 실패)", PosResponseCodeCatalog.Describe("D02").Description);
        CheckEq("#27 PAY502", "납부가능 일자 아님", PosResponseCodeCatalog.DescribeCardInfoDetail("PAY502"));
        CheckEq("#27 M2207", "국세 자진납부 입력 항목 관련 오류", PosResponseCodeCatalog.DescribeCardInfoDetail("M2207"));
        CheckEq("#27 M2300", "납부금액 검증 관련 오류", PosResponseCodeCatalog.DescribeCardInfoDetail("M2300"));
    }

    // ------------------------------------------------------------------
    // 3. 분류·출처
    // ------------------------------------------------------------------
    private static void CheckClass(string name, string? code, PosResponseCodeSource source, bool known, string label)
    {
        PosResponseCodeInfo info = PosResponseCodeCatalog.Describe(code);
        Check($"분류 {name}", info.Source == source && info.IsKnown == known && info.SourceLabel == label,
            $"Source={info.Source}, IsKnown={info.IsKnown}, Label={info.SourceLabel}");
    }

    private static void RunClassification()
    {
        CheckClass("000 공통", "000", PosResponseCodeSource.Common, true, "공통 응답");
        CheckClass("311 인터넷지로", "311", PosResponseCodeSource.InternetGiro, true, "인터넷지로 응답");
        CheckClass("111 서버(미정의)", "111", PosResponseCodeSource.ServerUnknown, false, "서버 응답");
        CheckClass("M01 중계", "M01", PosResponseCodeSource.KftcVanRelay, true, "KFTCVAN 중계 응답");
        CheckClass("M99 중계(미정의)", "M99", PosResponseCodeSource.KftcVanRelay, false, "KFTCVAN 중계 응답");
        CheckClass("N03 중계(미정의)", "N03", PosResponseCodeSource.KftcVanRelay, false, "KFTCVAN 중계 응답");
        CheckClass("V51 승인", "V51", PosResponseCodeSource.KftcVanApproval, true, "KFTCVAN 승인 응답 (별첨1)");
        CheckClass("VZZ 승인(미정의)", "VZZ", PosResponseCodeSource.KftcVanApproval, false, "KFTCVAN 승인 응답 (별첨1)");
        CheckClass("E03 원캡", "E03", PosResponseCodeSource.OneCap, true, "원캡 응답");
        CheckClass("E50 원캡(미정의)", "E50", PosResponseCodeSource.OneCap, false, "원캡 응답");
        CheckClass("R02 리더기", "R02", PosResponseCodeSource.OneCapReader, true, "원캡 응답 · 리더기");
        CheckClass("D02 VAN DLL", "D02", PosResponseCodeSource.OneCapVanDll, true, "원캡 응답 · VAN DLL");
        CheckClass("X12 알 수 없음", "X12", PosResponseCodeSource.Unknown, false, "출처를 알 수 없음");
        CheckClass("빈 문자열", string.Empty, PosResponseCodeSource.Unknown, false, "출처를 알 수 없음");
        CheckClass("null", null, PosResponseCodeSource.Unknown, false, "출처를 알 수 없음");
        CheckClass("\" V51 \" Trim", " V51 ", PosResponseCodeSource.KftcVanApproval, true, "KFTCVAN 승인 응답 (별첨1)");
        CheckClass("v51 소문자(Ordinal)", "v51", PosResponseCodeSource.Unknown, false, "출처를 알 수 없음");

        CheckEq("빈 값 DisplayCode", "(빈 값)", PosResponseCodeCatalog.Describe(null).DisplayCode);
        CheckEq("미정의 DisplayDescription", "정의되지 않은 코드", PosResponseCodeCatalog.Describe("VZZ").DisplayDescription);
    }

    // ------------------------------------------------------------------
    // 4. 성공 판정, 세부 코드
    // ------------------------------------------------------------------
    private static void RunSuccessAndDetail()
    {
        Check("000만 IsSuccess", PosResponseCodeCatalog.Describe("000").IsSuccess);
        Check("V00은 IsSuccess 아님", !PosResponseCodeCatalog.Describe("V00").IsSuccess);
        Check("V00은 IsKnown(별첨1 00 정상완료)", PosResponseCodeCatalog.Describe("V00").IsKnown);
        Check("311은 IsSuccess 아님", !PosResponseCodeCatalog.Describe("311").IsSuccess);

        Check("HasCardInfoDetail(800000, M01) 참", PosResponseCodeCatalog.HasCardInfoDetail("800000", "M01"));
        Check("HasCardInfoDetail(902614, M01) 거짓", !PosResponseCodeCatalog.HasCardInfoDetail("902614", "M01"));
        Check("HasCardInfoDetail(800000, N01) 거짓", !PosResponseCodeCatalog.HasCardInfoDetail("800000", "N01"));

        Check("M22(길이 3) null", PosResponseCodeCatalog.DescribeCardInfoDetail("M22") is null);
        Check("M220(길이 4) null", PosResponseCodeCatalog.DescribeCardInfoDetail("M220") is null);
        Check("M22000(길이 6) null", PosResponseCodeCatalog.DescribeCardInfoDetail("M22000") is null);
        Check("공백 세부 코드 null", PosResponseCodeCatalog.DescribeCardInfoDetail("  ") is null);
    }

    // ------------------------------------------------------------------
    // 5. 로그
    // ------------------------------------------------------------------
    private static string? Note(string? code, string? detail)
    {
        var record = new LogRecord(DateTime.Now, LogLevel.Info, LogCategory.Payment, code, "TX", "거래 확정", detail);
        byte[]? bytes = FileLogSink.BuildCodeNoteBytes(record);
        return bytes is null ? null : Encoding.UTF8.GetString(bytes);
    }

    private static void RunLog()
    {
        Check("LogCodeCatalog.Describe(000) == null", LogCodeCatalog.Describe("000") is null);
        CheckEq("LogCodeCatalog.Describe(V51)", "주민(사업자)번호 상이 · KFTCVAN 승인 응답 (별첨1)", LogCodeCatalog.Describe("V51"));
        string? s01 = LogCodeCatalog.Describe("S01");
        Check("LogCodeCatalog.Describe(S01) 출처 없음", s01 == "POS 소켓 리스닝 실패" && !s01.Contains(" · "), s01);
        Check("LogCodeCatalog.Describe(X12) == null", LogCodeCatalog.Describe("X12") is null);

        string nl = Environment.NewLine;
        string? m01Pay = Note("M01", "PAY502");
        CheckEq("노트 M01 + PAY502 두 줄",
            $"    └ 코드 설명: M01 = API 오류 (세부 응답코드 필드 참조) · KFTCVAN 중계 응답{nl}" +
            $"    └ 세부 코드: PAY502 = 납부가능 일자 아님{nl}",
            m01Pay);
        string? m01Blank = Note("M01", string.Empty);
        Check("노트 M01 + \"\" → 세부 코드: 없음", m01Blank != null && m01Blank.Contains("    └ 세부 코드: 없음"), m01Blank);
        string? m01Bad = Note("M01", "Z999");
        Check("노트 M01 + Z999 → 정의되지 않은 코드",
            m01Bad != null && m01Bad.Contains("    └ 세부 코드: Z999 = 정의되지 않은 코드"), m01Bad);
        Check("노트 000 + null → 줄 없음", Note("000", null) is null);
        CheckEq("노트 V51 한 줄",
            $"    └ 코드 설명: V51 = 주민(사업자)번호 상이 · KFTCVAN 승인 응답 (별첨1){nl}", Note("V51", null));
        Check("노트 코드 없음 → 줄 없음", Note(null, null) is null);
    }

    // ------------------------------------------------------------------
    // 6. 결제창 ViewModel
    // ------------------------------------------------------------------
    private sealed class FakeTransport
    {
        public string Code { get; set; } = "000";

        public string Detail { get; set; } = string.Empty;

        public bool Throw { get; set; }

        public Task<byte[]> Send(byte[] body, TimeSpan timeout)
        {
            if (Throw)
                return Task.FromException<byte[]>(new IOException("대역: 연결 실패"));

            PosRequestParseOutcome parsed = PosRequestTelegram.Parse(body);
            if (!parsed.IsSuccess || parsed.Telegram is null)
                throw new InvalidOperationException($"대역이 요청을 파싱하지 못함: {parsed.ErrorCode}");

            PosTelegram response = parsed.Telegram.Telegram.Clone();
            response.Write(3, "0210");
            response.Write(6, "C");
            response.Write(7, Code);
            response.Write(8, DateTime.Now.ToString("yyMMddHHmmss"));
            if (parsed.Telegram.TransactionTypeCode == "800000")
                response.Write(PosResponseCodeCatalog.CardInfoDetailFieldNumber, Detail);
            return Task.FromResult(response.ToBody());
        }
    }

    private static void RunViewModel()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"p45-test-{Guid.NewGuid():N}.db");
        try
        {
            var transport = new FakeTransport();
            var service = new ReceiptPrintService(
                () => new ShopSettings { SlipPrintEnabled = true, PrinterPort = "5", PrinterSpeed = 115200 },
                new FakeReceiptPrinter(PrintResult.Success()));
            var vm = new ViewModels.Payment.PaymentScreenViewModel(service, new LastReceiptStore(dbPath), transport.Send);
            PaymentTelegramTabViewModel approval = vm.Tabs.First(t => t.TransactionTypeCode == "902614");
            PaymentTelegramTabViewModel cardInfo = vm.Tabs.First(t => t.TransactionTypeCode == "800000");

            var outcomes = new List<TelegramOutcome>();
            approval.SendCompleted += (_, o) => outcomes.Add(o);
            cardInfo.SendCompleted += (_, o) => outcomes.Add(o);

            // 902614 000
            transport.Code = "000";
            Send(approval);
            Check("VM 902614 000 → 배지 정상", approval.StatusBadgeText == "정상" && approval.StatusBadgeIsPositive && !approval.StatusBadgeIsError,
                approval.StatusBadgeText);
            Check("VM 902614 000 → Band IsSuccess·Detail null",
                approval.ResponseCodeBand is { IsSuccess: true, Detail: null } && approval.HasResponseCodeBand);
            Check("VM 902614 000 → Outcome Success", outcomes.Count == 1 && outcomes[0] == TelegramOutcome.Success);

            // 902614 V51
            transport.Code = "V51";
            Send(approval);
            Check("VM 902614 V51 → 배지 거절 V51", approval.StatusBadgeText == "거절 V51" && approval.StatusBadgeIsError && !approval.StatusBadgeIsPositive,
                approval.StatusBadgeText);
            Check("VM 902614 V51 → Headline·SourceLabel",
                approval.ResponseCodeBand is { IsSuccess: false } b
                && b.Headline == "V51 · 주민(사업자)번호 상이" && b.SourceLabel == "KFTCVAN 승인 응답 (별첨1)" && b.Detail is null,
                approval.ResponseCodeBand?.Headline);
            Check("VM 902614 V51 → Outcome Failed(000일 때만 Success)", outcomes.Count == 2 && outcomes[1] == TelegramOutcome.Failed);

            // 902614 V00은 거절
            transport.Code = "V00";
            Send(approval);
            Check("VM 902614 V00 → 거절(정상 아님)", approval.StatusBadgeText == "거절 V00" && approval.ResponseCodeBand is { IsSuccess: false },
                approval.StatusBadgeText);
            Check("VM 902614 V00 → Outcome Failed", outcomes.Count == 3 && outcomes[2] == TelegramOutcome.Failed);

            // 800000 M01 + PAY502
            transport.Code = "M01";
            transport.Detail = "PAY502";
            Send(cardInfo);
            Check("VM 800000 M01+PAY502 → Detail",
                cardInfo.ResponseCodeBand?.Detail == "세부 PAY502 · 납부가능 일자 아님", cardInfo.ResponseCodeBand?.Detail);
            Check("VM 800000 M01 → 배지 거절 M01", cardInfo.StatusBadgeText == "거절 M01", cardInfo.StatusBadgeText);

            // 800000 M01 + 공백
            transport.Detail = string.Empty;
            Send(cardInfo);
            Check("VM 800000 M01+공백 → 세부 코드 없음", cardInfo.ResponseCodeBand?.Detail == "세부 코드 없음", cardInfo.ResponseCodeBand?.Detail);

            // 800000 M01 + 미정의
            transport.Detail = "Z999";
            Send(cardInfo);
            Check("VM 800000 M01+Z999 → 정의되지 않은 코드",
                cardInfo.ResponseCodeBand?.Detail == "세부 Z999 · 정의되지 않은 코드", cardInfo.ResponseCodeBand?.Detail);

            // 800000 N01 → 세부 없음
            transport.Code = "N01";
            Send(cardInfo);
            Check("VM 800000 N01 → Detail null", cardInfo.ResponseCodeBand is { Detail: null }, cardInfo.ResponseCodeBand?.Detail);

            // 전송 예외 → Band null, 배지 오류
            transport.Throw = true;
            Send(cardInfo);
            Check("VM 전송 예외 → Band null·배지 오류",
                cardInfo.ResponseCodeBand is null && !cardInfo.HasResponseCodeBand && cardInfo.StatusBadgeText == "오류",
                cardInfo.StatusBadgeText);
            transport.Throw = false;

            // 재생성 → Band null
            transport.Code = "311";
            Send(cardInfo);
            Check("VM 재생성 전 Band 있음", cardInfo.ResponseCodeBand is not null);
            cardInfo.RegenerateCommand.Execute(null);
            Check("VM 재생성 → Band null·배지 미전송",
                cardInfo.ResponseCodeBand is null && cardInfo.StatusBadgeText == "미전송", cardInfo.StatusBadgeText);

            // #7 행에 해설이 새지 않았는지(응답 행 값은 전문 원문 그대로)
            transport.Code = "V51";
            Send(approval);
            PosFieldRowViewModel? row7 = approval.ResponseRows.FirstOrDefault(r => r.Number == 7);
            Check("VM #7 행은 원문 값 그대로(해설 없음)", row7 != null && row7.Value.Trim() == "V51", row7?.Value);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (string p in new[] { dbPath, dbPath + "-journal", dbPath + "-wal", dbPath + "-shm" })
            {
                try { if (File.Exists(p)) File.Delete(p); } catch (Exception) { /* 임시 파일 */ }
            }
        }
    }

    private static void Send(PaymentTelegramTabViewModel tab) =>
        tab.SendCommand.ExecuteAsync(null).GetAwaiter().GetResult();
}
