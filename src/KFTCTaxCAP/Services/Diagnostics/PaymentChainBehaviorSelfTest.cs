using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
/// Phase 40 P40-3/P40-4(docs/payment_relay/development_plan.md) Task 테스트 — 결제 화면 ViewModel
/// (<see cref="PaymentScreenViewModel"/> + 탭 3개)만 구동해 전문 간 연쇄의 "빈 칸 시작 / 정상 응답일 때만 채움 /
/// 실패하면 출처 전문 단위로만 비움 / #34 목록·직접 입력"을 확인한다. 소켓 서버·리더기·VAN 없이 돈다.
///
/// <b>실패 응답 주입 방식(최소 침습)</b>: 스텁 VAN은 항상 <c>#7=000</c>만 돌려주고, 소켓 경로를 쓰면 서버·리더기가
/// 필요하다. 그래서 탭의 전송 함수 하나(<c>PaymentTelegramTabViewModel</c> 생성자의 <c>transportOverride</c>,
/// 운영 경로는 null)만 이 클래스의 <see cref="FakeTransport"/>로 바꿔 끼운다 — 요청 본문을 받아
/// <see cref="RealisticTestValueGenerator"/>(스텁이 쓰는 같은 생성기)로 정상 응답을 만들되, 전문별로 <c>#7=111</c>
/// 응답 / 파싱 불가한 짧은 본문 / 전송 예외를 선택할 수 있다. <c>Services/**</c>·서버는 건드리지 않는다.
///
/// 실 리더기·실 서버 왕복(800000/902614 실거래)은 P40-9 통합 검증 몫이고, 이 테스트가 그 연쇄 로직을 대신 확인한다.
/// <c>--receipt-print-test self</c>에서 <see cref="ReceiptPrintSelfTest"/>가 호출한다.
/// </summary>
internal static class PaymentChainBehaviorSelfTest
{
    private const string Tag = "[payment-chain-test]";
    private const string Notice = "501008";
    private const string CardInfo = "800000";
    private const string Approval = "902614";

    private enum Mode { Success, BadCode, ParseFail, Throw }

    public static bool RunAll()
    {
        bool ok = true;
        try
        {
            FileLogger.Info($"{Tag} 시작");
            ok &= RunBlankStart();
            ok &= RunNoticeSuccessFills();
            ok &= RunNoticeBadCodeClearsOnlyNoticeSources();
            ok &= RunCardInfoBadCodeClears();
            ok &= RunTransportExceptionAndParseFailClear();
            ok &= RunInstallmentOptionsAndDirectInput();
            ok &= RunHasResponseOrderAndReceiptPath();
            FileLogger.Info($"{Tag} 완료 — 종합={(ok ? "통과" : "실패")}");
        }
        catch (Exception ex)
        {
            FileLogger.Error($"{Tag} 예외로 중단: {ex}");
            return false;
        }

        return ok;
    }

    // ------------------------------------------------------------------
    // 전송 함수 대역 + 화면 구성
    // ------------------------------------------------------------------

    /// <summary>전문별 응답 모드를 정하는 전송 대역. 정상 응답은 스텁 순서(요청 clone → 응답 규칙 → 공통부 덮기)를 그대로 따른다.</summary>
    private sealed class FakeTransport
    {
        private readonly Random _random;

        public FakeTransport(int seed) { _random = new Random(seed); }

        public Dictionary<string, Mode> Modes { get; } = new()
        {
            [Notice] = Mode.Success, [CardInfo] = Mode.Success, [Approval] = Mode.Success,
        };

        public Task<byte[]> Send(byte[] body, TimeSpan timeout)
        {
            PosRequestParseOutcome parsed = PosRequestTelegram.Parse(body);
            if (!parsed.IsSuccess || parsed.Telegram is null)
                throw new InvalidOperationException($"대역이 요청을 파싱하지 못함: {parsed.ErrorCode}");

            PosTelegram request = parsed.Telegram.Telegram;
            Mode mode = Modes[parsed.Telegram.TransactionTypeCode];
            switch (mode)
            {
                case Mode.Throw:
                    return Task.FromException<byte[]>(new IOException("대역: 연결 실패"));
                case Mode.ParseFail:
                    return Task.FromResult(new byte[10]); // 스키마 총 길이와 다름 → PosProtocolException
            }

            DateTime now = DateTime.Now;
            PosTelegram response = request.Clone();
            RealisticTestValueGenerator.FillStubResponse(response, request, _random, now);
            response.Write(3, "0210");
            response.Write(6, "C");
            response.Write(7, mode == Mode.BadCode ? "111" : "000");
            response.Write(8, now.ToString("yyMMddHHmmss"));
            return Task.FromResult(response.ToBody());
        }
    }

    private sealed class Harness : IDisposable
    {
        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"p40-chain-{Guid.NewGuid():N}.db");

        public Harness(int seed)
        {
            Transport = new FakeTransport(seed);
            Printer = new FakeReceiptPrinter(PrintResult.Success());
            var service = new ReceiptPrintService(
                () => new ShopSettings { SlipPrintEnabled = true, PrinterPort = "5", PrinterSpeed = 115200 }, Printer);
            ViewModel = new PaymentScreenViewModel(service, new LastReceiptStore(_dbPath), Transport.Send);
            foreach (PaymentTelegramTabViewModel tab in ViewModel.Tabs)
            {
                PaymentTelegramTabViewModel captured = tab;
                captured.SendCompleted += (_, outcome) => Events.Add($"{captured.TransactionTypeCode}:{outcome}");
            }
        }

        public FakeTransport Transport { get; }
        public FakeReceiptPrinter Printer { get; }
        public PaymentScreenViewModel ViewModel { get; }
        public List<string> Events { get; } = new();

        public PaymentTelegramTabViewModel Tab(string code) => ViewModel.Tabs.First(t => t.TransactionTypeCode == code);

        public void Send(string code) => Tab(code).SendCommand.ExecuteAsync(null).GetAwaiter().GetResult();

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            foreach (string p in new[] { _dbPath, _dbPath + "-journal", _dbPath + "-wal", _dbPath + "-shm" })
            {
                try { if (File.Exists(p)) File.Delete(p); } catch (Exception) { /* 임시 파일 */ }
            }
        }
    }

    private static PosFieldRowViewModel Row(PaymentTelegramTabViewModel tab, int field) =>
        tab.RequestRows.First(r => r.Number == field);

    private static IEnumerable<TelegramFieldChainMap.ChainEntry> TargetsOf(string targetTelegram) =>
        TelegramFieldChainMap.Entries.Where(e => e.TargetTelegram == targetTelegram);

    /// <summary>출처 전문이 <paramref name="source"/>인 다른 전문 대상 항목(= 그 전문의 성공/실패가 좌우하는 항목).</summary>
    private static IEnumerable<TelegramFieldChainMap.ChainEntry> FedBy(string source) =>
        TelegramFieldChainMap.Entries.Where(e => e.SourceTelegram == source && e.TargetTelegram != source);

    private static string Tx(TelegramFieldChainMap.ChainEntry e) => $"{e.TargetTelegram}#{e.TargetFieldNumber}";

    private static bool IsBlankUnchained(Harness h, TelegramFieldChainMap.ChainEntry e)
    {
        PaymentTelegramTabViewModel tab = h.Tab(e.TargetTelegram);
        PosFieldRowViewModel row = Row(tab, e.TargetFieldNumber);
        return row.Value.Length == 0 && !row.IsChainedField && !row.HasOptions && tab.ReadRequestField(e.TargetFieldNumber).Length == 0;
    }

    private static bool Check(string name, bool ok, string? detail = null)
    {
        if (ok)
            FileLogger.Info($"{Tag} {name} — 통과");
        else
            FileLogger.Error(LogCategory.App, $"{Tag} ★ {name} — 실패{(detail is null ? "" : " — " + detail)}");
        return ok;
    }

    private static string NotBlankUnchained(Harness h, IEnumerable<TelegramFieldChainMap.ChainEntry> entries) =>
        string.Join(",", entries.Where(e => !IsBlankUnchained(h, e)).Select(Tx));

    // ------------------------------------------------------------------
    // 시나리오 1 — 창 생성 직후
    // ------------------------------------------------------------------
    private static bool RunBlankStart()
    {
        using var h = new Harness(1);
        bool ok = true;

        string bad = string.Join(",", TelegramFieldChainMap.Entries.Where(e => !IsBlankUnchained(h, e)).Select(Tx));
        ok &= Check("① 창 생성 직후 연쇄 대상 필드(표 전체 25건)는 전부 빈 칸·연쇄 표시 없음·목록 없음", bad.Length == 0, $"빈 칸이 아닌 항목: {bad}");

        // 비연쇄 kiosk 필드는 생성기 값이 그대로 있다(생성기는 손대지 않았다).
        PaymentTelegramTabViewModel t902 = h.Tab(Approval);
        bool kept = Row(t902, 5).Value == "000" && Row(t902, 39).Value == "O" && Row(t902, 41).Value == "Q" && Row(t902, 49).Value == "0"
            && Row(t902, 35).Value.StartsWith("010", StringComparison.Ordinal) && Row(t902, 32).Value.Length == 8
            && Row(h.Tab(Notice), 14).Value.Length == 19 && Row(h.Tab(CardInfo), 4).Value == CardInfo;
        ok &= Check("① 비연쇄 kiosk 필드(902614 #5/#32/#35/#39/#41/#49, 501008 #14 등)는 값이 있음", kept);

        var chainRowNumbers = new HashSet<string>(TelegramFieldChainMap.Entries.Select(Tx));
        int nonChainNonBlank = h.ViewModel.Tabs.Sum(t => t.RequestRows.Count(r => !chainRowNumbers.Contains($"{t.TransactionTypeCode}#{r.Number}") && r.Value.Length > 0));
        ok &= Check($"① 비연쇄 칸 중 값이 있는 칸 {nonChainNonBlank}개(>0)", nonChainNonBlank > 0);

        // 902614 #34 같은 목록 필드도 빈 박스 — 목록 유무로만 화면이 갈린다(HasOptions=false).
        ok &= Check("① 창 직후 902614 #34는 빈 칸·목록 없음", Row(t902, 34).Value.Length == 0 && !Row(t902, 34).HasOptions);

        // Phase 40 후속(2026-10-06) — 빈 연쇄 칸 출처 안내 문구.
        string wrongHint = string.Join(",", TelegramFieldChainMap.Entries.Where(e =>
        {
            PosFieldRowViewModel row = Row(h.Tab(e.TargetTelegram), e.TargetFieldNumber);
            string expected = e.SourceTelegram == e.TargetTelegram
                ? "← " + string.Join(" + ", e.SourceFieldNumbers.Select(n => $"#{n}"))
                : $"← {e.SourceTelegram} 응답";
            return row.ChainSourceHint != expected || !row.ShowChainHint;
        }).Select(Tx));
        ok &= Check("① 연쇄 대상 25칸 모두 출처 안내 문구 표시(예: \"← 501008 응답\", 902614 #29 \"← #27 + #28\")", wrongHint.Length == 0, wrongHint);
        int hintedNonChain = h.ViewModel.Tabs.Sum(t => t.RequestRows.Count(r =>
            !chainRowNumbers.Contains($"{t.TransactionTypeCode}#{r.Number}") && (r.ChainSourceHint is not null || r.ShowChainHint)));
        ok &= Check("① 비연쇄 칸에는 안내 문구 없음", hintedNonChain == 0, $"{hintedNonChain}칸");
        return ok;
    }

    // ------------------------------------------------------------------
    // 시나리오 2 — 501008 정상 응답
    // ------------------------------------------------------------------
    private static bool RunNoticeSuccessFills()
    {
        using var h = new Harness(2);
        bool ok = true;
        PaymentTelegramTabViewModel notice = h.Tab(Notice);

        h.Send(Notice);
        ok &= Check("② 501008 정상 전송 → SendCompleted=Success 1회, HasResponse=true",
            h.Events.SequenceEqual(new[] { "501008:Success" }) && notice.HasResponse, string.Join(",", h.Events));

        var wrong = new List<string>();
        foreach (TelegramFieldChainMap.ChainEntry e in FedBy(Notice))
        {
            string expected = TelegramFieldChainConverter.Convert(
                e.Conversion, e.SourceFieldNumbers.Select(n => notice.TryReadResponseField(n)!).ToList(),
                h.Tab(e.TargetTelegram).GetFieldLength(e.TargetFieldNumber), e.FixedValue);
            PosFieldRowViewModel row = Row(h.Tab(e.TargetTelegram), e.TargetFieldNumber);
            if (row.Value != expected || row.IsChainedField != (expected.Length > 0))
                wrong.Add($"{Tx(e)}(기대=\"{expected}\", 실제=\"{row.Value}\", 연쇄={row.IsChainedField})");
        }

        ok &= Check($"② 501008 출처 항목 {FedBy(Notice).Count()}건이 응답값으로 채워지고 연쇄 표시(연쇄 색)가 켜짐", wrong.Count == 0, string.Join("; ", wrong));

        PaymentTelegramTabViewModel t902 = h.Tab(Approval);
        string untouched = NotBlankUnchained(h, FedBy(CardInfo).Concat(TargetsOf(Approval).Where(e => e.SourceTelegram == Approval)));
        ok &= Check("② 800000 출처 항목(902614 #28/#33/#34)과 #29는 아직 빈 칸(#28이 비어 #29도 비어 있음)", untouched.Length == 0, untouched);
        ok &= Check("② 902614 #27은 채워졌고 #29는 #28 없이 계산되지 않음", Row(t902, 27).Value.Length > 0 && Row(t902, 29).Value.Length == 0 && !Row(t902, 29).IsChainedField);
        return ok;
    }

    // ------------------------------------------------------------------
    // 시나리오 3 — 501008 #7 ≠ 000: 501008에서 온 값만 비운다
    // ------------------------------------------------------------------
    private static bool RunNoticeBadCodeClearsOnlyNoticeSources()
    {
        using var h = new Harness(3);
        bool ok = true;
        h.Send(Notice);
        h.Send(CardInfo);

        // 사전 확인: 두 전문이 채운 상태.
        PaymentTelegramTabViewModel t902 = h.Tab(Approval);
        bool prepared = Row(t902, 28).Value.Length > 0 && Row(t902, 29).Value.Length > 0 && Row(t902, 34).Value.Length > 0 && Row(t902, 27).Value.Length > 0;
        ok &= Check("③ 사전 — 501008·800000 정상 후 902614 #27/#28/#29/#34가 모두 채워짐", prepared);

        string snapshot800 = string.Join("|", FedBy(CardInfo).Select(e => Row(h.Tab(e.TargetTelegram), e.TargetFieldNumber).Value));

        h.Transport.Modes[Notice] = Mode.BadCode;
        h.Events.Clear();
        h.Send(Notice);

        ok &= Check("③ 501008 #7=111 → SendCompleted=Failed 1회, 응답은 받았으므로 HasResponse=true(의미 불변)",
            h.Events.SequenceEqual(new[] { "501008:Failed" }) && h.Tab(Notice).HasResponse, string.Join(",", h.Events));
        string stillFilled = NotBlankUnchained(h, FedBy(Notice));
        ok &= Check("③ 501008 출처 항목(902614 #14~#27·#30·#31·#36·#37·공통부 #11/#12, 800000 #15/#16)은 빈 칸·연쇄 표시 해제", stillFilled.Length == 0, stillFilled);
        ok &= Check("③ #27이 비어 902614 #29도 빈 칸·연쇄 표시 해제", Row(t902, 29).Value.Length == 0 && !Row(t902, 29).IsChainedField);
        string noHint = string.Join(",", FedBy(Notice).Where(e => !Row(h.Tab(e.TargetTelegram), e.TargetFieldNumber).ShowChainHint).Select(Tx));
        ok &= Check("③ 비워진 501008 출처 칸과 #29에 출처 안내 문구가 다시 보임", noHint.Length == 0 && Row(t902, 29).ShowChainHint, noHint);
        string hintOnFilled = string.Join(",", FedBy(CardInfo).Where(e => Row(h.Tab(e.TargetTelegram), e.TargetFieldNumber).ShowChainHint).Select(Tx));
        ok &= Check("③ 값이 남아 있는 800000 출처 칸에는 안내 문구 없음", hintOnFilled.Length == 0, hintOnFilled);
        string after800 = string.Join("|", FedBy(CardInfo).Select(e => Row(h.Tab(e.TargetTelegram), e.TargetFieldNumber).Value));
        bool kept800 = after800 == snapshot800 && FedBy(CardInfo).All(e => Row(h.Tab(e.TargetTelegram), e.TargetFieldNumber).IsChainedField)
            && Row(t902, 34).HasOptions;
        ok &= Check("③ 800000 출처 항목(902614 #28/#33/#34)과 #34 목록은 그대로 유지(다른 출처 값 보존)", kept800, $"전={snapshot800}, 후={after800}");

        // 다시 정상 응답이 오면 되살아난다(사용자 편집값 덮어쓰기와 같은 경로).
        h.Transport.Modes[Notice] = Mode.Success;
        h.Send(Notice);
        ok &= Check("③ 이후 501008 정상 응답 → 다시 채워지고 #29도 계산됨", Row(t902, 27).Value.Length > 0 && Row(t902, 29).Value.Length > 0 && Row(t902, 14).IsChainedField);
        return ok;
    }

    // ------------------------------------------------------------------
    // 시나리오 4 — 800000 실패
    // ------------------------------------------------------------------
    private static bool RunCardInfoBadCodeClears()
    {
        using var h = new Harness(4);
        bool ok = true;
        h.Send(Notice);
        h.Send(CardInfo);
        PaymentTelegramTabViewModel t902 = h.Tab(Approval);

        string noticeValues = string.Join("|", FedBy(Notice).Select(e => Row(h.Tab(e.TargetTelegram), e.TargetFieldNumber).Value));

        h.Transport.Modes[CardInfo] = Mode.BadCode;
        h.Events.Clear();
        h.Send(CardInfo);

        ok &= Check("④ 800000 #7=111 → Failed", h.Events.SequenceEqual(new[] { "800000:Failed" }), string.Join(",", h.Events));
        ok &= Check("④ 902614 #28/#33/#34 빈 칸·연쇄 표시 해제·#34 목록 비움",
            NotBlankUnchained(h, FedBy(CardInfo)).Length == 0, NotBlankUnchained(h, FedBy(CardInfo)));
        ok &= Check("④ #28이 비어 #29도 빈 칸·연쇄 표시 해제", Row(t902, 29).Value.Length == 0 && !Row(t902, 29).IsChainedField);
        string noticeAfter = string.Join("|", FedBy(Notice).Select(e => Row(h.Tab(e.TargetTelegram), e.TargetFieldNumber).Value));
        ok &= Check("④ 501008에서 온 값(#14~#27 등)은 그대로 유지", noticeAfter == noticeValues && Row(t902, 27).Value.Length > 0 && Row(t902, 27).IsChainedField);
        return ok;
    }

    // ------------------------------------------------------------------
    // 시나리오 5 — 전송 예외 / 응답 파싱 실패
    // ------------------------------------------------------------------
    private static bool RunTransportExceptionAndParseFailClear()
    {
        bool ok = true;

        foreach ((Mode failMode, string label) in new[] { (Mode.Throw, "전송 예외(연결 실패·타임아웃)"), (Mode.ParseFail, "응답 파싱 실패") })
        {
            using var h = new Harness(5);
            h.Send(Notice);
            h.Send(CardInfo);
            PaymentTelegramTabViewModel t902 = h.Tab(Approval);
            string card = string.Join("|", FedBy(CardInfo).Select(e => Row(h.Tab(e.TargetTelegram), e.TargetFieldNumber).Value));

            h.Transport.Modes[Notice] = failMode;
            h.Events.Clear();
            h.Send(Notice);

            ok &= Check($"⑤ 501008 {label} → SendCompleted=Failed 정확히 1회", h.Events.SequenceEqual(new[] { "501008:Failed" }), string.Join(",", h.Events));
            // HasResponse 의미 불변: 예외는 응답이 없으므로 false(오류 표시), 파싱 실패는 기존대로 true.
            bool hasResponseOk = failMode == Mode.Throw
                ? !h.Tab(Notice).HasResponse && h.Tab(Notice).IsStatusError
                : h.Tab(Notice).HasResponse && h.Tab(Notice).IsStatusError;
            ok &= Check($"⑤ {label} — HasResponse 기존 의미 유지(예외=false, 파싱 실패=true) + 오류 표시", hasResponseOk,
                $"HasResponse={h.Tab(Notice).HasResponse}, IsStatusError={h.Tab(Notice).IsStatusError}");
            ok &= Check($"⑤ {label} — 501008 출처 항목만 비움(#29 포함)", NotBlankUnchained(h, FedBy(Notice)).Length == 0 && Row(t902, 29).Value.Length == 0,
                NotBlankUnchained(h, FedBy(Notice)));
            string cardAfter = string.Join("|", FedBy(CardInfo).Select(e => Row(h.Tab(e.TargetTelegram), e.TargetFieldNumber).Value));
            ok &= Check($"⑤ {label} — 800000 출처 항목은 유지", cardAfter == card && Row(t902, 34).HasOptions);
        }

        return ok;
    }

    // ------------------------------------------------------------------
    // 시나리오 6 — #34 목록·기본 선택·직접 입력(P40-4)
    // ------------------------------------------------------------------
    private static bool RunInstallmentOptionsAndDirectInput()
    {
        bool ok = true;
        bool sawCredit = false, sawCheck = false;

        for (int seed = 0; seed < 200 && !(sawCredit && sawCheck); seed++)
        {
            using var h = new Harness(seed);
            h.Send(Notice);
            h.Send(CardInfo);
            PaymentTelegramTabViewModel card = h.Tab(CardInfo);
            PaymentTelegramTabViewModel t902 = h.Tab(Approval);
            PosFieldRowViewModel row34 = Row(t902, 34);
            bool isCheck = card.TryReadResponseField(19) == "Y";
            if (isCheck ? sawCheck : sawCredit)
                continue;

            string list = card.TryReadResponseField(22)!;
            IReadOnlyList<string> expectedCodes = TelegramFieldChainConverter.SplitInstallmentList(list);
            string[] optionCodes = row34.Options.Select(o => o.Code).ToArray();

            if (isCheck)
            {
                sawCheck = true;
                ok &= Check("⑥ 체크카드 — LIST \"00\" → 항목 1개 \"00 (일시불)\", 값 00",
                    optionCodes.SequenceEqual(new[] { "00" }) && row34.Options[0].Display == "00 (일시불)" && row34.Value == "00" && row34.IsChainedField,
                    $"#22=\"{list}\", 코드=[{string.Join(",", optionCodes)}], 값={row34.Value}");
            }
            else
            {
                sawCredit = true;
                bool shape = optionCodes.SequenceEqual(expectedCodes) && optionCodes.Length == 13 && optionCodes[0] == "00" && !optionCodes.Contains("01");
                bool display = row34.Options[0].Display == "00 (일시불)" && row34.Options.First(o => o.Code == "03").Display == "03 (3개월)"
                    && row34.Options.First(o => o.Code == "24").Display == "24 (24개월)";
                ok &= Check("⑥ 신용카드 — 목록 = 800000 #22를 SplitInstallmentList로 분할한 13개(00으로 시작, 01 없음), 표시 \"00 (일시불)\"/\"03 (3개월)\"",
                    shape && display, $"코드=[{string.Join(",", optionCodes)}]");
                ok &= Check("⑥ 기본 선택 = 첫 항목(값 \"00\"), 포인트 항목은 자동으로 들어가지 않음(60 이상 없음)",
                    row34.Value == "00" && row34.IsChainedField && optionCodes.All(c => int.Parse(c) < 60));

                // 목록에서 03을 고르면 칸에는 코드만 간다 → 전송 원문 #34 = "03"(표시 문구가 섞이지 않음).
                PosFieldOption three = row34.Options.First(o => o.Code == "03");
                row34.Value = three.Code;
                string wire03 = new string(t902.ReadRequestField(34).ToCharArray());
                ok &= Check("⑥ 목록에서 \"03 (3개월)\" 선택(칸 값=코드 \"03\") → 전송 원문 #34 = \"03\"", wire03 == "03" && !wire03.Contains("("), $"#34=\"{wire03}\"");

                // 직접 입력(포인트 납부) — 목록에 없는 값도 막지 않고 그대로 원문에 들어간다.
                row34.Value = "63";
                ok &= Check("⑥ #34에 \"63\" 직접 입력 → 전송 원문 #34 = \"63\"(목록에 없어도 허용)", t902.ReadRequestField(34) == "63");

                // 같은 800000을 다시 받으면(정상) 직접 입력값을 덮어쓴다 — 다른 연쇄 값과 같은 규칙.
                h.Send(CardInfo);
                ok &= Check("⑥ 800000을 다시 정상 수신 → 직접 입력값을 새 LIST 첫 항목으로 덮어씀", t902.ReadRequestField(34) == "00");

                // 실패하면 값·목록 모두 비운다.
                h.Transport.Modes[CardInfo] = Mode.BadCode;
                h.Send(CardInfo);
                ok &= Check("⑥ 800000 실패 → #34 값·목록 비움", row34.Value.Length == 0 && !row34.HasOptions && t902.ReadRequestField(34).Length == 0);
            }
        }

        ok &= Check("⑥ 신용·체크 두 경우 모두 확인됨", sawCredit && sawCheck, $"신용={sawCredit}, 체크={sawCheck}");
        return ok;
    }

    // ------------------------------------------------------------------
    // 시나리오 7 — HasResponse 전이 순서·영수증 자동 출력 경로(Phase 34 회귀)
    // ------------------------------------------------------------------
    private static bool RunHasResponseOrderAndReceiptPath()
    {
        using var h = new Harness(7);
        bool ok = true;
        h.Send(Notice);
        h.Send(CardInfo);

        // 902614 정상 전송: SendCompleted가 HasResponse=true 직전에 오고, 승인이므로 자동 출력이 1회 호출된다.
        PaymentTelegramTabViewModel t902 = h.Tab(Approval);
        var order = new List<string>();
        t902.SendCompleted += (_, o) => order.Add($"SendCompleted:{o}");
        t902.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PaymentTelegramTabViewModel.HasResponse))
                order.Add($"HasResponse:{t902.HasResponse}");
        };

        h.Send(Approval);
        ok &= Check("⑦ 902614 전송 이벤트 순서: HasResponse:false(전송 직전) → SendCompleted:Success → HasResponse:true",
            order.SequenceEqual(new[] { "HasResponse:False", "SendCompleted:Success", "HasResponse:True" }) || order.SequenceEqual(new[] { "SendCompleted:Success", "HasResponse:True" }),
            string.Join(" → ", order));

        bool printed = WaitUntil(() => h.Printer.CallCount == 1);
        ok &= Check("⑦ 승인(#7=000) 902614 → 영수증 자동 출력 1회(HasResponse 전이 경로 유지)", printed, $"출력 호출={h.Printer.CallCount}");

        // 902614 #7≠000 → 응답은 왔으므로 HasResponse=true지만 승인이 아니라 추가 출력은 없다. 연쇄 출처 항목이 없어 다른 칸은 그대로.
        string before = string.Join("|", t902.RequestRows.Select(r => r.Value));
        h.Transport.Modes[Approval] = Mode.BadCode;
        h.Send(Approval);
        WaitUntil(() => false, 300);
        ok &= Check("⑦ 902614 #7=111 → Failed·HasResponse=true·추가 출력 없음·요청 칸 불변",
            h.Events.Last() == "902614:Failed" && t902.HasResponse && h.Printer.CallCount == 1
            && string.Join("|", t902.RequestRows.Select(r => r.Value)) == before);

        // 902614 전송 예외 → HasResponse=false, 출력 없음.
        h.Transport.Modes[Approval] = Mode.Throw;
        h.Send(Approval);
        ok &= Check("⑦ 902614 전송 예외 → Failed·HasResponse=false·출력 없음", h.Events.Last() == "902614:Failed" && !t902.HasResponse && h.Printer.CallCount == 1);
        return ok;
    }

    private static bool WaitUntil(Func<bool> condition, int timeoutMilliseconds = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return true;
            System.Threading.Thread.Sleep(20);
        }

        return condition();
    }
}
