namespace KFTCOneCAP.Wpf.Services.Receipt;

/// <summary>
/// "국세 납부확인증" 조립에 필요한 이름 붙은 표시용 값 모델(docs/receipt_print/PRD.md §2.2,
/// development_plan.md Phase 34 P34-1). <b>전문 필드 번호를 전혀 모른다</b> — 501008/800000/902614의
/// 어느 필드에서 왔는지는 이 타입을 채우는 쪽(ViewModel, P34-3 <c>NationalTaxReceiptAssembler</c>)의
/// 책임이다(PRD §7.1 계층 원칙, "영수증 모델 조립은 전문 필드 번호를 아는 유일한 곳(ViewModel)에 둔다").
///
/// 모든 값은 <b>SPEC 원문 그대로(트림·패딩 제거 전)</b>의 <c>string?</c>다 — 천 단위 쉼표, 날짜 형식,
/// 마스킹, 공백→기본값 같은 표시 변환은 전부 <see cref="ReceiptValueFormatter"/>가 한다. 이 모델
/// 자체는 변환하지 않는다(값을 있는 그대로 담아 나르는 순수 데이터 컨테이너).
/// </summary>
public sealed class NationalTaxReceipt
{
    /// <summary>전자납부번호(PRD §2.2 #1).</summary>
    public string? ElectronicPaymentNumber { get; set; }

    /// <summary>납세자명(PRD §2.2 #2).</summary>
    public string? TaxpayerName { get; set; }

    /// <summary>납세자번호 원문(PRD §2.2 #3) — 마스킹 전 원본. <see cref="ReceiptValueFormatter.MaskTaxpayerNumber"/>가 가공한다.</summary>
    public string? TaxpayerNumber { get; set; }

    /// <summary>세입징수관서(PRD §2.2 #4) — 42칸을 넘으면 조립기가 줄바꿈을 허용한다.</summary>
    public string? CollectingAgency { get; set; }

    /// <summary>세입징수관계좌번호(PRD §2.2 #5).</summary>
    public string? AccountNumber { get; set; }

    /// <summary>납기내기한 원문(PRD §2.2 #6, N8 <c>YYYYMMDD</c>).</summary>
    public string? DueDateWithinPeriod { get; set; }

    /// <summary>납기내금액 원문(PRD §2.2 #7, N15).</summary>
    public string? AmountWithinPeriod { get; set; }

    /// <summary>납기후기한 원문(PRD §2.2 #8, N8).</summary>
    public string? DueDateAfterPeriod { get; set; }

    /// <summary>납기후금액 원문(PRD §2.2 #9, N15).</summary>
    public string? AmountAfterPeriod { get; set; }

    /// <summary>납부세액 원문(PRD §2.2 #10, N15).</summary>
    public string? TaxAmount { get; set; }

    /// <summary>납부대행수수료 원문(PRD §2.2 #11, N15).</summary>
    public string? Fee { get; set; }

    /// <summary>총 납부금액 원문(PRD §2.2 #12, N15).</summary>
    public string? TotalAmount { get; set; }

    /// <summary>선불카드잔액 원문(PRD §2.2 #13, AN12 — 공백이면 <see cref="ReceiptValueFormatter.PrepaidBalance"/>가 <c>0</c>으로 취급).</summary>
    public string? PrepaidCardBalance { get; set; }

    /// <summary>납부카드(카드사명, PRD §2.2 #14).</summary>
    public string? CardCompanyName { get; set; }

    /// <summary>할부개월수 원문(PRD §2.2 #15, N2 — <c>00</c>/<c>01</c>은 일시불).</summary>
    public string? InstallmentMonths { get; set; }

    /// <summary>
    /// 카드번호(PRD §2.2 #16) — 800000 응답 <c>#14</c> 마스킹 카드번호(AN19, SPEC 20260930, 리더기가 이미
    /// 마스킹한 값) 원문. 800000 응답이 없으면 <c>null</c>(빈칸). Phase 36 선행 후 P34-3에서 연결했다(PRD §2.4).
    /// </summary>
    public string? CardNumber { get; set; }

    /// <summary>납부일자 원문(PRD §2.2 #17, N8).</summary>
    public string? PaymentDate { get; set; }

    /// <summary>하단 안내 2번 문구의 수수료율 원문(PRD §2.2 F, N4 — 예: <c>0080</c> = 0.8%). 값이 없으면 <c>null</c>.</summary>
    public string? FeeRatePercentRaw { get; set; }

    /// <summary>
    /// 재출력 여부(Phase 35용, PRD §4.2 — 제목 위에 <c>(재출력)</c> 한 줄 추가). Phase 34의 자동 출력
    /// 경로는 이 값을 항상 <c>false</c>로 둔다.
    /// </summary>
    public bool IsReprint { get; set; }
}
