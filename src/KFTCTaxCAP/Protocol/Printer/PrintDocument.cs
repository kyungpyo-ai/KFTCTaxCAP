using System.Collections.Generic;

namespace KFTCTaxCAP.Protocol.Printer;

/// <summary>줄 크기 — 보통 또는 가로·세로 2배(<c>GS ! 11</c>).</summary>
public enum PrintSize
{
    Normal,
    DoubleSize,
}

/// <summary>
/// 인쇄할 한 줄 — 텍스트 + 정렬 + 크기. 줄 목록 모델의 최소 단위(docs/receipt_print/development_plan.md
/// P33-1). <see cref="EscPosDocumentEncoder"/>가 이 목록을 바이트로 바꾼다. 순수 데이터 — 폭 검증·줄바꿈은
/// 하지 않는다(그건 <see cref="ReceiptTextLayout"/>과 인코더의 몫).
/// </summary>
public sealed class PrintLine
{
    public string Text { get; }
    public PrintAlignment Alignment { get; }
    public PrintSize Size { get; }

    public PrintLine(string? text, PrintAlignment alignment = PrintAlignment.Left, PrintSize size = PrintSize.Normal)
    {
        Text = text ?? string.Empty;
        Alignment = alignment;
        Size = size;
    }
}

/// <summary>
/// 출력할 문서 전체 — <see cref="PrintLine"/>의 순서 있는 목록. 조립 쪽(Phase 34 양식 코드, 진단 하네스)이
/// 채우고 <see cref="EscPosDocumentEncoder"/>가 소비한다. 빈 줄은 <see cref="AddBlankLine"/>으로 그대로
/// 허용한다(PRD §3.2 레이아웃의 빈 줄).
/// </summary>
public sealed class PrintDocument
{
    private readonly List<PrintLine> _lines = new();

    public IReadOnlyList<PrintLine> Lines => _lines;

    public PrintDocument AddLine(string? text, PrintAlignment alignment = PrintAlignment.Left, PrintSize size = PrintSize.Normal)
    {
        _lines.Add(new PrintLine(text, alignment, size));
        return this;
    }

    /// <summary>여러 줄(<see cref="ReceiptTextLayout.Wrap"/>·<see cref="ReceiptTextLayout.LabelValue"/> 결과 등)을 한 번에 추가한다.</summary>
    public PrintDocument AddLines(IEnumerable<string> texts, PrintAlignment alignment = PrintAlignment.Left, PrintSize size = PrintSize.Normal)
    {
        foreach (string text in texts)
            AddLine(text, alignment, size);

        return this;
    }

    public PrintDocument AddBlankLine() => AddLine(string.Empty);
}
