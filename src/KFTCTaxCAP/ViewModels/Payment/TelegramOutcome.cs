namespace KFTCTaxCAP.ViewModels.Payment;

/// <summary>
/// Phase 40 P40-3(PRD §14.3) — 탭 1회 전송의 <b>결과 신호</b>(<see cref="PaymentTelegramTabViewModel.SendCompleted"/>).
/// 전문 간 연쇄(<see cref="PaymentScreenViewModel"/>)는 이 신호만 보고 적용/비움을 정한다 — <c>HasResponse</c>는
/// 영수증 자동 출력 같은 "응답 수신" 의미라서 연쇄 판정에 쓰지 않는다.
/// </summary>
public enum TelegramOutcome
{
    /// <summary>응답 파싱 성공 <b>그리고</b> 응답 <c>#7</c>(트림) = <c>"000"</c>.</summary>
    Success,

    /// <summary>응답 <c>#7</c> ≠ <c>"000"</c>, 응답 파싱 실패, 전송 예외(연결 실패·타임아웃 등) 중 하나.</summary>
    Failed,
}
