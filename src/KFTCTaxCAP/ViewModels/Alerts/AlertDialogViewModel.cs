using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace KFTCTaxCAP.ViewModels.Alerts;

/// <summary>
/// 알림창 한 개의 화면 상태(docs/alert_dialog/PRD.md §3.1·§3.4, development_plan.md P38-2). 제목/본문 분리는
/// <see cref="AlertTextSplitter"/>, 버튼 구성·키보드 결과는 <see cref="AlertBehavior"/>에 맡기고 이 클래스는
/// 그 결과를 묶어 들고 있다가 "어떤 결과로 닫을지"만 정한다. WPF 타입을 참조하지 않는다 — 창 이동·포커스·
/// 알림음·애니메이션은 View(<c>Views/Dialogs/AlertDialog.xaml.cs</c>)가 맡는다.
/// </summary>
public sealed partial class AlertDialogViewModel : ObservableObject
{
    public AlertDialogViewModel(string? text, AlertKind kind, AlertButtons buttons)
    {
        var (title, body) = AlertTextSplitter.Split(text);
        Title = title;
        Body = body;
        Kind = kind;
        ButtonsMode = buttons;
        Buttons = AlertBehavior.ButtonsFor(buttons);
        Sound = AlertBehavior.SoundFor(kind);
        SelectCommand = new RelayCommand<AlertButtonSpec?>(spec =>
        {
            if (spec != null)
                Select(spec.Result);
        });
    }

    /// <summary>굵은 제목(문구의 첫 줄).</summary>
    public string Title { get; }

    /// <summary>본문(첫 줄 뒤 나머지, 줄바꿈은 <c>\n</c>으로 정규화됨). 없으면 빈 문자열.</summary>
    public string Body { get; }

    /// <summary>본문이 있으면 true — 없으면 View가 제목을 아이콘 세로 중앙에 둔다(PRD §2.2).</summary>
    public bool HasBody => Body.Length > 0;

    public AlertKind Kind { get; }

    public AlertButtons ButtonsMode { get; }

    /// <summary>표시 순서대로의 버튼(D3 — 주 버튼 왼쪽).</summary>
    public IReadOnlyList<AlertButtonSpec> Buttons { get; }

    /// <summary>열릴 때 재생할 알림음(재생은 View).</summary>
    public AlertSound Sound { get; }

    /// <summary>Ctrl+C로 복사할 문구(D7) — 제목 + 빈 줄 + 본문. 본문이 없으면 제목만.</summary>
    public string CopyText => HasBody ? Title + Environment.NewLine + Environment.NewLine + Body.Replace("\n", Environment.NewLine) : Title;

    /// <summary>닫힌 결과. 아직 닫히지 않았으면 null.</summary>
    public AlertResult? Result { get; private set; }

    /// <summary>버튼 클릭 — 파라미터는 그 버튼의 <see cref="AlertButtonSpec"/>.</summary>
    public IRelayCommand<AlertButtonSpec?> SelectCommand { get; }

    /// <summary>결과가 정해져 창을 닫아야 할 때(한 번만 발생).</summary>
    public event EventHandler? CloseRequested;

    /// <summary>결과를 확정하고 닫기를 요청한다. 이미 결과가 있으면 무시한다(중복 클릭·키 반복 방지).</summary>
    public void Select(AlertResult result)
    {
        if (Result.HasValue)
            return;

        Result = result;
        OnPropertyChanged(nameof(Result));
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Enter — 주 버튼 결과(PRD §3.4).</summary>
    public void SelectForEnter() => Select(AlertBehavior.ResultForEnter(ButtonsMode));

    /// <summary>ESC — OK→OK, OKCancel→Cancel, YesNo→No(PRD §3.4).</summary>
    public void SelectForEscape() => Select(AlertBehavior.ResultForEscape(ButtonsMode));

    /// <summary>
    /// 창이 결과 없이 닫힐 때(Alt+F4, 시스템 메뉴 닫기 등)의 결과 — ESC와 같다(PRD §3.4). 이미 결과가
    /// 있으면 그 결과를 그대로 돌려준다.
    /// </summary>
    public AlertResult ResolveResultOnClose()
    {
        if (!Result.HasValue)
        {
            Result = AlertBehavior.ResultForEscape(ButtonsMode);
            OnPropertyChanged(nameof(Result));
        }

        return Result.Value;
    }
}
