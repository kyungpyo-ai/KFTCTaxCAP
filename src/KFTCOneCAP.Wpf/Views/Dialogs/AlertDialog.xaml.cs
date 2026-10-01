using System;
using System.ComponentModel;
using System.Media;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using KFTCOneCAP.Wpf.ViewModels.Alerts;

namespace KFTCOneCAP.Wpf.Views.Dialogs;

/// <summary>
/// 알림창 창(docs/alert_dialog/PRD.md §3.4, development_plan.md P38-3). 코드비하인드는 창 핸들·입력 장치에
/// 묶인 것만 한다 — 끌어서 이동, 기본 버튼 포커스, 키보드(Enter/ESC/Ctrl+C), 닫기(Alt+F4) 결과 위임,
/// 알림음 재생, 작업 표시줄 깜빡임. 어떤 결과로 닫을지는 <see cref="AlertDialogViewModel"/>(→
/// <see cref="AlertBehavior"/>)가 정한다. 띄우기는 정적 헬퍼 <see cref="Show"/>(AlertDialog.Show.cs)로만 한다.
/// </summary>
public partial class AlertDialog : Window
{
    private readonly AlertDialogViewModel _viewModel;

    internal AlertDialog(AlertDialogViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        _viewModel.CloseRequested += ViewModel_CloseRequested;
        Loaded += AlertDialog_Loaded;
        ContentRendered += AlertDialog_ContentRendered;
        PreviewKeyDown += AlertDialog_PreviewKeyDown;
        Closing += AlertDialog_Closing;
        Closed += AlertDialog_Closed;
    }

    internal AlertDialogViewModel ViewModel => _viewModel;

    /// <summary>
    /// 본문 어절 단위 줄바꿈(PRD §4.4 — CSS keep-all). 레이아웃이 한 번 잡힌 뒤(본문 TextBlock의 실제 폭을 알 때)
    /// 헬퍼가 위치 계산 전에 부른다. 폭 측정은 본문 TextBlock과 같은 글꼴·크기·글자 모드·DPI의 FormattedText로
    /// 하고, 줄바꿈 판단은 순수 함수 <see cref="AlertWordWrapper"/>가 한다. 화면 글자만 바꾸며 Ctrl+C 복사는
    /// ViewModel의 원문(<see cref="AlertDialogViewModel.CopyText"/>)을 그대로 쓴다. TextWrapping=Wrap은 그대로 둬
    /// 측정 오차가 있어도 글자가 잘리지 않게 한다(안전장치).
    /// </summary>
    internal void ApplyKeepAllWrap(double pixelsPerDip)
    {
        if (!_viewModel.HasBody)
            return;

        // 1px 여유 — FormattedText 폭과 TextBlock 줄 배치의 반올림 차이로 WPF가 마지막 글자를 다시 넘기지 않게.
        double maxWidth = BodyText.ActualWidth - 1;
        if (maxWidth <= 0)
            return;

        var typeface = new Typeface(BodyText.FontFamily, BodyText.FontStyle, BodyText.FontWeight, BodyText.FontStretch);
        var mode = TextOptions.GetTextFormattingMode(BodyText);
        var culture = System.Globalization.CultureInfo.GetCultureInfo("ko-KR");

        double Measure(string s) => new FormattedText(
            s, culture, FlowDirection.LeftToRight, typeface, BodyText.FontSize, Brushes.Black,
            null, mode, pixelsPerDip).WidthIncludingTrailingWhitespace;

        BodyText.Text = AlertWordWrapper.Wrap(_viewModel.Body, maxWidth, Measure);
    }

    /// <summary>카드(그림자 여백 제외)의 창 안 위치 — 헬퍼가 owner 중앙 정렬에 쓴다.</summary>
    internal Thickness CardMargin => Root.Margin;

    private void AlertDialog_Loaded(object sender, RoutedEventArgs e)
    {
        UseClipFreeBodyIfFits();
        FocusDefaultButton();
        PlaySound(_viewModel.Sound);
        // 진입 애니메이션(D4)은 두지 않는다 — 반투명·확대 중에는 WPF가 ClearType을 쓰지 못해 글자가 잠깐 흐렸다가
        // 선명해지는 것이 보였다(P38-5 컴팩트 모드에서 사용자 확인, 2026-10-01 사용자 결정으로 D4 철회).
    }

    /// <summary>
    /// 활성화 실패 알림(PRD §3.4, 2026-10-01 사용자 확정). Topmost를 쓰지 않으므로 앱이 백그라운드일 때
    /// (owner 최소화·트레이 등) Windows 포그라운드 잠금 때문에 알림창이 다른 창 뒤에 뜰 수 있다 — 첫
    /// 렌더링 뒤에도 활성 창이 아니면 작업 표시줄 버튼을 깜빡여 알린다. 포그라운드가 되면 OS가 멈춘다.
    /// </summary>
    private void AlertDialog_ContentRendered(object? sender, EventArgs e)
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
                return;

            // WPF IsActive는 판정에 쓸 수 없다 — 포그라운드 전환이 거부돼도 스레드 안에서는 활성 창이 돼
            // IsActive=true가 된다(2026-10-01 실측: owner 최소화 재현에서 IsActive로 판정하면 깜빡이지 않았다).
            // 실제로 사용자 앞에 왔는지는 시스템 포그라운드 창으로 판정한다.
            if (GetForegroundWindow() == hwnd)
                return;

            var info = new FlashWindowInfo
            {
                cbSize = (uint)Marshal.SizeOf(typeof(FlashWindowInfo)),
                hwnd = hwnd,
                dwFlags = FLASHW_ALL | FLASHW_TIMERNOFG,
                uCount = 0,
                dwTimeout = 0,
            };
            FlashWindowEx(ref info);
        }
        catch (Exception)
        {
            // 깜빡임 실패는 알림창 동작과 무관 — 무시.
        }
    }

    private const uint FLASHW_ALL = 0x00000003;
    private const uint FLASHW_TIMERNOFG = 0x0000000C;

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashWindowInfo
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    /// <summary>user32 FlashWindowEx — Windows 2000부터 있는 API(DWM 아님, Win7 지원).</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FlashWindowInfo pwfi);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    private void ViewModel_CloseRequested(object? sender, EventArgs e) => Close();

    /// <summary>
    /// 본문이 최대 높이(320) 안에 들어가면 ScrollViewer 밖(클립 없는 <c>BodyPlain</c>)으로 옮긴다. Loaded는 첫
    /// 레이아웃 뒤·첫 렌더링 전이라 화면에 바뀌는 순간이 보이지 않는다. 레이어드 창(AllowsTransparency)에서
    /// ClearTypeHint로 ClearType을 켜도 ScrollContentPresenter의 클립 아래 글자는 ClearType이 약해진다
    /// (2026-10-01 P38-5 실측) — 넘치는 본문(D6)만 스크롤 영역에 남긴다.
    /// </summary>
    private void UseClipFreeBodyIfFits()
    {
        if (!_viewModel.HasBody)
            return;

        // ScrollViewer.ScrollableHeight는 Loaded 시점에 아직 갱신 전(0)이라 판정에 쓸 수 없다(실측: 긴 본문까지
        // 옮겨져 카드가 1000px 넘게 커짐). 첫 측정에서 TextBlock은 높이 무한대로 재므로 DesiredSize가 본문 전체
        // 높이다 — 이것이 최대 높이를 넘으면 스크롤 영역에 남긴다.
        if (BodyText.DesiredSize.Height > BodyScroll.MaxHeight)
            return;

        BodyScroll.Content = null;
        BodyScroll.Visibility = Visibility.Collapsed;
        BodyPlain.Child = BodyText;
        BodyPlain.Visibility = Visibility.Visible;
    }

    /// <summary>Alt+F4·시스템 닫기처럼 결과 없이 닫히면 ESC와 같은 결과(PRD §3.4).</summary>
    private void AlertDialog_Closing(object? sender, CancelEventArgs e) => _viewModel.ResolveResultOnClose();

    private void AlertDialog_Closed(object? sender, EventArgs e)
    {
        _viewModel.CloseRequested -= ViewModel_CloseRequested;
        Loaded -= AlertDialog_Loaded;
        ContentRendered -= AlertDialog_ContentRendered;
        PreviewKeyDown -= AlertDialog_PreviewKeyDown;
        Closing -= AlertDialog_Closing;
        Closed -= AlertDialog_Closed;
    }

    // ───────────────────────── 키보드 ─────────────────────────

    private void AlertDialog_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                _viewModel.SelectForEscape();
                break;

            case Key.Enter:
                e.Handled = true;
                // 알림창을 연 Enter 키의 자동 반복이 곧바로 알림창을 닫지 않게 한다.
                if (e.IsRepeat)
                    break;
                // 포커스 링이 보이는 버튼이 눌린다(D5 "어느 버튼이 눌릴지 보이게"). 처음 포커스는 주 버튼이라
                // Tab을 누르지 않았다면 항상 주 버튼 결과(PRD §3.4 Enter = 주 버튼).
                if (Keyboard.FocusedElement is Button { DataContext: AlertButtonSpec spec })
                    _viewModel.Select(spec.Result);
                else
                    _viewModel.SelectForEnter();
                break;

            case Key.C when (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control:
                e.Handled = true;
                CopyToClipboard(_viewModel.CopyText);
                break;
        }
    }

    /// <summary>D7 — 제목 + 빈 줄 + 본문을 클립보드에. 다른 프로세스가 클립보드를 잡고 있으면
    /// 실패할 수 있다(COMException 등) — 알림창 동작을 깨지 않도록 삼킨다.</summary>
    private static void CopyToClipboard(string text)
    {
        try
        {
            Clipboard.SetDataObject(text, copy: true);
        }
        catch (Exception)
        {
            // 복사 실패는 무시(알림창은 그대로 동작).
        }
    }

    // ───────────────────────── 이동 · 포커스 ─────────────────────────

    /// <summary>버튼·스크롤바 밖 아무 곳이나 끌어서 이동(원본 HTCAPTION 동작).</summary>
    private void Card_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsInside<ButtonBase>(e.OriginalSource as DependencyObject) || IsInside<ScrollBar>(e.OriginalSource as DependencyObject))
            return;

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // 마우스 버튼이 이미 떼어진 경우 등 — 무시.
        }
    }

    private static bool IsInside<T>(DependencyObject? element) where T : DependencyObject
    {
        while (element != null)
        {
            if (element is T)
                return true;
            element = element is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(element)
                : LogicalTreeHelper.GetParent(element);
        }

        return false;
    }

    /// <summary>D5 — 기본(주) 버튼에 키보드 포커스.</summary>
    private void FocusDefaultButton()
    {
        ButtonsHost.UpdateLayout();
        foreach (var spec in _viewModel.Buttons)
        {
            if (!spec.IsPrimary)
                continue;

            if (ButtonsHost.ItemContainerGenerator.ContainerFromItem(spec) is ContentPresenter presenter)
            {
                presenter.ApplyTemplate();
                if (presenter.ContentTemplate?.FindName("ChoiceButton", presenter) is Button button)
                {
                    button.Focus();
                    return;
                }
            }
        }
    }

    // ───────────────────────── 알림음 · 애니메이션 ─────────────────────────

    /// <summary>경고 → Exclamation, 오류 → Hand, 그 외 무음(PRD §3.4). 재생 실패는 무시.</summary>
    private static void PlaySound(AlertSound sound)
    {
        try
        {
            switch (sound)
            {
                case AlertSound.Exclamation:
                    SystemSounds.Exclamation.Play();
                    break;
                case AlertSound.Hand:
                    SystemSounds.Hand.Play();
                    break;
            }
        }
        catch (Exception)
        {
            // 소리 장치 없음 등 — 알림창 동작과 무관.
        }
    }
}
