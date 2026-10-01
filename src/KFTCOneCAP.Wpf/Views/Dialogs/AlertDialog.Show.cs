using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using KFTCOneCAP.Wpf.Services.Diagnostics;
using KFTCOneCAP.Wpf.ViewModels.Alerts;

namespace KFTCOneCAP.Wpf.Views.Dialogs;

/// <summary>
/// 알림창 정적 헬퍼 — <c>MessageBox.Show</c> 자리를 그대로 대체한다(docs/alert_dialog/PRD.md §3.1·§3.4).
/// </summary>
public partial class AlertDialog
{
    /// <summary>
    /// 알림창을 모달로 띄우고 결과를 돌려준다.
    /// <list type="bullet">
    /// <item>UI 스레드가 아니면 owner(없으면 Application)의 Dispatcher로 넘겨 동기 실행한다.</item>
    /// <item>owner가 이미 닫혔으면(창 핸들 없음) 띄우지 않고 ESC 결과(OK/Cancel/No)를 돌려준다.
    ///   owner의 <c>Closing</c> 처리기 안(아직 닫기를 취소할 수 있는 단계)에서는 정상적으로 띄운다 —
    ///   "저장하지 않고 종료하시겠습니까?" 질문이 바로 그 자리에서 뜬다(PRD §4.2 #7·#13).</item>
    /// <item>owner가 최소화·숨김(트레이)이면 owner 없이 화면 중앙에 띄운다(숨은 owner에 딸린 창은 같이 숨는다).</item>
    /// <item>카드(그림자 여백 제외)가 owner 중앙에 오도록 위치를 계산한다. Topmost는 쓰지 않는다.</item>
    /// <item><c>ShowInTaskbar</c> = 실제로 쓰는 owner가 없을 때만, <c>Title</c> = <paramref name="caption"/>(화면에는 안 그림).</item>
    /// </list>
    /// </summary>
    public static AlertResult Show(Window? owner, string text, string caption, AlertKind kind, AlertButtons buttons = AlertButtons.OK)
    {
        var dispatcher = owner?.Dispatcher ?? Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            // CP1 L-2 — 종료 중인 Dispatcher의 Invoke는 실행 없이 default(AlertResult)=OK를 돌려줄 수 있다.
            // YesNo/OKCancel에서 OK가 나오지 않도록 ESC 결과로 돌려준다.
            if (dispatcher.HasShutdownStarted)
                return AlertBehavior.ResultForEscape(buttons);

            return dispatcher.Invoke(() => ShowCore(owner, text, caption, kind, buttons));
        }

        // CP1 L-2 — 넘길 Dispatcher가 없는 백그라운드(MTA) 스레드에서는 창을 만들 수 없다(STA 예외).
        if (dispatcher == null && System.Threading.Thread.CurrentThread.GetApartmentState() != System.Threading.ApartmentState.STA)
        {
            FileLogger.Warn(LogCategory.Ui, $"[AlertDialog] UI 스레드가 없어 알림창을 띄우지 못함 (caption=\"{caption}\")");
            return AlertBehavior.ResultForEscape(buttons);
        }

        return ShowCore(owner, text, caption, kind, buttons);
    }

    private static AlertResult ShowCore(Window? owner, string text, string caption, AlertKind kind, AlertButtons buttons)
    {
        var fallback = AlertBehavior.ResultForEscape(buttons);

        Window? effectiveOwner = null;
        if (owner != null)
        {
            if (new WindowInteropHelper(owner).Handle == IntPtr.Zero)
            {
                FileLogger.Info(LogCategory.Ui, $"[AlertDialog] owner 창이 이미 닫혀 알림창을 띄우지 않음 (caption=\"{caption}\", 기본 결과 {fallback})");
                return fallback;
            }

            if (owner.IsVisible && owner.WindowState != WindowState.Minimized)
                effectiveOwner = owner;
        }

        var viewModel = new AlertDialogViewModel(text, kind, buttons);
        var dialog = new AlertDialog(viewModel)
        {
            Title = caption ?? string.Empty,
            ShowInTaskbar = effectiveOwner == null,
        };

        try
        {
            if (effectiveOwner != null)
                dialog.Owner = effectiveOwner;

            PlaceDialog(dialog, effectiveOwner);
            dialog.ShowDialog();
        }
        catch (InvalidOperationException ex)
        {
            // owner가 이 사이에 닫히기 시작해 Owner 지정/모달 표시가 거부된 경우 등.
            FileLogger.Warn(LogCategory.Ui, $"[AlertDialog] 알림창을 띄우지 못함 (caption=\"{caption}\"): {ex.Message}");
            return viewModel.Result ?? fallback;
        }

        return viewModel.Result ?? fallback;
    }

    /// <summary>
    /// 카드 중심을 owner 창(바깥 테두리 기준) 중심 — owner가 없으면 주 모니터 작업 영역 중심 — 에 맞춘다.
    /// 창에는 그림자용 투명 여백이 있어 창 자체를 가운데 두면 카드가 어긋나므로 여백을 빼고 계산한다.
    /// </summary>
    private static void PlaceDialog(AlertDialog dialog, Window? owner)
    {
        var content = (FrameworkElement)dialog.Content;
        content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        // 본문 어절 단위 줄바꿈(PRD §4.4): 본문 폭을 알도록 한 번 배치한 뒤 줄바꿈을 넣고 다시 잰다 —
        // 줄 수가 늘어난 높이로 중앙 위치를 계산해야 카드가 어긋나지 않는다.
        content.Arrange(new Rect(content.DesiredSize));
        double pixelsPerDip = VisualTreeHelper.GetDpi((Visual?)owner ?? dialog).PixelsPerDip;
        dialog.ApplyKeepAllWrap(pixelsPerDip);
        content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        var margin = dialog.CardMargin;
        double cardWidth = content.DesiredSize.Width - margin.Left - margin.Right;
        double cardHeight = content.DesiredSize.Height - margin.Top - margin.Bottom;

        Rect target = owner != null && TryGetWindowRectInDips(owner, out var ownerRect)
            ? ownerRect
            : SystemParameters.WorkArea;

        double centerX = target.Left + target.Width / 2;
        double centerY = target.Top + target.Height / 2;

        dialog.WindowStartupLocation = WindowStartupLocation.Manual;
        dialog.Left = Math.Round(centerX - cardWidth / 2 - margin.Left);
        dialog.Top = Math.Round(centerY - cardHeight / 2 - margin.Top);
    }

    /// <summary>owner의 실제 화면 사각형(최대화 상태 포함)을 DIP 단위로. Window.Left/Top은 최대화 시
    /// 복원 위치를 돌려주므로 GetWindowRect를 쓴다(Win7부터 있는 user32 API, DWM 아님).</summary>
    private static bool TryGetWindowRectInDips(Window owner, out Rect rect)
    {
        rect = Rect.Empty;
        var hwnd = new WindowInteropHelper(owner).Handle;
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var r))
            return false;

        var source = PresentationSource.FromVisual(owner);
        Matrix fromDevice = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var topLeft = fromDevice.Transform(new Point(r.Left, r.Top));
        var bottomRight = fromDevice.Transform(new Point(r.Right, r.Bottom));
        rect = new Rect(topLeft, bottomRight);
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);
}
