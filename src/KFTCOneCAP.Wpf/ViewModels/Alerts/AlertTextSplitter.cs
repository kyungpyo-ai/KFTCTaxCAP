namespace KFTCOneCAP.Wpf.ViewModels.Alerts;

/// <summary>
/// 알림창 문구의 "첫 줄 = 제목, 나머지 = 본문" 분리 규칙(docs/alert_dialog/PRD.md §2.2·§3.1, 사용자
/// 확정 2026-10-01). 순수 함수 — WPF 타입을 참조하지 않는다.
/// </summary>
public static class AlertTextSplitter
{
    /// <summary>
    /// 첫 번째 줄바꿈(<c>\r\n</c> 또는 <c>\n</c>) 앞을 제목, 뒤를 본문으로 나눈다.
    /// 본문 안에 남은 <c>\r\n</c>은 모두 <c>\n</c>으로 정규화하고, 제목·본문 양 끝의 공백·줄바꿈은
    /// 트림한다(본문 내부 줄 자체는 보존). 첫 줄이 비어 있으면(예: <c>"\n본문"</c>) 다음에 나오는
    /// 비어 있지 않은 줄을 제목으로 끌어올린다. <paramref name="text"/>가 null이거나 빈 문자열이면
    /// ("", "")를 반환한다.
    /// </summary>
    public static (string Title, string Body) Split(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return (string.Empty, string.Empty);

        // 줄바꿈을 \n 하나로 통일한 뒤 줄 단위로 나눈다 — \r\n과 \n이 섞여 있어도 동일하게 처리된다.
        var normalized = text!.Replace("\r\n", "\n").Replace("\r", "\n");
        var lines = normalized.Split('\n');

        var firstNonEmptyIndex = -1;
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Trim().Length > 0)
            {
                firstNonEmptyIndex = i;
                break;
            }
        }

        if (firstNonEmptyIndex < 0)
            return (string.Empty, string.Empty);

        var title = lines[firstNonEmptyIndex].Trim();
        var bodyLineCount = lines.Length - (firstNonEmptyIndex + 1);
        var bodyLines = new string[bodyLineCount > 0 ? bodyLineCount : 0];
        System.Array.Copy(lines, firstNonEmptyIndex + 1, bodyLines, 0, bodyLines.Length);
        var body = string.Join("\n", bodyLines).Trim();

        return (title, body);
    }
}
