using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace KFTCTaxCAP.Views.Controls;

/// <summary>
/// 결제 알림창(<see cref="PaymentNoticeWindow"/>) VanProcessing 상태에서 원판(circle.png) 위,
/// 리더기 몸통(reader_kftc.png) 아래에 배치되는 원판 테두리 효과. 2026-08-24 자산 분리(원판/몸통 별도
/// 이미지)에 따라 <see cref="PaymentProcessingIndicator"/>에서 떼어낸 별도 컨트롤이다 — z-order가 원판/몸통
/// 사이로 고정되어야 해서 몸통 "표면" 효과와 다른 레이어에 둔다(PaymentNoticeWindow.xaml 참고).
///
/// 2026-10-12 재구성: 테두리를 도는 진행광 → 유리 반사광 스윕을 거쳐, 사용자 확인으로 "은은한 맥박"으로
/// 정했다. 테두리가 위쪽 "카드 → 은행" 흐름(<see cref="PaymentSignalWaveIndicator"/>, 1.0초 주기, 0.6초에
/// 은행 맥박)과 같은 박자로 부드럽게 밝아졌다 어두워진다 — 은행이 커지는 순간이 가장 밝다. 두 컨트롤은
/// 창이 같은 시점에 Play하므로 박자가 맞는다.
/// </summary>
public partial class PaymentProcessingRing : UserControl
{
    private const double BeatSeconds = 1.0;     // 흐름 인디케이터 한 바퀴
    private const double RiseStartSeconds = 0.45; // 은행 맥박(0.6초) 조금 전부터 밝아지기 시작
    private const double PeakSeconds = 0.7;     // 은행이 가장 커지는 무렵(0.6 + 0.3/2 근처)
    private const double GlowPeakOpacity = 0.75;
    private const double CorePeakOpacity = 0.6;

    // 원판(circle.png) 실측 타원 파라미터 — XAML의 테두리 경로가 이 값을 쓴다.
    // 2026-08-24 5차 수정(사용자 재실측 피드백): 이전(4차) 실측은 좌/우/전면 극점 3개만 잡아 반경을
    // 추정하는 성긴 방식이라 정밀도가 부족했다. 이번엔 alpha>128 픽셀을 열(x)마다/행(y)마다 촘촘히
    // (1px 단위) 스캔해 전체 외곽 경계점(수백~수천 개)을 모으고, 최소자승법(algebraic ellipse fit)으로
    // 축정렬 타원(x-cx)^2/rx^2+(y-cy)^2/ry^2=1을 직접 피팅했다.
    // - 하단 경계(각 열의 최하단 y, col_bot)는 x=105~1431 전 구간에서 노이즈 없이 매끈해 전량 사용.
    // - 상단 경계(각 열의 최상단 y, col_top)는 x≈319~1168 구간에서 불연속 점프가 발생하는데, 이는
    //   원판 위 사각 플랫폼이 아니라 circle.png 자산 자체가 "뒤쪽(위) 안쪽 넓은 V자 영역"을 처음부터
    //   완전 투명(alpha=0)으로 비워둔 것이었다(어차피 리더기 몸통에 항상 가려지는 부분이라 자산
    //   제작 시 생략됨) — 이 구간은 피팅에서 제외하고, 좌/우 측면 플랭크(x=105~318, x=1169~1431)만
    //   사용했다. 총 1804개 경계점, 피팅 잔차(정규화 값이 1.0에서 벗어난 정도) 표준편차 0.0049로
    //   매우 타이트하게 수렴: 중심(768.83,610.35), 반경(666.17,346.96) — 1536x1024 기준.
    // - 340x226.67 캔버스로 스케일(340/1536≈0.221354) 변환: 중심(170.18,135.10), 반경(147.46,76.80).
    // - 검증: Python(Pillow)으로 위 타원을 circle.png 위에 빨간 선으로 그려 별도 PNG로 저장,
    //   원판의 흰/파란 테두리 라인과 픽셀 단위로 겹치는 것을 육안 확인했다(좌/우/하단 전 구간 및
    //   상단 좌우 플랭크 모두 일치).
    private const double EllipseCenterX = 170.18;
    private const double EllipseCenterY = 135.10;
    private const double EllipseRadiusX = 147.46;
    private const double EllipseRadiusY = 76.80;

    private bool _isPlaying;

    public PaymentProcessingRing()
    {
        InitializeComponent();
    }

    /// <summary>테두리 맥박을 흐름 인디케이터 박자에 맞춰 무한 재생한다.</summary>
    public void Play()
    {
        if (_isPlaying)
        {
            return;
        }
        _isPlaying = true;

        PulseGlow.BeginAnimation(OpacityProperty, Breath(GlowPeakOpacity));
        PulseCore.BeginAnimation(OpacityProperty, Breath(CorePeakOpacity));
    }

    /// <summary>
    /// 한 박자 동안 0 → (RiseStart~Peak, 서서히) 최고 → (Peak~박자 끝, 더 천천히) 0. 올라갈 때보다 내려갈 때를
    /// 길게 둬 "숨 쉬듯" 부드럽게 보이게 한다. 박자 끝에서 0으로 돌아오므로 반복 이음새가 없다.
    /// </summary>
    private static DoubleAnimationUsingKeyFrames Breath(double peak)
    {
        var anim = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromSeconds(BeatSeconds),
            RepeatBehavior = RepeatBehavior.Forever,
        };
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(RiseStartSeconds))));
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(peak, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(PeakSeconds)),
            new SineEase { EasingMode = EasingMode.EaseInOut }));
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(BeatSeconds)),
            new SineEase { EasingMode = EasingMode.EaseInOut }));
        return anim;
    }

    /// <summary>애니메이션을 정지하고 리소스를 해제한다.</summary>
    public void Stop()
    {
        _isPlaying = false;
        PulseGlow.BeginAnimation(OpacityProperty, null);
        PulseCore.BeginAnimation(OpacityProperty, null);
        PulseGlow.Opacity = 0;
        PulseCore.Opacity = 0;
    }
}
