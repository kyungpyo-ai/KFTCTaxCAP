using System;
using KFTCTaxCAP.Services.Receipt;

namespace KFTCTaxCAP.ViewModels.Payment;

/// <summary>
/// 결제창 세 탭(501008/800000/902614)의 응답값 → <see cref="NationalTaxReceipt"/>(docs/receipt_print/PRD.md §2.2,
/// development_plan.md Phase 34 P34-3). <b>전문 필드 번호를 아는 유일한 곳</b>이다 — <c>Services/Receipt/</c>는
/// 이름 붙은 값만 알고 필드 번호를 모른다(PRD §7.1 계층 원칙). 제목은 고정 문자열이라 <c>#21 징수 과목명</c>은
/// 읽지 않는다(2026-10-01 사용자 확정 — 종합소득세 전용).
///
/// 입력은 탭별 읽기 함수(<c>Func&lt;int, string?&gt;</c>, 보통 <see cref="PaymentTelegramTabViewModel.TryReadResponseField"/>)
/// 다. <b>그 탭에 (파싱된) 응답이 없으면 <c>null</c>을 돌려주는 함수</b>여야 한다 — 읽기 함수 자체를
/// <c>null</c>로 넘겨도 "응답 없음"으로 취급한다. 값은 원문 그대로 모델에 넣고, 트림·쉼표·날짜·마스킹·
/// 하이픈 같은 표시 변환은 <see cref="ReceiptValueFormatter"/>/<see cref="NationalTaxReceiptComposer"/>가 한다
/// (모두 트림 후 처리 — 여기서 다시 트림하지 않는다). 예외: 501008 짝 검사 비교만 이 클래스가 트림해서 한다.
/// </summary>
internal static class NationalTaxReceiptAssembler
{
    /// <summary>902614 응답 <c>#7</c> 응답코드 중 승인(출력 대상) 값(PRD §2.3).</summary>
    internal const string ApprovedResponseCode = "000";

    // ── 필드 번호 (PRD §2.2 표 그대로. 전문 번호를 이름에 붙인다 — 같은 이름 필드가 전문마다 번호가 다르다) ──
    private const int Field902614ResponseCode = 7;              // 공통부 #7 응답 코드
    private const int Field902614TaxpayerRegNo = 14;            // 대체 #3 주민(사업자,법인)등록번호 AN13
    private const int Field902614ElectronicPaymentNo = 15;      // #1 전자납부번호 AN19 (짝 검사 키)
    private const int Field902614AccountNo = 19;                // #5 징수관 계좌번호 AN6
    private const int Field902614CollectingAgency = 20;         // 대체 #4 징수 기관명 AHN20
    private const int Field902614TaxAmount = 27;                // #10 납부 세액 N15
    private const int Field902614Fee = 28;                      // #11 수수료 N15
    private const int Field902614TotalAmount = 29;              // #12 총 납부 금액 N15
    private const int Field902614PaymentDate = 32;              // #17 납부 일자 N8
    private const int Field902614Installment = 34;              // #15 할부 개월 수 N2
    private const int Field902614PayerName = 37;                // 대체 #2 납부자 성명 AHNS10
    private const int Field902614PrepaidBalance = 52;           // #13 선불카드 잔액 AN12

    private const int Field501008ResponseCode = 7;              // 공통부 #7 응답 코드 (1순위 사용 조건)
    private const int Field501008ElectronicPaymentNo = 14;      // 짝 검사 키 AN19
    private const int Field501008TaxpayerNo = 17;               // #3 납세 의무자 번호 AN13
    private const int Field501008TaxpayerName = 18;             // #2 납세 의무자 명 AHN40
    private const int Field501008CollectingAgency = 19;         // #4 징수 기관명 AHN40
    private const int Field501008AmountWithinPeriod = 25;       // #7 납기내 금액 N15
    private const int Field501008DueDateWithinPeriod = 26;      // #6 납기일(납기내) N8
    private const int Field501008AmountAfterPeriod = 27;        // #9 납기후 금액 N15
    private const int Field501008DueDateAfterPeriod = 28;       // #8 납기일(납기후) N8

    private const int Field800000MaskedCardNumber = 14;         // #16 마스킹 카드번호 AN19 (SPEC 20260930)
    private const int Field800000CardCompanyName = 18;          // #14 카드사명 AHN30
    private const int Field800000FeeRate = 26;                  // F 납부대행 수수료율 N4

    /// <summary>
    /// 승인 판정(PRD §2.3) — 902614 응답 <c>#7</c>(트림)이 <c>000</c>일 때만 true. 응답 없음·파싱 실패
    /// (읽기 결과 <c>null</c>)는 false.
    /// </summary>
    internal static bool IsApproved(Func<int, string?>? read902614) =>
        read902614?.Invoke(Field902614ResponseCode)?.Trim() == ApprovedResponseCode;

    /// <summary>
    /// 501008 탭을 1순위로 쓸지(PRD §2.2 아래 첫 항목) — 세 조건을 모두 만족할 때만 true:
    /// ① 501008 응답이 있고 그 <c>#7</c>(트림)이 <c>000</c>(정상 조회 — CP1 Opus 결정: 실패 응답은 요청
    /// 필드를 에코해 전자납부번호만 같을 수 있으므로 값으로 쓰지 않는다), ② 501008 <c>#14</c>(트림)가
    /// 902614 <c>#15</c>(트림)와 같다, ③ 둘 다 비어 있지 않다(빈 값끼리의 우연한 일치 배제). 하나라도 어긋나면
    /// 다른 고지서/실패 응답 값이 섞일 위험을 피해 501008을 <b>하나도</b> 쓰지 않는다.
    /// </summary>
    internal static bool Use501008(Func<int, string?>? read501008, Func<int, string?> read902614)
    {
        if (read501008?.Invoke(Field501008ResponseCode)?.Trim() != ApprovedResponseCode)
            return false;

        string? noticeNumber = read501008(Field501008ElectronicPaymentNo)?.Trim();
        string? approvalNumber = read902614(Field902614ElectronicPaymentNo)?.Trim();
        return !string.IsNullOrEmpty(noticeNumber)
            && !string.IsNullOrEmpty(approvalNumber)
            && string.Equals(noticeNumber, approvalNumber, StringComparison.Ordinal);
    }

    /// <summary>
    /// 세 탭 값으로 영수증 모델을 만든다. 501008은 <see cref="Use501008"/>이 true일 때만 쓰고(대체 출처가
    /// 있는 항목은 1순위가 공백이면 대체 — <see cref="PrimaryOrFallback"/>), 아니면 <b>하나도</b> 쓰지 않는다
    /// (대체 출처 또는 빈칸). 800000은 응답이 있을 때만(없으면 카드사명·
    /// 요율·카드번호 null). <see cref="NationalTaxReceipt.IsReprint"/>는 false(자동 출력 경로).
    /// </summary>
    internal static NationalTaxReceipt Assemble(
        Func<int, string?>? read501008,
        Func<int, string?>? read800000,
        Func<int, string?> read902614)
    {
        if (read902614 is null)
            throw new ArgumentNullException(nameof(read902614));

        // 짝 검사 실패면 501008 읽기 함수를 통째로 null로 바꿔, 아래에서 부분 적용이 생길 수 없게 한다.
        Func<int, string?>? notice = Use501008(read501008, read902614) ? read501008 : null;

        return new NationalTaxReceipt
        {
            ElectronicPaymentNumber = read902614(Field902614ElectronicPaymentNo),
            TaxpayerName = PrimaryOrFallback(notice?.Invoke(Field501008TaxpayerName), read902614(Field902614PayerName)),
            TaxpayerNumber = PrimaryOrFallback(notice?.Invoke(Field501008TaxpayerNo), read902614(Field902614TaxpayerRegNo)),
            CollectingAgency = PrimaryOrFallback(notice?.Invoke(Field501008CollectingAgency), read902614(Field902614CollectingAgency)),
            AccountNumber = read902614(Field902614AccountNo),
            DueDateWithinPeriod = notice?.Invoke(Field501008DueDateWithinPeriod),
            AmountWithinPeriod = notice?.Invoke(Field501008AmountWithinPeriod),
            DueDateAfterPeriod = notice?.Invoke(Field501008DueDateAfterPeriod),
            AmountAfterPeriod = notice?.Invoke(Field501008AmountAfterPeriod),
            TaxAmount = read902614(Field902614TaxAmount),
            Fee = read902614(Field902614Fee),
            TotalAmount = read902614(Field902614TotalAmount),
            PrepaidCardBalance = read902614(Field902614PrepaidBalance),
            CardCompanyName = read800000?.Invoke(Field800000CardCompanyName),
            InstallmentMonths = read902614(Field902614Installment),
            CardNumber = read800000?.Invoke(Field800000MaskedCardNumber),
            PaymentDate = read902614(Field902614PaymentDate),
            FeeRatePercentRaw = read800000?.Invoke(Field800000FeeRate),
            IsReprint = false,
        };
    }

    /// <summary>대체 출처가 있는 항목(#2/#3/#4) 전용 — 1순위 값이 <c>null</c>이거나 <b>공백뿐</b>이면 대체
    /// 출처를 쓴다(CP1 Opus 결정: 짝 검사를 통과했어도 501008 해당 필드가 비어 오면 빈칸보다 902614 값이
    /// 낫다). 대체 출처가 없는 항목(납기 4줄)은 이 헬퍼를 쓰지 않는다.</summary>
    private static string? PrimaryOrFallback(string? primary, string? fallback) =>
        string.IsNullOrWhiteSpace(primary) ? fallback : primary;
}
