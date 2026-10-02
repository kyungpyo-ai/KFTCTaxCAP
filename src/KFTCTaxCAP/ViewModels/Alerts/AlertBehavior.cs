using System;
using System.Collections.Generic;

namespace KFTCTaxCAP.ViewModels.Alerts;

/// <summary>
/// 알림음 종류. <c>System.Media</c> 타입을 쓰지 않고 enum으로만 표현한다 — 실제 재생은 View
/// 쪽(<c>AlertDialog.xaml.cs</c>)이 담당한다(docs/alert_dialog/PRD.md §3.4).
/// </summary>
public enum AlertSound
{
    None,
    Exclamation,
    Hand,
}

/// <summary>
/// 버튼 구성 하나를 화면에 표시할 때의 버튼 1개 사양(표시 순서대로 나열된다).
/// </summary>
public sealed class AlertButtonSpec
{
    public AlertButtonSpec(AlertResult result, string label, bool isPrimary)
    {
        Result = result;
        Label = label;
        IsPrimary = isPrimary;
    }

    public AlertResult Result { get; }

    public string Label { get; }

    public bool IsPrimary { get; }
}

/// <summary>
/// 버튼 구성 → 표시 순서/문구, 키보드(Enter/ESC) → 결과, 종류 → 알림음의 순수 매핑
/// (docs/alert_dialog/PRD.md §3.1·§3.3 D3·§3.4). WPF 타입을 참조하지 않는다.
/// </summary>
public static class AlertBehavior
{
    // D3(2026-10-01 사용자 확정) — 주 버튼이 항상 왼쪽: [확인][취소], [예][아니요].
    // 버튼 문구는 표준어 "아니요"를 쓴다(원본 "아니오"에서 정정).
    public static IReadOnlyList<AlertButtonSpec> ButtonsFor(AlertButtons buttons)
    {
        return buttons switch
        {
            AlertButtons.OK => new[]
            {
                new AlertButtonSpec(AlertResult.OK, "확인", isPrimary: true),
            },
            AlertButtons.OKCancel => new[]
            {
                new AlertButtonSpec(AlertResult.OK, "확인", isPrimary: true),
                new AlertButtonSpec(AlertResult.Cancel, "취소", isPrimary: false),
            },
            AlertButtons.YesNo => new[]
            {
                new AlertButtonSpec(AlertResult.Yes, "예", isPrimary: true),
                new AlertButtonSpec(AlertResult.No, "아니요", isPrimary: false),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(buttons), buttons, null),
        };
    }

    /// <summary>Enter 키 → 주 버튼 결과(확인/확인/예).</summary>
    public static AlertResult ResultForEnter(AlertButtons buttons)
    {
        return buttons switch
        {
            AlertButtons.OK => AlertResult.OK,
            AlertButtons.OKCancel => AlertResult.OK,
            AlertButtons.YesNo => AlertResult.Yes,
            _ => throw new ArgumentOutOfRangeException(nameof(buttons), buttons, null),
        };
    }

    /// <summary>
    /// ESC 키 → 결과(OK→OK, OKCancel→Cancel, YesNo→No). 창 닫기(Alt+F4)도 이 결과를 그대로 쓴다
    /// (원본 결함 — YesNo에서 ESC가 IDCANCEL로 가던 것을 §2.5 #2에서 정정).
    /// </summary>
    public static AlertResult ResultForEscape(AlertButtons buttons)
    {
        return buttons switch
        {
            AlertButtons.OK => AlertResult.OK,
            AlertButtons.OKCancel => AlertResult.Cancel,
            AlertButtons.YesNo => AlertResult.No,
            _ => throw new ArgumentOutOfRangeException(nameof(buttons), buttons, null),
        };
    }

    /// <summary>경고·오류에만 알림음(사용자 확정) — 그 외 종류는 무음.</summary>
    public static AlertSound SoundFor(AlertKind kind)
    {
        return kind switch
        {
            AlertKind.Warning => AlertSound.Exclamation,
            AlertKind.Error => AlertSound.Hand,
            _ => AlertSound.None,
        };
    }
}
