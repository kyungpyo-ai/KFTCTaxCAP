using System;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace KFTCTaxCAP.Controls;

/// <summary>
/// 2026-10-12 추가(사용자 피드백: "창을 열면 토글이 OFF→ON으로 움직이는 게 보인다").
/// `Themes/ToggleSwitch.xaml`의 노브 위치는 IsChecked Setter로 즉시 정해지고, 슬라이드 애니메이션은
/// 이 헬퍼가 사용자 조작(Click — 마우스·스페이스 키)일 때만 재생한다. 창을 열 때 저장값이 바인딩되거나
/// 코드에서 값이 바뀌는 경우에는 애니메이션 없이 최종 위치로 바로 그려진다.
/// </summary>
public static class ToggleSwitchAnimationHelper
{
    // 노브 이동 거리 = ON 위치 Margin.Left(23) - OFF 위치 Margin.Left(3)
    private const double KnobTravel = 20;

    public static readonly DependencyProperty AnimateOnClickProperty =
        DependencyProperty.RegisterAttached(
            "AnimateOnClick",
            typeof(bool),
            typeof(ToggleSwitchAnimationHelper),
            new PropertyMetadata(false, OnAnimateOnClickChanged));

    public static bool GetAnimateOnClick(DependencyObject obj) => (bool)obj.GetValue(AnimateOnClickProperty);

    public static void SetAnimateOnClick(DependencyObject obj, bool value) => obj.SetValue(AnimateOnClickProperty, value);

    private static void OnAnimateOnClickChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ToggleButton toggle)
            return;

        toggle.Click -= OnToggleClick;
        if ((bool)e.NewValue)
            toggle.Click += OnToggleClick;
    }

    private static void OnToggleClick(object sender, RoutedEventArgs e)
    {
        // ToggleButton.OnClick은 IsChecked를 바꾼 뒤 Click을 올리므로, 여기서 IsChecked는 이미 새 값이다.
        var toggle = (ToggleButton)sender;
        if (toggle.Template?.FindName("KnobTransform", toggle) is not TranslateTransform slide)
            return;

        // 노브는 이미 새 위치(Margin)에 있으므로, 이전 위치만큼 떨어진 곳에서 0으로 끌어온다.
        double from = toggle.IsChecked == true ? -KnobTravel : KnobTravel;
        var animation = new DoubleAnimation(from, 0, TimeSpan.FromSeconds(0.15))
        {
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 },
        };
        slide.BeginAnimation(TranslateTransform.XProperty, animation);
    }
}
