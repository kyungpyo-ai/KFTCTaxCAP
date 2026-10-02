using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using KFTCTaxCAP.ViewModels.Alerts;

namespace KFTCTaxCAP.Views.Dialogs;

/// <summary>
/// 알림창 종류별 아이콘(파스텔 원 + 벡터 심볼, docs/alert_dialog/PRD.md §2.1·§3.3 D2). 심볼 좌표는 모두
/// 40×40 상자 기준이며 중심은 (20,20)이다. 종류 → 색·모양 매핑은 이 컨트롤 안에만 둔다.
/// </summary>
public partial class AlertIcon : UserControl
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(AlertKind), typeof(AlertIcon),
        new FrameworkPropertyMetadata(AlertKind.Info, (d, _) => ((AlertIcon)d).ApplyKind()));

    // 선(Stroke 전용, 채우기 없음 — WPF는 열린 도형도 채우기를 하므로 ✓·?가 면으로 채워지지 않게
    // 선과 점을 다른 Path로 나눴다). 크기는 원본 글자 심볼과 비슷한 10~14px.
    private static readonly Geometry ErrorStroke = Frozen("M15.5,15.5 L24.5,24.5 M24.5,15.5 L15.5,24.5");
    private static readonly Geometry SuccessStroke = Frozen("M14.2,20.4 L18.3,24.3 L25.8,15.8");
    private static readonly Geometry WarningStroke = Frozen("M20,13 L20,22.2");
    private static readonly Geometry InfoStroke = Frozen("M20,18.6 L20,27");
    private static readonly Geometry QuestionStroke = Frozen(
        "M16.5,16.3 C16.5,14.3 18.1,12.9 20,12.9 C22,12.9 23.5,14.3 23.5,16.1 C23.5,17.6 22.6,18.4 21.4,19.2 " +
        "C20.4,19.8 20,20.5 20,21.6 L20,22.2");

    // 점(Fill 전용)
    private static readonly Geometry BottomDot = FrozenDot(20, 26.6);
    private static readonly Geometry TopDot = FrozenDot(20, 13.4);

    public AlertIcon()
    {
        InitializeComponent();
        Symbol.SetResourceReference(Shape.StrokeThicknessProperty, "AlertIconStrokeThickness");
        ApplyKind();
    }

    public AlertKind Kind
    {
        get => (AlertKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    private void ApplyKind()
    {
        string bgKey;
        string fgKey;
        Geometry stroke;
        Geometry? dot;
        switch (Kind)
        {
            case AlertKind.Warning:
                bgKey = "AlertWarningBgBrush"; fgKey = "AlertWarningFgBrush"; stroke = WarningStroke; dot = BottomDot;
                break;
            case AlertKind.Error:
                bgKey = "AlertErrorBgBrush"; fgKey = "AlertErrorFgBrush"; stroke = ErrorStroke; dot = null;
                break;
            case AlertKind.Success:
                bgKey = "AlertSuccessBgBrush"; fgKey = "AlertSuccessFgBrush"; stroke = SuccessStroke; dot = null;
                break;
            case AlertKind.Question:
                // Question은 Info 색을 재사용한다(PRD §3.2).
                bgKey = "AlertInfoBgBrush"; fgKey = "AlertInfoFgBrush"; stroke = QuestionStroke; dot = BottomDot;
                break;
            default:
                bgKey = "AlertInfoBgBrush"; fgKey = "AlertInfoFgBrush"; stroke = InfoStroke; dot = TopDot;
                break;
        }

        Circle.SetResourceReference(Shape.FillProperty, bgKey);
        Symbol.SetResourceReference(Shape.StrokeProperty, fgKey);
        Symbol.Data = stroke;
        Dot.SetResourceReference(Shape.FillProperty, fgKey);
        Dot.Data = dot;
    }

    private static Geometry Frozen(string data)
    {
        var geometry = Geometry.Parse(data);
        geometry.Freeze();
        return geometry;
    }

    // 점 지름 = 선 두께보다 약간 크게(2.8) — 같은 두께로 그리면 점이 선보다 작아 보인다.
    private static Geometry FrozenDot(double x, double y)
    {
        var geometry = new EllipseGeometry(new Point(x, y), 1.4, 1.4);
        geometry.Freeze();
        return geometry;
    }
}
