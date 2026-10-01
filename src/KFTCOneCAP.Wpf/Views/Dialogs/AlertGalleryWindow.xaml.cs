using System;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using KFTCOneCAP.Wpf.ViewModels.Alerts;

namespace KFTCOneCAP.Wpf.Views.Dialogs;

/// <summary>
/// <c>--alert-gallery</c>(개발 전용, docs/alert_dialog/development_plan.md P38-2/P38-3/P38-4 화면 검증).
/// 각 버튼이 <see cref="AlertDialog.Show"/>를 이 창을 owner로 호출하고, 반환 결과를 아래 ResultText에
/// 표시한다. 구조: ① 조합 선택(종류 × 본문 길이 × 버튼, 콤보 3개 + 띄우기 버튼), ② 원본 캡처 문구 4개 +
/// PRD §4.2 실제 문구 14행 버튼 목록, ③ 특수 동작 버튼 4개(백그라운드 스레드 / owner 없음 / owner 최소화 /
/// 닫힌 owner).
/// </summary>
public partial class AlertGalleryWindow : Window
{
    private const string Caption = "KFTCGiroCAP";

    /// <summary>조합 선택 콤보의 본문 길이 종류(development_plan.md P38-4 표).</summary>
    private enum BodyShape
    {
        TitleOnly,
        OneLine,
        ReaderResultMultiLine,
        VeryLong,
    }

    private static readonly (string Name, string Text, AlertKind Kind, AlertButtons Buttons)[] Cases =
    {
        // 원본 캡처(docs/alert_dialog/screenshots/*.png)와 같은 문구
        ("Error (원본 문구)", "다운로드 실패\n가맹점 다운로드 정보가 잘못 입력되었습니다.", AlertKind.Error, AlertButtons.OK),
        ("Warning (원본 문구)", "리더기 설정 실패\n리더기 포트가 동일합니다.", AlertKind.Warning, AlertButtons.OK),
        ("Success (원본 문구)", "무결성 체크 성공\n리더기 모델명 [TS-KT-RA00021601]\n모듈 ID [C910540001]\n체크 시간 [20260401140112]", AlertKind.Success, AlertButtons.OK),
        ("Question YesNo (원본 문구)", "프로그램을 종료하시겠습니까?", AlertKind.Question, AlertButtons.YesNo),
        // 추가 구성
        ("Info OKCancel", "출력할 직전 거래가 없습니다.\n확인을 누르면 계속합니다.", AlertKind.Info, AlertButtons.OKCancel),
        ("Error 아주 긴 본문(스크롤)", BuildLongText(), AlertKind.Error, AlertButtons.OK),
        // 제목 한 줄 규칙 확인용 — 말줄임이 없으므로 제목 폭(272)을 넘으면 넘침(잘림)이 그대로 보여야 한다
        ("Warning 긴 제목만 (넘침 확인)", "리더기1과 리더기2에 같은 COM 포트를 지정할 수 없습니다. 다른 포트를 선택해주세요.", AlertKind.Warning, AlertButtons.OK),
        ("Error 긴 제목 + 본문 (넘침 확인)", "설정을 저장하지 못했습니다. 레지스트리 접근 권한을 확인하고 다시 시도해주세요.\n본문은 둘째 줄부터 그대로 표시됩니다.", AlertKind.Error, AlertButtons.OKCancel),
    };

    /// <summary>
    /// PRD §4.2 재분류 표의 실제 문구 14행 — 각 행을 해당 종류·버튼으로 그대로 띄워 본다. 예외 메시지·
    /// 사유처럼 런타임 값이 들어가는 자리는 self 검증과 무관한 화면 확인용이라 대표값을 채워 넣는다.
    /// </summary>
    private static readonly (string RowNumber, string Screen, string Text, AlertKind Kind, AlertButtons Buttons)[] RealPhraseCases =
    {
        // #1·#3·#6·#12·#14는 제목·본문 한 줄 규칙과 사용자용 표현에 맞춘 확정 문구(PRD §4.2·§4.4, 2026-10-01)
        ("#1", "가맹점 설정", "입력값을 확인해주세요.\n카드입력 타임아웃은 30초 이상 입력해주세요.", AlertKind.Warning, AlertButtons.OK),
        ("#2", "가맹점 설정", "입력값을 확인해주세요.", AlertKind.Warning, AlertButtons.OK),
        ("#3", "가맹점 설정", "설정 저장 실패\n설정을 저장하지 못했습니다.\n다시 시도하고, 반복되면 담당자에게\n문의해주세요.", AlertKind.Error, AlertButtons.OK),
        ("#4", "가맹점 설정", "프린터 포트번호를 입력해주세요.", AlertKind.Warning, AlertButtons.OK),
        ("#5", "가맹점 설정", "출력할 직전 거래가 없습니다.", AlertKind.Info, AlertButtons.OK),
        ("#6", "가맹점 설정(재출력)", "전표 출력 실패\n프린터 포트를 열 수 없습니다.", AlertKind.Warning, AlertButtons.OK),
        ("#7", "가맹점 설정", "변경된 내용이 있습니다.\n저장하지 않고 종료하시겠습니까?", AlertKind.Question, AlertButtons.YesNo),
        ("#8", "리더기 설정", "리더기 상태체크 성공\n리더기 인증 식별번호 : 1234567890\n모듈 ID : C910540001", AlertKind.Success, AlertButtons.OK),
        ("#9", "리더기 설정", "리더기 초기화 실패\n리더기가 연결되어 있지 않습니다.", AlertKind.Error, AlertButtons.OK),
        ("#10", "리더기 설정", "리더기 키다운로드 성공\n모듈 ID : C910540001", AlertKind.Success, AlertButtons.OK),
        ("#11", "리더기 설정", "리더기 키다운로드 실패\n단계: 3/5 (키 전송)", AlertKind.Error, AlertButtons.OK),
        ("#12", "리더기 설정", "리더기 설정 실패\n리더기 포트가 동일합니다.\n서로 다른 포트를 선택해주세요.", AlertKind.Warning, AlertButtons.OK),
        ("#13", "리더기 설정", "변경된 내용이 있습니다.\n저장하지 않고 종료하시겠습니까?", AlertKind.Question, AlertButtons.YesNo),
        ("#14", "결제창", "전표 출력 실패\n프린터 포트를 열 수 없습니다.\n가맹점 설정에서 다시 출력하세요.", AlertKind.Warning, AlertButtons.OK),
    };

    public AlertGalleryWindow()
    {
        InitializeComponent();

        InitializeCombinationPicker();

        var header1 = new TextBlock
        {
            Text = "원본 캡처 문구 + 추가 구성",
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 6),
        };
        CasePanel.Children.Add(header1);

        foreach (var c in Cases)
        {
            var captured = c;
            AddButton(captured.Name, () =>
                Report(captured.Name, AlertDialog.Show(this, captured.Text, Caption, captured.Kind, captured.Buttons)));
        }

        var header2 = new TextBlock
        {
            Text = "PRD §4.2 실제 문구 14행",
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 16, 0, 6),
        };
        CasePanel.Children.Add(header2);

        foreach (var c in RealPhraseCases)
        {
            var captured = c;
            string label = $"{captured.RowNumber} {captured.Screen} ({captured.Kind}/{captured.Buttons})";
            AddButton(label, () =>
                Report(label, AlertDialog.Show(this, captured.Text, Caption, captured.Kind, captured.Buttons)));
        }

        var header3 = new TextBlock
        {
            Text = "특수 동작",
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 16, 0, 6),
        };
        CasePanel.Children.Add(header3);

        AddButton("백그라운드 스레드에서 띄우기 (Warning)", () =>
        {
            Task.Run(() =>
            {
                try
                {
                    var r = AlertDialog.Show(this, "리더기 설정 실패\n백그라운드 스레드에서 호출했습니다.", Caption, AlertKind.Warning);
                    Dispatcher.BeginInvoke(new Action(() => Report("백그라운드 스레드", r)));
                }
                catch (Exception ex)
                {
                    Dispatcher.BeginInvoke(new Action(() => ResultText.Text = "결과: 백그라운드 예외 " + ex.GetType().Name + " " + ex.Message));
                }
            });
        });

        AddButton("owner 없이 띄우기 (Info)", () =>
            Report("owner 없음", AlertDialog.Show(null, "owner 없음\n화면 중앙 · 작업 표시줄에 표시됩니다.", Caption, AlertKind.Info)));

        AddButton("owner 최소화 상태에서 띄우기 (1초 뒤, 화면 중앙)", () =>
        {
            WindowState = WindowState.Minimized;
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                var r = AlertDialog.Show(this, "owner 최소화\n갤러리 창이 최소화된 상태에서 띄웠습니다.", Caption, AlertKind.Info);
                WindowState = WindowState.Normal;
                Report("owner 최소화", r);
            };
            timer.Start();
        });

        AddButton("닫힌 owner로 호출 (YesNo → 뜨지 않고 No)", () =>
        {
            var temp = new Window { Width = 200, Height = 100, ShowInTaskbar = false, Title = "temp" };
            temp.Show();
            temp.Close();
            var started = DateTime.Now;
            var r = AlertDialog.Show(temp, "닫힌 owner\n이 창은 보이면 안 됩니다.", Caption, AlertKind.Question, AlertButtons.YesNo);
            Report($"닫힌 owner ({(DateTime.Now - started).TotalMilliseconds:0}ms)", r);
        });
    }

    /// <summary>
    /// 조합 선택 콤보 3개(종류/본문 길이/버튼) 초기값을 채운다. 선택 가능한 수는 5×4×3=60가지지만
    /// "띄우기" 버튼 하나로 조합 폭발 없이 고를 수 있다(P38-4 지시).
    /// </summary>
    private void InitializeCombinationPicker()
    {
        KindCombo.ItemsSource = Enum.GetValues(typeof(AlertKind));
        KindCombo.SelectedItem = AlertKind.Info;

        BodyCombo.ItemsSource = new[]
        {
            new ComboItem<BodyShape>(BodyShape.TitleOnly, "제목만"),
            new ComboItem<BodyShape>(BodyShape.OneLine, "본문 1줄"),
            new ComboItem<BodyShape>(BodyShape.ReaderResultMultiLine, "본문 여러 줄(리더기 결과 형식)"),
            new ComboItem<BodyShape>(BodyShape.VeryLong, "아주 긴 본문"),
        };
        BodyCombo.DisplayMemberPath = nameof(ComboItem<BodyShape>.Display);
        BodyCombo.SelectedIndex = 0;

        ButtonsCombo.ItemsSource = Enum.GetValues(typeof(AlertButtons));
        ButtonsCombo.SelectedItem = AlertButtons.OK;
    }

    private void FireButton_Click(object sender, RoutedEventArgs e)
    {
        if (KindCombo.SelectedItem is not AlertKind kind
            || BodyCombo.SelectedItem is not ComboItem<BodyShape> bodyItem
            || ButtonsCombo.SelectedItem is not AlertButtons buttons)
        {
            return;
        }

        string text = BuildCombinationText(kind, bodyItem.Value);
        string label = $"조합({kind}/{bodyItem.Display}/{buttons})";
        Report(label, AlertDialog.Show(this, text, Caption, kind, buttons));
    }

    private static string BuildCombinationText(AlertKind kind, BodyShape shape)
    {
        return shape switch
        {
            BodyShape.TitleOnly => $"{kind} 조합 테스트 제목만",
            BodyShape.OneLine => $"{kind} 조합 테스트 제목\n본문 한 줄입니다.",
            BodyShape.ReaderResultMultiLine => "리더기 상태체크 성공\n리더기 인증 식별번호 : 1234567890\n모듈 ID : C910540001",
            BodyShape.VeryLong => BuildLongText(),
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null),
        };
    }

    private void AddButton(string label, Action onClick)
    {
        var button = new Button
        {
            Content = label,
            Margin = new Thickness(0, 0, 0, 8),
            Height = 36,
            Style = (Style)FindResource("ReaderButtonStyle"),
        };
        button.Click += (_, _) => onClick();
        CasePanel.Children.Add(button);
    }

    private int _sequence;

    // 일련번호를 붙여 같은 결과가 연달아 나와도 새 결과인지 구분되게 한다.
    private void Report(string name, AlertResult result) => ResultText.Text = $"결과 #{++_sequence}: {name} → {result}";

    private static string BuildLongText()
    {
        var sb = new StringBuilder("설정을 저장하지 못했습니다.\n");
        sb.Append("System.UnauthorizedAccessException: 레지스트리 키에 대한 액세스가 거부되었습니다.");
        for (int i = 1; i <= 30; i++)
            sb.Append("\n   위치: KFTCOneCAP.Wpf.Services.Settings.ShopSettingsService.Save(ShopSettings settings) 줄 ").Append(100 + i);
        return sb.ToString();
    }

    /// <summary>ComboBox용 "표시 문구 ↔ 값" 래퍼(DisplayMemberPath 대상).</summary>
    private sealed class ComboItem<T>
    {
        public ComboItem(T value, string display)
        {
            Value = value;
            Display = display;
        }

        public T Value { get; }

        public string Display { get; }

        public override string ToString() => Display;
    }
}
