using System;
using System.Collections.Generic;

namespace KFTCTaxCAP.Protocol.Printer;

/// <summary>
/// 한 줄 폭(칸 수)을 CP949 <b>바이트</b> 기준으로 계산·줄바꿈하는 순수 함수 계층
/// (docs/receipt_print/PRD.md §3.1, development_plan.md P33-1). <c>string.Length</c>(문자 수) 기반
/// 계산을 쓰지 않는다 — 한글 1자는 2바이트(2칸), ASCII 1자는 1바이트(1칸)다.
/// </summary>
public static class ReceiptTextLayout
{
    /// <summary>기본 한 줄 칸 수. 실제 값은 <see cref="PrinterProfile.LineWidth"/>를 그대로 노출한다.</summary>
    public const int LineWidth = PrinterProfile.LineWidth;

    /// <summary><paramref name="text"/>의 CP949 바이트 수(=칸 수)를 반환한다. <c>null</c>은 0.</summary>
    public static int ByteWidth(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        return ReceiptEncoding.Value.GetByteCount(text);
    }

    /// <summary>
    /// <paramref name="text"/>를 <paramref name="widthBytes"/> 칸(바이트)마다 줄바꿈한다. 다음 글자를
    /// 더했을 때 폭을 넘기면 그 글자부터 다음 줄로 넘긴다 — 2바이트 글자를 쪼개지 않는다. UTF-16
    /// 서로게이트 쌍(<c>char.IsHighSurrogate</c>+<c>IsLowSurrogate</c>, 예: 이모지·일부 확장한자)도
    /// 한 글자로 묶어서 다룬다 — 둘로 쪼개면 단독 서로게이트가 남아 그 자체로 깨진 문자열이 된다
    /// (2026-09-30 CP1 리뷰 L-1). 공백도 1칸 글자로 취급해 그대로 옮기되, <b>각 줄의 끝 공백은
    /// 제거한다(줄 맨 앞 공백은 그대로 둔다)</b> — 2026-09-30 사용자 결정, 제거 대상은
    /// <see cref="char.IsWhiteSpace(char)"/> 기준 모든 공백 문자(전각 공백 U+3000·탭 포함, CP1 리뷰
    /// L-2). 줄 경계 바로 다음이 공백이면(그 공백이 폭을 넘겨 다음 줄로 밀리면) 그 공백은 다음 줄 맨
    /// 앞 공백으로 남는다(PRD §3.3 2번 문구 둘째 줄이 이 경우). 반대로 공백이 줄 끝에 걸려 폭 안에
    /// 들어가는 경우(PRD §3.3 1번 문구)는 그 줄 끝에서 제거된다. 빈 문자열·<c>null</c> → 빈 줄 1개
    /// (길이 0인 목록이 아니다).
    /// </summary>
    public static IReadOnlyList<string> Wrap(string? text, int widthBytes)
    {
        if (widthBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(widthBytes), widthBytes, "폭은 1 이상이어야 합니다.");

        if (string.IsNullOrEmpty(text))
            return new[] { string.Empty };

        string value = text!; // 위 IsNullOrEmpty 통과 후 non-null 확정(net48 레퍼런스 어셈블리엔
                               // NotNullWhen 주석이 없어 컴파일러가 좁히지 못하므로 null-forgiving으로 확정).

        var lines = new List<string>();
        int lineStart = 0;
        int lineBytes = 0;

        int i = 0;
        while (i < value.Length)
        {
            // 서로게이트 쌍(상위+하위)이면 2 char를 한 글자로 묶는다 — 그 외에는 1 char가 한 글자.
            int charLength = char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1])
                ? 2
                : 1;
            int charBytes = ReceiptEncoding.Value.GetByteCount(value.ToCharArray(i, charLength));
            if (lineBytes + charBytes > widthBytes)
            {
                lines.Add(value.Substring(lineStart, i - lineStart).TrimEnd());
                lineStart = i;
                lineBytes = 0;
            }

            lineBytes += charBytes;
            i += charLength;
        }

        lines.Add(value.Substring(lineStart).TrimEnd());
        return lines;
    }

    /// <summary>
    /// <c>라벨 + " : " + 값</c>을 만들어 <see cref="Wrap"/>한다(항목 행 조립 — PRD §3.2). 값이 <c>null</c>이면
    /// 빈 문자열로 취급한다(라벨은 그대로 찍힌다).
    /// </summary>
    public static IReadOnlyList<string> LabelValue(string label, string? value, int widthBytes)
        => Wrap($"{label} : {value ?? string.Empty}", widthBytes);

    /// <summary><paramref name="widthBytes"/>칸짜리 구분선(<c>'-'</c> 반복, PRD §3.2)을 만든다.</summary>
    public static string Separator(int widthBytes)
    {
        if (widthBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(widthBytes), widthBytes, "폭은 0 이상이어야 합니다.");

        return new string('-', widthBytes);
    }
}
