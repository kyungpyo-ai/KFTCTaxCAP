namespace KFTCOneCAP.Wpf.ViewModels.Alerts;

/// <summary>
/// 알림창을 닫은 결과(docs/alert_dialog/PRD.md §3.1). 버튼 클릭뿐 아니라 Enter/ESC/Alt+F4로 닫혔을
/// 때의 결과도 <see cref="AlertBehavior"/>가 이 타입으로 매핑한다.
/// </summary>
public enum AlertResult
{
    OK,
    Cancel,
    Yes,
    No,
}
