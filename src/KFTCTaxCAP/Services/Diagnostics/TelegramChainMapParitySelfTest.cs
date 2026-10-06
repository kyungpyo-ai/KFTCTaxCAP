using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KFTCTaxCAP.KioskSim.Protocol;
using KFTCTaxCAP.Protocol.Pos;

namespace KFTCTaxCAP.Services.Diagnostics;

/// <summary>
/// Phase 40 P40-7(docs/payment_relay/development_plan.md, PRD §14.5) — 본 앱 연쇄 표(<see cref="TelegramFieldChainMap"/>)와
/// 업체에 제공하는 KioskSim 연쇄 표(<c>TelegramChainMap</c>, csproj가 한 파일을 링크 컴파일)가 <b>같은 규칙</b>인지
/// 항목 단위로 대조한다. 두 표는 독립 전사(서로 참조하지 않음)라서, SPEC이 바뀔 때 한쪽만 고치는 실수를 사람 눈에
/// 맡기지 않으려는 장치다. 이 테스트가 항상 통과하도록 짜이지 않았음은 음성 시험(KioskSim 표 한 항목을 바꿔 FAIL 확인)으로
/// 검증한다.
///
/// 1) 표 대조: (대상 전문, 대상 필드) 키로 맞춰 출처 전문·출처 필드 목록(순서 포함)·변환 종류가 1:1인지, 한쪽에만 있는
///    항목이 없는지.
/// 2) 변환 결과 대조: 같은 입력에 두 변환 함수의 결과가 같은지(절삭·합산 두 규칙·목록 분할/첫 항목·그대로 옮김).
///
/// <c>--field-chain-converter-test</c>(<see cref="TelegramFieldChainConverterSelfTest"/>)가 호출한다.
/// </summary>
internal static class TelegramChainMapParitySelfTest
{
    private const string Tag = "[chain-map-parity-test]";

    /// <summary>
    /// 본 앱 변환 종류 → KioskSim 변환 종류 대응표. <c>Direct</c>/<c>RepresentationChange</c>/<c>Widen</c> 세 개가 KioskSim
    /// <c>Copy</c> 하나로 모이는 것은 <b>의도된 차이</b>다 — 본 앱은 표에 "왜 그대로 옮겨도 되는지"(같은 표현 / 표현만 다름 /
    /// 길이 확장)를 구분해 적지만 세 경우 모두 값 변환은 "그대로"이고 0 채움은 전문 쓰기 단계가 처리하므로, 업체가 읽는
    /// KioskSim 쪽은 한 종류로 단순화했다(그 이유는 KioskSim 파일의 Copy 주석에 있다). <c>Fixed</c>는 본 앱 표에 사용처가 없고
    /// KioskSim에는 대응 종류가 없다(표에 나타나면 불일치로 잡는다).
    /// </summary>
    private static TelegramChainConversion? MapConversion(FieldChainConversion appConversion) => appConversion switch
    {
        FieldChainConversion.Direct => TelegramChainConversion.Copy,
        FieldChainConversion.RepresentationChange => TelegramChainConversion.Copy,
        FieldChainConversion.Widen => TelegramChainConversion.Copy,
        FieldChainConversion.Truncate => TelegramChainConversion.TruncateCp949,
        FieldChainConversion.Sum => TelegramChainConversion.SumResponseFields,
        FieldChainConversion.SumAllRequired => TelegramChainConversion.SumRequestFields,
        FieldChainConversion.SelectFromList => TelegramChainConversion.SelectFirstFromList,
        _ => null,
    };

    public static bool RunAll()
    {
        bool tableOk = RunTableParity();
        bool valueOk = RunValueParity();
        bool ok = tableOk && valueOk;
        FileLogger.Info($"{Tag} 완료 — 표 대조={(tableOk ? "통과" : "실패")}, 변환 결과 대조={(valueOk ? "통과" : "실패")}, 종합={(ok ? "통과" : "실패")}");
        return ok;
    }

    private static string Key(string tx, int field) => $"{tx} #{field}";

    // ------------------------------------------------------------------
    // 1) 표 대조
    // ------------------------------------------------------------------
    private static bool RunTableParity()
    {
        var problems = new List<string>();

        Dictionary<string, TelegramFieldChainMap.ChainEntry> app = TelegramFieldChainMap.Entries
            .ToDictionary(e => Key(e.TargetTelegram, e.TargetFieldNumber));
        Dictionary<string, TelegramChainEntry> kiosk = TelegramChainMap.Entries
            .ToDictionary(e => Key(e.TargetTx, e.TargetField));

        foreach (string key in app.Keys.Except(kiosk.Keys).OrderBy(k => k, StringComparer.Ordinal))
            problems.Add($"{key}: 본 앱 표에만 있음({Describe(app[key])})");
        foreach (string key in kiosk.Keys.Except(app.Keys).OrderBy(k => k, StringComparer.Ordinal))
            problems.Add($"{key}: KioskSim 표에만 있음({kiosk[key]})");

        int compared = 0;
        foreach (string key in app.Keys.Intersect(kiosk.Keys).OrderBy(k => k, StringComparer.Ordinal))
        {
            compared++;
            TelegramFieldChainMap.ChainEntry a = app[key];
            TelegramChainEntry k = kiosk[key];

            if (a.SourceTelegram != k.SourceTx)
                problems.Add($"{key}: 출처 전문이 다름 — 본 앱 {a.SourceTelegram ?? "(없음)"}, KioskSim {k.SourceTx}");

            if (!a.SourceFieldNumbers.SequenceEqual(k.SourceFields))
                problems.Add($"{key}: 출처 필드가 다름(순서 포함) — 본 앱 #{string.Join(",#", a.SourceFieldNumbers)}, KioskSim #{string.Join(",#", k.SourceFields)}");

            TelegramChainConversion? expected = MapConversion(a.Conversion);
            if (expected is null)
                problems.Add($"{key}: 본 앱 변환 종류 {a.Conversion}에 대응하는 KioskSim 종류가 없음(대응표에 없음)");
            else if (expected.Value != k.Conversion)
                problems.Add($"{key}: 변환 종류가 다름 — 본 앱 {a.Conversion}(→ {expected.Value} 기대), KioskSim {k.Conversion}");

            // 같은 전문 요청 필드를 입력으로 삼는지(자기참조)도 같아야 한다.
            bool appSelf = a.SourceTelegram == a.TargetTelegram;
            if (appSelf != k.IsWithinSameRequest)
                problems.Add($"{key}: 자기참조 여부가 다름 — 본 앱 {appSelf}, KioskSim {k.IsWithinSameRequest}");
        }

        foreach (string p in problems)
            FileLogger.Error(LogCategory.App, $"{Tag} ★ [표 대조] {p}");

        bool ok = problems.Count == 0 && app.Count == kiosk.Count;
        if (ok)
            FileLogger.Info($"{Tag} 표 대조 — 본 앱 {app.Count}건 = KioskSim {kiosk.Count}건, 항목 {compared}건 전부 출처 전문·출처 필드(순서)·변환 종류 일치");
        return ok;
    }

    private static string Describe(TelegramFieldChainMap.ChainEntry e) =>
        $"{e.TargetTelegram} #{e.TargetFieldNumber} ← {e.SourceTelegram} #{string.Join("+#", e.SourceFieldNumbers)} ({e.Conversion})";

    // ------------------------------------------------------------------
    // 2) 변환 결과 대조
    // ------------------------------------------------------------------
    private static bool RunValueParity()
    {
        int samples = 0;
        var problems = new List<string>();

        void Compare(string name, string appResult, string kioskResult)
        {
            samples++;
            if (appResult != kioskResult)
                problems.Add($"{name}: 본 앱=\"{appResult}\", KioskSim=\"{kioskResult}\"");
        }

        // 절삭 — 한글/ASCII 혼합 경계. 본 앱은 뒤 공백을 자르지 않고(패딩은 쓰기 단계) KioskSim은 TrimEnd하므로 뒤 공백만 뗀 뒤 비교한다
        // (경계에서 글자가 남느냐 버려지느냐가 의미 차이다 — 그건 그대로 비교된다).
        var truncateSamples = new (string Value, int Bytes)[]
        {
            ("가나다라마", 7), ("AB가나다", 5), ("가나다", 6), ("가나다", 5), (string.Empty, 10), ("AB가", 20),
            ("테스트세무서 징수관", 19), ("테스트세무서 징수관", 18), ("테스트세무서 징수관", 20), ("김테스트", 10), ("김테스트", 7),
            ("A가B나C다", 4), ("A가B나C다", 3), ("가A", 2),
        };
        foreach ((string value, int bytes) in truncateSamples)
        {
            string app = TelegramFieldChainConverter.Convert(FieldChainConversion.Truncate, new[] { value }, bytes).TrimEnd(' ');
            string kiosk = TelegramChainMap.TruncateCp949(value, bytes);
            Compare($"절삭(\"{value}\", {bytes}바이트)", app, kiosk);
        }

        // 합산 두 규칙 — 공백 입력. 결과 표기(앞 0 유무)는 정수값으로 비교하되 빈 값은 빈 값끼리 비교한다.
        var sumSamples = new string[][]
        {
            new[] { "100", "200", "300" },
            new[] { "100", string.Empty, "50" },
            new[] { string.Empty, string.Empty, string.Empty },
            new[] { "100", "   " },
            new[] { "123450", "0", "0" },
            new[] { "000000000344980", "000000000000000", "000000000000000" },
            new[] { "000000000344980", "000000000002759" },
            new[] { string.Empty, "50" },
            new[] { "0", "0" },
        };
        foreach (string[] values in sumSamples)
        {
            string label = "[" + string.Join("|", values.Select(v => $"\"{v}\"")) + "]";

            Compare($"합산 응답 필드(공백=0) {label}",
                NormalizeNumber(TelegramFieldChainConverter.Convert(FieldChainConversion.Sum, values, 15)),
                NormalizeNumber(KioskConvert(TelegramChainConversion.SumResponseFields, values)));

            Compare($"합산 요청 필드(하나라도 공백이면 빈 칸) {label}",
                NormalizeNumber(TelegramFieldChainConverter.Convert(FieldChainConversion.SumAllRequired, values, 15)),
                NormalizeNumber(KioskConvert(TelegramChainConversion.SumRequestFields, values)));
        }

        // 두 규칙이 서로 다르게 동작한다는 것 자체도 양쪽에서 확인(공백 포함 입력: Sum=값, SumAllRequired=빈 칸).
        string[] withBlank = { "100", string.Empty };
        Compare("합산 규칙 구분 — 응답 합산은 공백을 0으로(본 앱/KioskSim 둘 다 \"100\")",
            NormalizeNumber(TelegramFieldChainConverter.Convert(FieldChainConversion.Sum, withBlank, 15)) + "/" + NormalizeNumber(KioskConvert(TelegramChainConversion.SumResponseFields, withBlank)),
            "100/100");
        Compare("합산 규칙 구분 — 요청 합산은 공백이면 빈 칸(본 앱/KioskSim 둘 다 빈 값)",
            TelegramFieldChainConverter.Convert(FieldChainConversion.SumAllRequired, withBlank, 15) + "/" + KioskConvert(TelegramChainConversion.SumRequestFields, withBlank),
            "/");

        // 목록 분할·첫 항목 선택.
        var listSamples = new[]
        {
            "000203".PadRight(60), "00".PadRight(60), new string(' ', 60), string.Empty, "00203", "0", "00020304050607080910111224".PadRight(60),
            string.Concat(Enumerable.Range(0, 30).Select(i => i.ToString("D2"))), "0203".PadRight(60),
        };
        foreach (string list in listSamples)
        {
            string label = $"\"{list.TrimEnd()}\"({list.Length}자)";
            Compare($"목록 분할 {label}",
                string.Join(",", TelegramFieldChainConverter.SplitInstallmentList(list)),
                string.Join(",", TelegramChainMap.SplitInstallmentList(list)));
            Compare($"목록 첫 항목 선택 {label}",
                TelegramFieldChainConverter.Convert(FieldChainConversion.SelectFromList, new[] { list }, 2),
                KioskConvert(TelegramChainConversion.SelectFirstFromList, new[] { list }));
        }

        // 그대로 옮김(Direct/RepresentationChange/Widen ↔ Copy) — 값이 그대로여야 한다.
        foreach (string value in new[] { "000000003915", "9001011649195", "김테스트", "2610510", "001" })
        {
            foreach (FieldChainConversion c in new[] { FieldChainConversion.Direct, FieldChainConversion.RepresentationChange, FieldChainConversion.Widen })
                Compare($"그대로 옮김 {c}(\"{value}\")",
                    TelegramFieldChainConverter.Convert(c, new[] { value }, 20),
                    KioskConvert(TelegramChainConversion.Copy, new[] { value }));
        }

        // 알려진 처리 차이(의미 차이 아님, 실패로 세지 않고 기록만): 숫자가 아닌 합산 입력. 본 앱은 PosProtocolException(연쇄 표 적용 쪽이 잡아
        // 그 항목을 건너뜀), KioskSim은 빈 칸을 돌려준다. 정상 응답·사용자 편집이 정수라는 전제에서는 일어나지 않는다.
        try
        {
            TelegramFieldChainConverter.Convert(FieldChainConversion.Sum, new[] { "100", "abc" }, 15);
            FileLogger.Warn($"{Tag} 참고 — 비숫자 합산 입력(\"100\",\"abc\"): 본 앱이 예외를 던지지 않음(기대와 다름)");
        }
        catch (PosProtocolException)
        {
            FileLogger.Info($"{Tag} 참고(알려진 처리 차이, 실패 아님) — 비숫자 합산 입력(\"100\",\"abc\"): 본 앱=PosProtocolException, KioskSim=\"{KioskConvert(TelegramChainConversion.SumResponseFields, new[] { "100", "abc" })}\"(빈 칸)");
        }

        foreach (string p in problems)
            FileLogger.Error(LogCategory.App, $"{Tag} ★ [변환 결과 대조] {p}");

        bool ok = problems.Count == 0;
        if (ok)
            FileLogger.Info($"{Tag} 변환 결과 대조 — 샘플 {samples}건(절삭 {truncateSamples.Length}, 합산 두 규칙 {sumSamples.Length * 2 + 2}, 목록 {listSamples.Length * 2}, 그대로 옮김 15) 전부 일치");
        return ok;
    }

    /// <summary>KioskSim 변환 함수를 종류만 지정해 부른다(표 항목을 만들 필요 없이 — 출처 필드 번호는 변환 결과와 무관).</summary>
    private static string KioskConvert(TelegramChainConversion conversion, IReadOnlyList<string> values)
    {
        var entry = new TelegramChainEntry("T", 1, "S", conversion, Enumerable.Range(1, values.Count).ToArray());
        return TelegramChainMap.Convert(entry, values, 20);
    }

    /// <summary>합산 결과의 앞 0 유무 같은 표기 차이를 정수값으로 맞춘다. 빈 값은 그대로 빈 값(= 빈 칸).</summary>
    private static string NormalizeNumber(string value) =>
        value.Length == 0 ? value : long.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
}
