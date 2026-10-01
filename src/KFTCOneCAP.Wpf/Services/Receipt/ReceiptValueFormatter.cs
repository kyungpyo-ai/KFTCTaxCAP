using System;
using System.Globalization;
using System.Text;

namespace KFTCOneCAP.Wpf.Services.Receipt;

/// <summary>
/// <see cref="NationalTaxReceipt"/>의 SPEC 원문 값을 영수증에 찍을 표시 문자열로 바꾸는 순수 함수 모음
/// (docs/receipt_print/PRD.md §2.2 "표시 변환" 열, §3.3, §3.4, §9. development_plan.md Phase 34 P34-1).
/// 전문 필드 번호를 모른다 — 문자열만 받고 문자열만 돌려준다. I/O·로그·WPF 참조 없음.
/// </summary>
public static class ReceiptValueFormatter
{
    /// <summary>
    /// 금액(N 필드, 0 패딩) → 천 단위 쉼표(예: <c>"000000000100000"</c> → <c>"100,000"</c>).
    /// 전부 0이면 <c>"0"</c>(빈 값이 아니다 — PRD §9 확정값). 공백뿐이거나 <c>null</c>이면 빈 문자열
    /// (빈 값 → 라벨만 찍기, PRD §9 #7). 숫자로 파싱되지 않는 값(SPEC 위반)은 트림한 원문을 그대로
    /// 돌려준다(조용히 삼키지 않는다).
    /// </summary>
    public static string Amount(string? raw)
    {
        string trimmed = raw?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            return string.Empty;

        if (long.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out long value))
            return value.ToString("N0", CultureInfo.InvariantCulture);

        return trimmed;
    }

    /// <summary>
    /// 날짜(N8, <c>YYYYMMDD</c>) → <c>YYYY.MM.DD</c>. 8자리 숫자가 아니면(형식 위반) 트림한 원문을
    /// 그대로 돌려준다. 공백뿐이거나 <c>null</c>이면 빈 문자열.
    /// </summary>
    public static string Date(string? raw)
    {
        string trimmed = raw?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            return string.Empty;

        if (trimmed.Length == 8 && IsAllDigits(trimmed))
        {
            return $"{trimmed.Substring(0, 4)}.{trimmed.Substring(4, 2)}.{trimmed.Substring(6, 2)}";
        }

        return trimmed;
    }

    /// <summary>
    /// 할부개월수(N2) → <c>00</c>/<c>01</c>은 <c>일시불</c>, 그 외 숫자는 앞자리 0을 뗀 <c>N개월</c>
    /// (예: <c>03</c> → <c>3개월</c>, PRD §2.2 #15). 숫자가 아니면 트림한 원문 그대로. 공백뿐이거나
    /// <c>null</c>이면 빈 문자열.
    /// </summary>
    public static string Installment(string? raw)
    {
        string trimmed = raw?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            return string.Empty;

        if (trimmed is "00" or "01")
            return "일시불";

        if (IsAllDigits(trimmed) && int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out int months))
            return $"{months}개월";

        return trimmed;
    }

    /// <summary>
    /// 납세자번호 마스킹(PRD §3.4) — 트림 후 앞 6자리만 보이고 나머지는 전부 <c>*</c>(예: 13자리면
    /// <c>800101*******</c>). 6자 이하면 그대로(짧은 값을 억지로 자르지 않는다). 공백뿐이거나
    /// <c>null</c>이면 빈 문자열.
    /// </summary>
    public static string MaskTaxpayerNumber(string? raw)
    {
        string trimmed = raw?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            return string.Empty;

        if (trimmed.Length <= 6)
            return trimmed;

        return trimmed.Substring(0, 6) + new string('*', trimmed.Length - 6);
    }

    /// <summary>
    /// 선불카드잔액(PRD §2.2 #13) — 공백뿐이면 <c>"0"</c>(다른 항목의 "빈 값 → 라벨만"과 다른 예외,
    /// 2026-09-30 확정). 숫자면 <see cref="Amount"/>와 같은 천 단위 쉼표 규칙.
    /// </summary>
    public static string PrepaidBalance(string? raw)
    {
        string trimmed = raw?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            return "0";

        return Amount(trimmed);
    }

    /// <summary>
    /// 수수료율(PRD §2.2 F, §3.3) — N4 원문을 소수점 2자리로 해석한다(정수부/소수부 각 2자리, 예:
    /// <c>0050</c> = 0.50% → <c>0.5</c>). 소수부 끝의 0은 떼고, 소수부가 전부 0이면 소수점도 뗀다
    /// (<c>0080</c> → <c>0.8</c>, <c>0075</c> → <c>0.75</c>, <c>1000</c> → <c>10</c>). <b>%는 붙이지
    /// 않는다</b> — 안내 문구에 끼워 넣는 조립기(<see cref="NationalTaxReceiptComposer"/>)의 몫이다.
    /// 공백·숫자가 아닌 값은 <c>null</c>(요율 없음 — 조립기가 요율 없는 문구로 대체한다, PRD §3.3).
    /// </summary>
    public static string? FeeRatePercent(string? raw)
    {
        string trimmed = raw?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || !IsAllDigits(trimmed))
            return null;

        if (!int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out int hundredths))
            return null;

        decimal percent = hundredths / 100m;
        return percent.ToString("0.##", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 카드번호(PRD §2.2 #16, §3.4) — 4자리마다 <c>-</c>를 넣는다. 마스킹은 하지 않는다(리더기가 이미
    /// 마스킹된 값을 준다는 전제, §2.4 — 값은 800000 응답 <c>#14</c> 마스킹 카드번호, Phase 36을 P34-3 전에
    /// 진행해 Phase 34부터 실사용). 공백뿐
    /// 이거나 <c>null</c>이면 빈 문자열.
    /// </summary>
    public static string CardNumber(string? raw)
    {
        string trimmed = raw?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            return string.Empty;

        var sb = new StringBuilder(trimmed.Length + trimmed.Length / 4);
        for (int i = 0; i < trimmed.Length; i++)
        {
            if (i > 0 && i % 4 == 0)
                sb.Append('-');

            sb.Append(trimmed[i]);
        }

        return sb.ToString();
    }

    /// <summary>
    /// 제어문자 방어(PRD §9 #14, 2026-09-30 확정 — CP1 L-3 이월분) — 0x00~0x1F·0x7F를 공백으로
    /// 치환한 뒤 뒤 공백을 뗀다(SPEC 필드의 뒤 공백 패딩 제거 겸용 — PRD §2.2의 여러 항목이 요구하는
    /// "뒤 공백 제거"는 이 함수 하나로 처리된다). ESC/POS 명령 바이트(예: <c>ESC @</c> = <c>1B 40</c>)가
    /// 값 안에 섞여 있어도 <c>1B</c>(ESC)가 공백으로 바뀌므로 인코딩 결과에 그 명령 시퀀스가 그대로
    /// 나타나지 않는다. <c>null</c>이면 빈 문자열.
    /// </summary>
    public static string Sanitize(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return string.Empty;

        string value = raw!; // IsNullOrEmpty 통과 후 non-null 확정(net48 NotNullWhen 미지원, ReceiptTextLayout.Wrap과 동일 패턴).

        var sb = new StringBuilder(value.Length);
        foreach (char c in value)
        {
            sb.Append(IsControlChar(c) ? ' ' : c);
        }

        return sb.ToString().TrimEnd();
    }

    private static bool IsControlChar(char c) => c <= 0x1F || c == 0x7F;

    private static bool IsAllDigits(string text)
    {
        foreach (char c in text)
        {
            if (c < '0' || c > '9')
                return false;
        }

        return text.Length > 0;
    }
}
