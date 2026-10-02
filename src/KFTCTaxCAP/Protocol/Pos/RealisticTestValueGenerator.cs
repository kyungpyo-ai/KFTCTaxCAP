using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace KFTCTaxCAP.Protocol.Pos;

/// <summary>
/// Phase 37 P37-1(docs/receipt_print/development_plan.md "Phase 37 — 결제창·VAN 스텁 테스트값 현실화") — 결제창
/// (kiosk 역할 대행)의 요청값과 VAN 스텁의 응답값을 <b>실제와 비슷한 테스트값</b>으로 채우는 생성기. 이름류는
/// "테스트" 표시(<c>김테스트</c>, <c>테스트세무서 징수관</c>), 금액·날짜·번호는 현실적 범위에서 거래마다 변동하고,
/// 금액은 서로 맞는다(본세+농특세+교육세=납부세액, 수수료=납부세액×요율 내림, 합계 일치).
///
/// <b>기존 <see cref="PosRandomValueGenerator"/>와의 관계</b>: 그 생성기는 표현·길이만 보는 "의미 없는 임의값"
/// 생성기로 그대로 남는다(소켓 클라이언트·연쇄 변환 회귀 하네스가 사용 — 확정 사항 6). 이 클래스는 반대로
/// 필드 번호별 업무 규칙을 안다. 쓰는 곳은 결제창(<c>PaymentTelegramTabViewModel</c>)과
/// <c>StubVanRelayService</c> 둘뿐이며, 실 VAN이 붙으면 스텁 쪽 사용은 스텁과 함께 사라진다(개발용 장치).
///
/// <b>규칙 범위(확정 사항 7)</b>: 요청 규칙은 SET 장소에 kiosk가 있는 필드 전부, 응답 규칙은 kiosk도 원캡도 아니고
/// 소유자가 있는(디지털예산/인터넷지로/VAN) 필드 전부를 <b>빠짐없이</b> 가진다 — 의미 있는 값이 없는 필드는
/// 공백 규칙(<c>""</c>)으로 명시한다(난수 문자열을 넣지 않는다). 원캡 담당 필드(902614 <c>#53</c>처럼 원캡이
/// 섞인 필드 포함)는 어느 쪽 규칙에도 없다 — 원캡이 카드리딩으로 채운 진짜 값을 건드리지 않는다.
/// <c>Services/Diagnostics/RealisticTestValueGeneratorSelfTest</c>가 스키마와 대조해 누락·초과를 잡는다.
///
/// 순수 함수다 — <see cref="Random"/>과 거래 시각(<see cref="DateTime"/>)을 주입받고 I/O·로그가 없다.
/// 길이는 CP949 <b>바이트</b> 기준이고 모든 값은 <see cref="PosTelegram.Write(int,string)"/> →
/// <see cref="PosField.Pad"/>를 거친다(초과면 예외 — 조용히 자르지 않는다).
/// </summary>
public static class RealisticTestValueGenerator
{
    public const string Notice501008 = "501008";
    public const string CardInfo800000 = "800000";
    public const string CardApproval902614 = "902614";

    // ── 이름류(테스트 표시) — CP949 바이트 길이를 주석에 적는다 ──
    /// <summary>납세 의무자 명 / 납부자 성명 — 8바이트. 902614 <c>#37</c>(AHNS10)로 연쇄 절삭돼도 잘리지 않는다.</summary>
    public const string TaxpayerName = "김테스트";

    /// <summary>징수 기관명 — 19바이트. 902614 <c>#20</c>(AHN20)로 연쇄 절삭돼도 잘리지 않는다.</summary>
    public const string CollectingAgencyName = "테스트세무서 징수관";

    /// <summary>징수 과목명 — 10바이트(902614 <c>#21</c> AHN20). 영수증 제목은 고정(Phase 34)이라 전문에만 쓰인다.</summary>
    public const string TaxItemName = "종합소득세";

    public const string AccountingName = "일반회계";
    public const string JurisdictionName = "국세청";
    public const string PayerAddress = "서울특별시 테스트구 테스트로 1";

    /// <summary>세목 코드 끝 3자리 = 결정구분 <c>5</c>(고지분) + 세목 <c>10</c>(종합소득세). <b>추정 테스트값</b>
    /// (확정 사항 4 — SPEC은 형식 "세목년월 YYMM + 결정구분 1 + 세목 2"만 정한다).</summary>
    private const string TaxItemCodeSuffix = "510";

    /// <summary>수수료율(N4, 소수점 2자리 — SPEC p.14 "0.5% → 0050"). 신용 0.8% / 체크 0.5%(확정 사항 3).</summary>
    public const int CreditFeeRateHundredths = 80;
    public const int CheckFeeRateHundredths = 50;

    /// <summary>신용카드 할부개월 LIST(800000 <c>#22</c>, 2바이트 단위 연속 — SPEC p.14).</summary>
    public const string CreditInstallmentList = "020304050607080910111224";

    /// <summary>SPEC 20260930 p.14 카드사 코드표 12개(코드·이름 쌍 그대로).</summary>
    public static readonly IReadOnlyList<KeyValuePair<string, string>> CardCompanies = new[]
    {
        new KeyValuePair<string, string>("34", "광주은행카드"),
        new KeyValuePair<string, string>("37", "전북은행카드"),
        new KeyValuePair<string, string>("53", "씨티카드"),
        new KeyValuePair<string, string>("61", "비씨카드"),
        new KeyValuePair<string, string>("62", "KB국민카드"),
        new KeyValuePair<string, string>("63", "하나카드(구 외환 포함)"),
        new KeyValuePair<string, string>("65", "삼성카드"),
        new KeyValuePair<string, string>("66", "신한카드"),
        new KeyValuePair<string, string>("67", "현대카드"),
        new KeyValuePair<string, string>("71", "NH카드"),
        new KeyValuePair<string, string>("75", "우리카드"),
        new KeyValuePair<string, string>("97", "롯데카드"),
    };

    /// <summary>902614 <c>#33</c> 카드사 코드 초기값(연쇄 전) — 신한카드.</summary>
    private const string DefaultCardCompanyCode = "66";

    /// <summary>요청 금액 초기값·본세 범위 R(10,000~1,000,000), 10원 단위.</summary>
    private const int AmountMinTens = 1_000;
    private const int AmountMaxTensInclusive = 100_000;

    public static bool IsSupported(string transactionTypeCode) =>
        transactionTypeCode == Notice501008 || transactionTypeCode == CardInfo800000 || transactionTypeCode == CardApproval902614;

    /// <summary>
    /// 값 표의 "요청" 열로 채운 요청 전문을 만든다. kiosk 담당이 아닌 필드는 <see cref="PosTelegram.CreateEmpty"/>의
    /// 공백 그대로다. 902614 <c>#42</c>(키오스크 고유번호)는 공백 규칙 — 가맹점 설정값이므로 호출자(결제창)가
    /// 덮는다(P30-7과 같은 방식).
    /// </summary>
    public static PosTelegram GenerateRequest(PosTelegramSchema schema, Random random, DateTime now)
    {
        if (schema is null)
            throw new ArgumentNullException(nameof(schema));

        PosTelegram telegram = PosTelegram.CreateEmpty(schema);
        foreach (KeyValuePair<int, string> entry in BuildRequestValues(schema.TransactionTypeCode, random, now))
            telegram.Write(entry.Key, entry.Value);

        return telegram;
    }

    /// <summary>
    /// 값 표의 "응답" 열로 <paramref name="response"/>(보통 요청 clone)를 채운다 — kiosk·원캡 담당 필드는 건드리지
    /// 않는다. 요청 값을 참조하는 계산(800000 <c>#24</c>/<c>#25</c>, 902614 <c>#38</c>)은 <paramref name="request"/>에서
    /// 읽는다. 공통부 <c>#3/#6/#7/#8</c> 성공값 덮어쓰기는 호출자(스텁)가 <b>이 메서드 뒤에</b> 한다. 지원하지 않는
    /// 전문이면 아무것도 하지 않는다(스텁이 다른 전문을 받아도 죽지 않게).
    /// </summary>
    public static void FillStubResponse(PosTelegram response, PosTelegram request, Random random, DateTime now)
    {
        if (response is null)
            throw new ArgumentNullException(nameof(response));
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        if (!IsSupported(response.Schema.TransactionTypeCode))
            return;

        foreach (KeyValuePair<int, string> entry in BuildResponseValues(response.Schema.TransactionTypeCode, request, random, now))
            response.Write(entry.Key, entry.Value);
    }

    // ------------------------------------------------------------------
    // 요청 규칙(kiosk 담당 필드 전부)
    // ------------------------------------------------------------------

    /// <summary>요청 규칙 — 필드 번호 → 원시 값(패딩 전). self 검사가 키 집합을 스키마의 kiosk 필드와 대조한다.</summary>
    internal static IReadOnlyDictionary<int, string> BuildRequestValues(string transactionTypeCode, Random random, DateTime now)
    {
        if (random is null)
            throw new ArgumentNullException(nameof(random));

        var values = new SortedDictionary<int, string>();
        DateTime today = now.Date;

        // ── 공통부(SPEC p.5~7) ──
        values[1] = "IGN";
        values[2] = "095";
        values[3] = "0200";
        values[4] = transactionTypeCode;
        values[6] = "G";
        values[8] = now.ToString("yyMMddHHmmss", CultureInfo.InvariantCulture);
        values[9] = "0EC0" + Digits(random, 8);

        switch (transactionTypeCode)
        {
            case Notice501008:
                // #5 상태 코드는 501008에서 kiosk 담당이 아니다(디지털예산/인터넷지로) — 요청 규칙 없음.
                values[11] = "01";       // 국세청 분류코드(SPEC p.7, Q9)
                values[12] = "2600000";  // 국세청 지로번호
                values[14] = ElectronicPaymentNumber(random, now);
                values[16] = string.Empty; // 실 납부자번호 — 연대납부 아님
                break;

            case CardInfo800000:
                // #5(인터넷지로/VAN), #11/#12(2026-09-22 개정으로 SET 장소 없음)는 kiosk 담당이 아니다.
                values[15] = RealisticAmount(random).ToString(CultureInfo.InvariantCulture); // 연쇄 전 초기값
                values[16] = "10";
                break;

            case CardApproval902614:
            {
                values[5] = "000"; // 902614만 #5가 kiosk 담당
                values[11] = "01";      // 초기값 — 501008 응답이 오면 연쇄 맵(#11/#12, 2026-10-01 추가)이 그 값으로 덮는다
                values[12] = "2600000";

                string taxpayerNumber = TaxpayerNumber(random);
                long baseTax = RealisticAmount(random);
                long fee = FloorFee(baseTax, CreditFeeRateHundredths);

                values[14] = taxpayerNumber;                     // 연쇄(501008 #17)
                values[15] = ElectronicPaymentNumber(random, now); // 연쇄(501008 #14)
                values[16] = "001";
                values[18] = TaxItemCode(now);                   // 연쇄(501008 #20)
                values[19] = Digits(random, 6);                  // 연쇄(501008 #22)
                values[20] = CollectingAgencyName;               // 연쇄(501008 #19)
                values[21] = TaxItemName;                        // 연쇄(501008 #21)
                values[22] = "0";
                values[23] = now.ToString("yyyy", CultureInfo.InvariantCulture);
                values[24] = baseTax.ToString(CultureInfo.InvariantCulture); // 연쇄(501008 #30)
                values[25] = "0";                                // 연쇄(501008 #32 교육세)
                values[26] = "0";                                // 연쇄(501008 #31 농특세)
                values[27] = baseTax.ToString(CultureInfo.InvariantCulture);          // = #24+#25+#26
                values[28] = fee.ToString(CultureInfo.InvariantCulture);              // = #27 × 0.8% 내림
                values[29] = (baseTax + fee).ToString(CultureInfo.InvariantCulture);  // = #27+#28
                values[30] = "1";
                values[31] = YearMonthDay(today.AddDays(14));    // 연쇄(501008 #26)
                values[32] = YearMonthDay(today);
                values[33] = DefaultCardCompanyCode;             // 연쇄(800000 #17)
                values[34] = "00";
                values[35] = "010" + Digits(random, 8);
                values[36] = taxpayerNumber;                     // #14와 같게
                values[37] = TaxpayerName;
                values[39] = "O";
                values[41] = "Q";
                values[42] = string.Empty; // 키오스크 고유번호 — 가맹점 설정값, 결제창이 덮는다
                values[49] = "0";
                break;
            }

            default:
                throw new ArgumentException($"지원하지 않는 거래 구분 코드: {transactionTypeCode}", nameof(transactionTypeCode));
        }

        return values;
    }

    // ------------------------------------------------------------------
    // 응답 규칙(kiosk도 원캡도 아닌, 소유자가 있는 필드 전부)
    // ------------------------------------------------------------------

    /// <summary>응답 규칙 — 필드 번호 → 원시 값. self 검사가 키 집합을 스키마의 "kiosk·원캡 제외, 소유자 있음" 필드와 대조한다.</summary>
    internal static IReadOnlyDictionary<int, string> BuildResponseValues(
        string transactionTypeCode, PosTelegram request, Random random, DateTime now)
    {
        if (random is null)
            throw new ArgumentNullException(nameof(random));

        var values = new SortedDictionary<int, string>();
        DateTime today = now.Date;

        switch (transactionTypeCode)
        {
            case Notice501008:
            {
                // 공통부 응답 전용 필드(#5 디지털예산/인터넷지로, #7, #10 디지털예산, #13 디지털예산/인터넷지로).
                values[5] = "000";          // 상태 코드 — 정상(요청 규칙의 "000"과 같은 의미, 표에 응답 행이 없어 판단)
                values[7] = "000";          // 스텁이 다시 덮는다(순서 유지)
                values[10] = string.Empty;  // 이용기관/센터 전문 관리 번호 — 표에 없음
                values[13] = string.Empty;  // FILLER

                long baseTax = RealisticAmount(random);
                const long agriculturalTax = 0; // 종합소득세 — 부가세목 없음으로 단순화
                const long educationTax = 0;
                long amountWithinPeriod = baseTax + agriculturalTax + educationTax;
                string dueDate = YearMonthDay(today.AddDays(random.Next(7, 31)));       // today + R(7~30)
                string noticeDate = YearMonthDay(today.AddDays(-random.Next(5, 21)));   // today − R(5~20)

                values[15] = "001";
                values[17] = TaxpayerNumber(random);
                values[18] = TaxpayerName;
                values[19] = CollectingAgencyName;
                values[20] = TaxItemCode(now);
                values[21] = TaxItemName;
                values[22] = Digits(random, 6);
                values[23] = "0";
                values[24] = now.ToString("yyyy", CultureInfo.InvariantCulture);
                values[25] = amountWithinPeriod.ToString(CultureInfo.InvariantCulture);
                values[26] = dueDate;
                values[27] = amountWithinPeriod.ToString(CultureInfo.InvariantCulture); // 납기후 = 납기내(샘플 영수증)
                values[28] = dueDate;
                values[29] = "2";
                values[30] = baseTax.ToString(CultureInfo.InvariantCulture);
                values[31] = agriculturalTax.ToString(CultureInfo.InvariantCulture);
                values[32] = educationTax.ToString(CultureInfo.InvariantCulture);
                for (int n = 33; n <= 39; n++)
                    values[n] = "0";
                values[40] = AccountingName;
                values[41] = JurisdictionName;
                values[42] = PayerAddress;
                values[43] = string.Empty;
                values[44] = "1";
                values[45] = "N";
                values[46] = "N";
                values[47] = "0";
                values[48] = "0";
                values[49] = noticeDate;
                values[50] = "1";
                values[51] = "0";
                values[52] = amountWithinPeriod.ToString(CultureInfo.InvariantCulture);
                values[53] = string.Empty;
                values[54] = "Y";
                values[55] = "10";
                values[56] = string.Empty;
                break;
            }

            case CardInfo800000:
            {
                values[5] = "000";  // 상태 코드(인터넷지로/VAN) — 정상
                values[7] = "000";

                KeyValuePair<string, string> company = CardCompanies[random.Next(CardCompanies.Count)];
                bool isCheckCard = random.Next(100) < 20; // N 80% / Y 20%
                int rate = isCheckCard ? CheckFeeRateHundredths : CreditFeeRateHundredths;

                values[17] = company.Key;
                values[18] = company.Value;
                values[19] = isCheckCard ? "Y" : "N";
                values[20] = "Y";
                values[21] = "N";
                values[22] = isCheckCard ? string.Empty : CreditInstallmentList;
                values[23] = string.Empty;

                // #24/#25는 요청 #15(납부세액)에서 계산한다. 요청 #15가 공백이면 0원으로 본다. 숫자가 아니거나
                // 계산 결과가 N12에 들어가지 않으면(의미 없는 임의값 요청 — 예: PosRandomValueGenerator로 15자리를
                // 다 채운 회귀 하네스 요청) 의미 있는 값이 없으므로 둘 다 공백으로 둔다(확정 사항 7).
                if (TryComputeFee(request.Read(15), rate, out long fee, out long total))
                {
                    values[24] = fee.ToString(CultureInfo.InvariantCulture);
                    values[25] = total.ToString(CultureInfo.InvariantCulture);
                }
                else
                {
                    values[24] = string.Empty;
                    values[25] = string.Empty;
                }

                values[26] = rate.ToString("D4", CultureInfo.InvariantCulture);
                values[27] = string.Empty;
                values[28] = string.Empty;
                break;
            }

            case CardApproval902614:
                values[7] = "000";
                values[10] = string.Empty;   // 이용기관/센터 전문 관리 번호 — 표에 없음
                values[13] = string.Empty;   // FILLER
                values[38] = request.Read(36); // 카드소유주 주민번호 = 요청 납부자 주민번호
                values[40] = string.Empty;   // 기 납부 이용 시스템 — 기 납부 아님
                values[52] = string.Empty;   // 선불카드 잔액 — 선불카드 아님
                break;

            default:
                throw new ArgumentException($"지원하지 않는 거래 구분 코드: {transactionTypeCode}", nameof(transactionTypeCode));
        }

        return values;
    }

    // ------------------------------------------------------------------
    // 값 조각
    // ------------------------------------------------------------------

    /// <summary>800000 <c>#24</c>/<c>#25</c> 계산 상한(N12).</summary>
    private const long MaxN12 = 999_999_999_999L;

    private static bool TryComputeFee(string requestAmount, int rateHundredths, out long fee, out long total)
    {
        fee = 0;
        total = 0;

        long baseAmount;
        if (requestAmount.Length == 0)
        {
            baseAmount = 0;
        }
        else if (requestAmount.Length > 15
            || !long.TryParse(requestAmount, NumberStyles.None, CultureInfo.InvariantCulture, out baseAmount))
        {
            return false;
        }

        if (baseAmount > MaxN12)
            return false;

        fee = FloorFee(baseAmount, rateHundredths);
        total = baseAmount + fee;
        return total <= MaxN12;
    }

    /// <summary>수수료 = floor(금액 × 요율(1/100 %) / 10000) — 원 단위 내림.</summary>
    internal static long FloorFee(long amount, int rateHundredths) => amount * rateHundredths / 10_000;

    /// <summary>R(10,000~1,000,000), 10원 단위.</summary>
    private static long RealisticAmount(Random random) => random.Next(AmountMinTens, AmountMaxTensInclusive + 1) * 10L;

    /// <summary>전자납부번호 — <c>0126</c> + <c>yyMMdd</c> + 숫자 9자리 = 19자리.</summary>
    private static string ElectronicPaymentNumber(Random random, DateTime now) =>
        "0126" + now.ToString("yyMMdd", CultureInfo.InvariantCulture) + Digits(random, 9);

    /// <summary>테스트 주민번호 — <c>900101</c> + <c>1</c> + 숫자 6자리 = 13자리.</summary>
    private static string TaxpayerNumber(Random random) => "9001011" + Digits(random, 6);

    /// <summary>세목 코드 — 이번 달 <c>yyMM</c> + <c>5</c> + <c>10</c> = 7자리(추정 테스트값, 확정 사항 4).</summary>
    private static string TaxItemCode(DateTime now) =>
        now.ToString("yyMM", CultureInfo.InvariantCulture) + TaxItemCodeSuffix;

    private static string YearMonthDay(DateTime date) => date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    private static string Digits(Random random, int count)
    {
        var sb = new StringBuilder(count);
        for (int i = 0; i < count; i++)
            sb.Append((char)('0' + random.Next(10)));
        return sb.ToString();
    }
}
