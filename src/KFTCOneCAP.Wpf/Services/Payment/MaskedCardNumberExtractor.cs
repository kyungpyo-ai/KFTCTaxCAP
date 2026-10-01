using System;

namespace KFTCOneCAP.Wpf.Services.Payment;

/// <summary>
/// <c>800000 #14</c>(마스킹 카드번호, AN19 — SPEC 20260930 p.13~14)에 넣을 값을 리더기 카드번호에서 뽑는 순수
/// 함수(docs/receipt_print/development_plan.md Phase 36 확정 사항 2, 2026-09-30 사용자 확정).
///
/// <para><b>규칙</b>: 입력의 <b>맨 앞부터 숫자(<c>0-9</c>)·<c>*</c>가 이어지는 구간만</b> 취한다 — 구분자
/// (<c>D</c>/<c>=</c> 등 그 밖의 어떤 문자든)가 나오면 거기서 끊어 뒤(유효기간 등)는 절대 섞지 않는다.
/// 19자를 넘으면 앞 19자만, 결과는 <b>왼쪽 정렬 + 공백 채움</b>. 원캡은 추가 마스킹하지 않는다(리더기가
/// 이미 9~12번째·마지막 자리를 <c>*</c>로 마스킹, 확정 사항 3).</para>
///
/// <para><b>메모리(Phase 25)</b>: 결과를 새 배열로 만들지 않고 호출부가 준 <paramref name="destination"/>에
/// 바로 쓴다 — 호출부가 그 버퍼를 <c>finally</c>에서 <c>SecureClear.Clear</c>로 지우면 이 함수가 만든
/// 사본은 하나도 남지 않는다. 값은 로그·예외 메시지에 넣지 않는다.</para>
/// </summary>
internal static class MaskedCardNumberExtractor
{
    /// <summary><c>800000 #14</c> 필드 길이(AN19).</summary>
    internal const int FieldLength = 19;

    /// <summary>추출 결과가 이 길이 미만이면 호출부는 기존 "카드번호 8자리 미만" 방어 경로로 간다
    /// (앞 8자리 BIN이 카드사·체크 구분에 쓰이므로 SPEC p.14 "앞 8자리 BIN은 마스킹 제외").</summary>
    internal const int MinimumLength = 8;

    /// <summary>
    /// <paramref name="cardNumber"/>의 선두 숫자·<c>*</c> 연속 구간(최대 19자)을 <paramref name="destination"/>
    /// (길이 정확히 <see cref="FieldLength"/>)에 왼쪽 정렬로 복사하고 나머지를 공백으로 채운다.
    /// </summary>
    /// <returns>복사한 유효 문자 수(0~19). <paramref name="cardNumber"/>가 null/빈 값이면 0(버퍼는 전부 공백).</returns>
    /// <exception cref="ArgumentException"><paramref name="destination"/> 길이가 19가 아님.</exception>
    internal static int ExtractInto(char[]? cardNumber, char[] destination)
    {
        if (destination == null)
            throw new ArgumentNullException(nameof(destination));
        if (destination.Length != FieldLength)
            throw new ArgumentException($"destination 길이는 {FieldLength}이어야 함: 길이={destination.Length}", nameof(destination));

        int count = 0;
        if (cardNumber != null)
        {
            while (count < cardNumber.Length && count < FieldLength && IsCardNumberChar(cardNumber[count]))
            {
                destination[count] = cardNumber[count];
                count++;
            }
        }

        for (int i = count; i < FieldLength; i++)
            destination[i] = ' ';

        return count;
    }

    private static bool IsCardNumberChar(char c) => (c >= '0' && c <= '9') || c == '*';
}
