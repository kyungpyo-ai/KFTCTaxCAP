namespace KFTCOneCAP.Wpf.ViewModels.Alerts;

/// <summary>
/// ViewModel → View로 알림창 요청을 전달하는 불변 페이로드(docs/alert_dialog/PRD.md §3.1·§4.3).
/// <c>AlertRequested</c> 이벤트(<see cref="System.EventHandler{AlertMessage}"/>)가 이 타입 하나로
/// 통일된다 — ShopSetup/ReaderSetup/PaymentScreen 세 ViewModel이 모두 같은 이름·타입을 쓴다(Phase 39).
/// WPF 타입을 참조하지 않는다 — <see cref="Text"/>는 "첫 줄 = 제목, 나머지 = 본문" 원문 문자열을
/// 그대로 들고 있고, 분리는 <see cref="AlertTextSplitter"/>가 View 쪽에서 담당한다.
/// </summary>
public sealed class AlertMessage
{
    public AlertMessage(AlertKind kind, string? text)
    {
        Kind = kind;
        Text = text ?? string.Empty;
    }

    public AlertKind Kind { get; }

    public string Text { get; }
}
