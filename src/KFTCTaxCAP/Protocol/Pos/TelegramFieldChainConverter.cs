using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace KFTCTaxCAP.Protocol.Pos;

/// <summary>
/// <see cref="TelegramFieldChainMap.ChainEntry.Conversion"/>이 가리키는 "무엇을 해야 하는지"를 실제로
/// "어떻게 하는지"로 구현한다(PRD.md §13.7, Phase 30 P30-3). 출처 값(들)을 받아 대상 필드에 쓸 원시
/// 문자열을 만드는 순수 함수 계층이다 — <see cref="PosTelegram"/>/스키마에 의존하지 않아 소켓·UI 없이
/// 단위 검증이 가능하다(<see cref="Diagnostics.TelegramFieldChainConverterSelfTest"/>).
///
/// 여기서 다루는 값은 이미 <see cref="PosTelegram.Read"/>로 읽혀 <see cref="PosField.Trim"/>을 거친
/// "패딩이 제거된" 원시 문자열이라고 가정한다 — 이 클래스는 그 값을 그대로/절삭/합산/고정값 중 하나로
/// 가공해서 돌려줄 뿐, 패딩·CP949 인코딩은 호출자(P30-4가 <see cref="PosTelegram.Write"/>로 넘길 때
/// <see cref="PosField.Pad"/>가 처리)의 몫이다.
/// </summary>
public static class TelegramFieldChainConverter
{
    /// <summary>
    /// <paramref name="conversion"/>에 따라 <paramref name="sourceValues"/>를 가공해 대상 필드에 쓸
    /// 원시 문자열을 반환한다.
    /// </summary>
    /// <param name="conversion">변환 종류.</param>
    /// <param name="sourceValues">출처 값(들). <see cref="FieldChainConversion.Sum"/>/<see cref="FieldChainConversion.SumAllRequired"/>이면 2개 이상,
    /// <see cref="FieldChainConversion.Fixed"/>면 무시된다(빈 목록이어도 된다), 그 외에는 정확히 1개를
    /// 기대한다.</param>
    /// <param name="targetLengthBytes">대상 필드의 CP949 바이트 길이 한도(<see cref="PosField.Length"/>).
    /// <see cref="FieldChainConversion.Truncate"/>에서만 쓰인다.</param>
    /// <param name="fixedValue"><see cref="FieldChainConversion.Fixed"/> 전용 고정 문자열.</param>
    public static string Convert(
        FieldChainConversion conversion,
        IReadOnlyList<string> sourceValues,
        int targetLengthBytes,
        string? fixedValue = null)
    {
        switch (conversion)
        {
            case FieldChainConversion.Direct:
            case FieldChainConversion.RepresentationChange:
            case FieldChainConversion.Widen:
                // PRD §13.7 — 표현 변환은 자릿수가 같아 값을 그대로 옮기면 되고, 길이 확장은
                // PosField.Pad()의 0-패딩이 처리하므로 여기서 손댈 필요가 없다.
                return sourceValues[0];

            case FieldChainConversion.Truncate:
                return TruncateAvoidingSplitHangul(sourceValues[0], targetLengthBytes);

            case FieldChainConversion.Sum:
                return SumAsInteger(sourceValues, allRequired: false);

            case FieldChainConversion.SumAllRequired:
                return SumAsInteger(sourceValues, allRequired: true);

            case FieldChainConversion.SelectFromList:
            {
                // 변환 결과 = 목록의 첫 항목(PRD §14.4 기본 선택). 목록이 비면 빈 문자열.
                IReadOnlyList<string> items = SplitInstallmentList(sourceValues[0]);
                return items.Count > 0 ? items[0] : string.Empty;
            }

            case FieldChainConversion.Fixed:
                // 현재 TelegramFieldChainMap에 이 변환을 쓰는 항목은 없다(SPEC이 고정값을 다시 요구할 때를 위해 남김).
                return fixedValue ?? string.Empty;

            default:
                throw new ArgumentOutOfRangeException(nameof(conversion), conversion, "알 수 없는 변환 종류");
        }
    }

    /// <summary>
    /// 할부개월 LIST(800000 <c>#22</c> AN60 — "2바이트 단위 코드를 연속하여 구성, space padding", SPEC p.13~14)를
    /// 2바이트씩 잘라 목록으로 만든다(PRD §14.4). 구분자는 없고 뒤쪽 space는 버린다. 전부 space/빈 값이면 빈 목록.
    /// 길이가 홀수인(2바이트로 나누어떨어지지 않는) 경우 끝의 반쪽 코드 1글자는 불완전한 코드라 버린다.
    /// 화면(콤보 항목 구성)도 이 함수를 써서 분할 규칙을 한 곳에만 둔다. 모든 코드는 ASCII 숫자라 문자 수 = 바이트 수다.
    /// </summary>
    public static IReadOnlyList<string> SplitInstallmentList(string list)
    {
        var items = new List<string>();
        if (string.IsNullOrEmpty(list))
            return items;

        string trimmed = list.TrimEnd(' ');
        for (int i = 0; i + 2 <= trimmed.Length; i += 2)
            items.Add(trimmed.Substring(i, 2));

        return items;
    }

    /// <summary>
    /// 반글자 방지 절삭(PRD §13.7). CP949에서 한글은 1자당 2바이트다. <paramref name="targetLengthBytes"/>를
    /// 넘는 값을 자르되, 마지막 글자가 반쪽으로 잘리면 그 글자를 통째로 버린다(남는 자리는 나중에
    /// <see cref="PosField.Pad"/>가 공백으로 채운다). 문자 단위로 순회하며 누적 바이트 수를 계산해서
    /// 넘기 직전에 멈춘다. 이미 한도 이내인 값은 그대로 반환한다(불필요한 재할당 없이).
    /// </summary>
    private static string TruncateAvoidingSplitHangul(string value, int targetLengthBytes)
    {
        if (value.Length == 0)
            return value;

        int currentBytes = PosMessageEncoding.Value.GetByteCount(value);
        if (currentBytes <= targetLengthBytes)
            return value;

        int accumulatedBytes = 0;
        int charCount = 0;

        foreach (char c in value)
        {
            int charBytes = PosMessageEncoding.Value.GetByteCount(new[] { c });
            if (accumulatedBytes + charBytes > targetLengthBytes)
                break;

            accumulatedBytes += charBytes;
            charCount++;
        }

        return value.Substring(0, charCount);
    }

    /// <summary>
    /// <paramref name="sourceValues"/>를 정수로 파싱해 합산한다. 빈 입력(Trim 후 빈 문자열, 공백 문자만 있는 값
    /// 포함)의 처리는 <paramref name="allRequired"/>로 갈린다.
    /// <list type="bullet">
    /// <item><c>false</c>(<see cref="FieldChainConversion.Sum"/>, 응답 필드 합산): <b>빈 입력 = 0</b>. 실 VAN이 해당
    /// 업무부(예: 501008 #31/#32)를 공백으로 돌려줄 수 있어서다(P30 체크포인트 지적 L-5, Phase 30 동작).
    /// 전부 비어도 결과는 <c>"0"</c>이다.</item>
    /// <item><c>true</c>(<see cref="FieldChainConversion.SumAllRequired"/>, 같은 전문 요청 필드 합산 — 902614 #29):
    /// <b>입력 하나라도 비면 결과도 빈 문자열</b>(PRD §14.3 — 실패로 비워진 #28로 #29가 #27만 계산되면 안 된다).</item>
    /// </list>
    /// 합산 결과 자체의 자리수 초과는 여기서 검사하지 않는다 — 그건 나중에
    /// <see cref="PosField.Pad"/>가 예외로 드러낸다(PRD §13.7 — 조용히 잘리지 않게 하는 게 의도적 설계).
    /// 반면 소스 값이 숫자로 파싱조차 안 되는 경우(사용자가 연쇄로 채워진 필드를 직접 문자로 편집한 뒤
    /// 재계산되는 경로, PRD §13.3)는 <see cref="FormatException"/>을 그대로 흘리지 않고 이 프로젝트의
    /// 형식 오류 관례(<see cref="PosProtocolException"/>)로 감싸 던진다(P30 체크포인트 지적 M-3).
    /// </summary>
    private static string SumAsInteger(IReadOnlyList<string> sourceValues, bool allRequired)
    {
        // allRequired면 입력 하나라도 비었을 때 합산하지 않는다 — 비숫자 검사보다 먼저 판정해, 빈 입력이 있으면
        // 다른 입력이 비숫자여도 예외 없이 빈 값이다.
        if (allRequired && sourceValues.Any(string.IsNullOrWhiteSpace))
            return string.Empty;

        long total = 0;
        foreach (string source in sourceValues)
        {
            if (string.IsNullOrWhiteSpace(source))
                continue; // 공백 = 0(allRequired가 아닌 경우만 여기까지 온다).

            // N 필드 값은 앞자리 0을 포함할 수 있으나 long.Parse는 앞자리 0을 그대로 숫자로 해석하므로
            // 문제없다. CultureInfo.InvariantCulture 명시는 이 저장소 관례(PosMessageFramer.cs 참고).
            try
            {
                total += long.Parse(source, NumberStyles.Integer, CultureInfo.InvariantCulture);
            }
            catch (FormatException ex)
            {
                throw new PosProtocolException($"연쇄 합산 소스 값이 숫자가 아님: '{source}'", ex);
            }
        }

        return total.ToString(CultureInfo.InvariantCulture);
    }
}
