using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace KFTCTaxCAP.KioskSim.Protocol
{
    // =====================================================================================
    // 전문 간 필드 연쇄 표 — "앞 전문의 응답값으로 뒤 전문의 요청 필드를 채우는" 규칙 전체
    //
    // 국세 납부는 501008(국고 상세 고지내역 조회) → 800000(카드 정보 조회) → 902614(국고 신용카드
    // 승인요청) 순서로 진행되고, 앞 전문의 응답 필드가 뒤 전문의 요청 필드로 들어갑니다. 키오스크가
    // 구현해야 하는 이 규칙을 이 파일 하나에 모았습니다 — 이 파일만 읽어도 어느 응답 필드가 어느
    // 요청 필드로, 어떤 변환을 거쳐 들어가는지 알 수 있습니다. 화면(Forms/MainForm.cs)은 이 표를
    // 순서대로 적용하기만 합니다(ApplyChainFromResponse).
    //
    // ※ SPEC 원문이 정본(source of truth)이며, 이 소스의 테이블은 어디까지나 참고용 사본입니다.
    //    실제 연동 개발 시에는 반드시 발주처가 제공하는 최신 SPEC 문서와 대조해 확인하시기 바랍니다.
    //    이 소스와 SPEC이 어긋나는 부분을 발견하시면 SPEC을 우선하십시오. (README.md와 같은 문구)
    //
    // ※ SPEC 원문에 명문 근거가 있는 항목은 [SPEC 명문]으로 표시했습니다(902614 #11/#12/#29).
    //    나머지 항목은 SPEC에 연쇄 규칙이 적혀 있지 않아, 업무 담당자가 확정한 값 출처를 옮긴 것입니다.
    //
    // 동작 규칙(두 테스트 프로그램 공통):
    //   1. 연쇄 대상 필드는 빈 칸으로 시작합니다. 앞 전문의 정상 응답을 받아야 채워집니다.
    //   2. 앞 전문 응답의 #7 응답 코드가 "000"일 때만 연쇄합니다.
    //   3. 앞 전문이 실패하면(#7 ≠ "000", 응답을 못 받음, 응답 길이가 맞지 않음) 그 전문에서 온
    //      값만 비웁니다. 다른 전문에서 온 값은 그대로 둡니다.
    //   4. 연쇄로 채운 값도 전송 전에 고칠 수 있습니다. 앞 전문을 다시 보내면 성공·실패 모두 덮어씁니다.
    //
    // 이 파일은 BCL(System.*) 타입만 씁니다 — 다른 KioskSim 소스에 의존하지 않으므로 그대로 떼어
    // 다른 프로젝트에 넣어도 컴파일됩니다.
    // =====================================================================================

    /// <summary>연쇄 값 변환 방식.</summary>
    public enum TelegramChainConversion
    {
        /// <summary>
        /// 그대로 옮긴다(뒤쪽 공백만 제거). 길이가 늘어나는 숫자 필드(예: N12 → N15)와 표현만 다른
        /// 필드(예: N3 → AN3, AN2 → N2)도 여기 속한다 — 앞 '0' 채움/뒤 공백 채움은 전문을 만들 때
        /// 필드 표현(N이면 우측 정렬 + 앞 '0', 그 외는 좌측 정렬 + 뒤 공백)대로 처리되므로
        /// 이 단계에서 따로 하지 않는다(Protocol/TelegramBuffer.cs의 Write 참고).
        /// </summary>
        Copy,

        /// <summary>
        /// 반글자 방지 절삭. 출처 필드가 대상 필드보다 길 때(예: AHN40 → AHN20) CP949 바이트 기준으로
        /// 대상 길이에 맞춰 자른다. 한글은 1글자가 2바이트라 한도 경계에서 글자가 반으로 쪼개질 수
        /// 있는데, 그 경우 그 글자를 통째로 버린다(남는 자리는 공백 채움).
        /// </summary>
        TruncateCp949,

        /// <summary>
        /// 앞 전문 <b>응답</b> 필드들을 더한다. 응답의 숫자 필드는 값이 없을 때 서버가 공백을
        /// 보낼 수 있으므로 <b>공백은 0으로 본다</b>.
        /// </summary>
        SumResponseFields,

        /// <summary>
        /// 같은 전문의 <b>요청</b> 필드들을 더한다(예: 902614 총 납부 금액 = 납부 세액 + 수수료).
        /// 더할 값 중 <b>하나라도 비어 있으면 결과도 빈 칸</b>이다 — 수수료를 아직 받지 못했는데
        /// 납부 세액만으로 총액을 만들어 보내면 안 되기 때문이다. 입력 필드가 바뀔 때마다(응답으로
        /// 채워지거나 비워질 때, 사용자가 고칠 때) 다시 계산한다.
        /// </summary>
        SumRequestFields,

        /// <summary>
        /// 목록에서 고른다. 출처 값(예: 800000 카드 할부개월 LIST, AN60)을 2바이트씩 잘라 목록을
        /// 만들고, 기본값으로 첫 항목을 넣는다. 사용자는 화면에서 목록의 다른 항목을 고르거나 값을
        /// 직접 입력할 수 있다(<see cref="TelegramChainMap.SplitInstallmentList"/> 참고).
        /// </summary>
        SelectFirstFromList,
    }

    /// <summary>연쇄 표의 한 줄 — "대상 전문의 대상 필드 = 출처 전문의 출처 필드들을 변환한 값".</summary>
    public sealed class TelegramChainEntry
    {
        /// <summary>값을 채울 요청 전문(예: "902614").</summary>
        public string TargetTx { get; }

        /// <summary>값을 채울 요청 필드 번호.</summary>
        public int TargetField { get; }

        /// <summary>
        /// 값의 출처 전문. 대상 전문과 다르면 출처는 그 전문의 <b>응답</b>이고, 같으면 같은 전문의
        /// <b>요청</b> 필드다(<see cref="IsWithinSameRequest"/>).
        /// </summary>
        public string SourceTx { get; }

        /// <summary>출처 필드 번호들(합산은 여러 개, 나머지는 1개). 순서가 의미를 가진다(표기 순서 그대로).</summary>
        public IReadOnlyList<int> SourceFields { get; }

        /// <summary>변환 방식.</summary>
        public TelegramChainConversion Conversion { get; }

        /// <summary>출처가 다른 전문의 응답이 아니라 같은 전문의 요청 필드인가(예: 902614 #29 = #27 + #28).</summary>
        public bool IsWithinSameRequest => SourceTx == TargetTx;

        public TelegramChainEntry(string targetTx, int targetField, string sourceTx,
            TelegramChainConversion conversion, params int[] sourceFields)
        {
            if (sourceFields == null || sourceFields.Length == 0)
                throw new ArgumentException("출처 필드가 하나 이상 있어야 한다.", nameof(sourceFields));

            TargetTx = targetTx;
            TargetField = targetField;
            SourceTx = sourceTx;
            Conversion = conversion;
            SourceFields = Array.AsReadOnly((int[])sourceFields.Clone());
        }

        public override string ToString()
            => $"{TargetTx} #{TargetField} ← {SourceTx} #{string.Join("+#", SourceFields)} ({Conversion})";
    }

    /// <summary>연쇄 표와 변환 함수.</summary>
    public static class TelegramChainMap
    {
        private const string Notice501008 = "501008";   // 국고 상세 고지내역 조회
        private const string CardInfo800000 = "800000"; // 카드 정보 조회
        private const string Approval902614 = "902614"; // 국고 신용카드 승인요청

        /// <summary>CP949(전문 인코딩). 절삭 바이트 계산에 쓴다.</summary>
        private static readonly Encoding Cp949 = Encoding.GetEncoding(949);

        /// <summary>
        /// 연쇄 표 전체(25항목). 같은 전문 안에서 계산하는 항목(<see cref="TelegramChainEntry.IsWithinSameRequest"/>)은
        /// 그 입력이 되는 항목보다 뒤에 둔다 — 위에서부터 차례로 적용하면 입력이 먼저 채워진다.
        /// </summary>
        public static readonly IReadOnlyList<TelegramChainEntry> Entries = Array.AsReadOnly(new[]
        {
            // ---------------------------------------------------------------------------------
            // 800000 카드 정보 조회 ← 501008 응답
            // ---------------------------------------------------------------------------------

            // #15 납부세액(N15) ← 501008 #30 본세 + #31 농어촌 특별세 + #32 교육세(각 N15). 공백은 0.
            new TelegramChainEntry(CardInfo800000, 15, Notice501008, TelegramChainConversion.SumResponseFields, 30, 31, 32),

            // #16 납세자 유형(AN2) ← 501008 #55 납세자 유형(AN2). 그대로.
            new TelegramChainEntry(CardInfo800000, 16, Notice501008, TelegramChainConversion.Copy, 55),

            // ---------------------------------------------------------------------------------
            // 902614 국고 신용카드 승인요청 ← 501008 응답
            // ---------------------------------------------------------------------------------

            // #11 이용기관 발행기관 분류코드(N2) ← 501008 #11 지로 이용기관 분류코드(N2). 그대로.
            // [SPEC 명문] "'501008' 응답부에 전달한 값 SET".
            new TelegramChainEntry(Approval902614, 11, Notice501008, TelegramChainConversion.Copy, 11),

            // #12 이용기관 지로 번호(N7) ← 501008 #12 지로 이용기관 지로번호(N7). 그대로.
            // [SPEC 명문] "'501008' 응답부에 전달한 값 SET".
            new TelegramChainEntry(Approval902614, 12, Notice501008, TelegramChainConversion.Copy, 12),

            // #14 주민(사업자,법인)등록번호(AN13) ← 501008 #17 납세 의무자 번호(AN13). 그대로.
            new TelegramChainEntry(Approval902614, 14, Notice501008, TelegramChainConversion.Copy, 17),

            // #15 전자납부번호(AN19) ← 501008 #14 전자납부번호(AN19). 그대로.
            new TelegramChainEntry(Approval902614, 15, Notice501008, TelegramChainConversion.Copy, 14),

            // #16 납부 순번(AN3) ← 501008 #15 납부 순번(N3). 자릿수가 같아 그대로(표현만 N → AN).
            new TelegramChainEntry(Approval902614, 16, Notice501008, TelegramChainConversion.Copy, 15),

            // (#17 예비 정보 FIELD는 연쇄 아님 — 공백)

            // #18 징수 과목 코드(N7) ← 501008 #20 징수 과목 코드(AN7). 자릿수가 같아 그대로(표현만 AN → N).
            new TelegramChainEntry(Approval902614, 18, Notice501008, TelegramChainConversion.Copy, 20),

            // #19 징수관 계좌번호(AN6) ← 501008 #22 징수관 계좌번호(AN6). 그대로.
            new TelegramChainEntry(Approval902614, 19, Notice501008, TelegramChainConversion.Copy, 22),

            // #20 징수 기관명(AHN20) ← 501008 #19 징수 기관명(AHN40). 대상이 짧아 반글자 방지 절삭.
            new TelegramChainEntry(Approval902614, 20, Notice501008, TelegramChainConversion.TruncateCp949, 19),

            // #21 징수 과목명(AHN20) ← 501008 #21 징수 과목명(AHN40). 대상이 짧아 반글자 방지 절삭.
            new TelegramChainEntry(Approval902614, 21, Notice501008, TelegramChainConversion.TruncateCp949, 21),

            // #22 소계정(N1) ← 501008 #23 소계정(N1). 그대로.
            new TelegramChainEntry(Approval902614, 22, Notice501008, TelegramChainConversion.Copy, 23),

            // #23 징수 결의 회계 년도(N4) ← 501008 #24 징수 결의 회계 년도(N4). 그대로.
            new TelegramChainEntry(Approval902614, 23, Notice501008, TelegramChainConversion.Copy, 24),

            // #24 납부세액(본세)(N15) ← 501008 #30 본세(N15). 그대로.
            new TelegramChainEntry(Approval902614, 24, Notice501008, TelegramChainConversion.Copy, 30),

            // #25 납부세액(교육세)(N15) ← 501008 #32 교육세(N15). 그대로. (번호 순서가 아니라 이름으로 맞춘다)
            new TelegramChainEntry(Approval902614, 25, Notice501008, TelegramChainConversion.Copy, 32),

            // #26 납부세액(농어촌특별세)(N15) ← 501008 #31 농어촌 특별세(N15). 그대로.
            new TelegramChainEntry(Approval902614, 26, Notice501008, TelegramChainConversion.Copy, 31),

            // #27 납부 세액(N15) ← 501008 #30 본세 + #31 농어촌 특별세 + #32 교육세. 공백은 0.
            new TelegramChainEntry(Approval902614, 27, Notice501008, TelegramChainConversion.SumResponseFields, 30, 31, 32),

            // #30 납기 내후 구분(AN1) ← 501008 #44 납기 내후 구분(AN1). 그대로.
            new TelegramChainEntry(Approval902614, 30, Notice501008, TelegramChainConversion.Copy, 44),

            // #31 납기 일자(N8) ← 501008 #26 납기일(납기내)(N8). 납기내 일자로 고정.
            new TelegramChainEntry(Approval902614, 31, Notice501008, TelegramChainConversion.Copy, 26),

            // #36 납부자 주민(사업자)등록번호(AN13) ← 501008 #17 납세 의무자 번호(AN13). 그대로(#14와 같은 출처).
            new TelegramChainEntry(Approval902614, 36, Notice501008, TelegramChainConversion.Copy, 17),

            // #37 납부자 성명(AHNS10) ← 501008 #18 납세 의무자 명(AHN40). 대상이 짧아 반글자 방지 절삭.
            new TelegramChainEntry(Approval902614, 37, Notice501008, TelegramChainConversion.TruncateCp949, 18),

            // ---------------------------------------------------------------------------------
            // 902614 국고 신용카드 승인요청 ← 800000 응답
            // ---------------------------------------------------------------------------------

            // #28 수수료(N15) ← 800000 #24 납부대행 수수료 금액(N12). 그대로(길이 확장은 앞 '0' 채움).
            new TelegramChainEntry(Approval902614, 28, CardInfo800000, TelegramChainConversion.Copy, 24),

            // #33 카드사 코드(N2) ← 800000 #17 카드사 코드(AN2). 자릿수가 같아 그대로(표현만 AN → N).
            new TelegramChainEntry(Approval902614, 33, CardInfo800000, TelegramChainConversion.Copy, 17),

            // #34 할부 개월 수(N2) ← 800000 #22 카드 할부개월 LIST(AN60) 중 하나. 기본값 = 첫 항목.
            // 포인트 납부는 목록에 없다 — 할부 개월 수 + 60을 직접 입력한다(예: 포인트 3개월 = "63").
            new TelegramChainEntry(Approval902614, 34, CardInfo800000, TelegramChainConversion.SelectFirstFromList, 22),

            // ---------------------------------------------------------------------------------
            // 902614 국고 신용카드 승인요청 ← 같은 902614 요청(위 항목들이 채운 값)
            // ---------------------------------------------------------------------------------

            // #29 총 납부 금액(N15) ← 902614 #27 납부 세액 + #28 수수료. 하나라도 비면 빈 칸.
            // [SPEC 명문] 총 납부 금액 = 납부 세액 + 수수료.
            new TelegramChainEntry(Approval902614, 29, Approval902614, TelegramChainConversion.SumRequestFields, 27, 28),

            // (연쇄 아님: 902614 #32 납부 일자·#35 연락 전화 번호·#42 키오스크 고유번호는 키오스크가 직접 입력,
            //  #39 'O'·#41 'Q'·#49 '0'은 고정값, #38 카드소유주 주민(사업자)등록번호는 인터넷지로 담당이라 공백,
            //  800000 #11/#12는 공백)
        });

        /// <summary>이 전문의 이 필드를 채우는 표 항목을 찾는다. 연쇄 필드가 아니면 null.</summary>
        public static TelegramChainEntry? FindEntry(string txType, int fieldNumber)
        {
            foreach (var entry in Entries)
            {
                if (entry.TargetTx == txType && entry.TargetField == fieldNumber)
                    return entry;
            }
            return null;
        }

        /// <summary>이 전문의 이 필드가 연쇄로 채워지는 필드인가.</summary>
        public static bool IsChainTarget(string txType, int fieldNumber)
            => FindEntry(txType, fieldNumber) != null;

        /// <summary>
        /// 표 한 줄을 계산한다.
        /// </summary>
        /// <param name="entry">연쇄 표의 한 줄.</param>
        /// <param name="sourceValues">출처 필드 값들(<see cref="TelegramChainEntry.SourceFields"/>와 같은 순서).</param>
        /// <param name="targetByteLength">대상 필드 길이(바이트). 반글자 방지 절삭에만 쓴다.</param>
        /// <returns>대상 필드에 넣을 값. 빈 문자열이면 "빈 칸"이다.</returns>
        public static string Convert(TelegramChainEntry entry, IReadOnlyList<string> sourceValues, int targetByteLength)
        {
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));
            if (sourceValues == null || sourceValues.Count != entry.SourceFields.Count)
                throw new ArgumentException($"{entry}: 출처 값 개수가 출처 필드 개수와 다르다.", nameof(sourceValues));

            switch (entry.Conversion)
            {
                case TelegramChainConversion.Copy:
                    return (sourceValues[0] ?? string.Empty).TrimEnd(' ');

                case TelegramChainConversion.TruncateCp949:
                    return TruncateCp949(sourceValues[0], targetByteLength);

                case TelegramChainConversion.SumResponseFields:
                    return Sum(sourceValues, blankIsZero: true);

                case TelegramChainConversion.SumRequestFields:
                    return Sum(sourceValues, blankIsZero: false);

                case TelegramChainConversion.SelectFirstFromList:
                    var list = SplitInstallmentList(sourceValues[0]);
                    return list.Count > 0 ? list[0] : string.Empty;

                default:
                    throw new NotSupportedException($"{entry}: 알 수 없는 변환 방식.");
            }
        }

        /// <summary>
        /// 반글자 방지 절삭. <paramref name="value"/>를 CP949 바이트 기준 <paramref name="maxBytes"/> 이하로
        /// 자르되, 한글처럼 2바이트인 글자가 경계에 걸리면 그 글자를 통째로 버린다. 뒤쪽 공백은 제거한다.
        /// 예: "서울특별시 강남구 세무서장"(AHN40) → AHN20이면 20바이트 안에 들어가는 글자까지만 남긴다.
        /// </summary>
        public static string TruncateCp949(string value, int maxBytes)
        {
            if (maxBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxBytes), maxBytes, "대상 길이는 1 이상이어야 한다.");

            string text = (value ?? string.Empty).TrimEnd(' ');
            var result = new StringBuilder();
            int usedBytes = 0;
            int i = 0;
            while (i < text.Length)
            {
                // 서로게이트 쌍(CP949 밖의 문자)은 두 char를 한 글자로 묶어 센다.
                int charCount = char.IsHighSurrogate(text[i]) && i + 1 < text.Length ? 2 : 1;
                string oneChar = text.Substring(i, charCount);
                int byteCount = Cp949.GetByteCount(oneChar);

                if (usedBytes + byteCount > maxBytes)
                    break; // 이 글자를 넣으면 한도를 넘는다(반 글자로 쪼개지 않고 통째로 버린다).

                result.Append(oneChar);
                usedBytes += byteCount;
                i += charCount;
            }
            return result.ToString().TrimEnd(' ');
        }

        /// <summary>
        /// 할부개월 LIST(800000 #22, AN60)를 2바이트씩 잘라 목록으로 만든다. SPEC: "할부개월수 2Byte 단위
        /// 코드를 연속하여 구성(space padding), 총 60Byte로 최대 30개". 구분자는 없다.
        /// <list type="bullet">
        /// <item>뒤쪽 공백(space padding)은 버린다.</item>
        /// <item>남은 길이가 홀수면 끝의 1글자는 항목이 될 수 없으므로 버린다.</item>
        /// <item>실제 값은 "00"(일시불)으로 시작한다(예: 신용카드 "000203…", 체크카드는 "00" 하나).
        ///       목록에 없는 "00"을 따로 끼워 넣지 않는다 — 받은 값만 보여준다.</item>
        /// </list>
        /// </summary>
        public static IReadOnlyList<string> SplitInstallmentList(string list)
        {
            string text = (list ?? string.Empty).TrimEnd(' ');
            var items = new List<string>();
            for (int i = 0; i + 2 <= text.Length; i += 2)
                items.Add(text.Substring(i, 2));
            return items.AsReadOnly();
        }

        /// <summary>
        /// 할부 개월 수 코드를 사람이 읽는 문구로 바꾼다(화면 목록 표시용).
        /// "00"/"01" → "일시불", "03" → "3개월". 포인트 납부(할부 개월 수 + 60) 값은
        /// "60"/"61" → "포인트 일시불", "63" → "포인트 3개월". 숫자가 아니면 빈 문자열.
        /// </summary>
        public static string DescribeInstallment(string code)
        {
            if (!int.TryParse((code ?? string.Empty).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int months))
                return string.Empty;

            if (months >= 60)
            {
                int pointMonths = months - 60;
                return pointMonths <= 1 ? "포인트 일시불" : $"포인트 {pointMonths}개월";
            }
            return months <= 1 ? "일시불" : $"{months}개월";
        }

        /// <summary>
        /// 숫자 문자열들을 더한다. 공백(Trim 후 빈 문자열)을 <paramref name="blankIsZero"/>가 true면 0으로,
        /// false면 "계산할 수 없음"으로 본다(결과 빈 칸). 숫자가 아닌 값이 섞여 있으면 결과는 빈 칸이다.
        /// 결과는 앞 '0' 없이 돌려준다(전문을 만들 때 N 필드 규칙대로 앞 '0'이 채워진다). 결과가 대상
        /// 필드 자릿수를 넘으면 잘라 내지 않는다 — 전문을 만들 때 길이 초과로 드러난다.
        /// </summary>
        private static string Sum(IReadOnlyList<string> values, bool blankIsZero)
        {
            long total = 0;
            foreach (string raw in values)
            {
                string text = (raw ?? string.Empty).Trim();
                if (text.Length == 0)
                {
                    if (blankIsZero)
                        continue;
                    return string.Empty;
                }
                if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out long number))
                    return string.Empty;
                total += number;
            }
            return total.ToString(CultureInfo.InvariantCulture);
        }
    }
}
