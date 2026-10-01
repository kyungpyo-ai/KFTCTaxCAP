using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KFTCOneCAP.Wpf.Protocol.Printer;
using KFTCOneCAP.Wpf.Services.Printer;

namespace KFTCOneCAP.Wpf.Services.Diagnostics;

/// <summary>
/// Phase 33 P33-1~P33-3 완료 조건 전용 셀프테스트(docs/receipt_print/development_plan.md) —
/// <see cref="ReceiptTextLayout"/>/<see cref="EscPosDocumentEncoder"/>/<see cref="PrinterStatusParser"/>
/// (P33-1/P33-2)와 <see cref="SerialReceiptPrinter"/>의 포트 불필요 경로(P33-3 — InvalidPort/
/// PortOpenFailed/동시 호출 직렬화)를 검증한다. 250/251처럼 실제로 존재하지 않는 포트 번호만 쓰므로
/// 실장비 없이도 전부 돌아간다.
///
/// <c>App.xaml.cs</c>가 <c>--receipt-print-test self</c> 인자로 실행될 때만 <see cref="RunAll"/>을 호출한다.
/// <c>dump</c>/<c>print</c>/<c>status</c> 모드는 <see cref="ReceiptPrintDiagnosticHarness"/>가 맡는다.
/// Phase 34(P34-1/P34-2, docs/receipt_print/development_plan.md)부터 <see cref="NationalTaxReceiptSelfTest"/>도
/// 여기서 함께 실행한다 — 영수증 조립·출력 서비스는 별도 모드를 만들지 않고 같은 <c>self</c> 한 번에 묶는다.
/// </summary>
internal static class ReceiptPrintSelfTest
{
    public static void RunAll()
    {
        try
        {
            FileLogger.Info("[receipt-print-test self] 시작");

            bool byteWidthOk = RunByteWidthCase();
            bool wrapAgencyOk = RunAgencyWrapCase();
            bool wrapNoticesOk = RunPrdNoticeWrapCases();
            bool wrapOddBoundaryOk = RunOddBoundaryWrapCase();
            bool wrapEmptyNullOk = RunEmptyAndNullWrapCases();
            bool wrapSpaceRulesOk = RunTrailingAndLeadingSpaceCases();
            bool encoderOk = RunEncoderCases();
            bool statusOk = RunStatusParserCases();
            bool invalidPortOk = RunSerialPrinterInvalidPortCases();
            bool portOpenFailedOk = RunSerialPrinterPortOpenFailedCase();
            bool concurrencyOk = RunSerialPrinterConcurrencyCase();
            bool receiptOk = NationalTaxReceiptSelfTest.RunAll();
            bool assemblerOk = NationalTaxReceiptAssemblerSelfTest.RunAll(); // Phase 34 P34-4
            bool reprintOk = LastReceiptReprintSelfTest.RunAll(); // Phase 35 P35-1

            bool allPassed = byteWidthOk && wrapAgencyOk && wrapNoticesOk && wrapOddBoundaryOk
                && wrapEmptyNullOk && wrapSpaceRulesOk && encoderOk && statusOk
                && invalidPortOk && portOpenFailedOk && concurrencyOk && receiptOk && assemblerOk && reprintOk;

            FileLogger.Info(
                $"[receipt-print-test self] 완료 — ByteWidth={(byteWidthOk ? "통과" : "실패")}, " +
                $"세입징수관서 줄바꿈={(wrapAgencyOk ? "통과" : "실패")}, " +
                $"PRD §3.3 안내문구 줄바꿈={(wrapNoticesOk ? "통과" : "실패")}, " +
                $"홀수 경계={(wrapOddBoundaryOk ? "통과" : "실패")}, " +
                $"빈 값/null={(wrapEmptyNullOk ? "통과" : "실패")}, " +
                $"줄 끝/맨 앞 공백 규칙={(wrapSpaceRulesOk ? "통과" : "실패")}, " +
                $"인코더={(encoderOk ? "통과" : "실패")}, 상태 파서={(statusOk ? "통과" : "실패")}, " +
                $"InvalidPort={(invalidPortOk ? "통과" : "실패")}, " +
                $"PortOpenFailed={(portOpenFailedOk ? "통과" : "실패")}, " +
                $"동시 호출 직렬화={(concurrencyOk ? "통과" : "실패")}, " +
                $"Phase34 조립={(receiptOk ? "통과" : "실패")}, " +
                $"Phase34 결제창 조립기={(assemblerOk ? "통과" : "실패")}, " +
                $"Phase35 직전거래 재출력={(reprintOk ? "통과" : "실패")}, " +
                $"종합={(allPassed ? "통과" : "실패")}");
        }
        catch (Exception ex)
        {
            FileLogger.Error($"[receipt-print-test self] 예외로 중단: {ex}");
        }
    }

    /// <summary>완료 조건: ByteWidth("세입징수관서 : 국세청 서울지방국세청 강서") == 41.</summary>
    private static bool RunByteWidthCase()
    {
        const string text = "세입징수관서 : 국세청 서울지방국세청 강서";
        int actual = ReceiptTextLayout.ByteWidth(text);
        bool ok = actual == 41;

        if (ok)
            FileLogger.Info($"[receipt-print-test self] [ByteWidth] 통과 — \"{text}\" = {actual}바이트");
        else
            FileLogger.Error(LogCategory.App, $"[receipt-print-test self] ★ [ByteWidth] 기대=41, 실제={actual}");

        return ok;
    }

    /// <summary>
    /// 완료 조건: Wrap("세입징수관서 : 국세청 서울지방국세청 강서세무서 징수관", 42) → 2줄, 둘째 줄
    /// "세무서 징수관"(PRD §3.2 레이아웃).
    /// </summary>
    private static bool RunAgencyWrapCase()
    {
        const string text = "세입징수관서 : 국세청 서울지방국세청 강서세무서 징수관";
        IReadOnlyList<string> lines = ReceiptTextLayout.Wrap(text, 42);

        bool ok = lines.Count == 2
            && lines[0] == "세입징수관서 : 국세청 서울지방국세청 강서"
            && lines[1] == "세무서 징수관";

        if (ok)
        {
            FileLogger.Info(
                $"[receipt-print-test self] [세입징수관서 줄바꿈] 통과 — {lines.Count}줄, " +
                $"1줄=\"{lines[0]}\", 2줄=\"{lines[1]}\"");
        }
        else
        {
            FileLogger.Error(LogCategory.App,
                $"[receipt-print-test self] ★ [세입징수관서 줄바꿈] 결과 불일치 — {lines.Count}줄: " +
                string.Join(" | ", lines));
        }

        return ok;
    }

    /// <summary>
    /// 완료 조건: PRD §3.3 안내 문구 2개(요율 0.8%)를 Wrap(…, 42)한 결과가 PRD §3.2 그림의 줄과 글자
    /// 단위로 같다. 문구 원문은 §3.3, 기대 줄바꿈 결과는 §3.2 레이아웃 그림에서 그대로 가져온다
    /// (2번 문구 둘째 줄 맨 앞 공백 포함 — Wrap이 공백도 1칸 글자로 옮기는 규칙의 결과).
    /// </summary>
    private static bool RunPrdNoticeWrapCases()
    {
        bool allOk = true;

        // 원문 "…계약서 및…"·"…수 없습니다."의 공백이 마침 42바이트째에 걸려 줄 폭 안에 들어가지만,
        // 줄 끝 공백은 제거하는 규칙(2026-09-30 사용자 결정)에 따라 이 두 줄 끝에는 공백이 남지 않는다
        // — PRD §3.2 그림과 글자 단위로 완전히 같다.
        const string notice1 =
            "1.신용카드로 납부한 국세는 할부거래계약서 및 약관 등에도 불구하고 납부를 취소할 수 없습니다.";
        var expected1 = new[]
        {
            "1.신용카드로 납부한 국세는 할부거래계약서",
            "및 약관 등에도 불구하고 납부를 취소할 수",
            "없습니다.",
        };
        allOk &= CheckWrapEquals("PRD §3.3 1번 문구", notice1, 42, expected1);

        // 2번 문구는 요율이 0.8%일 때의 원문(개발계획서 지시 그대로). 둘째 줄 맨 앞 공백은 원문
        // "…납부세액의 0.8%인…"에서 42칸째에 걸린 공백이 다음 줄 맨 앞으로 넘어간 것이다(PRD §3.3).
        const string notice2 =
            "2.신용카드로 국세를 납부할 경우 납부세액의 0.8%인 납부대행수수료는 납세자가 부담합니다.";
        var expected2 = new[]
        {
            "2.신용카드로 국세를 납부할 경우 납부세액의",
            " 0.8%인 납부대행수수료는 납세자가 부담합니",
            "다.",
        };
        allOk &= CheckWrapEquals("PRD §3.3 2번 문구", notice2, 42, expected2);

        return allOk;
    }

    private static bool CheckWrapEquals(string caseName, string input, int widthBytes, string[] expected)
    {
        IReadOnlyList<string> actual = ReceiptTextLayout.Wrap(input, widthBytes);
        bool ok = actual.SequenceEqual(expected);

        if (ok)
        {
            FileLogger.Info(
                $"[receipt-print-test self] [{caseName}] 통과 — {actual.Count}줄: " +
                string.Join(" | ", actual.Select(l => $"\"{l}\"")));
        }
        else
        {
            FileLogger.Error(LogCategory.App,
                $"[receipt-print-test self] ★ [{caseName}] 불일치 — 기대: " +
                string.Join(" | ", expected.Select(l => $"\"{l}\"")) + " / 실제: " +
                string.Join(" | ", actual.Select(l => $"\"{l}\"")));
        }

        return ok;
    }

    /// <summary>
    /// 완료 조건: 홀수 폭 경계에서 2바이트 글자가 쪼개지지 않는다 — "a" + "가"×21(총 43바이트)를 폭 42로
    /// Wrap하면 첫 줄이 정확히 41바이트("a" + "가"×20)이고 둘째 줄이 "가" 1글자여야 한다.
    /// </summary>
    private static bool RunOddBoundaryWrapCase()
    {
        string text = "a" + new string('가', 21);
        IReadOnlyList<string> lines = ReceiptTextLayout.Wrap(text, 42);

        int firstLineBytes = ReceiptTextLayout.ByteWidth(lines.Count > 0 ? lines[0] : string.Empty);
        // "2줄=='가'" 자체가 이미 "가"가 반으로 쪼개지지 않았다는 것을 증명한다(쪼개졌다면 단독
        // 서로게이트나 다른 글자가 나왔을 것) — 2026-09-30 CP1 리뷰 L-6, 별도 대체 문자 검사는
        // Wrap이 디코딩을 하지 않아 애초에 발생할 수 없는 상황을 검사하는 의미 없는 코드였다.
        bool ok = lines.Count == 2 && firstLineBytes == 41 && lines[1] == "가";

        if (ok)
        {
            FileLogger.Info(
                $"[receipt-print-test self] [홀수 경계] 통과 — 1줄 {firstLineBytes}바이트(\"{lines[0]}\"), " +
                $"2줄=\"{lines[1]}\"");
        }
        else
        {
            FileLogger.Error(LogCategory.App,
                $"[receipt-print-test self] ★ [홀수 경계] 기대: 2줄/1줄=41바이트/2줄=\"가\" — 실제: " +
                $"{lines.Count}줄, 1줄={firstLineBytes}바이트, " +
                string.Join(" | ", lines.Select(l => $"\"{l}\"")));
        }

        return ok;
    }

    /// <summary>Wrap의 빈 문자열/null 입력 → 빈 줄 1개(길이 0인 목록이 아님).</summary>
    private static bool RunEmptyAndNullWrapCases()
    {
        bool allOk = true;

        IReadOnlyList<string> emptyResult = ReceiptTextLayout.Wrap(string.Empty, 42);
        if (!(emptyResult.Count == 1 && emptyResult[0] == string.Empty))
        {
            FileLogger.Error(LogCategory.App,
                $"[receipt-print-test self] ★ [빈 문자열] 빈 줄 1개를 기대했으나 {emptyResult.Count}줄");
            allOk = false;
        }

        IReadOnlyList<string> nullResult = ReceiptTextLayout.Wrap(null, 42);
        if (!(nullResult.Count == 1 && nullResult[0] == string.Empty))
        {
            FileLogger.Error(LogCategory.App,
                $"[receipt-print-test self] ★ [null] 빈 줄 1개를 기대했으나 {nullResult.Count}줄");
            allOk = false;
        }

        if (allOk)
            FileLogger.Info("[receipt-print-test self] [빈 값/null] 통과 — 둘 다 빈 줄 1개");

        return allOk;
    }

    /// <summary>
    /// 2026-09-30 사용자 결정 — "줄 끝 공백은 제거, 줄 맨 앞 공백은 유지"를 좁은 폭(10)의 최소 예제로
    /// 각각 독립 확인한다(PRD §3.3 문구는 42칸 안에서 두 규칙이 섞여 나오므로, 여기서는 폭을 좁혀
    /// 각 규칙을 단독으로 재현한다).
    /// </summary>
    private static bool RunTrailingAndLeadingSpaceCases()
    {
        bool allOk = true;

        // 줄 끝 공백 제거: "123456789 abc"를 폭 10으로 감으면 "123456789"(9바이트) + 공백(1바이트)
        // 이 정확히 10바이트에 맞아 그 줄에 남을 자리가 있지만, 줄 끝 공백 제거 규칙에 따라 공백이
        // 떨어져 나가고 "123456789"만 남는다. 다음 줄은 "abc".
        allOk &= CheckWrapEquals(
            "줄 끝 공백 제거", "123456789 abc", 10,
            new[] { "123456789", "abc" });

        // 줄 맨 앞 공백 유지: "1234567890 abc"를 폭 10으로 감으면 "1234567890"(10바이트)로 이미 꽉 차
        // 공백이 들어갈 자리가 없어 다음 줄로 밀린다 — 그 공백은 다음 줄 맨 앞에 그대로 남는다.
        allOk &= CheckWrapEquals(
            "줄 맨 앞 공백 유지", "1234567890 abc", 10,
            new[] { "1234567890", " abc" });

        return allOk;
    }

    /// <summary>
    /// 완료 조건: 인코더 출력이 <c>1B 40</c>으로 시작하고 <c>1D 56 42 ..</c>로 끝난다. 2배 줄 앞에
    /// <c>1D 21 11</c>, 뒤 줄(보통 크기) 앞에 <c>1D 21 00</c>. 폭 초과 줄은 <see cref="ArgumentException"/>.
    /// 더미 데이터만 사용(착수 전 확정 사항 5 — 개인정보 없음).
    /// </summary>
    private static bool RunEncoderCases()
    {
        bool allOk = true;

        var doc = new PrintDocument();
        doc.AddLine("종합소득세 납부확인증", PrintAlignment.Center, PrintSize.DoubleSize);
        doc.AddLine(ReceiptTextLayout.Separator(42));
        doc.AddLine("납세자명 : 홍길동");
        byte[] bytes = EscPosDocumentEncoder.Encode(doc);

        // 시작: 1B 40
        bool startsOk = bytes.Length >= 2 && bytes[0] == 0x1B && bytes[1] == 0x40;
        if (!startsOk)
        {
            FileLogger.Error(LogCategory.App,
                $"[receipt-print-test self] ★ [인코더 시작] 1B 40 기대, 실제 처음 2바이트={ToHex(bytes, 0, 2)}");
            allOk = false;
        }

        // 끝: 1D 56 42 .. (마지막 4바이트)
        bool endsOk = bytes.Length >= 4
            && bytes[bytes.Length - 4] == 0x1D
            && bytes[bytes.Length - 3] == 0x56
            && bytes[bytes.Length - 2] == 0x42;
        if (!endsOk)
        {
            FileLogger.Error(LogCategory.App,
                $"[receipt-print-test self] ★ [인코더 끝] 1D 56 42 .. 기대, 실제 마지막 4바이트=" +
                $"{ToHex(bytes, System.Math.Max(0, bytes.Length - 4), 4)}");
            allOk = false;
        }

        // 2배 줄(제목) 앞에 1D 21 11 — FS &(1C 26) 다음, ESC a(1B 61) 바로 뒤에 와야 한다.
        byte[] doubleSizePrefix = { 0x1D, 0x21, 0x11 };
        bool doubleSizeOk = ContainsSequence(bytes, doubleSizePrefix);
        if (!doubleSizeOk)
        {
            FileLogger.Error(LogCategory.App, "[receipt-print-test self] ★ [2배 줄] 1D 21 11 시퀀스를 찾지 못함");
            allOk = false;
        }

        // 보통 크기 줄(구분선 등) 앞에 1D 21 00.
        byte[] normalSizePrefix = { 0x1D, 0x21, 0x00 };
        bool normalSizeOk = ContainsSequence(bytes, normalSizePrefix);
        if (!normalSizeOk)
        {
            FileLogger.Error(LogCategory.App, "[receipt-print-test self] ★ [보통 크기 줄] 1D 21 00 시퀀스를 찾지 못함");
            allOk = false;
        }

        if (startsOk && endsOk && doubleSizeOk && normalSizeOk)
        {
            FileLogger.Info(
                $"[receipt-print-test self] [인코더 시작/끝/배율] 통과 — 총 {bytes.Length}바이트");
        }

        // 폭 초과 줄 → ArgumentException.
        bool overflowThrows;
        try
        {
            var overflowDoc = new PrintDocument();
            overflowDoc.AddLine(new string('가', 22)); // 22자 * 2바이트 = 44바이트 > 42
            EscPosDocumentEncoder.Encode(overflowDoc);
            FileLogger.Error(LogCategory.App, "[receipt-print-test self] ★ [폭 초과] 예외가 나야 하는데 나지 않음");
            overflowThrows = false;
        }
        catch (ArgumentException)
        {
            overflowThrows = true;
            FileLogger.Info("[receipt-print-test self] [폭 초과] 통과 — ArgumentException 발생 확인");
        }
        catch (Exception ex)
        {
            FileLogger.Error(LogCategory.App,
                $"[receipt-print-test self] ★ [폭 초과] 예상과 다른 예외 타입: {ex.GetType().Name}");
            overflowThrows = false;
        }

        allOk &= overflowThrows;

        return allOk;
    }

    private static bool ContainsSequence(byte[] haystack, byte[] needle)
    {
        for (int i = 0; i <= haystack.Length - needle.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
                return true;
        }

        return false;
    }

    private static string ToHex(byte[] bytes, int offset, int count)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < count && offset + i < bytes.Length; i++)
        {
            if (i > 0)
                sb.Append(' ');
            sb.Append(bytes[offset + i].ToString("X2"));
        }

        return sb.ToString();
    }

    /// <summary>
    /// 완료 조건: escpos_reference.md §2 표의 비트마다 참/거짓 케이스 1개 이상. 고정 비트가 틀린 바이트
    /// (0x00, 0xFF) → IsValid == false.
    /// </summary>
    private static bool RunStatusParserCases()
    {
        bool allOk = true;

        // 고정 비트만 맞춘 "정상" 바이트: bit0=0,bit1=1,bit4=1,bit7=0, 나머지(2,3,5,6)=0 → 0x12.
        const byte baseValid = 0x12;

        // n=2(오프라인 원인) — 커버 열림 없음/오류 없음.
        allOk &= CheckOfflineCause("정상(오류·커버 없음)", baseValid, expectValid: true, expectCoverOpen: false, expectError: false);
        // bit2(0x04) 커버 열림.
        allOk &= CheckOfflineCause("커버 열림", (byte)(baseValid | 0x04), expectValid: true, expectCoverOpen: true, expectError: false);
        // bit6(0x40) 오류 발생.
        allOk &= CheckOfflineCause("오류 발생", (byte)(baseValid | 0x40), expectValid: true, expectCoverOpen: false, expectError: true);

        // n=4(용지 센서) — near-end/end 없음.
        allOk &= CheckPaperSensor("정상(용지 이상 없음)", baseValid, expectValid: true, expectPaperEnd: false, expectPaperNearEnd: false);
        // bit5(0x20) 용지 끝.
        allOk &= CheckPaperSensor("용지 끝(bit5)", (byte)(baseValid | 0x20), expectValid: true, expectPaperEnd: true, expectPaperNearEnd: false);
        // bit6(0x40) 용지 끝(다른 비트로도 성립).
        allOk &= CheckPaperSensor("용지 끝(bit6)", (byte)(baseValid | 0x40), expectValid: true, expectPaperEnd: true, expectPaperNearEnd: false);
        // bit2(0x04) 용지 거의 끝.
        allOk &= CheckPaperSensor("용지 거의 끝(bit2)", (byte)(baseValid | 0x04), expectValid: true, expectPaperEnd: false, expectPaperNearEnd: true);
        // bit3(0x08) 용지 거의 끝.
        allOk &= CheckPaperSensor("용지 거의 끝(bit3)", (byte)(baseValid | 0x08), expectValid: true, expectPaperEnd: false, expectPaperNearEnd: true);

        // 고정 비트가 틀린 바이트 — IsValid == false.
        allOk &= CheckOfflineCause("고정 비트 위반(0x00)", 0x00, expectValid: false, expectCoverOpen: false, expectError: false);
        allOk &= CheckOfflineCause("고정 비트 위반(0xFF)", 0xFF, expectValid: false, expectCoverOpen: false, expectError: false);
        allOk &= CheckPaperSensor("고정 비트 위반(0x00)", 0x00, expectValid: false, expectPaperEnd: false, expectPaperNearEnd: false);
        allOk &= CheckPaperSensor("고정 비트 위반(0xFF)", 0xFF, expectValid: false, expectPaperEnd: false, expectPaperNearEnd: false);

        return allOk;
    }

    private static bool CheckOfflineCause(string caseName, byte statusByte, bool expectValid, bool expectCoverOpen, bool expectError)
    {
        PrinterStatus status = PrinterStatusParser.ParseOfflineCause(statusByte);
        bool ok = status.IsValid == expectValid && status.CoverOpen == expectCoverOpen && status.Error == expectError;

        if (ok)
        {
            FileLogger.Info(
                $"[receipt-print-test self] [상태 n=2 {caseName}] 통과 — byte=0x{statusByte:X2}, " +
                $"IsValid={status.IsValid}, CoverOpen={status.CoverOpen}, Error={status.Error}");
        }
        else
        {
            FileLogger.Error(LogCategory.App,
                $"[receipt-print-test self] ★ [상태 n=2 {caseName}] 불일치 — byte=0x{statusByte:X2}, " +
                $"기대(Valid={expectValid},Cover={expectCoverOpen},Error={expectError}), " +
                $"실제(Valid={status.IsValid},Cover={status.CoverOpen},Error={status.Error})");
        }

        return ok;
    }

    private static bool CheckPaperSensor(string caseName, byte statusByte, bool expectValid, bool expectPaperEnd, bool expectPaperNearEnd)
    {
        PrinterStatus status = PrinterStatusParser.ParsePaperSensor(statusByte);
        bool ok = status.IsValid == expectValid && status.PaperEnd == expectPaperEnd && status.PaperNearEnd == expectPaperNearEnd;

        if (ok)
        {
            FileLogger.Info(
                $"[receipt-print-test self] [상태 n=4 {caseName}] 통과 — byte=0x{statusByte:X2}, " +
                $"IsValid={status.IsValid}, PaperEnd={status.PaperEnd}, PaperNearEnd={status.PaperNearEnd}");
        }
        else
        {
            FileLogger.Error(LogCategory.App,
                $"[receipt-print-test self] ★ [상태 n=4 {caseName}] 불일치 — byte=0x{statusByte:X2}, " +
                $"기대(Valid={expectValid},End={expectPaperEnd},Near={expectPaperNearEnd}), " +
                $"실제(Valid={status.IsValid},End={status.PaperEnd},Near={status.PaperNearEnd})");
        }

        return ok;
    }

    /// <summary>
    /// 완료 조건(development_plan.md P33-3): 포트 "0"/"abc"/""로 <see cref="PrintFailureReason.InvalidPort"/>,
    /// 실제 <see cref="System.IO.Ports.SerialPort"/>를 만들지 않는다(포트 문자열 파싱 단계에서 걸러지므로
    /// 실제 하드웨어·COM 포트가 전혀 필요 없다).
    /// </summary>
    private static bool RunSerialPrinterInvalidPortCases()
    {
        bool allOk = true;
        var printer = new SerialReceiptPrinter();

        allOk &= CheckInvalidPort(printer, "0");
        allOk &= CheckInvalidPort(printer, "abc");
        allOk &= CheckInvalidPort(printer, string.Empty);

        return allOk;
    }

    private static bool CheckInvalidPort(SerialReceiptPrinter printer, string portText)
    {
        var connection = new PrinterConnection(portText, 9600);
        PrintResult result = printer.PrintAsync(new byte[] { 0x1B, 0x40 }, connection, CancellationToken.None)
            .GetAwaiter().GetResult();

        bool ok = !result.IsSuccess && result.Failure == PrintFailureReason.InvalidPort;

        if (ok)
        {
            FileLogger.Info($"[receipt-print-test self] [InvalidPort \"{portText}\"] 통과 — Failure={result.Failure}");
        }
        else
        {
            FileLogger.Error(LogCategory.App,
                $"[receipt-print-test self] ★ [InvalidPort \"{portText}\"] 기대=InvalidPort, 실제={result.Failure}" +
                $"(IsSuccess={result.IsSuccess})");
        }

        return ok;
    }

    /// <summary>
    /// 완료 조건(development_plan.md P33-3): 없는 포트(COM250 — 250/251처럼 실제로 쓰이지 않는 번호를
    /// 골라 실장비 없이도 항상 존재하지 않음을 보장한다)로 <see cref="PrintFailureReason.PortOpenFailed"/>,
    /// 예외가 밖으로 전파되지 않고 반복 호출해도 정상 반환됨(포트 누수 없음, 2회 연속 호출로 확인).
    /// </summary>
    private static bool RunSerialPrinterPortOpenFailedCase()
    {
        var printer = new SerialReceiptPrinter();
        var connection = new PrinterConnection("250", 9600);

        PrintResult result1 = printer.PrintAsync(new byte[] { 0x1B, 0x40 }, connection, CancellationToken.None)
            .GetAwaiter().GetResult();
        PrintResult result2 = printer.PrintAsync(new byte[] { 0x1B, 0x40 }, connection, CancellationToken.None)
            .GetAwaiter().GetResult();

        bool ok = !result1.IsSuccess && result1.Failure == PrintFailureReason.PortOpenFailed
            && !result2.IsSuccess && result2.Failure == PrintFailureReason.PortOpenFailed;

        if (ok)
        {
            FileLogger.Info(
                $"[receipt-print-test self] [PortOpenFailed COM250] 통과 — 2회 연속 호출 모두 " +
                $"Failure={result1.Failure}(예외 전파 없음, 포트 누수 없음)");
        }
        else
        {
            FileLogger.Error(LogCategory.App,
                $"[receipt-print-test self] ★ [PortOpenFailed COM250] 기대=PortOpenFailed×2, 실제=" +
                $"{result1.Failure}/{result2.Failure}");
        }

        return ok;
    }

    /// <summary>
    /// 완료 조건(development_plan.md P33-3/P33-4): 같은 <see cref="SerialReceiptPrinter"/>로 동시에
    /// 2건을 호출하면 순차 실행된다. COM251처럼 존재하지 않는 포트 번호를 쓰면 <c>SerialPort.Open()</c>이
    /// 거의 즉시 실패하므로, 실패 자체의 타임스탬프만으로는 직렬화를 병렬과 구분할 수 없다(둘 다
    /// 순식간에 끝나 버린다) — <see cref="SerialReceiptPrinter.TestOnlyCriticalSectionDelayMs"/>로
    /// 임계구역에 인위적 지연을 걸어, 직렬화됐다면 두 호출의 지연이 누적되고(총 소요 ≈ 2×지연) 병렬이면
    /// 거의 동시에 끝나는(총 소요 ≈ 1×지연) 관찰 가능한 차이를 만든다.
    ///
    /// <b>2026-09-30 CP1 리뷰 M-1 수정</b>: 이전 버전은 판정 임계값(250ms)이 지연(300ms)보다 작아
    /// 병렬 실행(≈300ms)도 통과해버렸다 — 직렬이면 늦은 쪽 완료가 ≈600ms(2×지연), 병렬이면
    /// ≈300ms(1×지연)이므로, 그 중간보다 확실히 위인 1.5×지연(450ms)을 임계값으로 쓴다.
    /// </summary>
    private static bool RunSerialPrinterConcurrencyCase()
    {
        const int delayMs = 300;
        const int serializedThresholdMs = 450; // 1.5×delayMs — 직렬(≈600ms)과 병렬(≈300ms)의 중간보다 위.

        SerialReceiptPrinter.TestOnlyCriticalSectionDelayMs = delayMs;
        try
        {
            var printer = new SerialReceiptPrinter();
            var connection = new PrinterConnection("251", 9600); // COM250과 별개 번호 — 다른 테스트와 겹치지 않게.
            var stopwatch = Stopwatch.StartNew();

            long completedAt1 = 0;
            long completedAt2 = 0;
            PrintResult? result1 = null;
            PrintResult? result2 = null;

            Task task1 = Task.Run(async () =>
            {
                result1 = await printer.PrintAsync(new byte[] { 0x1B, 0x40 }, connection, CancellationToken.None);
                completedAt1 = stopwatch.ElapsedMilliseconds;
            });
            Task task2 = Task.Run(async () =>
            {
                result2 = await printer.PrintAsync(new byte[] { 0x1B, 0x40 }, connection, CancellationToken.None);
                completedAt2 = stopwatch.ElapsedMilliseconds;
            });

            Task.WhenAll(task1, task2).GetAwaiter().GetResult();

            long totalElapsedMs = Math.Max(completedAt1, completedAt2);
            bool bothPortOpenFailed = result1?.Failure == PrintFailureReason.PortOpenFailed
                && result2?.Failure == PrintFailureReason.PortOpenFailed;
            bool serialized = totalElapsedMs >= serializedThresholdMs;

            bool ok = bothPortOpenFailed && serialized;

            if (ok)
            {
                FileLogger.Info(
                    $"[receipt-print-test self] [동시 호출 직렬화] 통과 — 지연={delayMs}ms×2건, " +
                    $"완료 시각(늦은 쪽)={totalElapsedMs}ms(임계값 {serializedThresholdMs}ms 이상 — " +
                    "병렬이었다면 지연 1회분 근방에서 끝났어야 함), 두 호출 모두 PortOpenFailed");
            }
            else
            {
                FileLogger.Error(LogCategory.App,
                    $"[receipt-print-test self] ★ [동시 호출 직렬화] 실패 — 완료 시각(늦은 쪽)={totalElapsedMs}ms" +
                    $"(임계값 {serializedThresholdMs}ms), result1={result1?.Failure}, result2={result2?.Failure}");
            }

            return ok;
        }
        finally
        {
            // 테스트 전용 지연은 반드시 0으로 되돌린다 — 남아 있으면 이후 실제 출력(print/status 모드,
            // 다음 self 재실행)마다 불필요한 지연이 생긴다.
            SerialReceiptPrinter.TestOnlyCriticalSectionDelayMs = 0;
        }
    }
}
