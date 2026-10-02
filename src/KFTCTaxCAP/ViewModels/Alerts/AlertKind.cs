namespace KFTCTaxCAP.ViewModels.Alerts;

/// <summary>
/// 알림창 종류 5종. 원본 MFC <c>CModernMessageBox</c>의 enum을 그대로 옮긴 것이다
/// (docs/alert_dialog/PRD.md §2.1·§3.1). 종류별 아이콘 색·알림음은 <see cref="AlertBehavior"/>가
/// 매핑한다.
/// </summary>
public enum AlertKind
{
    Info,
    Warning,
    Error,
    Success,
    Question,
}
