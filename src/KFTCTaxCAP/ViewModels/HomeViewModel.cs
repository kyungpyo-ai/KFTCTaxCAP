using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KFTCTaxCAP.Services;
using KFTCTaxCAP.ViewModels.Alerts;

namespace KFTCTaxCAP.ViewModels;

/// <summary>
/// 홈 화면(Views/HomeWindow.xaml)의 ViewModel.
/// Phase 7(MVVM 전환, docs/payment_relay/development_plan.md P7-5): HomeWindow.xaml.cs(245줄)는
/// 대부분이 View/OS 책임(트레이 아이콘 WinForms interop, DWM 타이틀바, 창 워밍업, 눌림 애니메이션
/// 프레임 확보용 Dispatcher.BeginInvoke)이라 옮길 것이 적다. 이 ViewModel은 "카드 클릭 시 무엇을
/// 할지"만 Command로 노출하고, 그 결과를 이벤트로 알린다 — 실제 창 생성/타이밍/DWM 같은 WPF
/// Window·WinForms 타입을 다루는 코드는 전부 Views/HomeWindow.xaml.cs에 남아 있다. 이곳으로
/// 옮기면 ViewModel이 Window/WinForms 타입을 알게 되어 계층 규칙(ViewModels → Services → ...,
/// docs/payment_relay/ROADMAP.md "계층 구조")이 깨지기 때문이다.
/// </summary>
public sealed partial class HomeViewModel : ObservableObject
{
    /// <summary>타이틀바 문구 — "KFTCTaxCAP Ver 1.0.0 (테스트) | 리더기 모듈 Ver 1.0.0.0"(PRD_WPF.md §3.2, 2026-10-15).</summary>
    public string WindowTitle { get; } = $"KFTCTaxCAP {AppVersionInfo.VersionText}";

    /// <summary>헤더 부제의 버전 부분 — 타이틀바와 같은 표기. 앞의 프로그램 설명보다 작은 글씨로 붙는다(HomeWindow.xaml).</summary>
    public string SubtitleVersion { get; } = AppVersionInfo.VersionText;

    /// <summary>리더기 설정 카드 클릭(PRD 3.7/4.2) — 실제 창 생성/오픈은 View가 담당한다.</summary>
    public event EventHandler? ReaderSetupRequested;

    /// <summary>가맹점 설정 카드 클릭(Phase 23, docs/operations/development_plan.md P23-4) — 실제
    /// 창 생성/오픈은 View가 담당한다. "준비 중" 안내에서 실제 화면 오픈으로 바뀌었다(전표 설정
    /// 카드가 Phase 31에서 삭제되며 그 시절 안내 이벤트도 함께 정리했다).</summary>
    public event EventHandler? ShopSetupRequested;

    /// <summary>결제 카드 클릭(Phase 29, docs/payment_relay/development_plan.md P29-7, PRD.md §12.1) —
    /// 실제 창 생성/오픈은 View가 담당한다.</summary>
    public event EventHandler? PaymentScreenRequested;

    /// <summary>아직 동작이 없는 헤더 버튼(로그 전송·최신 버전 업데이트)의 "준비 중" 안내(2026-10-15, 외부 업체
    /// 테스트 배포 전 — 눌러도 반응이 없어 고장처럼 보였다). 다른 ViewModel과 같은 <see cref="AlertMessage"/> 패턴.</summary>
    public event EventHandler<AlertMessage>? AlertRequested;

    internal const string LogSendNotReadyMessage = "준비 중인 기능입니다.\n로그 전송은 다음 버전에서 제공됩니다.";
    internal const string UpdateNotReadyMessage = "준비 중인 기능입니다.\n업데이트는 다음 버전에서 제공됩니다.";

    [RelayCommand]
    private void OpenReaderSetup() => ReaderSetupRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void OpenShopSetup() => ShopSetupRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void OpenTrans() => PaymentScreenRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void SendLog() => AlertRequested?.Invoke(this, new AlertMessage(AlertKind.Info, LogSendNotReadyMessage));

    [RelayCommand]
    private void UpdateVersion() => AlertRequested?.Invoke(this, new AlertMessage(AlertKind.Info, UpdateNotReadyMessage));
}
