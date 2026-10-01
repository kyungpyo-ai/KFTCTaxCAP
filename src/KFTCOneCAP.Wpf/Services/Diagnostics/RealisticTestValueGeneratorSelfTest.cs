using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using KFTCOneCAP.Wpf.Protocol.Pos;
using KFTCOneCAP.Wpf.Protocol.Pos.Schemas;
using KFTCOneCAP.Wpf.Protocol.Printer;
using KFTCOneCAP.Wpf.Services.Receipt;
using KFTCOneCAP.Wpf.Services.Van;
using KFTCOneCAP.Wpf.ViewModels.Payment;

namespace KFTCOneCAP.Wpf.Services.Diagnostics;

/// <summary>
/// Phase 37 P37-1(docs/receipt_print/development_plan.md "P37-1. 테스트값 생성기 · 배선 · self") 완료 조건 전용
/// 셀프테스트 — <see cref="RealisticTestValueGenerator"/>를 고정 시드(<see cref="Random(int)"/>)·고정 날짜로 돌려
/// 다음을 확인한다.
/// <list type="number">
/// <item>① 규칙 커버리지(kiosk·디지털예산·인터넷지로·VAN 담당 필드가 전부 규칙에 있고, 원캡·소유자 없음 필드는
///   없음) + 모든 값이 <see cref="PosField.Pad"/> 통과 + 원캡 필드 불변 + 비담당 필드 공백</item>
/// <item>② 금액 정합 ③ 날짜 형식·범위 ④ 카드사 코드·이름 쌍 ⑤ 체크카드 규칙 ⑥ AHN 필드 "테스트" 표시·바이트 길이</item>
/// <item>⑦ 실제 <see cref="StubVanRelayService"/> + 연쇄 맵(<see cref="TelegramFieldChainMap"/>) + Phase 34 조립기/영수증
///   조립기로 501008 → 800000 → 902614 → 영수증까지 돌려 줄마다 값을 검증하고 줄 미리보기를 로그에 남긴다.</item>
/// </list>
/// <c>App.xaml.cs</c>가 <c>--realistic-test-value-test</c> 인자로 실행될 때만 <see cref="RunAll"/>을 호출한다.
/// 값은 전부 테스트값이라 개인정보가 없다(주민번호 꼴 <c>9001011xxxxxx</c>도 생성기가 만든 가짜).
/// </summary>
internal static class RealisticTestValueGeneratorSelfTest
{
    private const string Tag = "[realistic-test-value-test]";

    /// <summary>고정 날짜 — 월말·연말 경계(납기일 +30일이 해를 넘는 경우, 세목 코드 YYMM)도 같이 때린다.</summary>
    private static readonly DateTime[] FixedNows =
    {
        new(2026, 10, 1, 9, 30, 15),
        new(2026, 12, 31, 23, 59, 59),
        new(2027, 1, 5, 0, 0, 1),
    };

    private const int SeedsPerDate = 200;

    /// <summary>원캡이 800000 <c>#14</c>에 넣는 마스킹 카드번호 꼴의 더미(리더기가 이미 마스킹한 형태, Phase 36).</summary>
    private const string OneCapMaskedCardDummy = "11112222****444*";

    private const int MaxFailureLogsPerCase = 5;

    public static void RunAll()
    {
        try
        {
            FileLogger.Info($"{Tag} 시작");

            bool coverageOk = RunCoverageCase();
            var tally = new LoopTally();
            RunGeneratedLoop(tally);
            bool flowOk = RunStubChainReceiptFlow();

            bool case1 = coverageOk && tally.Ok[1];
            bool allPassed = case1 && tally.Ok[2] && tally.Ok[3] && tally.Ok[4] && tally.Ok[5] && tally.Ok[6] && flowOk;

            FileLogger.Info(
                $"{Tag} 완료 — ①커버리지·Pad·원캡 불변={Mark(case1)}, ②금액 정합={Mark(tally.Ok[2])}, " +
                $"③날짜·형식={Mark(tally.Ok[3])}, ④카드사 쌍={Mark(tally.Ok[4])}, ⑤체크카드 규칙={Mark(tally.Ok[5])}, " +
                $"⑥AHN 테스트 표시={Mark(tally.Ok[6])}, ⑦스텁→연쇄→영수증={Mark(flowOk)}, " +
                $"반복={tally.Iterations}회(날짜 {FixedNows.Length}개 × 시드 {SeedsPerDate}개), 종합={Mark(allPassed)}");
        }
        catch (Exception ex)
        {
            FileLogger.Error($"{Tag} 예외로 중단: {ex}");
        }
    }

    private static string Mark(bool ok) => ok ? "통과" : "실패";

    private static IEnumerable<PosTelegramSchema> Schemas() => new[]
    {
        NoticeInquirySchema.Create(),
        CardInfoInquirySchema.Create(),
        CardApprovalSchema.Create(),
    };

    // ------------------------------------------------------------------
    // ① 규칙 커버리지(정적)
    // ------------------------------------------------------------------

    /// <summary>
    /// 요청 규칙 키 = SET 장소에 kiosk가 있고 원캡이 없는 필드 전부, 응답 규칙 키 = 소유자가 있고 kiosk·원캡이 없는 필드
    /// 전부(디지털예산/인터넷지로/VAN). 빠진 필드·넘친 필드가 하나라도 있으면 실패. 공백 규칙도 규칙으로 센다.
    /// </summary>
    private static bool RunCoverageCase()
    {
        bool allOk = true;
        DateTime now = FixedNows[0];

        foreach (PosTelegramSchema schema in Schemas())
        {
            string code = schema.TransactionTypeCode;
            var expectedRequest = schema.Fields
                .Where(f => f.Owners.HasFlag(PosFieldOwner.Kiosk) && !f.Owners.HasFlag(PosFieldOwner.OneCap))
                .Select(f => f.Number).ToList();
            var expectedResponse = schema.Fields
                .Where(f => f.Owners != PosFieldOwner.None
                    && !f.Owners.HasFlag(PosFieldOwner.Kiosk) && !f.Owners.HasFlag(PosFieldOwner.OneCap))
                .Select(f => f.Number).ToList();
            var oneCapFields = schema.Fields.Where(f => f.Owners.HasFlag(PosFieldOwner.OneCap)).Select(f => f.Number).ToList();
            var noOwnerFields = schema.Fields.Where(f => f.Owners == PosFieldOwner.None).Select(f => f.Number).ToList();

            IReadOnlyDictionary<int, string> requestRules = RealisticTestValueGenerator.BuildRequestValues(code, new Random(1), now);
            PosTelegram request = RealisticTestValueGenerator.GenerateRequest(schema, new Random(1), now);
            IReadOnlyDictionary<int, string> responseRules = RealisticTestValueGenerator.BuildResponseValues(code, request, new Random(2), now);

            allOk &= CompareKeys($"[{code}] 요청 규칙", requestRules, expectedRequest);
            allOk &= CompareKeys($"[{code}] 응답 규칙", responseRules, expectedResponse);

            FileLogger.Info(
                $"{Tag} ① [{code}] 요청 규칙 {requestRules.Count}개({Numbers(requestRules.Keys)}, 공백 규칙: {Numbers(BlankKeys(requestRules))}) / " +
                $"응답 규칙 {responseRules.Count}개({Numbers(responseRules.Keys)}, 공백 규칙: {Numbers(BlankKeys(responseRules))}) / " +
                $"규칙 없음 — 원캡 {Numbers(oneCapFields)}, 소유자 없음 {Numbers(noOwnerFields)}");
        }

        return allOk;
    }

    private static IEnumerable<int> BlankKeys(IReadOnlyDictionary<int, string> rules) =>
        rules.Where(kv => kv.Value.Length == 0).Select(kv => kv.Key);

    private static string Numbers(IEnumerable<int> numbers)
    {
        var list = numbers.OrderBy(n => n).ToList();
        return list.Count == 0 ? "없음" : string.Join(",", list.Select(n => "#" + n));
    }

    private static bool CompareKeys(string name, IReadOnlyDictionary<int, string> rules, IReadOnlyCollection<int> expected)
    {
        var missing = expected.Where(n => !rules.ContainsKey(n)).ToList();
        var extra = rules.Keys.Where(n => !expected.Contains(n)).ToList();
        bool ok = missing.Count == 0 && extra.Count == 0;
        if (ok)
            FileLogger.Info($"{Tag} ① {name} 커버리지 통과 — 스키마 대상 {expected.Count}개 전부 규칙 있음, 초과 없음");
        else
            FileLogger.Error(LogCategory.App, $"{Tag} ★ ① {name} 커버리지 실패 — 누락 {Numbers(missing)}, 초과 {Numbers(extra)}");
        return ok;
    }

    // ------------------------------------------------------------------
    // ①~⑥ 반복 생성 검사
    // ------------------------------------------------------------------

    private sealed class LoopTally
    {
        public readonly bool[] Ok = { true, true, true, true, true, true, true };
        public readonly int[] FailureLogs = new int[7];
        public readonly HashSet<string> SeenCardCodes = new();
        public int CheckCardCount;
        public int Iterations;

        public void Fail(int caseNo, string message)
        {
            Ok[caseNo] = false;
            if (FailureLogs[caseNo]++ < MaxFailureLogsPerCase)
                FileLogger.Error(LogCategory.App, $"{Tag} ★ {CaseMark(caseNo)} {message}");
        }

        public void Check(int caseNo, bool condition, Func<string> message)
        {
            if (!condition)
                Fail(caseNo, message());
        }
    }

    private static string CaseMark(int caseNo) => caseNo switch
    {
        1 => "①", 2 => "②", 3 => "③", 4 => "④", 5 => "⑤", 6 => "⑥", _ => "?",
    };

    private static void RunGeneratedLoop(LoopTally t)
    {
        PosTelegramSchema s501 = NoticeInquirySchema.Create();
        PosTelegramSchema s800 = CardInfoInquirySchema.Create();
        PosTelegramSchema s902 = CardApprovalSchema.Create();

        foreach (DateTime now in FixedNows)
        {
            for (int seed = 0; seed < SeedsPerDate; seed++)
            {
                t.Iterations++;
                string ctx = $"[now={now:yyyy-MM-dd HH:mm:ss}, seed={seed}]";
                try
                {
                    var random = new Random(seed);
                    PosTelegram q501 = RealisticTestValueGenerator.GenerateRequest(s501, random, now);
                    PosTelegram q800 = RealisticTestValueGenerator.GenerateRequest(s800, random, now);
                    PosTelegram q902 = RealisticTestValueGenerator.GenerateRequest(s902, random, now);

                    // ① 요청: kiosk 비담당 필드는 공백(원캡 표식을 넣기 전에 확인).
                    foreach (PosTelegram q in new[] { q501, q800, q902 })
                    {
                        foreach (PosField f in q.Schema.Fields.Where(f => !f.Owners.HasFlag(PosFieldOwner.Kiosk)))
                        {
                            string v = q.Read(f.Number);
                            t.Check(1, v.Length == 0, () => $"{ctx} [{q.Schema.TransactionTypeCode}] 요청의 kiosk 비담당 #{f.Number}가 공백이 아님: \"{v}\"");
                        }
                    }

                    // 원캡이 채웠다고 가정한 표식값 — 응답 생성이 이 바이트를 건드리지 않는지 본다.
                    WriteOneCapMarkers(q800);
                    WriteOneCapMarkers(q902);

                    PosTelegram r501 = SimulateStub(q501, random, now);
                    PosTelegram r800 = SimulateStub(q800, random, now);
                    PosTelegram r902 = SimulateStub(q902, random, now);

                    foreach ((PosTelegram q, PosTelegram r) in new[] { (q501, r501), (q800, r800), (q902, r902) })
                        CheckStructure(t, ctx, q, r);

                    CheckAmounts(t, ctx, q800, r501, r800, q902);
                    CheckDatesAndFormats(t, ctx, now, q501, r501, q800, q902, r902);
                    CheckCard(t, ctx, r800);
                    CheckHangul(t, ctx, new[] { q501, r501, q800, r800, q902, r902 });
                }
                catch (Exception ex)
                {
                    // Pad 길이 초과(PosProtocolException) 등 — ①로 집계.
                    t.Fail(1, $"{ctx} 생성 중 예외: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        // ④ 분포: 200×3회면 12개 코드가 전부 나와야 정상(고정 시드라 결정적).
        t.Check(4, t.SeenCardCodes.Count == RealisticTestValueGenerator.CardCompanies.Count,
            () => $"카드사 코드 {t.SeenCardCodes.Count}/{RealisticTestValueGenerator.CardCompanies.Count}종만 나옴");
        double checkRatio = (double)t.CheckCardCount / t.Iterations;
        t.Check(5, checkRatio > 0.12 && checkRatio < 0.28, () => $"체크카드 비율 {checkRatio:P1} — 20% 근처 기대");

        FileLogger.Info(
            $"{Tag} ①~⑥ 반복 {t.Iterations}회 — 카드사 코드 {t.SeenCardCodes.Count}종 출현, " +
            $"체크카드 {t.CheckCardCount}회({checkRatio:P1}), 실패 로그 수 ①{t.FailureLogs[1]} ②{t.FailureLogs[2]} " +
            $"③{t.FailureLogs[3]} ④{t.FailureLogs[4]} ⑤{t.FailureLogs[5]} ⑥{t.FailureLogs[6]}");
    }

    /// <summary>스텁(<c>StubVanRelayService.BuildFakeSuccess</c>)과 같은 순서 — 요청 clone → 응답 규칙 → 공통부 4개 덮기.
    /// 실제 스텁은 1초 지연이 있어 반복 검사에는 이 재현을 쓰고, ⑦에서 실제 스텁을 한 번 태운다.</summary>
    private static PosTelegram SimulateStub(PosTelegram request, Random random, DateTime now)
    {
        PosTelegram response = request.Clone();
        RealisticTestValueGenerator.FillStubResponse(response, request, random, now);
        response.Write(3, "0210");
        response.Write(6, "C");
        response.Write(7, "000");
        response.Write(8, now.ToString("yyMMddHHmmss", CultureInfo.InvariantCulture));
        return response;
    }

    private static void WriteOneCapMarkers(PosTelegram telegram)
    {
        foreach (PosField f in telegram.Schema.FieldsOwnedByOneCap())
        {
            string marker = telegram.Schema.TransactionTypeCode == RealisticTestValueGenerator.CardInfo800000 && f.Number == 14
                ? OneCapMaskedCardDummy
                : new string(f.Type == PosFieldType.N ? '7' : 'Z', f.Length);
            telegram.Write(f.Number, marker);
        }
    }

    /// <summary>① 원캡 필드 바이트 불변, 소유자 없음 필드 공백, 본문 0x00 없음.</summary>
    private static void CheckStructure(LoopTally t, string ctx, PosTelegram q, PosTelegram r)
    {
        string code = q.Schema.TransactionTypeCode;
        byte[] qb = q.ToBody();
        byte[] rb = r.ToBody();
        t.Check(1, !rb.Any(b => b == 0x00), () => $"{ctx} [{code}] 응답 본문에 0x00");

        foreach (PosField f in q.Schema.Fields)
        {
            if (f.Owners.HasFlag(PosFieldOwner.OneCap))
            {
                bool same = true;
                for (int i = f.Position; i < f.EndPosition; i++)
                    same &= qb[i] == rb[i];
                t.Check(1, same, () => $"{ctx} [{code}] 원캡 필드 #{f.Number}가 응답 생성 중 바뀜");
            }
            else if (f.Owners == PosFieldOwner.None)
            {
                string v = r.Read(f.Number);
                t.Check(1, v.Length == 0, () => $"{ctx} [{code}] 소유자 없음 #{f.Number}가 공백이 아님: \"{v}\"");
            }
        }

        t.Check(1, r.Read(3) == "0210" && r.Read(6) == "C" && r.Read(7) == "000",
            () => $"{ctx} [{code}] 공통부 덮기 순서 깨짐 — #3={r.Read(3)}, #6={r.Read(6)}, #7={r.Read(7)}");
    }

    /// <summary>② 금액 정합.</summary>
    private static void CheckAmounts(LoopTally t, string ctx, PosTelegram q800, PosTelegram r501, PosTelegram r800, PosTelegram q902)
    {
        long n30 = Num(r501, 30), n31 = Num(r501, 31), n32 = Num(r501, 32), n25 = Num(r501, 25);
        t.Check(2, n25 == n30 + n31 + n32, () => $"{ctx} 501008 #25({n25}) ≠ #30+#31+#32({n30}+{n31}+{n32})");
        t.Check(2, Num(r501, 27) == n25 && Num(r501, 52) == n25, () => $"{ctx} 501008 #27/#52가 #25와 다름");
        t.Check(2, n31 == 0 && n32 == 0, () => $"{ctx} 501008 #31/#32가 0이 아님");
        t.Check(2, n30 >= 10_000 && n30 <= 1_000_000 && n30 % 10 == 0, () => $"{ctx} 501008 #30({n30}) 범위/10원 단위 위반");
        for (int n = 33; n <= 39; n++)
        {
            int field = n;
            t.Check(2, Num(r501, field) == 0, () => $"{ctx} 501008 #{field}가 0이 아님");
        }
        t.Check(2, Num(r501, 51) == 0, () => $"{ctx} 501008 #51이 0이 아님");

        long q15 = Num(q800, 15), rate = Num(r800, 26), fee = Num(r800, 24), total = Num(r800, 25);
        t.Check(2, q15 >= 10_000 && q15 <= 1_000_000 && q15 % 10 == 0, () => $"{ctx} 800000 요청 #15({q15}) 범위 위반");
        t.Check(2, fee == q15 * rate / 10_000, () => $"{ctx} 800000 #24({fee}) ≠ floor(#15 {q15} × #26 {rate} / 10000)");
        t.Check(2, total == q15 + fee, () => $"{ctx} 800000 #25({total}) ≠ #15+#24({q15}+{fee})");

        long c24 = Num(q902, 24), c25 = Num(q902, 25), c26 = Num(q902, 26), c27 = Num(q902, 27), c28 = Num(q902, 28), c29 = Num(q902, 29);
        t.Check(2, c27 == c24 + c25 + c26, () => $"{ctx} 902614 초기값 #27({c27}) ≠ #24+#25+#26");
        t.Check(2, c28 == RealisticTestValueGenerator.FloorFee(c27, RealisticTestValueGenerator.CreditFeeRateHundredths),
            () => $"{ctx} 902614 초기값 #28({c28}) ≠ floor(#27 × 0.8%)");
        t.Check(2, c29 == c27 + c28, () => $"{ctx} 902614 초기값 #29({c29}) ≠ #27+#28");
    }

    /// <summary>③ 날짜·형식(번호 꼴 포함).</summary>
    private static void CheckDatesAndFormats(
        LoopTally t, string ctx, DateTime now, PosTelegram q501, PosTelegram r501, PosTelegram q800, PosTelegram q902, PosTelegram r902)
    {
        DateTime today = now.Date;
        string yyMM = now.ToString("yyMM", CultureInfo.InvariantCulture);
        string nowStamp = now.ToString("yyMMddHHmmss", CultureInfo.InvariantCulture);

        DateTime due = Date(r501, 26);
        t.Check(3, due > today && due <= today.AddDays(30) && due >= today.AddDays(7), () => $"{ctx} 501008 #26({r501.Read(26)}) 범위 위반");
        t.Check(3, r501.Read(28) == r501.Read(26), () => $"{ctx} 501008 #28 ≠ #26");
        DateTime notice = Date(r501, 49);
        t.Check(3, notice < today && notice >= today.AddDays(-20) && notice <= today.AddDays(-5), () => $"{ctx} 501008 #49({r501.Read(49)}) 범위 위반");
        t.Check(3, r501.Read(24) == now.ToString("yyyy", CultureInfo.InvariantCulture), () => $"{ctx} 501008 #24 ≠ 올해");
        t.Check(3, r501.Read(20) == yyMM + "510", () => $"{ctx} 501008 #20({r501.Read(20)}) ≠ {yyMM}510");

        DateTime c31 = Date(q902, 31), c32 = Date(q902, 32);
        t.Check(3, c31 == today.AddDays(14) && c32 == today, () => $"{ctx} 902614 #31/#32({q902.Read(31)}/{q902.Read(32)}) 위반");
        t.Check(3, q902.Read(18) == yyMM + "510" && q902.Read(23) == now.ToString("yyyy", CultureInfo.InvariantCulture),
            () => $"{ctx} 902614 #18/#23 위반");

        foreach (PosTelegram q in new[] { q501, q800, q902 })
        {
            string code = q.Schema.TransactionTypeCode;
            t.Check(3, q.Read(1) == "IGN" && q.Read(2) == "095" && q.Read(3) == "0200" && q.Read(4) == code && q.Read(6) == "G",
                () => $"{ctx} [{code}] 공통부 고정값 위반");
            t.Check(3, q.Read(8) == nowStamp, () => $"{ctx} [{code}] #8({q.Read(8)}) ≠ {nowStamp}");
            t.Check(3, Regex.IsMatch(q.Read(9), @"^0EC0\d{8}$"), () => $"{ctx} [{code}] #9({q.Read(9)}) 형식 위반");
        }

        string epn = "^0126" + now.ToString("yyMMdd", CultureInfo.InvariantCulture) + @"\d{9}$";
        t.Check(3, Regex.IsMatch(q501.Read(14), epn) && Regex.IsMatch(q902.Read(15), epn), () => $"{ctx} 전자납부번호 형식 위반");
        t.Check(3, q501.Read(11) == "01" && q501.Read(12) == "2600000" && q902.Read(11) == "01" && q902.Read(12) == "2600000",
            () => $"{ctx} #11/#12 위반");
        t.Check(3, q800.Read(11).Length == 0 && q800.Read(12).Length == 0, () => $"{ctx} 800000 #11/#12가 공백이 아님");
        t.Check(3, q902.Read(5) == "000" && q501.Read(5).Length == 0 && q800.Read(5).Length == 0, () => $"{ctx} 요청 #5 규칙 위반");
        t.Check(3, Regex.IsMatch(r501.Read(17), @"^9001011\d{6}$"), () => $"{ctx} 501008 #17({r501.Read(17)}) 형식 위반");
        t.Check(3, Regex.IsMatch(q902.Read(14), @"^9001011\d{6}$") && q902.Read(36) == q902.Read(14), () => $"{ctx} 902614 #14/#36 위반");
        t.Check(3, r902.Read(38) == q902.Read(36), () => $"{ctx} 902614 응답 #38 ≠ 요청 #36");
        t.Check(3, Regex.IsMatch(q902.Read(35), @"^010\d{8}$"), () => $"{ctx} 902614 #35({q902.Read(35)}) 형식 위반");
        t.Check(3, Regex.IsMatch(r501.Read(22), @"^\d{6}$") && Regex.IsMatch(q902.Read(19), @"^\d{6}$"), () => $"{ctx} 계좌번호 형식 위반");
        t.Check(3, q902.Read(16) == "001" && r501.Read(15) == "001" && q902.Read(33) == "66" && q902.Read(34) == "00"
                && q902.Read(39) == "O" && q902.Read(41) == "Q" && q902.Read(49) == "0" && q902.Read(30) == "1" && q902.Read(42).Length == 0,
            () => $"{ctx} 902614 고정값(#16/#33/#34/#39/#41/#49/#30/#42) 위반");
    }

    /// <summary>④ 카드사 쌍, ⑤ 체크카드 규칙.</summary>
    private static void CheckCard(LoopTally t, string ctx, PosTelegram r800)
    {
        string code = r800.Read(17);
        string name = r800.Read(18);
        bool pairOk = RealisticTestValueGenerator.CardCompanies.Any(kv => kv.Key == code && kv.Value == name);
        t.Check(4, pairOk, () => $"{ctx} 800000 #17/#18 쌍({code}/{name})이 SPEC 표에 없음");
        t.SeenCardCodes.Add(code);

        string check = r800.Read(19);
        if (check == "Y")
        {
            t.CheckCardCount++;
            t.Check(5, r800.Read(26) == "0050" && r800.Read(22).Length == 0, () => $"{ctx} 체크카드인데 #26={r800.Read(26)}, #22=\"{r800.Read(22)}\"");
        }
        else
        {
            t.Check(5, check == "N" && r800.Read(26) == "0080" && r800.Read(22) == RealisticTestValueGenerator.CreditInstallmentList,
                () => $"{ctx} 신용카드 규칙 위반 — #19={check}, #26={r800.Read(26)}, #22=\"{r800.Read(22)}\"");
        }

        t.Check(5, r800.Read(20) == "Y" && r800.Read(21) == "N" && r800.Read(23).Length == 0 && r800.Read(27).Length == 0 && r800.Read(28).Length == 0,
            () => $"{ctx} 800000 #20/#21/#23/#27/#28 위반");
    }

    /// <summary>⑥ AHN/AHNS 필드 값은 정해진 목록 안(의미 없는 한글 난수 없음) + "테스트" 표시 + CP949 바이트 길이.
    /// 원캡 필드를 뺀 나머지 비-AHN 필드에는 한글 등 비ASCII가 없어야 한다.</summary>
    private static void CheckHangul(LoopTally t, string ctx, IEnumerable<PosTelegram> telegrams)
    {
        var allowed = new HashSet<string>(RealisticTestValueGenerator.CardCompanies.Select(kv => kv.Value))
        {
            string.Empty,
            RealisticTestValueGenerator.TaxpayerName,
            RealisticTestValueGenerator.CollectingAgencyName,
            RealisticTestValueGenerator.TaxItemName,
            RealisticTestValueGenerator.AccountingName,
            RealisticTestValueGenerator.JurisdictionName,
            RealisticTestValueGenerator.PayerAddress,
        };

        foreach (PosTelegram tg in telegrams)
        {
            string code = tg.Schema.TransactionTypeCode;
            foreach (PosField f in tg.Schema.Fields)
            {
                if (f.Owners.HasFlag(PosFieldOwner.OneCap))
                    continue;

                string v = tg.Read(f.Number);
                if (f.Type == PosFieldType.AHN || f.Type == PosFieldType.AHNS)
                    t.Check(6, allowed.Contains(v), () => $"{ctx} [{code}] AHN #{f.Number} 값이 허용 목록 밖: \"{v}\"");
                else
                    t.Check(6, v.All(c => c >= 0x20 && c < 0x7F), () => $"{ctx} [{code}] 비-AHN #{f.Number}에 비ASCII: \"{v}\"");
            }
        }

        PosTelegram[] arr = telegrams.ToArray();
        PosTelegram r501 = arr[1], q902 = arr[4];
        t.Check(6, r501.Read(18) == "김테스트" && q902.Read(37) == "김테스트", () => $"{ctx} 납세자명/납부자 성명에 \"김테스트\" 없음");
        t.Check(6, r501.Read(19) == "테스트세무서 징수관" && q902.Read(20) == "테스트세무서 징수관", () => $"{ctx} 징수 기관명 \"테스트세무서 징수관\" 없음");
        t.Check(6, r501.Read(42).Contains("테스트"), () => $"{ctx} 501008 #42 주소에 \"테스트\" 없음");
        t.Check(6, PosMessageEncoding.Value.GetByteCount("김테스트") == 8 && PosMessageEncoding.Value.GetByteCount("테스트세무서 징수관") == 19,
            () => "CP949 바이트 길이 위반(김테스트=8, 테스트세무서 징수관=19 기대)");
    }

    private static long Num(PosTelegram tg, int field)
    {
        string v = tg.Read(field);
        return v.Length == 0 ? -1 : long.Parse(v, NumberStyles.None, CultureInfo.InvariantCulture);
    }

    private static DateTime Date(PosTelegram tg, int field) =>
        DateTime.ParseExact(tg.Read(field), "yyyyMMdd", CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------
    // ⑦ 실제 스텁 → 연쇄 → 902614 → 영수증
    // ------------------------------------------------------------------

    private static bool RunStubChainReceiptFlow()
    {
        const string c = "⑦";
        bool ok = true;
        try
        {
            var random = new Random(37);
            DateTime now = DateTime.Now; // 실제 스텁이 DateTime.Now로 응답을 만들므로 같은 기준을 쓴다.
            DateTime today = now.Date;
            var stub = new StubVanRelayService();

            // 1) 501008
            PosTelegram q501 = RealisticTestValueGenerator.GenerateRequest(NoticeInquirySchema.Create(), random, now);
            PosTelegram r501 = RelayThroughStub(stub, q501);

            // 2) 800000 — 501008 응답 연쇄(#15/#16) + 원캡 #14(마스킹 카드번호 더미)
            PosTelegram q800 = RealisticTestValueGenerator.GenerateRequest(CardInfoInquirySchema.Create(), random, now);
            ApplyCrossChain(r501, q800);
            q800.Write(14, OneCapMaskedCardDummy);
            PosTelegram r800 = RelayThroughStub(stub, q800);

            // 3) 902614 — 501008/800000 응답 연쇄 + 고정값(#34) + 자기참조(#29)
            PosTelegram q902 = RealisticTestValueGenerator.GenerateRequest(CardApprovalSchema.Create(), random, now);
            ApplyCrossChain(r501, q902);
            ApplyCrossChain(r800, q902);
            ApplyInternalChain(q902);
            PosTelegram r902 = RelayThroughStub(stub, q902);

            FileLogger.Info(
                $"{Tag} {c} 연쇄 결과 — 501008 #30/#31/#32={r501.Read(30)}/{r501.Read(31)}/{r501.Read(32)}, " +
                $"800000 요청 #15={q800.Read(15)} 응답 #17/#18/#19/#24/#25/#26={r800.Read(17)}/{r800.Read(18)}/{r800.Read(19)}/" +
                $"{r800.Read(24)}/{r800.Read(25)}/{r800.Read(26)}, 902614 요청 #14={q902.Read(14)} #15={q902.Read(15)} " +
                $"#20={q902.Read(20)} #27/#28/#29={q902.Read(27)}/{q902.Read(28)}/{q902.Read(29)} #31={q902.Read(31)} " +
                $"#33={q902.Read(33)} #34={q902.Read(34)} #37={q902.Read(37)}, 응답 #7={r902.Read(7)} #38={r902.Read(38)}");

            ok &= LogCheck($"{c} 스텁 응답 공통부(0210/C/000) 3전문", new[] { r501, r800, r902 }
                .All(r => r.Read(3) == "0210" && r.Read(6) == "C" && r.Read(7) == "000"));
            ok &= LogCheck($"{c} 연쇄 정합 — 902614 #27 = 501008 #30+#31+#32, #28 = 800000 #24, #29 = #27+#28, #33 = 800000 #17",
                Num(q902, 27) == Num(r501, 30) + Num(r501, 31) + Num(r501, 32)
                && Num(q902, 28) == Num(r800, 24) && Num(q902, 29) == Num(q902, 27) + Num(q902, 28)
                && q902.Read(33) == r800.Read(17));
            ok &= LogCheck($"{c} 연쇄 후 의미 유지 — #37 김테스트(10바이트 절삭 무손실), #20 테스트세무서 징수관(20바이트 절삭 무손실), #38=#36",
                q902.Read(37) == "김테스트" && q902.Read(20) == "테스트세무서 징수관" && r902.Read(38) == q902.Read(36));

            // 4) 영수증 — Phase 34 조립기 + 조립기
            Func<int, string?> read501 = n => r501.Read(n);
            Func<int, string?> read800 = n => r800.Read(n);
            Func<int, string?> read902 = n => r902.Read(n);
            ok &= LogCheck($"{c} 승인 판정 + 501008 짝 검사", NationalTaxReceiptAssembler.IsApproved(read902)
                && NationalTaxReceiptAssembler.Use501008(read501, read902));

            NationalTaxReceipt receipt = NationalTaxReceiptAssembler.Assemble(read501, read800, read902);
            List<string> lines = NationalTaxReceiptComposer.Compose(receipt).Lines.Select(l => l.Text).ToList();
            for (int i = 0; i < lines.Count; i++)
                FileLogger.Info($"{Tag} {c} 영수증 줄 {i + 1:D2} | {lines[i]}");

            ok &= CheckReceiptLines(lines, r501, r800, r902, today);
        }
        catch (Exception ex)
        {
            FileLogger.Error(LogCategory.App, $"{Tag} ★ {c} 예외: {ex}");
            return false;
        }

        return ok;
    }

    private static PosTelegram RelayThroughStub(StubVanRelayService stub, PosTelegram request)
    {
        PosRequestParseOutcome parsed = PosRequestTelegram.Parse(request.ToBody());
        if (!parsed.IsSuccess || parsed.Telegram is null)
            throw new InvalidOperationException($"[{request.Schema.TransactionTypeCode}] 요청 파싱 실패: {parsed.ErrorCode}");

        VanRelayOutcome outcome = stub.RelayAsync(parsed.Telegram).GetAwaiter().GetResult();
        if (outcome.ResponseBody is null)
            throw new InvalidOperationException($"[{request.Schema.TransactionTypeCode}] 스텁 응답 본문 없음(Kind={outcome.Kind})");

        return PosTelegram.FromBytes(request.Schema, outcome.ResponseBody);
    }

    /// <summary><c>PaymentScreenViewModel.ApplyChainMappingsFrom</c>과 같은 규칙 — 다른 전문 응답 → 이 요청.</summary>
    private static void ApplyCrossChain(PosTelegram sourceResponse, PosTelegram targetRequest)
    {
        foreach (TelegramFieldChainMap.ChainEntry e in TelegramFieldChainMap.Entries)
        {
            if (e.SourceTelegram != sourceResponse.Schema.TransactionTypeCode
                || e.TargetTelegram != targetRequest.Schema.TransactionTypeCode
                || e.SourceTelegram == e.TargetTelegram)
                continue;

            var sources = e.SourceFieldNumbers.Select(sourceResponse.Read).ToList();
            string computed = TelegramFieldChainConverter.Convert(e.Conversion, sources, targetRequest.Schema[e.TargetFieldNumber].Length, e.FixedValue);
            targetRequest.Write(e.TargetFieldNumber, computed);
        }
    }

    /// <summary>고정값 연쇄(#34) → 자기참조 합산(#29) — <c>PaymentTelegramTabViewModel</c>의 적용 순서와 같다.</summary>
    private static void ApplyInternalChain(PosTelegram request)
    {
        string code = request.Schema.TransactionTypeCode;
        foreach (TelegramFieldChainMap.ChainEntry e in TelegramFieldChainMap.Entries
                     .Where(e => e.TargetTelegram == code && e.Conversion == FieldChainConversion.Fixed))
            request.Write(e.TargetFieldNumber, TelegramFieldChainConverter.Convert(e.Conversion, Array.Empty<string>(), request.Schema[e.TargetFieldNumber].Length, e.FixedValue));

        foreach (TelegramFieldChainMap.ChainEntry e in TelegramFieldChainMap.Entries
                     .Where(e => e.TargetTelegram == code && e.SourceTelegram == code))
        {
            var sources = e.SourceFieldNumbers.Select(request.Read).ToList();
            request.Write(e.TargetFieldNumber, TelegramFieldChainConverter.Convert(e.Conversion, sources, request.Schema[e.TargetFieldNumber].Length, e.FixedValue));
        }
    }

    /// <summary>영수증 줄을 라벨별로 검증한다 — 모든 라벨 줄의 값이 기대값과 같고, 라벨 줄이 아닌 줄은 제목/구분선/빈 줄/
    /// 안내 문구뿐이어야 한다(의미 없는 문자열이 끼어들 자리가 없음).</summary>
    private static bool CheckReceiptLines(List<string> lines, PosTelegram r501, PosTelegram r800, PosTelegram r902, DateTime today)
    {
        const string c = "⑦";
        int width = PrinterProfile.LineWidth;
        long baseTax = Num(r501, 30) + Num(r501, 31) + Num(r501, 32);
        long rate = Num(r800, 26);
        long fee = RealisticTestValueGenerator.FloorFee(baseTax, (int)rate);
        string ratePercent = r800.Read(19) == "Y" ? "0.5" : "0.8";

        var expected = new Dictionary<string, string>
        {
            ["전자납부번호"] = r501.Read(14),
            ["납세자명"] = "김테스트",
            ["납세자번호"] = "900101*******",
            ["세입징수관서"] = "테스트세무서 징수관",
            ["세입징수관계좌번호"] = r501.Read(22),
            ["납기내기한"] = Dotted(r501.Read(26)),
            ["납기내금액(원)"] = Comma(baseTax),
            ["납기후기한"] = Dotted(r501.Read(26)),
            ["납기후금액(원)"] = Comma(baseTax),
            ["납부세액(원)"] = Comma(baseTax),
            ["납부대행수수료(원)"] = Comma(fee),
            ["총 납부금액(원)"] = Comma(baseTax + fee),
            ["선불카드잔액(원)"] = "0",
            ["납부카드"] = r800.Read(18),
            ["할부개월수"] = "일시불",
            ["카드번호"] = "1111-2222-****-444*",
            ["납부일자"] = today.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture),
        };

        bool ok = true;
        var actual = new Dictionary<string, string>();
        var otherLines = new List<string>();
        foreach (string line in lines)
        {
            int idx = line.IndexOf(" :", StringComparison.Ordinal);
            string label = idx > 0 ? line.Substring(0, idx) : string.Empty;
            if (idx > 0 && expected.ContainsKey(label))
                actual[label] = line.Substring(idx + 2).TrimStart();
            else
                otherLines.Add(line);
        }

        foreach (KeyValuePair<string, string> kv in expected)
        {
            actual.TryGetValue(kv.Key, out string? got);
            ok &= LogCheck($"{c} 영수증 \"{kv.Key}\" = \"{kv.Value}\"", got == kv.Value, got is null ? "줄 없음" : $"실제=\"{got}\"");
        }

        ok &= LogCheck($"{c} 납기내기한이 오늘 이후", DateTime.ParseExact(r501.Read(26), "yyyyMMdd", CultureInfo.InvariantCulture) > today);
        ok &= LogCheck($"{c} 납부카드가 SPEC 코드표 이름", RealisticTestValueGenerator.CardCompanies.Any(kv => kv.Value == r800.Read(18) && kv.Key == r800.Read(17)));
        ok &= LogCheck($"{c} 수수료 = floor(납부세액 × {rate}/10000)", Num(r902, 28) == fee);

        // 라벨 줄이 아닌 줄 = 제목·구분선·빈 줄·안내 문구(요율 포함)만.
        string notice2 = $"2.신용카드로 국세를 납부할 경우 납부세액의 {ratePercent}%인 납부대행수수료는 납세자가 부담합니다.";
        var allowedOther = new HashSet<string>(
            ReceiptTextLayout.Wrap("종합소득세 납부확인증", width / 2)
                .Concat(new[] { ReceiptTextLayout.Separator(width), string.Empty })
                .Concat(ReceiptTextLayout.Wrap("1.신용카드로 납부한 국세는 할부거래계약서 및 약관 등에도 불구하고 납부를 취소할 수 없습니다.", width))
                .Concat(ReceiptTextLayout.Wrap(notice2, width)));
        var unexpected = otherLines.Where(l => !allowedOther.Contains(l)).ToList();
        ok &= LogCheck($"{c} 라벨 외 줄은 제목/구분선/빈 줄/안내 문구뿐(요율 {ratePercent}%)", unexpected.Count == 0,
            unexpected.Count == 0 ? null : "예상 밖 줄: " + string.Join(" | ", unexpected.Select(l => $"\"{l}\"")));
        ok &= LogCheck($"{c} 안내 문구 2번에 요율 {ratePercent}% 포함", string.Join("", lines).Contains($"{ratePercent}%인"));

        return ok;
    }

    private static string Comma(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string Dotted(string yyyymmdd) => $"{yyyymmdd.Substring(0, 4)}.{yyyymmdd.Substring(4, 2)}.{yyyymmdd.Substring(6, 2)}";

    private static bool LogCheck(string name, bool ok, string? detail = null)
    {
        if (ok)
            FileLogger.Info($"{Tag} {name} — 통과");
        else
            FileLogger.Error(LogCategory.App, $"{Tag} ★ {name} — 실패{(detail is null ? "" : " — " + detail)}");
        return ok;
    }
}
