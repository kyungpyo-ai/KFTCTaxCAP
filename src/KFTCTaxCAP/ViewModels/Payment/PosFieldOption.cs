namespace KFTCTaxCAP.ViewModels.Payment;

/// <summary>
/// Phase 40 P40-4(PRD §14.4) — 목록에서 고르는 요청 필드(현재 902614 <c>#34</c> 할부 개월 수)의 항목 1개.
/// <see cref="Code"/>는 전문에 실제로 들어가는 값(2자리 코드, 예 <c>03</c>)이고 <see cref="Display"/>는 화면 표시
/// 문구(<c>03 (3개월)</c>)다. 화면은 선택 시 <see cref="Code"/>만 칸에 넣는다 — 표시 문구가 전송 원문에 섞이지 않는다.
/// </summary>
public sealed class PosFieldOption
{
    public PosFieldOption(string code, string display)
    {
        Code = code;
        Display = display;
    }

    public string Code { get; }

    public string Display { get; }
}
