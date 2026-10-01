namespace KFTCOneCAP.Wpf.ViewModels.Alerts;

/// <summary>
/// 알림창 버튼 구성 3종(docs/alert_dialog/PRD.md §3.1). 원본의 YesNoCancel은 실제로 쓰인 적이 없어
/// 이식하지 않는다.
/// </summary>
public enum AlertButtons
{
    OK,
    OKCancel,
    YesNo,
}
