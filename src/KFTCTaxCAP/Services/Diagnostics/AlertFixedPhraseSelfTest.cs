using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using KFTCTaxCAP.Services.Reader;
using KFTCTaxCAP.Services.Receipt;
using KFTCTaxCAP.Services.Van;
using KFTCTaxCAP.ViewModels;
using KFTCTaxCAP.ViewModels.Alerts;
using KFTCTaxCAP.ViewModels.Payment;

namespace KFTCTaxCAP.Services.Diagnostics;

/// <summary>
/// Phase 39 P39-4 전용 셀프테스트(docs/alert_dialog/development_plan.md P39-4 완료 조건) — <c>--alert-dialog-test
/// self</c>에서 <see cref="AlertDialogSelfTest"/>와 함께 돈다. 두 묶음:
/// (A) PRD §4.2 재분류 표대로 각 화면 ViewModel이 실제로 올리는 <see cref="AlertMessage.Kind"/>·문구를
///     실제 ViewModel/빌더를 호출해 대조(기대값은 PRD 리터럴).
/// (B) 실제 상수·빌더가 만드는 모든 고정 문구의 제목·본문 각 줄 폭을 Pretendard·Malgun Gothic 두 글꼴로
///     측정해 PRD §4.4 한도(제목 272, 본문 271) 안에 들어가는지 검증(길이 미정 값이 든 줄은 정보 로그만).
/// </summary>
internal static class AlertFixedPhraseSelfTest
{
    internal static bool RunAll()
    {
        bool kindOk = RunKindAndPhraseCases();
        bool widthOk = RunWidthCases();

        bool allPassed = kindOk && widthOk;

        FileLogger.Info(
            "[alert-dialog-test self] [Phase39 알림창 P39-4] 완료 — " +
            $"종류·문구={(kindOk ? "통과" : "실패")}, 줄 폭={(widthOk ? "통과" : "실패")}, " +
            $"종합={(allPassed ? "통과" : "실패")}");

        return allPassed;
    }

    // ====================================================================
    // (A) PRD §4.2 종류·문구 대조 — 기대값은 PRD 리터럴(개발계획 "실수가 나기 쉬운 곳" ③).
    // ====================================================================

    private static bool RunKindAndPhraseCases()
    {
        bool ok = true;

        ok &= CheckShopSetupTimeoutInvalid();
        ok &= CheckShopSetupInvalidPort();
        ok &= CheckShopSetupSaveFailed();
        ok &= CheckReaderBuildMessageCases();
        ok &= CheckReaderBuildKeyDownloadMessageCases();

        return ok;
    }

    /// <summary>#1 — 카드입력 타임아웃 "10"(30 미만) → TryConfirm() false, Warning,
    /// "입력값을 확인해주세요.\n카드입력 타임아웃은 30초 이상 입력해주세요."</summary>
    private static bool CheckShopSetupTimeoutInvalid()
    {
        var vm = new ShopSetupViewModel(new Services.Receipt.ReceiptPrintService(), new Services.Storage.LastReceiptStore());
        AlertMessage? captured = null;
        vm.AlertRequested += (_, m) => captured = m;

        vm.CardReadTimeoutSecondsText = "10";
        bool result = vm.TryConfirm();

        const string expected = "입력값을 확인해주세요.\n카드입력 타임아웃은 30초 이상 입력해주세요.";
        bool ok = !result && captured is { Kind: AlertKind.Warning } && captured.Text == expected;
        LogKindCheck("#1 ShopSetup 타임아웃 미달", ok,
            $"TryConfirm={result}, Kind={captured?.Kind.ToString() ?? "(없음)"}, Text=\"{captured?.Text}\"");
        return ok;
    }

    /// <summary>#2 — 전표 인쇄 ON + 포트 공란 → TryConfirm() false, Warning, "입력값을 확인해주세요.",
    /// FocusPrinterPortRequested 1회.</summary>
    private static bool CheckShopSetupInvalidPort()
    {
        var vm = new ShopSetupViewModel(new Services.Receipt.ReceiptPrintService(), new Services.Storage.LastReceiptStore());
        AlertMessage? captured = null;
        int focusCount = 0;
        vm.AlertRequested += (_, m) => captured = m;
        vm.FocusPrinterPortRequested += (_, _) => focusCount++;

        vm.CardReadTimeoutSecondsText = "30";
        vm.SlipPrintEnabled = true;
        vm.PrinterPort = string.Empty;
        bool result = vm.TryConfirm();

        const string expected = "입력값을 확인해주세요.";
        bool ok = !result && captured is { Kind: AlertKind.Warning } && captured.Text == expected && focusCount == 1;
        LogKindCheck("#2 ShopSetup 포트 공란", ok,
            $"TryConfirm={result}, Kind={captured?.Kind.ToString() ?? "(없음)"}, Text=\"{captured?.Text}\", FocusCount={focusCount}");
        return ok;
    }

    /// <summary>#3 — 저장 주입점이 예외를 던지면 TryConfirm() false, <b>Error</b>, 고정 문구(예외 메시지
    /// 미노출). 성공 경로는 실제 레지스트리에 쓰므로 이 케이스는 실패 경로만 돈다(개발계획 지시).</summary>
    private static bool CheckShopSetupSaveFailed()
    {
        var vm = new ShopSetupViewModel(
            new Services.Receipt.ReceiptPrintService(),
            new Services.Storage.LastReceiptStore(),
            saveOverride: _ => throw new InvalidOperationException("TEST — 레지스트리 접근 거부(가상)"));
        AlertMessage? captured = null;
        vm.AlertRequested += (_, m) => captured = m;

        vm.CardReadTimeoutSecondsText = "30";
        vm.SlipPrintEnabled = false; // 포트 검증을 건너뛰어 저장 단계까지 도달시킨다.
        bool result = vm.TryConfirm();

        const string expected = "설정 저장 실패\n설정을 저장하지 못했습니다.\n다시 시도하고, 반복되면 담당자에게\n문의해주세요.";
        bool noExceptionLeak = captured != null && !captured.Text.Contains("TEST") && !captured.Text.Contains("InvalidOperationException");
        bool ok = !result && captured is { Kind: AlertKind.Error } && captured.Text == expected && noExceptionLeak;
        LogKindCheck("#3 ShopSetup 저장 실패", ok,
            $"TryConfirm={result}, Kind={captured?.Kind.ToString() ?? "(없음)"}, Text=\"{captured?.Text}\"");
        return ok;
    }

    /// <summary>#8·#9 — ReaderSetupViewModel.BuildMessage: 명령 3종 × ReaderCommandOutcomeKind 전부.
    /// Success일 때만 Success, 나머지는 전부 Error.</summary>
    private static bool CheckReaderBuildMessageCases()
    {
        bool ok = true;

        foreach (var combo in BuildMessageCombos())
        {
            var message = ReaderSetupViewModel.BuildMessage(
                combo.CommandLabel, combo.Kind, combo.ResponseCode, combo.DllResultName, combo.DllResult, combo.Detail, combo.SuccessExtra);

            AlertKind expectedKind = combo.Kind == ReaderCommandOutcomeKind.Success ? AlertKind.Success : AlertKind.Error;
            bool titleStartsRight = combo.Kind == ReaderCommandOutcomeKind.Success
                ? message.Text.StartsWith($"리더기 {combo.CommandLabel} 성공", StringComparison.Ordinal)
                : message.Text.StartsWith($"리더기 {combo.CommandLabel} 실패", StringComparison.Ordinal);

            bool caseOk = message.Kind == expectedKind && titleStartsRight;
            LogKindCheck($"#8/#9 BuildMessage({combo.CommandLabel}, {combo.Kind})", caseOk,
                $"Kind={message.Kind}(기대={expectedKind}), Text=\"{message.Text.Replace("\n", "\\n")}\"");
            ok &= caseOk;
        }

        return ok;
    }

    /// <summary>#10·#11 — BuildKeyDownloadMessage: 성공 1 + 실패(단계 5종 × {응답코드, 단말기 교체,
    /// 응답코드 없음}). 성공만 Success, 나머지 Error.</summary>
    private static bool CheckReaderBuildKeyDownloadMessageCases()
    {
        bool ok = true;

        var success = KeyDownloadOutcome.Success(RepresentativeModuleId);
        var successMessage = ReaderSetupViewModel.BuildKeyDownloadMessage(success);
        bool successOk = successMessage.Kind == AlertKind.Success &&
                          successMessage.Text == $"리더기 키다운로드 성공\n모듈 ID : {RepresentativeModuleId}";
        LogKindCheck("#10 BuildKeyDownloadMessage(성공)", successOk, $"Text=\"{successMessage.Text.Replace("\n", "\\n")}\"");
        ok &= successOk;

        foreach (var combo in KeyDownloadFailureCombos())
        {
            var message = ReaderSetupViewModel.BuildKeyDownloadMessage(combo.Outcome);
            bool caseOk = message.Kind == AlertKind.Error && message.Text.StartsWith("리더기 키다운로드 실패", StringComparison.Ordinal);
            LogKindCheck($"#11 BuildKeyDownloadMessage({combo.Label})", caseOk, $"Text=\"{message.Text.Replace("\n", "\\n")}\"");
            ok &= caseOk;
        }

        return ok;
    }

    private static void LogKindCheck(string caseName, bool ok, string detail)
    {
        if (ok)
            FileLogger.Info($"[alert-dialog-test self] [P39-4(A) — {caseName}] 통과 — {detail}");
        else
            FileLogger.Error(LogCategory.App, $"[alert-dialog-test self] ★ [P39-4(A) — {caseName}] 불일치 — {detail}");
    }

    // ====================================================================
    // (B) 제목·본문 줄 폭 — 실제 상수·빌더를 열거해 잰다(PRD §4.4).
    // ====================================================================

    /// <summary>StatusResponseParser.cs:53(ReaderAuthIdLength=16) — SPEC상 리더기 인증 식별 번호 최대
    /// 길이(H/W모델명12+F/W버전4, X(16)). 대표값은 이 길이를 꽉 채운 숫자 문자열.</summary>
    private const string RepresentativeReaderAuthId = "1234567890123456"; // 16자

    /// <summary>StatusResponseParser.cs:54 / KeyDownloadStartResponseParser.cs:47 / KeyDownloadUsingKeyResponseParser.cs:35
    /// (ModuleIdLength=10) — 모듈 ID 최대 길이(모델코드3+IPEK버전1+Y1+M1+"####"4, X(10)).</summary>
    private const string RepresentativeModuleId = "C910540001"; // 10자

    private const string RepresentativeResponseCode2 = "05";
    private const string DeviceReplacementResponseCode = "395";

    private const double TitleMaxWidth = 272;
    private const double BodyMaxWidth = 271; // 272 - 1(AlertDialog.ApplyKeepAllWrap과 같은 여유)
    private const double BoundaryThresholdPixels = 2;

    private static bool RunWidthCases()
    {
        bool ok = true;

        var cases = BuildWidthCases();

        ok &= VerifyPretendardLoaded();
        ok &= MeasureAllCasesOnUiThread(cases, "Pretendard(일반 모드)", GetPretendardFontFamily);
        ok &= MeasureAllCasesOnUiThread(cases, "Malgun Gothic(이름 직접)", () => new FontFamily("Malgun Gothic"));

        ok &= RunDetectionPowerCase();

        return ok;
    }

    /// <summary>검출력 확인(개발계획 P39-4 (B)) — Phase 38 이전 #12 제목 문구는 축약 전이라 272를
    /// 넘어야 한다. 두 글꼴 중 하나라도 넘침을 검출하지 못하면 측정 로직 자체가 신뢰할 수 없다는
    /// 뜻이므로 실패 처리한다.</summary>
    private static bool RunDetectionPowerCase()
    {
        const string oldTitle = "리더기1과 리더기2에 같은 COM 포트를 지정할 수 없습니다.";

        bool detectedPretendard = false;
        bool detectedMalgun = false;
        double widthPretendard = 0;
        double widthMalgun = 0;

        RunOnUiThread(() =>
        {
            widthPretendard = MeasureTitle(oldTitle, GetPretendardFontFamily());
            widthMalgun = MeasureTitle(oldTitle, new FontFamily("Malgun Gothic"));
        });

        detectedPretendard = widthPretendard > TitleMaxWidth;
        detectedMalgun = widthMalgun > TitleMaxWidth;

        bool ok = detectedPretendard && detectedMalgun;
        string detail = $"Pretendard 폭={widthPretendard:F1}(한도={TitleMaxWidth}, 검출={detectedPretendard}), " +
                         $"Malgun Gothic 폭={widthMalgun:F1}(한도={TitleMaxWidth}, 검출={detectedMalgun})";

        if (ok)
            FileLogger.Info($"[alert-dialog-test self] [P39-4(B) 검출력 확인 — Phase38 이전 #12 제목] 통과 — {detail}");
        else
            FileLogger.Error(LogCategory.App, $"[alert-dialog-test self] ★ [P39-4(B) 검출력 확인 — Phase38 이전 #12 제목] 불일치(넘침을 검출하지 못함) — {detail}");

        return ok;
    }

    /// <summary>
    /// 일반 모드 글꼴(Typography.xaml:17과 같은 Pretendard + Malgun Gothic 대체)을 <b>명시적으로</b> 만든다.
    /// CP1 L-1(2026-10-02) — 앱 리소스 <c>PretendardFontFamily</c>를 그대로 쓰면 ① 컴팩트 모드에서는 그 값이 Malgun
    /// Gothic이고 ② <c>pack://application</c> 해석이 어긋나 Pretendard 로드에 실패해도 대체 글꼴로 조용히 재서 "통과"가
    /// 나왔다. 그래서 어셈블리를 명시한 pack URI로 만들고, <see cref="VerifyPretendardLoaded"/>로 실제 로드된 글꼴
    /// 파일을 확인한다.
    /// </summary>
    private static FontFamily GetPretendardFontFamily() =>
        new FontFamily(new Uri("pack://application:,,,/KFTCTaxCAP;component/"), "./Assets/Fonts/#Pretendard, Malgun Gothic");

    /// <summary>CP1 L-1 — 제목(Bold)·본문(Normal) 측정에 실제로 Pretendard 글꼴 파일이 쓰이는지 확인한다. 대체 글꼴
    /// (Malgun Gothic 등)로 떨어졌으면 Pretendard 측정값이 의미가 없으므로 실패 처리한다.</summary>
    private static bool VerifyPretendardLoaded()
    {
        bool ok = true;
        RunOnUiThread(() =>
        {
            var family = GetPretendardFontFamily();
            foreach (var weight in new[] { FontWeights.Bold, FontWeights.Normal })
            {
                var typeface = new Typeface(family, FontStyles.Normal, weight, FontStretches.Normal);
                string fontUri = typeface.TryGetGlyphTypeface(out GlyphTypeface glyph) ? glyph.FontUri.ToString() : "(로드 실패)";
                bool loaded = fontUri.IndexOf("Pretendard", StringComparison.OrdinalIgnoreCase) >= 0;
                if (loaded)
                    FileLogger.Info($"[alert-dialog-test self] [P39-4(B) Pretendard 로드 확인 — {weight}] 통과 — {fontUri}");
                else
                    FileLogger.Error(LogCategory.App,
                        $"[alert-dialog-test self] ★ [P39-4(B) Pretendard 로드 확인 — {weight}] 대체 글꼴로 떨어짐 — {fontUri}");
                ok &= loaded;
            }
        });
        return ok;
    }

    private enum LineRole { Title, Body }

    private sealed class WidthCase
    {
        internal string Source { get; set; } = string.Empty;
        internal string Text { get; set; } = string.Empty;
        internal bool LengthUnknown { get; set; }
    }

    private static List<WidthCase> BuildWidthCases()
    {
        var cases = new List<WidthCase>
        {
            // ShopSetupViewModel 고정 문구 6개.
            new() { Source = "ShopSetupViewModel.TimeoutInvalidMessage", Text = ShopSetupViewModel.TimeoutInvalidMessage },
            new() { Source = "ShopSetupViewModel.InvalidInputMessage", Text = ShopSetupViewModel.InvalidInputMessage },
            new() { Source = "ShopSetupViewModel.SaveFailedMessage", Text = ShopSetupViewModel.SaveFailedMessage },
            new() { Source = "ShopSetupViewModel.PrinterPortRequiredMessage", Text = ShopSetupViewModel.PrinterPortRequiredMessage },
            new() { Source = "ShopSetupViewModel.NoLastReceiptMessage", Text = ShopSetupViewModel.NoLastReceiptMessage },
            new() { Source = "ShopSetupViewModel.DiscardChangesQuestion", Text = ShopSetupViewModel.DiscardChangesQuestion },

            // ReaderSetupViewModel 고정 문구 2개.
            new() { Source = "ReaderSetupViewModel.DuplicatePortMessage", Text = ReaderSetupViewModel.DuplicatePortMessage },
            new() { Source = "ReaderSetupViewModel.DiscardChangesQuestion", Text = ReaderSetupViewModel.DiscardChangesQuestion },
        };

        // BuildReceiptPrintFailureMessage / BuildReceiptPrintWarningMessage — None 제외 사유 전부(9종). 전부
        // 고정 문구(사유 문장이 enum→문구 고정 매핑이라 길이가 미정인 값이 섞이지 않는다).
        foreach (ReceiptPrintFailureReason reason in Enum.GetValues(typeof(ReceiptPrintFailureReason)))
        {
            if (reason == ReceiptPrintFailureReason.None)
                continue;

            cases.Add(new WidthCase
            {
                Source = $"PaymentScreenViewModel.BuildReceiptPrintFailureMessage({reason})",
                Text = PaymentScreenViewModel.BuildReceiptPrintFailureMessage(reason),
            });
            cases.Add(new WidthCase
            {
                Source = $"PaymentScreenViewModel.BuildReceiptPrintWarningMessage({reason})",
                Text = PaymentScreenViewModel.BuildReceiptPrintWarningMessage(reason),
            });
        }

        // BuildMessage(명령 3종 × 종류 전부).
        foreach (var combo in BuildMessageCombos())
        {
            var message = ReaderSetupViewModel.BuildMessage(
                combo.CommandLabel, combo.Kind, combo.ResponseCode, combo.DllResultName, combo.DllResult, combo.Detail, combo.SuccessExtra);

            // DllCallFailure/CommunicationError는 DLL detail·통신 오류 상세(길이 미정 값)를 담는다 —
            // 그 외(Success/BusinessFailure/Timeout)는 고정 문구 또는 SPEC 최대 길이 대표값만 담는다.
            bool lengthUnknown = combo.Kind is ReaderCommandOutcomeKind.DllCallFailure or ReaderCommandOutcomeKind.CommunicationError;
            cases.Add(new WidthCase
            {
                Source = $"ReaderSetupViewModel.BuildMessage({combo.CommandLabel}, {combo.Kind})",
                Text = message.Text,
                LengthUnknown = lengthUnknown,
            });
        }

        // BuildKeyDownloadMessage — 성공 1 + 실패(단계 5종 × 3variant).
        var success = KeyDownloadOutcome.Success(RepresentativeModuleId);
        cases.Add(new WidthCase
        {
            Source = "ReaderSetupViewModel.BuildKeyDownloadMessage(성공)",
            Text = ReaderSetupViewModel.BuildKeyDownloadMessage(success).Text,
        });

        foreach (var combo in KeyDownloadFailureCombos())
        {
            cases.Add(new WidthCase
            {
                Source = $"ReaderSetupViewModel.BuildKeyDownloadMessage({combo.Label})",
                Text = ReaderSetupViewModel.BuildKeyDownloadMessage(combo.Outcome).Text,
                // "응답코드 없음" variant는 Detail(길이 미정 값)로 떨어진다.
                LengthUnknown = combo.Label.Contains("응답코드 없음"),
            });
        }

        return cases;
    }

    private static bool MeasureAllCasesOnUiThread(List<WidthCase> cases, string fontLabel, Func<FontFamily> familyFactory)
    {
        bool ok = true;
        var boundaryLines = new List<string>();

        RunOnUiThread(() =>
        {
            var family = familyFactory();

            foreach (var wc in cases)
            {
                var (title, body) = AlertTextSplitter.Split(wc.Text);

                if (!string.IsNullOrEmpty(title))
                {
                    double width = MeasureTitle(title, family);
                    ok &= CheckLine(wc.Source, fontLabel, LineRole.Title, title, width, TitleMaxWidth, wc.LengthUnknown, boundaryLines);
                }

                if (!string.IsNullOrEmpty(body))
                {
                    foreach (var line in body.Split('\n'))
                    {
                        double width = MeasureBody(line, family);
                        ok &= CheckLine(wc.Source, fontLabel, LineRole.Body, line, width, BodyMaxWidth, wc.LengthUnknown, boundaryLines);
                    }
                }
            }
        });

        if (boundaryLines.Count > 0)
            FileLogger.Info($"[alert-dialog-test self] [P39-4(B) {fontLabel} 경계 줄] " + string.Join(" | ", boundaryLines));

        return ok;
    }

    private static bool CheckLine(string source, string fontLabel, LineRole role, string text, double width, double maxWidth,
        bool lengthUnknown, List<string> boundaryLines)
    {
        string roleLabel = role == LineRole.Title ? "제목" : "본문";

        if (lengthUnknown)
        {
            FileLogger.Info(
                $"[alert-dialog-test self] [P39-4(B) {fontLabel}/{roleLabel}/길이 미정] {source} — 폭={width:F1}, 한도={maxWidth} (정보— 판정 제외)");
            return true;
        }

        bool overflow = width > maxWidth;
        bool isBoundary = Math.Abs(maxWidth - width) <= BoundaryThresholdPixels;

        if (isBoundary)
            boundaryLines.Add($"{source}({fontLabel}/{roleLabel}, 폭={width:F1}, 한도={maxWidth})");

        if (!overflow)
        {
            FileLogger.Info(
                $"[alert-dialog-test self] [P39-4(B) {fontLabel}/{roleLabel}] {source} — 폭={width:F1}, 한도={maxWidth}" +
                (isBoundary ? " [경계]" : string.Empty));
        }
        else
        {
            FileLogger.Error(LogCategory.App,
                $"[alert-dialog-test self] ★ [P39-4(B) {fontLabel}/{roleLabel}] {source} — 넘침: 폭={width:F1} > 한도={maxWidth}, 문구=\"{text}\"");
        }

        return !overflow;
    }

    private static double MeasureTitle(string text, FontFamily family) => Measure(text, family, FontWeights.Bold, 15);

    private static double MeasureBody(string text, FontFamily family) => Measure(text, family, FontWeights.Normal, 13);

    /// <summary>AlertDialog.xaml.cs:48-67(ApplyKeepAllWrap)와 같은 측정 방식 — FormattedText,
    /// TextFormattingMode.Ideal, pixelsPerDip 1.0, WidthIncludingTrailingWhitespace.</summary>
    private static double Measure(string text, FontFamily family, FontWeight weight, double fontSize)
    {
        var typeface = new Typeface(family, FontStyles.Normal, weight, FontStretches.Normal);
        var culture = CultureInfo.GetCultureInfo("ko-KR");
        var formatted = new FormattedText(
            text, culture, FlowDirection.LeftToRight, typeface, fontSize, Brushes.Black,
            null, TextFormattingMode.Ideal, 1.0);
        return formatted.WidthIncludingTrailingWhitespace;
    }

    /// <summary>위험 ⑤ — FormattedText 측정·리소스 조회는 UI 스레드에서 한다(<c>--alert-dialog-test self</c>는
    /// Task.Run의 백그라운드 스레드에서 돈다).</summary>
    private static void RunOnUiThread(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.Invoke(action);
    }

    // ====================================================================
    // 공유 조합 생성 — (A)·(B) 양쪽에서 쓴다(이중 유지 방지).
    // ====================================================================

    private sealed class BuildMessageCombo
    {
        internal string CommandLabel { get; set; } = string.Empty;
        internal ReaderCommandOutcomeKind Kind { get; set; }
        internal string? ResponseCode { get; set; }
        internal string DllResultName { get; set; } = string.Empty;
        internal int DllResult { get; set; }
        internal string Detail { get; set; } = string.Empty;
        internal string? SuccessExtra { get; set; }
    }

    /// <summary>ReaderSetupViewModel.BuildMessage — 명령 3종({@초기화, 상태체크, 무결성 체크}, 실제 라벨 그대로)
    /// × ReaderCommandOutcomeKind 전부. 초기화는 운영 코드(ExecuteInitAsync)와 같이 성공이어도 successExtra가
    /// 없다. 상태체크·무결성 체크는 성공일 때만 SPEC 최대 길이 대표값(리더기 인증 식별번호 16자·모듈 ID 10자)을
    /// 담는다(ReaderSetupViewModel.cs:263-264, :290-291과 같은 조립 형식).</summary>
    private static IEnumerable<BuildMessageCombo> BuildMessageCombos()
    {
        string[] commandLabels = { "초기화", "상태체크", "무결성 체크" };

        foreach (var label in commandLabels)
        {
            foreach (ReaderCommandOutcomeKind kind in Enum.GetValues(typeof(ReaderCommandOutcomeKind)))
            {
                string? successExtra = null;
                if (kind == ReaderCommandOutcomeKind.Success && label != "초기화")
                    successExtra = $"리더기 인증 식별번호 : {RepresentativeReaderAuthId}\n모듈 ID : {RepresentativeModuleId}";

                yield return kind switch
                {
                    ReaderCommandOutcomeKind.Success => new BuildMessageCombo
                    {
                        CommandLabel = label, Kind = kind, ResponseCode = "00", SuccessExtra = successExtra,
                    },
                    ReaderCommandOutcomeKind.BusinessFailure => new BuildMessageCombo
                    {
                        CommandLabel = label, Kind = kind, ResponseCode = RepresentativeResponseCode2,
                    },
                    ReaderCommandOutcomeKind.DllCallFailure => new BuildMessageCombo
                    {
                        CommandLabel = label, Kind = kind, DllResultName = "READER_PORT_NOT_OPEN", DllResult = -1,
                        Detail = "포트가 열려 있지 않습니다",
                    },
                    ReaderCommandOutcomeKind.Timeout => new BuildMessageCombo
                    {
                        CommandLabel = label, Kind = kind,
                    },
                    ReaderCommandOutcomeKind.CommunicationError => new BuildMessageCombo
                    {
                        CommandLabel = label, Kind = kind, Detail = "LRC 오류",
                    },
                    _ => throw new ArgumentOutOfRangeException(nameof(kind)),
                };
            }
        }
    }

    private sealed class KeyDownloadFailureCombo
    {
        internal string Label { get; set; } = string.Empty;
        internal KeyDownloadOutcome Outcome { get; set; } = null!;
    }

    /// <summary>BuildKeyDownloadMessage — 단계 5종 × {응답코드, 단말기 교체, 응답코드 없음}. "응답코드"·
    /// "단말기 교체"는 KeyDownloadOutcome.ServerFailure(서버 P-39 응답코드 경로)로, "응답코드 없음"은
    /// KeyDownloadOutcome.ReaderFailure(DLL 연동 실패 경로, 응답코드 없이 Detail만)로 만든다 — 실제 운영
    /// 흐름에서 모든 조합이 모든 단계에 나오지는 않지만(예: 395는 서버 구간에서만), BuildKeyDownloadMessage
    /// 자체의 문구 조립 로직(단계 라벨 + 응답코드/Detail 선택)을 단계와 무관하게 전수 검증하는 것이 이
    /// 테스트의 목적이다.</summary>
    private static IEnumerable<KeyDownloadFailureCombo> KeyDownloadFailureCombos()
    {
        foreach (KeyDownloadStage stage in Enum.GetValues(typeof(KeyDownloadStage)))
        {
            yield return new KeyDownloadFailureCombo
            {
                Label = $"{stage}/응답코드",
                Outcome = KeyDownloadOutcome.ServerFailure(
                    stage, KeyDownloadVanCallOutcome.NonSuccessResponseCode(string.Empty, RepresentativeResponseCode2), RepresentativeModuleId),
            };
            yield return new KeyDownloadFailureCombo
            {
                Label = $"{stage}/단말기 교체",
                Outcome = KeyDownloadOutcome.ServerFailure(
                    stage, KeyDownloadVanCallOutcome.NonSuccessResponseCode(string.Empty, DeviceReplacementResponseCode), RepresentativeModuleId),
            };
            yield return new KeyDownloadFailureCombo
            {
                Label = $"{stage}/응답코드 없음",
                Outcome = KeyDownloadOutcome.ReaderFailure(
                    stage, ReaderFailureCategory.DllFailure, string.Empty, RepresentativeModuleId, "타임아웃(응답 없음)"),
            };
        }
    }
}
