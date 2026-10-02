using System;
using System.Collections.Generic;
using System.Text;

namespace KFTCTaxCAP.ViewModels.Alerts;

/// <summary>
/// 알림창 본문 어절 단위 줄바꿈(CSS <c>word-break: keep-all</c> 동작, docs/alert_dialog/PRD.md §4.4, 사용자 확정
/// 2026-10-01). WPF TextBlock은 한글을 글자 단위로 끊어 "지정할 수 없습니 / 다."처럼 낱말 중간에서 넘어가므로,
/// 공백 위치에만 줄바꿈(<c>\n</c>)을 넣은 문자열을 만들어 TextBlock에 준다.
/// <list type="bullet">
/// <item>기존 <c>\n</c>은 그대로 둔다(단락 경계).</item>
/// <item>한 어절이 폭보다 길 때만 그 어절을 글자 단위로 끊는다(예외 메시지·경로 등).</item>
/// <item>줄 안의 공백(여러 개 포함)은 보존하고, 줄이 바뀌는 자리의 공백만 버린다. 단락 첫 줄의 앞 공백(들여쓰기)도 보존.</item>
/// </list>
/// 순수 함수다 — 실제 글자 폭은 호출자(View)가 폰트·크기·DPI로 재는 함수(<paramref name="measure"/>)로 주입한다.
/// </summary>
public static class AlertWordWrapper
{
    public static string Wrap(string? text, double maxWidth, Func<string, double> measure)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        if (measure == null)
            throw new ArgumentNullException(nameof(measure));

        var paragraphs = text!.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        var output = new List<string>();
        foreach (var paragraph in paragraphs)
            WrapParagraph(paragraph, maxWidth, measure, output);

        return string.Join("\n", output);
    }

    private static void WrapParagraph(string paragraph, double maxWidth, Func<string, double> measure, List<string> output)
    {
        string line = string.Empty;
        bool lineHasWord = false;
        bool firstLine = true;

        foreach (var (spaces, word) in Tokenize(paragraph))
        {
            // 첫 줄의 앞 공백(들여쓰기)과 줄 안의 공백은 살리고, 새 줄 맨 앞으로 넘어가는 공백은 버린다.
            string candidate = lineHasWord ? line + spaces + word : (firstLine ? spaces : string.Empty) + word;
            if (measure(candidate) <= maxWidth)
            {
                line = candidate;
                lineHasWord = true;
                continue;
            }

            if (lineHasWord)
            {
                output.Add(line);
                firstLine = false;
                line = string.Empty;
                lineHasWord = false;
                candidate = word;
                if (measure(candidate) <= maxWidth)
                {
                    line = candidate;
                    lineHasWord = true;
                    continue;
                }
            }

            // 한 어절이 한 줄보다 길다 — 이 어절만 글자 단위로 끊는다(앞에 붙은 들여쓰기는 첫 조각에 유지).
            string prefix = candidate.Substring(0, candidate.Length - word.Length);
            var current = new StringBuilder(prefix);
            bool currentHasChar = false;
            foreach (char ch in word)
            {
                if (currentHasChar && measure(current.ToString() + ch) > maxWidth)
                {
                    output.Add(current.ToString());
                    firstLine = false;
                    current.Clear();
                    currentHasChar = false;
                }

                current.Append(ch);
                currentHasChar = true;
            }

            line = current.ToString();
            lineHasWord = true;
        }

        // 빈 단락(연속 \n)도 빈 줄로 보존한다. 단락 끝 공백만 남은 경우는 버린다.
        output.Add(lineHasWord ? line : string.Empty);
    }

    /// <summary>단락을 (앞 공백, 어절) 쌍으로 나눈다. 끝에 남은 공백은 버린다.</summary>
    private static IEnumerable<(string Spaces, string Word)> Tokenize(string paragraph)
    {
        int i = 0;
        while (i < paragraph.Length)
        {
            int spaceStart = i;
            while (i < paragraph.Length && paragraph[i] == ' ')
                i++;
            int wordStart = i;
            while (i < paragraph.Length && paragraph[i] != ' ')
                i++;

            if (i > wordStart)
                yield return (paragraph.Substring(spaceStart, wordStart - spaceStart), paragraph.Substring(wordStart, i - wordStart));
        }
    }
}
