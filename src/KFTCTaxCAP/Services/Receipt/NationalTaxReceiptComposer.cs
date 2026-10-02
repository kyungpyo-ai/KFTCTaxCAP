using System;
using System.Collections.Generic;
using KFTCTaxCAP.Protocol.Printer;

namespace KFTCTaxCAP.Services.Receipt;

/// <summary>
/// <see cref="NationalTaxReceipt"/> → <see cref="PrintDocument"/>(docs/receipt_print/PRD.md §3 전체,
/// development_plan.md Phase 34 P34-1). 레이아웃 순서·구분선·빈 줄·하단 안내 문구·제목 배율은 PRD §3.2를
/// 그대로 따른다. 전문 필드 번호를 모른다 — <see cref="NationalTaxReceipt"/>가 이미 채워 온 이름 붙은
/// 값만 다룬다. 순수 함수(I/O 없음) — <see cref="ReceiptTextLayout"/>·<see cref="ReceiptValueFormatter"/>만
/// 쓴다.
///
/// <b>모든 값은 표시 변환(해당하면) 다음 <see cref="ReceiptValueFormatter.Sanitize"/>를 거쳐
/// <see cref="ReceiptTextLayout.LabelValue"/>에 들어간다</b> — 제어문자 방어(PRD §9 #14)와 "뒤 공백
/// 제거"(PRD §2.2 여러 항목)를 한 지점에서 보장하기 위해서다.
/// </summary>
public static class NationalTaxReceiptComposer
{
    private const string ReprintLabel = "재출력";

    /// <summary>제목 — <b>고정 문자열</b>(2026-10-01 사용자 확정: 이 프로그램은 종합소득세 전용이라 전문의
    /// <c>징수 과목명</c>(501008/902614 <c>#21</c>)을 읽지 않는다, PRD §2.2 T·§3.2). 2배 크기 21칸에 정확히 맞는다.</summary>
    private const string Title = "종합소득세 납부확인증";
    private const string Notice1 =
        "1.신용카드로 납부한 국세는 할부거래계약서 및 약관 등에도 불구하고 납부를 취소할 수 없습니다.";
    private const string Notice2WithoutRatePrefix = "2.신용카드로 국세를 납부할 경우 ";
    private const string Notice2Suffix = "납부대행수수료는 납세자가 부담합니다.";

    public static PrintDocument Compose(NationalTaxReceipt receipt)
    {
        if (receipt is null)
            throw new ArgumentNullException(nameof(receipt));

        int width = PrinterProfile.LineWidth;
        int titleWidth = width / 2;
        var doc = new PrintDocument();

        // PRD §4.2 — 재출력이면 제목 위에 "(재출력)" 한 줄(가운데, 보통 크기). Phase 34 자동 출력
        // 경로는 IsReprint가 항상 false라 이 줄이 나오지 않는다(Phase 35에서 실사용).
        if (receipt.IsReprint)
        {
            doc.AddLine($"({ReprintLabel})", PrintAlignment.Center, PrintSize.Normal);
        }

        // 제목: 고정 "종합소득세 납부확인증", 가로·세로 2배, 가운데 정렬(PRD §3.2). 폭 LineWidth/2(21칸)에
        // 정확히 맞지만, 상수를 바꿀 때를 대비해 Wrap을 그대로 거친다.
        doc.AddLines(ReceiptTextLayout.Wrap(Title, titleWidth), PrintAlignment.Center, PrintSize.DoubleSize);

        doc.AddLine(ReceiptTextLayout.Separator(width));

        AddLabelValue(doc, "전자납부번호", receipt.ElectronicPaymentNumber, width);
        AddLabelValue(doc, "납세자명", receipt.TaxpayerName, width);
        AddLabelValue(doc, "납세자번호", ReceiptValueFormatter.MaskTaxpayerNumber(receipt.TaxpayerNumber), width);
        AddLabelValue(doc, "세입징수관서", receipt.CollectingAgency, width);
        AddLabelValue(doc, "세입징수관계좌번호", receipt.AccountNumber, width);

        doc.AddLine(ReceiptTextLayout.Separator(width));

        AddLabelValue(doc, "납기내기한", ReceiptValueFormatter.Date(receipt.DueDateWithinPeriod), width);
        AddLabelValue(doc, "납기내금액(원)", ReceiptValueFormatter.Amount(receipt.AmountWithinPeriod), width);
        AddLabelValue(doc, "납기후기한", ReceiptValueFormatter.Date(receipt.DueDateAfterPeriod), width);
        AddLabelValue(doc, "납기후금액(원)", ReceiptValueFormatter.Amount(receipt.AmountAfterPeriod), width);

        doc.AddLine(ReceiptTextLayout.Separator(width));

        AddLabelValue(doc, "납부세액(원)", ReceiptValueFormatter.Amount(receipt.TaxAmount), width);
        AddLabelValue(doc, "납부대행수수료(원)", ReceiptValueFormatter.Amount(receipt.Fee), width);
        AddLabelValue(doc, "총 납부금액(원)", ReceiptValueFormatter.Amount(receipt.TotalAmount), width);
        AddLabelValue(doc, "선불카드잔액(원)", ReceiptValueFormatter.PrepaidBalance(receipt.PrepaidCardBalance), width);

        doc.AddLine(ReceiptTextLayout.Separator(width));

        AddLabelValue(doc, "납부카드", receipt.CardCompanyName, width);
        AddLabelValue(doc, "할부개월수", ReceiptValueFormatter.Installment(receipt.InstallmentMonths), width);
        AddLabelValue(doc, "카드번호", ReceiptValueFormatter.CardNumber(receipt.CardNumber), width);
        AddLabelValue(doc, "납부일자", ReceiptValueFormatter.Date(receipt.PaymentDate), width);

        doc.AddLine(ReceiptTextLayout.Separator(width));
        doc.AddBlankLine();

        doc.AddLines(ReceiptTextLayout.Wrap(Notice1, width));

        doc.AddBlankLine();

        string? feeRatePercent = ReceiptValueFormatter.FeeRatePercent(receipt.FeeRatePercentRaw);
        string notice2 = feeRatePercent is null
            ? $"{Notice2WithoutRatePrefix}{Notice2Suffix}"
            : $"{Notice2WithoutRatePrefix}납부세액의 {feeRatePercent}%인 {Notice2Suffix}";
        doc.AddLines(ReceiptTextLayout.Wrap(notice2, width));

        return doc;
    }

    /// <summary><c>라벨 : 값</c> 줄(들)을 문서에 추가한다 — 값은 여기서 <see cref="ReceiptValueFormatter.Sanitize"/>를
    /// 거친다(호출부는 표시 변환까지만 하고 이 메서드로 넘긴다). 빈 값이면 "라벨만"(PRD §9 #7) —
    /// <see cref="ReceiptTextLayout.Wrap"/>이 줄 끝 공백을 트림하므로 결과는 <c>"라벨 :"</c>가 된다.</summary>
    private static void AddLabelValue(PrintDocument document, string label, string? value, int width)
    {
        IReadOnlyList<string> lines = ReceiptTextLayout.LabelValue(label, ReceiptValueFormatter.Sanitize(value), width);
        document.AddLines(lines);
    }
}
