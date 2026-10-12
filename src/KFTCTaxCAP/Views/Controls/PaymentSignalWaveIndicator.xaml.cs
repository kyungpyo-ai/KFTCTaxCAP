using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace KFTCTaxCAP.Views.Controls;

/// <summary>
/// 결제 알림창(<see cref="PaymentNoticeWindow"/>) VanProcessing 상태에서 리더기 위쪽에 표시되는 "카드 → 승인"
/// 흐름 인디케이터(Phase 42, 2026-10-12 — 이전 "신호 웨이브"·"왕복 빛 패킷"·"물결 점"을 대체). 카드 아이콘과
/// 은행 아이콘 사이의 점들이 꼬리 달린 혜성처럼 왼쪽에서 오른쪽으로 끝없이 흐르고, 흐름이 닿을 때마다 은행
/// 아이콘이 살짝 커졌다 돌아온다. 클래스·파일 이름은 창과의 연결을 유지하려고 그대로 둔다(<c>Play</c>/<c>Stop</c> 동일).
/// </summary>
public partial class PaymentSignalWaveIndicator : UserControl
{
    private const int DotCount = 6;
    private const double DotSize = 12;
    private const double DotRestOpacity = 0.2;

    // 점 중심: 카드 오른쪽 끝(92)과 은행 왼쪽 끝(348) 사이에 고르게, 아이콘 세로 중앙(y=55).
    private const double FirstDotX = 130;
    private const double DotSpacing = 36;
    private const double DotY = 55;

    // 타임라인(초). 한 바퀴 안에서 점은 Stagger 간격으로 차례로 켜진다.
    // 2026-10-12 사용자 요청으로 1.4초 → 1.0초(전체 타이밍 약 0.7배). 속도는 렌더 부하와 무관하다(프레임 수 동일).
    private const double CycleSeconds = 1.0;
    private const double StaggerSeconds = 0.1;
    private const double DotRiseSeconds = 0.07;  // 빠르게 켜짐 → 머리
    private const double DotFallSeconds = 0.35;  // 천천히 꺼짐 → 꼬리
    private const double BankPulseSeconds = 0.3;
    private const double CardNudgeSeconds = 0.25;
    private const double CardNudgePixels = 3;
    private const double FadeInSeconds = 0.3;

    private static readonly Brush DotBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)));

    private readonly List<Ellipse> _dots = new();
    private bool _isPlaying;

    public PaymentSignalWaveIndicator()
    {
        InitializeComponent();
        BuildDots();
    }

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }

    private void BuildDots()
    {
        for (int i = 0; i < DotCount; i++)
        {
            var dot = new Ellipse
            {
                Width = DotSize,
                Height = DotSize,
                Fill = DotBrush,
                Opacity = DotRestOpacity,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(1, 1),
            };
            Canvas.SetLeft(dot, FirstDotX + i * DotSpacing - DotSize / 2);
            Canvas.SetTop(dot, DotY - DotSize / 2);
            Dots.Children.Add(dot);
            _dots.Add(dot);
        }
    }

    /// <summary>0.3초 페이드인과 함께 흐름을 무한 재생한다.</summary>
    public void Play()
    {
        if (_isPlaying)
        {
            return;
        }
        _isPlaying = true;

        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(FadeInSeconds)));

        CardNudge.BeginAnimation(TranslateTransform.XProperty,
            Pulse(0, CardNudgePixels, 0, CardNudgeSeconds / 3, CardNudgeSeconds));

        double peakAt = DotRiseSeconds;
        double endAt = DotRiseSeconds + DotFallSeconds;
        for (int i = 0; i < _dots.Count; i++)
        {
            double begin = i * StaggerSeconds;
            var dot = _dots[i];
            dot.RenderTransform.BeginAnimation(ScaleTransform.ScaleXProperty, Pulse(1.0, 1.35, begin, peakAt, endAt));
            dot.RenderTransform.BeginAnimation(ScaleTransform.ScaleYProperty, Pulse(1.0, 1.35, begin, peakAt, endAt));
            dot.BeginAnimation(UIElement.OpacityProperty, Pulse(DotRestOpacity, 1.0, begin, peakAt, endAt));
        }

        // 마지막 점 다음 차례(0.6초)에 은행 아이콘이 맥박친다 — 흐름이 "도착"하는 느낌.
        double arrive = _dots.Count * StaggerSeconds;
        BankScale.BeginAnimation(ScaleTransform.ScaleXProperty, Pulse(1.0, 1.12, arrive, BankPulseSeconds / 2, BankPulseSeconds));
        BankScale.BeginAnimation(ScaleTransform.ScaleYProperty, Pulse(1.0, 1.12, arrive, BankPulseSeconds / 2, BankPulseSeconds));
    }

    /// <summary>
    /// 한 바퀴(<see cref="CycleSeconds"/>) 동안 rest → (peakAt초) peak → (endAt초) rest → 바퀴 끝까지 대기를 반복한다.
    /// 시작 시각만 늦춰 같은 모양을 반복하므로 요소끼리 박자가 어긋나지 않는다.
    /// </summary>
    private static DoubleAnimationUsingKeyFrames Pulse(double rest, double peak, double beginSeconds, double peakAt, double endAt)
    {
        var anim = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromSeconds(CycleSeconds),
            RepeatBehavior = RepeatBehavior.Forever,
            BeginTime = TimeSpan.FromSeconds(beginSeconds),
        };
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(rest, KeyTime.FromPercent(0.0)));
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(peak, KeyTime.FromPercent(peakAt / CycleSeconds), new SineEase { EasingMode = EasingMode.EaseOut }));
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(rest, KeyTime.FromPercent(endAt / CycleSeconds), new SineEase { EasingMode = EasingMode.EaseInOut }));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(rest, KeyTime.FromPercent(1.0)));
        return anim;
    }

    /// <summary>애니메이션을 정지하고 대기 상태로 되돌린다(숨김은 창이 즉시 처리).</summary>
    public void Stop()
    {
        _isPlaying = false;
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;

        CardNudge.BeginAnimation(TranslateTransform.XProperty, null);
        CardNudge.X = 0;

        foreach (var dot in _dots)
        {
            var scale = (ScaleTransform)dot.RenderTransform;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            dot.BeginAnimation(UIElement.OpacityProperty, null);
            scale.ScaleX = 1;
            scale.ScaleY = 1;
            dot.Opacity = DotRestOpacity;
        }
        BankScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        BankScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        BankScale.ScaleX = 1;
        BankScale.ScaleY = 1;
    }
}
