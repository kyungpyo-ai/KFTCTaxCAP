using System;
using System.Collections.Generic;
using KFTCTaxCAP.ViewModels.Alerts;

namespace KFTCTaxCAP.Services.Diagnostics;

/// <summary>
/// Phase 38 P38-4 전용 셀프테스트(docs/alert_dialog/development_plan.md P38-1·P38-4 완료 조건) —
/// <see cref="AlertTextSplitter"/>/<see cref="AlertBehavior"/>/<see cref="AlertDialogViewModel"/>을 창을
/// 띄우지 않고 순수 로직으로만 검증한다(<c>--alert-dialog-test self</c>). 다른 <c>*SelfTest</c>와 같은
/// <c>Check(...)</c> + <see cref="FileLogger"/> 통과/실패 로그 방식을 쓴다.
/// </summary>
internal static class AlertDialogSelfTest
{
    internal static bool RunAll()
    {
        bool splitOk = RunSplitCases();
        bool buttonsOk = RunButtonsForCases();
        bool enterOk = RunResultForEnterCases();
        bool escapeOk = RunResultForEscapeCases();
        bool soundOk = RunSoundForCases();
        bool viewModelOk = RunViewModelCases();
        bool wrapOk = RunWordWrapCases();

        bool allPassed = splitOk && buttonsOk && enterOk && escapeOk && soundOk && viewModelOk && wrapOk;

        FileLogger.Info(
            "[alert-dialog-test self] [Phase38 알림창] 완료 — " +
            $"Split={(splitOk ? "통과" : "실패")}, ButtonsFor={(buttonsOk ? "통과" : "실패")}, " +
            $"ResultForEnter={(enterOk ? "통과" : "실패")}, ResultForEscape={(escapeOk ? "통과" : "실패")}, " +
            $"SoundFor={(soundOk ? "통과" : "실패")}, ViewModel={(viewModelOk ? "통과" : "실패")}, " +
            $"WordWrap={(wrapOk ? "통과" : "실패")}, " +
            $"종합={(allPassed ? "통과" : "실패")}");

        return allPassed;
    }

    // ------------------------------------------------------------------
    // AlertTextSplitter.Split — PRD §3.1·개발계획 P38-1/P38-4 케이스.
    // ------------------------------------------------------------------

    private static bool RunSplitCases()
    {
        bool ok = true;

        ok &= CheckSplit("\"제목\"", "제목", ("제목", string.Empty));
        ok &= CheckSplit("\"제목\\n본문\"", "제목\n본문", ("제목", "본문"));
        ok &= CheckSplit("\"제목\\r\\n본문1\\r\\n본문2\"", "제목\r\n본문1\r\n본문2", ("제목", "본문1\n본문2"));
        ok &= CheckSplit("\"\"(빈 문자열)", string.Empty, (string.Empty, string.Empty));
        ok &= CheckSplit("null", null, (string.Empty, string.Empty));
        ok &= CheckSplit("\"\\n본문\"(제목=본문)", "\n본문", ("본문", string.Empty));
        ok &= CheckSplit("\"제목\\n\"", "제목\n", ("제목", string.Empty));
        ok &= CheckSplit("\"  제목  \\n  본문  \"", "  제목  \n  본문  ", ("제목", "본문"));
        // CP1 L-1 — \r\n과 \n이 섞인 입력, \r만 있는 입력에서 제목에 \r이 남지 않는지.
        ok &= CheckSplit("\"제목\\r\\n본문1\\n본문2\"(혼합)", "제목\r\n본문1\n본문2", ("제목", "본문1\n본문2"));
        ok &= CheckSplit("\"제목\\r\"", "제목\r", ("제목", string.Empty));
        ok &= CheckSplit("\"\\r\\n\\r\\n\"(줄바꿈만)", "\r\n\r\n", (string.Empty, string.Empty));

        return ok;
    }

    private static bool CheckSplit(string caseName, string? input, (string Title, string Body) expected)
    {
        var actual = AlertTextSplitter.Split(input);
        bool ok = actual.Title == expected.Title && actual.Body == expected.Body;

        if (ok)
        {
            FileLogger.Info($"[alert-dialog-test self] [Split — {caseName}] 통과 — Title=\"{actual.Title}\", Body=\"{actual.Body}\"");
        }
        else
        {
            FileLogger.Error(LogCategory.App,
                $"[alert-dialog-test self] ★ [Split — {caseName}] 불일치 — 기대=(Title=\"{expected.Title}\", Body=\"{expected.Body}\"), " +
                $"실제=(Title=\"{actual.Title}\", Body=\"{actual.Body}\")");
        }

        return ok;
    }

    // ------------------------------------------------------------------
    // AlertBehavior.ButtonsFor — 순서·문구·IsPrimary(D3 확정 — 주 버튼 왼쪽).
    // ------------------------------------------------------------------

    private static bool RunButtonsForCases()
    {
        bool ok = true;

        ok &= CheckButtons(AlertButtons.OK, new[]
        {
            (AlertResult.OK, "확인", true),
        });

        ok &= CheckButtons(AlertButtons.OKCancel, new[]
        {
            (AlertResult.OK, "확인", true),
            (AlertResult.Cancel, "취소", false),
        });

        ok &= CheckButtons(AlertButtons.YesNo, new[]
        {
            (AlertResult.Yes, "예", true),
            (AlertResult.No, "아니요", false),
        });

        return ok;
    }

    private static bool CheckButtons(AlertButtons buttons, (AlertResult Result, string Label, bool IsPrimary)[] expected)
    {
        IReadOnlyList<AlertButtonSpec> actual = AlertBehavior.ButtonsFor(buttons);
        bool ok = actual.Count == expected.Length;

        if (ok)
        {
            for (int i = 0; i < expected.Length; i++)
            {
                if (actual[i].Result != expected[i].Result || actual[i].Label != expected[i].Label || actual[i].IsPrimary != expected[i].IsPrimary)
                {
                    ok = false;
                    break;
                }
            }
        }

        string actualText = string.Join(" | ", ToDisplay(actual));
        if (ok)
        {
            FileLogger.Info($"[alert-dialog-test self] [ButtonsFor — {buttons}] 통과 — {actualText}");
        }
        else
        {
            string expectedText = string.Join(" | ", ToDisplay(expected));
            FileLogger.Error(LogCategory.App,
                $"[alert-dialog-test self] ★ [ButtonsFor — {buttons}] 불일치 — 기대=[{expectedText}], 실제=[{actualText}]");
        }

        return ok;
    }

    private static IEnumerable<string> ToDisplay(IReadOnlyList<AlertButtonSpec> specs)
    {
        foreach (var s in specs)
            yield return $"{s.Result}/\"{s.Label}\"/{(s.IsPrimary ? "주" : "보조")}";
    }

    private static IEnumerable<string> ToDisplay((AlertResult Result, string Label, bool IsPrimary)[] specs)
    {
        foreach (var s in specs)
            yield return $"{s.Result}/\"{s.Label}\"/{(s.IsPrimary ? "주" : "보조")}";
    }

    // ------------------------------------------------------------------
    // AlertBehavior.ResultForEnter / ResultForEscape.
    // ------------------------------------------------------------------

    private static bool RunResultForEnterCases()
    {
        bool ok = true;
        ok &= CheckEnum("ResultForEnter(OK)", AlertBehavior.ResultForEnter(AlertButtons.OK), AlertResult.OK);
        ok &= CheckEnum("ResultForEnter(OKCancel)", AlertBehavior.ResultForEnter(AlertButtons.OKCancel), AlertResult.OK);
        ok &= CheckEnum("ResultForEnter(YesNo)", AlertBehavior.ResultForEnter(AlertButtons.YesNo), AlertResult.Yes);
        return ok;
    }

    private static bool RunResultForEscapeCases()
    {
        bool ok = true;
        ok &= CheckEnum("ResultForEscape(OK)", AlertBehavior.ResultForEscape(AlertButtons.OK), AlertResult.OK);
        ok &= CheckEnum("ResultForEscape(OKCancel)", AlertBehavior.ResultForEscape(AlertButtons.OKCancel), AlertResult.Cancel);
        ok &= CheckEnum("ResultForEscape(YesNo)", AlertBehavior.ResultForEscape(AlertButtons.YesNo), AlertResult.No);
        return ok;
    }

    private static bool RunSoundForCases()
    {
        bool ok = true;
        ok &= CheckEnum("SoundFor(Info)", AlertBehavior.SoundFor(AlertKind.Info), AlertSound.None);
        ok &= CheckEnum("SoundFor(Warning)", AlertBehavior.SoundFor(AlertKind.Warning), AlertSound.Exclamation);
        ok &= CheckEnum("SoundFor(Error)", AlertBehavior.SoundFor(AlertKind.Error), AlertSound.Hand);
        ok &= CheckEnum("SoundFor(Success)", AlertBehavior.SoundFor(AlertKind.Success), AlertSound.None);
        ok &= CheckEnum("SoundFor(Question)", AlertBehavior.SoundFor(AlertKind.Question), AlertSound.None);
        return ok;
    }

    private static bool CheckEnum<T>(string caseName, T actual, T expected) where T : struct, Enum
    {
        bool ok = actual.Equals(expected);
        if (ok)
        {
            FileLogger.Info($"[alert-dialog-test self] [{caseName}] 통과 — {actual}");
        }
        else
        {
            FileLogger.Error(LogCategory.App,
                $"[alert-dialog-test self] ★ [{caseName}] 불일치 — 기대={expected}, 실제={actual}");
        }

        return ok;
    }

    // ------------------------------------------------------------------
    // AlertDialogViewModel.
    // ------------------------------------------------------------------

    private static bool RunViewModelCases()
    {
        bool ok = true;

        ok &= RunSelectSecondCallIgnoredCase();
        ok &= RunResolveResultOnCloseWithoutSelectCase();
        ok &= RunResolveResultOnCloseKeepsExistingResultCase();
        ok &= RunCloseRequestedFiresOnceCase();
        ok &= RunCopyTextCases();
        ok &= RunHasBodyCases();

        return ok;
    }

    /// <summary>완료 조건: 두 번째 Select 호출은 무시된다(첫 결과가 그대로 유지).</summary>
    private static bool RunSelectSecondCallIgnoredCase()
    {
        var vm = new AlertDialogViewModel("제목\n본문", AlertKind.Info, AlertButtons.OKCancel);
        vm.Select(AlertResult.OK);
        vm.Select(AlertResult.Cancel); // 무시되어야 함.

        bool ok = vm.Result == AlertResult.OK;
        LogViewModelCheck("Select 두 번째 호출 무시", ok, $"Result={vm.Result}");
        return ok;
    }

    /// <summary>완료 조건: 결과 없이 ResolveResultOnClose() → ESC 결과(OKCancel이면 Cancel).</summary>
    private static bool RunResolveResultOnCloseWithoutSelectCase()
    {
        var vm = new AlertDialogViewModel("제목\n본문", AlertKind.Info, AlertButtons.OKCancel);
        var resolved = vm.ResolveResultOnClose();

        bool ok = resolved == AlertResult.Cancel && vm.Result == AlertResult.Cancel;
        LogViewModelCheck("ResolveResultOnClose(결과 없음) → ESC 결과", ok, $"resolved={resolved}, Result={vm.Result}");
        return ok;
    }

    /// <summary>완료 조건: 이미 결과가 있으면 ResolveResultOnClose()가 그 결과를 그대로 유지한다.</summary>
    private static bool RunResolveResultOnCloseKeepsExistingResultCase()
    {
        var vm = new AlertDialogViewModel("제목\n본문", AlertKind.Question, AlertButtons.YesNo);
        vm.Select(AlertResult.Yes);
        var resolved = vm.ResolveResultOnClose();

        bool ok = resolved == AlertResult.Yes && vm.Result == AlertResult.Yes;
        LogViewModelCheck("ResolveResultOnClose(이미 결과 있음) → 유지", ok, $"resolved={resolved}, Result={vm.Result}");
        return ok;
    }

    /// <summary>완료 조건: CloseRequested는 (첫 번째 결정 시) 정확히 1회만 발생한다.</summary>
    private static bool RunCloseRequestedFiresOnceCase()
    {
        var vm = new AlertDialogViewModel("제목\n본문", AlertKind.Info, AlertButtons.OK);
        int fireCount = 0;
        vm.CloseRequested += (_, _) => fireCount++;

        vm.Select(AlertResult.OK);
        vm.Select(AlertResult.OK); // 중복 호출 — 추가 발생 없어야 함.
        vm.ResolveResultOnClose(); // 이미 결과 있음 — 추가 발생 없어야 함.

        bool ok = fireCount == 1;
        LogViewModelCheck("CloseRequested 1회만 발생", ok, $"fireCount={fireCount}");
        return ok;
    }

    /// <summary>완료 조건: CopyText — 본문 있음(제목+빈 줄+본문), 본문 없음(제목만).</summary>
    private static bool RunCopyTextCases()
    {
        bool ok = true;

        var withBody = new AlertDialogViewModel("제목\n본문1\n본문2", AlertKind.Info, AlertButtons.OK);
        string expectedWithBody = "제목" + Environment.NewLine + Environment.NewLine + "본문1" + Environment.NewLine + "본문2";
        bool withBodyOk = withBody.CopyText == expectedWithBody;
        LogViewModelCheck("CopyText(본문 있음)", withBodyOk, $"CopyText=\"{withBody.CopyText.Replace("\r\n", "\\r\\n")}\"");
        ok &= withBodyOk;

        var withoutBody = new AlertDialogViewModel("제목만", AlertKind.Info, AlertButtons.OK);
        bool withoutBodyOk = withoutBody.CopyText == "제목만";
        LogViewModelCheck("CopyText(본문 없음)", withoutBodyOk, $"CopyText=\"{withoutBody.CopyText}\"");
        ok &= withoutBodyOk;

        return ok;
    }

    /// <summary>완료 조건: HasBody — 본문 있을 때 true, 없을 때 false.</summary>
    private static bool RunHasBodyCases()
    {
        bool ok = true;

        var withBody = new AlertDialogViewModel("제목\n본문", AlertKind.Info, AlertButtons.OK);
        ok &= CheckBool("HasBody(본문 있음)", withBody.HasBody, true);

        var withoutBody = new AlertDialogViewModel("제목만", AlertKind.Info, AlertButtons.OK);
        ok &= CheckBool("HasBody(본문 없음)", withoutBody.HasBody, false);

        return ok;
    }

    private static bool CheckBool(string caseName, bool actual, bool expected)
    {
        bool ok = actual == expected;
        if (ok)
        {
            FileLogger.Info($"[alert-dialog-test self] [{caseName}] 통과 — {actual}");
        }
        else
        {
            FileLogger.Error(LogCategory.App,
                $"[alert-dialog-test self] ★ [{caseName}] 불일치 — 기대={expected}, 실제={actual}");
        }

        return ok;
    }

    // ------------------------------------------------------------------
    // AlertWordWrapper.Wrap — 본문 어절 단위 줄바꿈(PRD §4.4, keep-all). 가짜 측정 함수: 글자(공백 포함)당 10.
    // ------------------------------------------------------------------

    private static double FakeMeasure(string s) => s.Length * 10;

    private static bool RunWordWrapCases()
    {
        bool ok = true;

        // 어절 경계에서만 줄바꿈 — "가나다 라마바"(70) 다음 " 사아"를 붙이면 100 > 75.
        ok &= CheckWrap("어절 경계", "가나다 라마바 사아", 75, "가나다 라마바\n사아");
        // 낱말 중간에서 끊지 않음 — 글자 단위였다면 "지정할 수 없습니\n다."가 되는 폭(80).
        ok &= CheckWrap("낱말 중간 끊김 없음", "지정할 수 없습니다.", 80, "지정할 수\n없습니다.");
        // 한 어절이 폭보다 길 때만 글자 단위로 끊는다.
        ok &= CheckWrap("긴 단일 어절 글자 단위", "가나다라마바사아자차", 35, "가나다\n라마바\n사아자\n차");
        ok &= CheckWrap("긴 어절 앞 짧은 어절", "AB 가나다라마바", 35, "AB\n가나다\n라마바");
        // 기존 \n(단락 경계)과 빈 단락 보존.
        ok &= CheckWrap("기존 \\n 보존", "가나 다라\n마바", 1000, "가나 다라\n마바");
        ok &= CheckWrap("빈 단락 보존", "가\n\n나", 1000, "가\n\n나");
        ok &= CheckWrap("\\n 보존 + 단락 안 줄바꿈", "가나다 라마\n바사 아자차", 50, "가나다\n라마\n바사\n아자차");
        // 공백 여러 개 — 줄 안에서는 보존, 줄이 바뀌는 자리의 공백은 버림. 단락 첫 줄 들여쓰기 보존.
        ok &= CheckWrap("공백 여러 개(줄 안 보존)", "가  나   다", 1000, "가  나   다");
        ok &= CheckWrap("공백 여러 개(줄바꿈 자리 버림)", "가  나   다", 45, "가  나\n다");
        ok &= CheckWrap("들여쓰기 보존", "   위치: 가나", 60, "   위치:\n가나");
        ok &= CheckWrap("끝 공백 버림", "가나 ", 1000, "가나");
        ok &= CheckWrap("null", null, 100, string.Empty);
        ok &= CheckWrap("빈 문자열", string.Empty, 100, string.Empty);

        return ok;
    }

    private static bool CheckWrap(string caseName, string? input, double width, string expected)
    {
        string actual = AlertWordWrapper.Wrap(input, width, FakeMeasure);
        bool ok = actual == expected;
        string Show(string s) => s.Replace("\n", "\\n");

        if (ok)
            FileLogger.Info($"[alert-dialog-test self] [WordWrap — {caseName}] 통과 — \"{Show(actual)}\"");
        else
            FileLogger.Error(LogCategory.App,
                $"[alert-dialog-test self] ★ [WordWrap — {caseName}] 불일치 — 기대=\"{Show(expected)}\", 실제=\"{Show(actual)}\"");

        return ok;
    }

    private static void LogViewModelCheck(string caseName, bool ok, string detail)
    {
        if (ok)
        {
            FileLogger.Info($"[alert-dialog-test self] [ViewModel — {caseName}] 통과 — {detail}");
        }
        else
        {
            FileLogger.Error(LogCategory.App, $"[alert-dialog-test self] ★ [ViewModel — {caseName}] 불일치 — {detail}");
        }
    }
}
