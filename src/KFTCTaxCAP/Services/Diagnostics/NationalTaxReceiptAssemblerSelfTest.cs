using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KFTCTaxCAP.Protocol.Printer;
using KFTCTaxCAP.Services.Printer;
using KFTCTaxCAP.Services.Receipt;
using KFTCTaxCAP.Services.Settings;
using KFTCTaxCAP.Services.Storage;
using KFTCTaxCAP.ViewModels.Alerts;
using KFTCTaxCAP.ViewModels.Payment;

namespace KFTCTaxCAP.Services.Diagnostics;

/// <summary>
/// Phase 34 P34-4(docs/receipt_print/development_plan.md) — <see cref="NationalTaxReceiptAssembler"/>가 PRD §2.2
/// 표와 1:1인지, 501008 짝 검사가 전부/전무로 동작하는지, 승인 판정(§2.3)이 맞는지 가짜 탭으로 검증한다.
/// 가짜 탭은 필드마다 <b>다른 표식값</b>(<c>{전문 약자}{필드 번호 2자리}</c>, 예: 501008 #21 → <c>"A21"</c>,
/// 800000 #18 → <c>"B18"</c>, 902614 #37 → <c>"C37"</c>)을 돌려주므로, 영수증 항목에 들어간 표식값만 보면
/// 어느 전문의 몇 번 필드에서 왔는지 바로 판정된다. 값은 더미이며 개인정보가 없다.
///
/// <see cref="ReceiptPrintSelfTest.RunAll"/>이 호출한다(<c>--receipt-print-test self</c>).
/// </summary>
internal static class NationalTaxReceiptAssemblerSelfTest
{
    private const string PairKey = "EPN000000000000001"; // 501008 #14 == 902614 #15 짝 검사용 더미 전자납부번호

    internal static bool RunAll()
    {
        bool case1 = RunAllThreeMatchedCase();
        bool case2 = RunNo501008Case();
        bool case3 = RunMismatched501008Case();
        bool case4 = RunNo800000Case();
        bool case5 = RunApprovalJudgementCases();
        bool case6 = RunMaskedCardNumberLineCase();
        bool case7 = RunEmptyPairKeyCase();
        bool case8 = RunWarningMessageCases();
        bool case9 = RunNotice501008FailedResponseCase();
        bool case10 = RunBlankPrimaryFallsBackCase();
        bool case11 = RunNoSynchronousCompletionCases();

        bool allPassed = case1 && case2 && case3 && case4 && case5 && case6 && case7 && case8
            && case9 && case10 && case11;

        FileLogger.Info(
            "[receipt-print-test self] [Phase34 결제창 조립기] 완료 — " +
            $"①세탭+일치={Mark(case1)}, ②501008없음={Mark(case2)}, ③번호불일치={Mark(case3)}, " +
            $"④800000없음={Mark(case4)}, ⑤승인판정={Mark(case5)}, ⑥카드번호줄={Mark(case6)}, " +
            $"⑦빈 짝키={Mark(case7)}, ⑧경고문구={Mark(case8)}, ⑨501008 #7≠000={Mark(case9)}, " +
            $"⑩1순위 공백→대체={Mark(case10)}, ⑪동기 완료 경로 없음(M-1)={Mark(case11)}, 종합={Mark(allPassed)}");

        return allPassed;
    }

    private static string Mark(bool ok) => ok ? "통과" : "실패";

    // ------------------------------------------------------------------
    // 가짜 탭
    // ------------------------------------------------------------------

    /// <summary>필드 번호 → <c>{prefix}{번호:D2}</c>. <paramref name="overrides"/>로 특정 필드만 바꾼다.</summary>
    private static Func<int, string?> FakeTab(string prefix, IReadOnlyDictionary<int, string?>? overrides = null) =>
        n => overrides != null && overrides.TryGetValue(n, out string? v) ? v : $"{prefix}{n:D2}";

    private static Func<int, string?> Fake501008(string pairKey = PairKey, string responseCode = "000", string? taxpayerName = "A18") =>
        FakeTab("A", new Dictionary<int, string?> { [7] = responseCode, [14] = pairKey, [18] = taxpayerName });

    private static Func<int, string?> Fake800000() => FakeTab("B");

    private static Func<int, string?> Fake902614(string responseCode = "000", string pairKey = PairKey) =>
        FakeTab("C", new Dictionary<int, string?> { [7] = responseCode, [15] = pairKey });

    // ------------------------------------------------------------------
    // 기대값 (PRD §2.2 표를 그대로 옮김 — 조립기 코드를 보지 않고 표에서 직접 적었다)
    // ------------------------------------------------------------------

    private static Dictionary<string, string?> Expected(bool with501008, bool with800000) => new()
    {
        ["ElectronicPaymentNumber"] = PairKey,                   // #1 902614 #15
        ["TaxpayerName"] = with501008 ? "A18" : "C37",           // #2
        ["TaxpayerNumber"] = with501008 ? "A17" : "C14",         // #3
        ["CollectingAgency"] = with501008 ? "A19" : "C20",       // #4
        ["AccountNumber"] = "C19",                               // #5
        ["DueDateWithinPeriod"] = with501008 ? "A26" : null,     // #6
        ["AmountWithinPeriod"] = with501008 ? "A25" : null,      // #7
        ["DueDateAfterPeriod"] = with501008 ? "A28" : null,      // #8
        ["AmountAfterPeriod"] = with501008 ? "A27" : null,       // #9
        ["TaxAmount"] = "C27",                                   // #10
        ["Fee"] = "C28",                                         // #11
        ["TotalAmount"] = "C29",                                 // #12
        ["PrepaidCardBalance"] = "C52",                          // #13
        ["CardCompanyName"] = with800000 ? "B18" : null,         // #14
        ["InstallmentMonths"] = "C34",                           // #15
        ["CardNumber"] = with800000 ? "B14" : null,              // #16
        ["PaymentDate"] = "C32",                                 // #17
        ["FeeRatePercentRaw"] = with800000 ? "B26" : null,       // F
    };

    private static Dictionary<string, string?> Actual(NationalTaxReceipt r) => new()
    {
        ["ElectronicPaymentNumber"] = r.ElectronicPaymentNumber,
        ["TaxpayerName"] = r.TaxpayerName,
        ["TaxpayerNumber"] = r.TaxpayerNumber,
        ["CollectingAgency"] = r.CollectingAgency,
        ["AccountNumber"] = r.AccountNumber,
        ["DueDateWithinPeriod"] = r.DueDateWithinPeriod,
        ["AmountWithinPeriod"] = r.AmountWithinPeriod,
        ["DueDateAfterPeriod"] = r.DueDateAfterPeriod,
        ["AmountAfterPeriod"] = r.AmountAfterPeriod,
        ["TaxAmount"] = r.TaxAmount,
        ["Fee"] = r.Fee,
        ["TotalAmount"] = r.TotalAmount,
        ["PrepaidCardBalance"] = r.PrepaidCardBalance,
        ["CardCompanyName"] = r.CardCompanyName,
        ["InstallmentMonths"] = r.InstallmentMonths,
        ["CardNumber"] = r.CardNumber,
        ["PaymentDate"] = r.PaymentDate,
        ["FeeRatePercentRaw"] = r.FeeRatePercentRaw,
    };

    private static bool CompareModel(string caseName, NationalTaxReceipt actual, Dictionary<string, string?> expected)
    {
        Dictionary<string, string?> actualValues = Actual(actual);
        var mismatches = expected
            .Where(kv => actualValues[kv.Key] != kv.Value)
            .Select(kv => $"{kv.Key}: 기대={Show(kv.Value)}, 실제={Show(actualValues[kv.Key])}")
            .ToList();

        if (actual.IsReprint)
            mismatches.Add("IsReprint: 기대=false, 실제=true");

        bool ok = mismatches.Count == 0;
        if (ok)
            FileLogger.Info($"[receipt-print-test self] [조립기 — {caseName}] 통과 — {expected.Count}개 항목 전부 PRD §2.2 출처와 일치");
        else
            FileLogger.Error(LogCategory.App,
                $"[receipt-print-test self] ★ [조립기 — {caseName}] 불일치 {mismatches.Count}건 — {string.Join(" / ", mismatches)}");
        return ok;
    }

    private static string Show(string? v) => v is null ? "null" : $"\"{v}\"";

    // ------------------------------------------------------------------
    // 케이스
    // ------------------------------------------------------------------

    /// <summary>① 세 탭 모두 + 전자납부번호 일치 → 501008 1순위 전부, 800000 값 사용.</summary>
    private static bool RunAllThreeMatchedCase()
    {
        NationalTaxReceipt r = NationalTaxReceiptAssembler.Assemble(Fake501008(), Fake800000(), Fake902614());
        return CompareModel("① 세 탭 + 번호 일치", r, Expected(with501008: true, with800000: true));
    }

    /// <summary>② 501008 응답 없음 → 대체 출처(902614 #21/#37/#14/#20) + 납기 4줄 null.</summary>
    private static bool RunNo501008Case()
    {
        // 두 형태 모두: 읽기 함수 자체가 null / 읽기 함수가 모든 필드에 null(탭은 있으나 응답 없음).
        NationalTaxReceipt r1 = NationalTaxReceiptAssembler.Assemble(null, Fake800000(), Fake902614());
        NationalTaxReceipt r2 = NationalTaxReceiptAssembler.Assemble(_ => null, Fake800000(), Fake902614());
        return CompareModel("② 501008 없음(함수 null)", r1, Expected(with501008: false, with800000: true))
             & CompareModel("② 501008 없음(응답 null)", r2, Expected(with501008: false, with800000: true));
    }

    /// <summary>③ 501008 응답은 있으나 전자납부번호 불일치 → ②와 같다(부분 적용 없음).</summary>
    private static bool RunMismatched501008Case()
    {
        NationalTaxReceipt r = NationalTaxReceiptAssembler.Assemble(
            Fake501008(pairKey: "EPN999999999999999"), Fake800000(), Fake902614());
        // 트림 비교 확인: 뒤 공백만 다른 키는 일치로 본다.
        NationalTaxReceipt rTrim = NationalTaxReceiptAssembler.Assemble(
            Fake501008(pairKey: PairKey + "  "), Fake800000(), Fake902614());
        return CompareModel("③ 번호 불일치", r, Expected(with501008: false, with800000: true))
             & CompareModel("③' 뒤 공백만 다름(트림 후 일치)", rTrim, Expected(with501008: true, with800000: true));
    }

    /// <summary>④ 800000 응답 없음 → 카드사명·요율·카드번호 null(영수증에서는 빈칸/요율 없는 문구).</summary>
    private static bool RunNo800000Case()
    {
        NationalTaxReceipt r1 = NationalTaxReceiptAssembler.Assemble(Fake501008(), null, Fake902614());
        NationalTaxReceipt r2 = NationalTaxReceiptAssembler.Assemble(Fake501008(), _ => null, Fake902614());
        bool ok = CompareModel("④ 800000 없음(함수 null)", r1, Expected(with501008: true, with800000: false))
                & CompareModel("④ 800000 없음(응답 null)", r2, Expected(with501008: true, with800000: false));

        // 영수증 결과: 납부카드/카드번호 라벨만, 2번 문구는 요율 없는 형태.
        List<string> lines = NationalTaxReceiptComposer.Compose(r1).Lines.Select(l => l.Text).ToList();
        string notice2NoRate = string.Join("",
            ReceiptTextLayout.Wrap("2.신용카드로 국세를 납부할 경우 납부대행수수료는 납세자가 부담합니다.", PrinterProfile.LineWidth));
        bool composed = lines.Contains("납부카드 :") && lines.Contains("카드번호 :")
            && string.Join("", lines).Contains(notice2NoRate);
        LogCheck("④ 800000 없음 → 영수증 납부카드/카드번호 빈칸 + 요율 없는 문구", composed);
        return ok && composed;
    }

    /// <summary>⑤ 승인 판정 — <c>#7</c>(트림)이 000일 때만 true.</summary>
    private static bool RunApprovalJudgementCases()
    {
        bool ok = true;
        ok &= CheckJudgement("#7=000", NationalTaxReceiptAssembler.IsApproved(Fake902614("000")), true);
        ok &= CheckJudgement("#7=111(인터넷지로 오류)", NationalTaxReceiptAssembler.IsApproved(Fake902614("111")), false);
        ok &= CheckJudgement("#7=R32(원캡 오류)", NationalTaxReceiptAssembler.IsApproved(Fake902614("R32")), false);
        ok &= CheckJudgement("#7=공백", NationalTaxReceiptAssembler.IsApproved(Fake902614("   ")), false);
        ok &= CheckJudgement("응답 파싱 실패(모든 필드 null)", NationalTaxReceiptAssembler.IsApproved(_ => null), false);
        ok &= CheckJudgement("읽기 함수 null", NationalTaxReceiptAssembler.IsApproved(null), false);
        return ok;
    }

    private static bool CheckJudgement(string name, bool actual, bool expected)
    {
        bool ok = actual == expected;
        LogCheck($"⑤ 승인 판정 {name} → {expected}", ok, ok ? null : $"실제={actual}");
        return ok;
    }

    /// <summary>
    /// ⑥ 800000 응답 <c>#14</c> 마스킹 카드번호(리더기가 이미 마스킹, 더미값) → 영수증 줄
    /// <c>카드번호 : 1111-2222-****-444*</c>. 패딩(뒤 공백)이 남은 원문이어도 같은 줄이 나와야 한다.
    /// </summary>
    private static bool RunMaskedCardNumberLineCase()
    {
        bool ok = true;
        foreach (string raw in new[] { "11112222****444*", "11112222****444*   " })
        {
            Func<int, string?> card = FakeTab("B", new Dictionary<int, string?> { [14] = raw });
            NationalTaxReceipt r = NationalTaxReceiptAssembler.Assemble(Fake501008(), card, Fake902614());
            bool found = NationalTaxReceiptComposer.Compose(r).Lines.Any(l => l.Text == "카드번호 : 1111-2222-****-444*");
            LogCheck($"⑥ 800000 #14 {Show(raw)} → \"카드번호 : 1111-2222-****-444*\"", found);
            ok &= found;
        }
        return ok;
    }

    /// <summary>⑦ 짝 키가 양쪽 모두 빈 값 → 우연한 일치로 보지 않고 501008을 쓰지 않는다.</summary>
    private static bool RunEmptyPairKeyCase()
    {
        bool use = NationalTaxReceiptAssembler.Use501008(Fake501008(pairKey: "   "), Fake902614(pairKey: ""));
        LogCheck("⑦ 501008 #14·902614 #15 둘 다 빈 값 → 501008 미사용", !use);
        return !use;
    }

    /// <summary>
    /// 결제창 자동 출력 경고창 끝에 붙는 재출력 안내 줄(PRD §5, Phase 35 — 2026-10-01 확정, Phase 39에서
    /// 2026-10-01 축약 문구로 교체). 상수를 참조하지 않고 PRD 문구를 그대로 적어 둔다(구현 상수가 틀려도
    /// 잡히도록, docs/alert_dialog/PRD.md §4.4).
    /// </summary>
    internal const string ExpectedReprintHintSuffix = "\n가맹점 설정에서 다시 출력하세요.";

    /// <summary>⑧ 결제창 경고 문구(PRD §5 표 + Phase 35부터 끝에 재출력 안내 한 줄, Phase 39 새 형식 —
    /// 제목 `전표 출력 실패` + 사유 줄, 행 없는 사유는 제목만).</summary>
    private static bool RunWarningMessageCases()
    {
        var expected = new (ReceiptPrintFailureReason Reason, string Text)[]
        {
            (ReceiptPrintFailureReason.InvalidPort, "전표 출력 실패\n프린터 포트 설정 확인." + ExpectedReprintHintSuffix),
            (ReceiptPrintFailureReason.PortOpenFailed, "전표 출력 실패\n프린터 포트를 열 수 없습니다." + ExpectedReprintHintSuffix),
            (ReceiptPrintFailureReason.NoResponse, "전표 출력 실패\n프린터 응답 없음." + ExpectedReprintHintSuffix),
            (ReceiptPrintFailureReason.PaperEnd, "전표 출력 실패\n용지 없음." + ExpectedReprintHintSuffix),
            (ReceiptPrintFailureReason.CoverOpen, "전표 출력 실패\n프린터 덮개 열림." + ExpectedReprintHintSuffix),
            (ReceiptPrintFailureReason.WriteFailed, "전표 출력 실패\n전송 오류." + ExpectedReprintHintSuffix),
            (ReceiptPrintFailureReason.PrinterError, "전표 출력 실패" + ExpectedReprintHintSuffix),
            (ReceiptPrintFailureReason.CompositionFailed, "전표 출력 실패" + ExpectedReprintHintSuffix),
            (ReceiptPrintFailureReason.Unexpected, "전표 출력 실패" + ExpectedReprintHintSuffix),
        };

        bool ok = true;
        foreach ((ReceiptPrintFailureReason reason, string text) in expected)
        {
            string actual = PaymentScreenViewModel.BuildReceiptPrintWarningMessage(reason);
            bool one = actual == text;
            LogCheck($"⑧ 경고 문구 {reason}", one, one ? null : $"기대={Show(text)}, 실제={Show(actual)}");
            ok &= one;
        }
        return ok;
    }

    /// <summary>⑨ 501008 응답 <c>#7</c>≠000(조회 실패 — 요청 에코로 전자납부번호만 같음) → 501008 미사용(②와 같음).</summary>
    private static bool RunNotice501008FailedResponseCase()
    {
        NationalTaxReceipt r = NationalTaxReceiptAssembler.Assemble(
            Fake501008(responseCode: "111"), Fake800000(), Fake902614());
        bool use = NationalTaxReceiptAssembler.Use501008(Fake501008(responseCode: "   "), Fake902614());
        LogCheck("⑨' 501008 #7 공백 → Use501008=false", !use);
        return CompareModel("⑨ 501008 #7=111(번호는 일치)", r, Expected(with501008: false, with800000: true)) & !use;
    }

    /// <summary>⑩ 짝 검사 통과했지만 501008 <c>#18</c> 납세자명이 공백뿐 → 902614 <c>#37</c>. 나머지 501008 항목은 그대로 1순위.</summary>
    private static bool RunBlankPrimaryFallsBackCase()
    {
        NationalTaxReceipt r = NationalTaxReceiptAssembler.Assemble(
            Fake501008(taxpayerName: "    "), Fake800000(), Fake902614());
        Dictionary<string, string?> expected = Expected(with501008: true, with800000: true);
        expected["TaxpayerName"] = "C37";
        NationalTaxReceipt rNull = NationalTaxReceiptAssembler.Assemble(
            Fake501008(taxpayerName: null), Fake800000(), Fake902614());
        return CompareModel("⑩ 1순위 납세자명 공백 → 902614 #37", r, expected)
             & CompareModel("⑩' 1순위 납세자명 null → 902614 #37", rNull, expected);
    }

    /// <summary>
    /// ⑪ CP1 M-1 — <see cref="PaymentScreenViewModel.PrintReceiptIfApprovedAsync"/>에 <b>동기 완료 경로가 없다</b>:
    /// 출력 서비스가 즉시(동기로) 실패해도 메서드가 반환되는 시점에는 출력이 아직 시작되지 않았고(프린터 호출 0회),
    /// 경고 이벤트도 아직 올라가지 않았다. 호출 컨텍스트로 "Post를 큐에만 쌓는" SynchronizationContext를 써서
    /// (UI 디스패처 흉내) 결정적으로 판정한 뒤, 큐를 돌려 끝까지 진행되면 이벤트가 정확히 1번 올라오는지 본다.
    /// 두 형태: Fake 프린터 즉시 실패(InvalidPort) / 실제 <see cref="SerialReceiptPrinter"/> + 포트 빈 값(실제 동기 실패 경로).
    /// </summary>
    private static bool RunNoSynchronousCompletionCases()
    {
        var fake = new FakeReceiptPrinter(PrintResult.Fail(PrintFailureReason.InvalidPort));
        bool ok = RunNoSynchronousCompletionCase("Fake 즉시 실패", fake, printerPort: "5", () => fake.CallCount);
        ok &= RunNoSynchronousCompletionCase("SerialReceiptPrinter 포트 빈 값", new SerialReceiptPrinter(), printerPort: "", null);
        return ok;
    }

    private static bool RunNoSynchronousCompletionCase(string name, IReceiptPrinter printer, string printerPort, Func<int>? callCount)
    {
        var service = new ReceiptPrintService(
            () => new ShopSettings { SlipPrintEnabled = true, PrinterPort = printerPort, PrinterSpeed = 115200 },
            printer);
        // Phase 35 — 자동 출력 경로가 승인 시 직전 영수증을 저장하므로 운영 DB가 아닌 임시 DB를 넣는다.
        string dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"p35-assembler-test-{Guid.NewGuid():N}.db");
        try
        {
            return RunNoSynchronousCompletionCaseCore(name, service, new LastReceiptStore(dbPath), callCount);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { System.IO.File.Delete(dbPath); } catch (Exception) { /* 임시 파일 — 지우지 못해도 판정과 무관 */ }
        }
    }

    private static bool RunNoSynchronousCompletionCaseCore(
        string name, ReceiptPrintService service, LastReceiptStore store, Func<int>? callCount)
    {
        var viewModel = new PaymentScreenViewModel(service, store);
        var messages = new List<AlertMessage>();
        viewModel.AlertRequested += (_, m) => messages.Add(m);

        SynchronizationContext? previous = SynchronizationContext.Current;
        var context = new QueueSynchronizationContext();
        Task task;
        bool atReturnOk;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            task = viewModel.PrintReceiptIfApprovedAsync(Fake902614("000"));

            // 반환 시점: 아직 끝나지 않았고, 이벤트 0회, (Fake면) 프린터 호출 0회.
            atReturnOk = !task.IsCompleted && messages.Count == 0 && (callCount is null || callCount() == 0);

            context.RunUntil(task, TimeSpan.FromSeconds(10));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        const string expectedMessage = "전표 출력 실패\n프린터 포트 설정 확인." + ExpectedReprintHintSuffix;
        bool finalOk = task.IsCompleted && messages.Count == 1 && messages[0].Text == expectedMessage
            && messages[0].Kind == AlertKind.Warning
            && (callCount is null || callCount() == 1);

        LogCheck($"⑪ {name} — 반환 시점 미완료·이벤트 0회", atReturnOk,
            atReturnOk ? null : $"IsCompleted={task.IsCompleted}, 이벤트={messages.Count}, 호출={callCount?.Invoke()}");
        LogCheck($"⑪ {name} — 큐 처리 후 경고 이벤트 1회(InvalidPort 문구)", finalOk,
            finalOk ? null : $"IsCompleted={task.IsCompleted}, 이벤트={messages.Count}, 호출={callCount?.Invoke()}");
        return atReturnOk && finalOk;
    }

    /// <summary>Post를 큐에만 쌓고 <see cref="RunUntil"/>이 호출 스레드에서 하나씩 실행하는 컨텍스트(디스패처 흉내).</summary>
    private sealed class QueueSynchronizationContext : SynchronizationContext
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();

        public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

        public override void Send(SendOrPostCallback d, object? state) => throw new NotSupportedException();

        public void RunUntil(Task task, TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (!task.IsCompleted && DateTime.UtcNow < deadline)
            {
                if (_queue.TryTake(out (SendOrPostCallback Callback, object? State) item, TimeSpan.FromMilliseconds(50)))
                    item.Callback(item.State);
            }
        }
    }

    private static void LogCheck(string name, bool ok, string? detail = null)
    {
        if (ok)
            FileLogger.Info($"[receipt-print-test self] [조립기 — {name}] 통과");
        else
            FileLogger.Error(LogCategory.App, $"[receipt-print-test self] ★ [조립기 — {name}] 실패{(detail is null ? "" : " — " + detail)}");
    }
}
