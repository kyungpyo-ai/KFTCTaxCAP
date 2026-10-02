# 실행계획서: 알림창(메시지 박스) UI/UX 개선 (5차 범위)

> `PRD.md`(무엇을) → `ROADMAP.md`(순서) → **이 문서(Task 단위 지시·완료 조건)**. 각 Phase의 절은 그 Phase
> **착수 직전**에 작성한다. 현재 작성된 절: **Phase 38(작성 2026-10-01, 완료 2026-10-01)**, **Phase 39(작성 2026-10-02)**.

## 공통 작업 규칙 (모든 Phase)

### 모델·에이전트

- **메인 세션 모델은 Opus 5.5로 고정**(2026-10-01 사용자 지시). 설계·검증·실기 판독·문서·사용자와 함께하는
  화면 대조는 메인이 직접 한다.
- **구현 위임 모델은 Task 성격으로 고른다**(2026-10-01 사용자 지시 — "무조건 Sonnet이 아니라 더 알맞은 것으로").
  에이전트 정의의 기본 모델과 다르면 Agent 호출에 `model`을 명시한다.

| 기준 | 모델 | 예 |
|---|---|---|
| 시각 품질·디자인 판단이 결과를 좌우(원본 대조, 그림자·간격·벡터 아이콘 미세 조정), 근거가 캡처라 명세로 완결되지 않음 | **Opus** | 알림창 XAML·스타일 |
| 명세(PRD 표·이 문서)로 입력·출력이 완결되는 로직, 여러 파일 수정 | **Sonnet** | 계약 타입·매핑 로직, 키보드·알림음 동작, 하네스, ViewModel 이벤트 계약 변경 |
| 표대로 바꾸기만 하는 반복 치환, 판단 거의 없음 | **Haiku** | View 호출부 `MessageBox.Show` → `AlertDialog.Show` 치환(Phase 39) |

| 에이전트 | 기본 모델(frontmatter) | 이 범위에서 쓰는 곳 |
|---|---|---|
| (메인 세션, 직접) | **Opus 5.5** | 설계 판단, Task 검증(diff 직접 읽기), 원본 캡처 대조, 문서 갱신 |
| `csharp-wpf-developer` | 없음(세션 상속 = Opus) — Sonnet/Haiku로 쓸 때는 `model` 명시 | 알림창 전체(XAML·VM·헬퍼·하네스), Phase 39 호출부 교체. `mcp__windows__*`로 캡처 검증까지 |
| `checkpoint-reviewer` | Opus | 체크포인트 독립 검증(같은 세션 서브에이전트, 중립 프롬프트) |

**에이전트를 쓸지 판단 기준**: ① 여러 파일에 걸친 새 코드 작성이고 근거 문서로 지시를 완결할 수 있으면 위임,
② 사용자와 주고받으며 판단해야 하거나(디자인 육안 확인) 몇 줄 수정·문서 작업이면 Opus 직접.

### Task 사이클 (앞 범위 방식 그대로)

1. **구현(위임)** — 담당 에이전트가 구현 + Task 테스트 실행 + 결과 보고(변경 파일·실행 명령·캡처 경로·못 한 것).
2. **검증(Opus)** — 보고를 그대로 믿지 않는다. diff를 직접 읽고 "Opus 확인 포인트"를 대조하며, Task 테스트의
   핵심 명령 1개를 직접 다시 실행한다. 화면 Task는 캡처를 직접 열어 본다.
3. **수정** — 결함은 같은 에이전트에 SendMessage로 이어서 맡기고, Opus는 수정분만 재확인한다.
4. 체크포인트에서는 `checkpoint-reviewer`가 한 번 더 본다(Opus 검증을 대체하지 않음).
5. 체크박스 갱신 → (사용자 요청 시) 커밋.

**`checkpoint-reviewer` 프롬프트는 중립으로**: diff 범위, PRD/이 문서의 해당 절 경로, CP 확인 항목만. Opus의
결론이나 "여기는 괜찮다"는 판단을 넣지 않는다.

### 관리자 권한(`app.manifest`) — 화면 검증 시

앱이 `requireAdministrator`라 에이전트의 GUI 자동화(캡처·클릭)가 UIPI에 막힌다. 화면 검증이 필요할 때만
`asInvoker`로 낮추고 **확인 즉시 `requireAdministrator`로 되돌린다**(Phase 23·31·33 선례). 되돌릴 때마다
매니페스트 주석에 기록하고, 각 Phase 마지막 항목에서 원복을 확인한다. 실행 중인 `KFTCTaxCAP.exe`가 사용자
세션의 것인지 확인하지 않고 종료하지 않는다.

---

# Phase 38 — 알림창 컴포넌트

**이 Phase가 끝나면**: `--alert-gallery`로 5종 알림창을 모든 버튼 구성·본문 길이로 띄워 볼 수 있고, 모양이 원본
캡처를 기반으로 PRD §3.3 개선안이 반영된 상태로 사용자 확인을 받았다. 기존 화면은 아직 기본 `MessageBox`를 쓴다.

> **실수가 나기 쉬운 곳**: ① `AllowsTransparency=True` 창에서 그림자 여백만큼 창 크기가 커져 owner 중앙
> 배치가 어긋나는 것(카드가 아니라 투명 여백 포함 창이 중앙에 놓임 — 의도한 것인지 확인), ② YesNo에서
> ESC/Alt+F4가 `No`가 아닌 값으로 가는 것(원본 결함 재현), ③ `ViewModels/Alerts/`에 `System.Windows` 타입
> (`MessageBoxResult`, `Visibility` 등)이 섞여 들어가는 것, ④ 리소스 병합 순서(`App.xaml.cs:158-164`)를
> 어겨 `StaticResource` 해석이 실패하는 것.

## 착수 전 확정 사항

1. **디자인 기준·종류 재분류·Topmost 없음·경고/오류만 알림음·첫 줄 제목·WPF 앱만** — 2026-10-01 사용자 확정(PRD §0).
2. **디자인 개선 D1~D8**(PRD §3.3) — 2026-10-01 사용자 확정: **전부 적용**하고 P38-5에서 갤러리·원본 대조로 최종
   판단(빼거나 조정 가능). D3 = 주 버튼 왼쪽 [확인][취소]·[예][아니요], 버튼 문구 `아니요`.
3. **원본 캡처** — 2026-10-01 도착: `screenshots/Error.png`·`Warning.png`·`Success.png`·`Question.png`. Opus 픽셀
   측정 결과를 PRD §2.6에 기록했고 §3.2 수치에 반영했다(폭 376, 버튼 높이 40, 텍스트→버튼 넓은 간격, 예 버튼 색 차이 → D9).

## 착수 전 전제 (코드 실측, 2026-10-01)

- **현재 알림** — `MessageBox.Show` 7곳, 전부 View 코드비하인드(PRD §4.1·§4.2). 이 Phase에서는 건드리지 않는다.
- **리소스** — `App.xaml`은 비어 있고 `App.xaml.cs:158-164`가 Colors → Typography(일반/Compact) → Layout(일반/
  Compact) → Buttons → ComboBox → ToggleSwitch → TextBox 순으로 코드 병합. 컴팩트 = 화면 높이 ≤ 800
  (`App.xaml.cs:146`), 컴팩트 폰트는 Malgun Gothic(`Typography.Compact.xaml:37-38`).
- **재사용 가능한 것** — `TitleTextBrush`(#191F28), `Blue500Brush`(#0064DD), `PrimaryButtonStyle`(hover #0052B8,
  pressed #00449A, `Buttons.xaml:144`), `ModernButtonBase`(반지름 8, 눌림 0.97 축소, `:21`), `PretendardFontFamily`,
  카드 그림자 패턴(`ShopSetupWindow.xaml:36-38`), `CardIconBgBrush`(#EBF4FF = 원본 Info 원 배경과 같은 값).
- **없는 것** — 경고(앰버)·정보 전용 색 토큰, `AllowsTransparency=True` 창(지금은 Popup·ComboBox만), 다이얼로그 추상화.
- **진단 인자** — `App.xaml.cs`의 `else if (e.Args[0] == "--...")` 체인. UI 확인용은 `--gallery`(StyleGalleryWindow)
  선례, 로직 자체 검증은 `*SelfTest.RunAll`을 `Task.Run`으로 돌리고 FileLogger에 통과/실패를 남기는 방식.
- **`app.manifest`는 `requireAdministrator`** — 위 "관리자 권한" 규칙.

## 이 Phase에서 손대지 않는 것

- 기존 View·ViewModel(`ShopSetup*`, `ReaderSetup*`, `PaymentScreen*`)과 그 하네스 — Phase 39.
- 결제 알림창(`PaymentNoticeWindow`), 홈 화면, KioskSim.

## 위험 · 미확정

| # | 항목 | 대응 |
|---|---|---|
| 1 | `AllowsTransparency=True`가 Win7/소프트웨어 렌더링에서 느리거나 깜박임 | 창이 작아 영향 작다고 판단. Win7 실기가 없으면 "미확인"으로 기록. 문제 시 그림자 없는 대체 경로(D1만 철회) |
| 2 | 진입 애니메이션(D4)이 원격 데스크톱/저사양에서 끊김 | 140ms로 짧게. `SystemParameters.ClientAreaAnimation == false`면 애니메이션 생략 |
| 3 | owner가 최소화·트레이 상태(홈 화면이 트레이로 내려간 결제 중)일 때 알림창 위치 | owner가 보이지 않으면(`WindowState.Minimized` 또는 `!IsVisible`) 화면 중앙. 갤러리에서 재현 |
| 4 | 원본 캡처가 소스 수치와 다름 | 캡처 기준으로 PRD §2 수정 후 반영(CLAUDE.md 원칙) |
| 5 | 벡터 심볼(D2)이 원본 글자 심볼과 인상이 다름 | P38-5 육안 대조에서 사용자 판단. 필요하면 글자 심볼로 되돌릴 수 있게 아이콘을 한 컨트롤(`AlertIcon`)에 가둔다 |

## 체크포인트

| 체크포인트 | Task | 성격 |
|---|---|---|
| **CP1** | P38-1 ~ P38-4 | 컴포넌트 전체. 계약 타입의 WPF 비참조, 키·결과 매핑, 리소스 병합, owner/스레드 처리, 하네스 재실행 |
| — | P38-5 | 원본 캡처 대조 + 사용자 육안 확인 · 문서 확정(Opus + 사용자) |

## Task별 담당 · 모델

| Task | 담당 | 모델 | 위임 단위 | 판단 이유 / 위임 프롬프트에 넣을 근거 |
|---|---|---|---|---|
| P38-1 계약 타입·순수 로직 | **`csharp-wpf-developer`** | **Sonnet** | 단독 | 입력·출력이 PRD §3.1·§3.4 표로 완결되는 순수 로직 — 판단 여지 적음. 근거: PRD §3.1·§3.4·§5.1, 이 절 P38-1 |
| P38-2 토큰·스타일·창 외형 | **`csharp-wpf-developer`** | **Opus** | P38-2+3 한 번에 | 그림자·반지름·간격·벡터 아이콘 굵기처럼 **보이는 품질**이 결과이고 근거가 원본 캡처라 명세로 완결되지 않는다 — 캡처를 보고 조정하는 판단이 필요. 근거: PRD §2 전체·§3.2·§3.3, `screenshots/{Error,Warning,Success,Question}.png`, 이 절 P38-2/P38-3, P38-1 결과 요약(타입 이름) |
| P38-3 창 동작 | 〃 | **Opus** | (위와 함께) | 외형과 같은 코드비하인드(애니메이션·DragMove·포커스 링)에 얽혀 있어 나누면 같은 파일을 두 에이전트가 고친다 |
| P38-4 진단 하네스 | **`csharp-wpf-developer`**(새 에이전트) | **Sonnet** | 단독 | 기존 하네스 패턴(`App.xaml.cs` 체인, `*SelfTest`) 복제 수준. 근거: PRD §6, 이 절 P38-4, P38-1~3 결과 요약 |
| CP1 | **`checkpoint-reviewer`** | Opus | P38-1~4 | 중립 프롬프트: diff 범위(Phase 38 착수 커밋..HEAD + 미커밋), PRD §3·§5·§6, 이 절 CP1 항목 |
| CP1 결함 수정 | 해당 Task 에이전트(SendMessage) → Opus 재확인 | Task 모델 그대로 | — | 리뷰어 지적 원문 |
| P38-5 원본 대조·확정 | **Opus 직접 + 사용자** | Opus | — | 디자인 육안 판단은 사용자, 캡처 판독·PRD 확정은 Opus. 조정이 필요하면 P38-2 에이전트에 SendMessage |

---

## P38-1. `ViewModels/Alerts/` — 계약 타입 · 순수 로직

| 파일(제안) | 내용 |
|---|---|
| `ViewModels/Alerts/AlertKind.cs` | `enum AlertKind { Info, Warning, Error, Success, Question }` |
| `ViewModels/Alerts/AlertButtons.cs` | `enum AlertButtons { OK, OKCancel, YesNo }` |
| `ViewModels/Alerts/AlertResult.cs` | `enum AlertResult { OK, Cancel, Yes, No }` |
| `ViewModels/Alerts/AlertMessage.cs` | 불변 클래스 `AlertMessage(AlertKind Kind, string Text)` — net48/C# 버전에 맞춰 record 또는 sealed class. `Text` null → 빈 문자열 |
| `ViewModels/Alerts/AlertTextSplitter.cs` | `(string Title, string Body) Split(string? text)` — 첫 `\r\n` 또는 `\n`에서 나눔. 본문의 나머지 `\r\n`은 `\n`으로 정규화. 끝 공백·줄바꿈 트림. 첫 줄이 비어 있으면(`"\n본문"`) 본문 첫 줄을 제목으로 올림 |
| `ViewModels/Alerts/AlertBehavior.cs` | 순수 매핑: `ButtonsFor(AlertButtons)` → 표시 순서대로 `(AlertResult Result, string Label, bool IsPrimary)` 목록(D3 확정 — **주 버튼 왼쪽**: [확인][취소], [예][아니요]), `ResultForEnter(AlertButtons)`(주 버튼), `ResultForEscape(AlertButtons)`(OK→OK, OKCancel→Cancel, YesNo→No — Alt+F4도 같음), `SoundFor(AlertKind)` → `AlertSound { None, Exclamation, Hand }`(경고→Exclamation, 오류→Hand) |

**규칙**
- 이 폴더는 `System.Windows`·`System.Media`를 참조하지 않는다(알림음도 enum으로만 표현, 재생은 View).
- 버튼 문구: OK=`확인`, Cancel=`취소`, Yes=`예`, No=`아니요`(원본은 `아니오` — 표준어로 정정, 2026-10-01 사용자 확정).

**완료 조건**
- [x] 빌드 경고 0 증가
- [x] `Split` 케이스: `"제목"`, `"제목\n본문"`, `"제목\r\n본문1\r\n본문2"`, `""`, `null`, `"\n본문"`, `"제목\n"` — 결과를 P38-4 self에 넣을 수 있게 보고
- [x] `ResultForEscape(YesNo) == No`, `ResultForEnter(YesNo) == Yes`
- [x] `grep "System.Windows" ViewModels/Alerts` 0건

**검증 기록(2026-10-01)** — Sonnet 구현, 리플렉션으로 Split 8케이스·매핑 확인 보고. Opus가 diff 직접 읽음(정규화·첫 빈 줄 처리·D3 순서·`아니요`·WPF 비참조) + `dotnet build` 재실행(경고 0·오류 0). 지적 없음.

### Opus 확인 포인트 (P38-1)
- `\r\n`과 `\n`이 섞인 문자열에서 제목에 `\r`이 남지 않는지.
- 버튼 순서가 PRD §3.3 D3 확정안(주 버튼 왼쪽)과 같은지.

## P38-2. 토큰 · 스타일 · 창 외형 (`csharp-wpf-developer`, **Opus**)

| 파일(제안) | 내용 |
|---|---|
| `Themes/Colors.xaml` | `AlertInfoBg/Fg`, `AlertWarningBg/Fg`, `AlertErrorBg/Fg`, `AlertSuccessBg/Fg`(Color + Brush, PRD §3.2 표). 기존 키는 건드리지 않는다 |
| `Themes/Alert.xaml` | 알림창 치수(PRD §3.2 — 폭 376, 여백 24, 아이콘 40, 아이콘→텍스트 16, 버튼 간격 14, 본문 줄 높이 20, 본문 최대 높이 320), 주 버튼(기존 `PrimaryButtonStyle` 기반, **높이 40**), 보조 버튼(`AlertSecondaryButtonStyle` — #F2F4F6/#E6E8EC/#DCDEE2, 글자 #4E5968, 테두리 없음), 포커스 링(D5). 컴팩트 모드는 폰트만 바뀌므로 별도 파일 없이 기존 Typography 키를 참조 |
| `App.xaml.cs` | 병합 목록에 `Alert.xaml` 추가(Buttons 뒤) |
| `Views/Dialogs/AlertIcon.xaml(.cs)` | 종류별 원(40, 파스텔) + 벡터 심볼(`Path`, 선 2.5, 둥근 끝, 심볼 영역 약 20). `Kind` 의존 속성 하나로 색·모양 전환. **아이콘 관련 코드는 이 컨트롤 밖에 두지 않는다**(위험 #5) |
| `Views/Dialogs/AlertDialog.xaml` | `WindowStyle=None`, `AllowsTransparency=True`, `Background=Transparent`, `ResizeMode=NoResize`, `SizeToContent=WidthAndHeight`. 바깥 투명 여백(그림자용) 안에 카드 `Border`(반지름 12, 흰 배경, 1px #E5E8EB, `DropShadowEffect` PRD §3.3 D1). 레이아웃 PRD §3.2 — 아이콘 왼쪽, 제목·본문 오른쪽, 본문 없으면 제목을 아이콘 세로 중앙, 본문 `ScrollViewer`(최대 320, 넘칠 때만 스크롤바), 버튼 줄(1개면 전체 폭, 2개면 균등) |
| `ViewModels/Alerts/AlertDialogViewModel.cs` | `Title`, `Body`, `HasBody`, `Kind`, `Buttons`(P38-1 목록), `SelectCommand(AlertResult)`, `Result`, `CloseRequested` 이벤트. CommunityToolkit.Mvvm 사용. WPF 타입 금지 |

**규칙**
- 색·치수는 리소스 키로만 쓰고 XAML에 리터럴을 흩뿌리지 않는다.
- 원본 캡처가 있으면 **캡처를 보고** 간격·크기를 맞추고, PRD 수치와 다르게 맞춘 것은 보고에 목록으로 적는다
  (Opus가 PRD를 고친다).
- 개선안(D1~D8) 중 사용자가 철회한 것은 적용하지 않는다(착수 전 확정 사항 2).

**완료 조건**
- [x] 5종 각각의 캡처(본문 있음/없음) — `mcp__windows__windows_screenshot`, 경로 보고
- [x] 제목만 있는 알림창에서 제목이 아이콘 세로 중앙
- [x] 아주 긴 본문(예외 스택 수준)에서 창이 화면을 넘지 않고 본문만 스크롤
- [x] 컴팩트 모드에서 폰트가 Malgun Gothic — P38-5에서 `--alert-gallery compact`(진단용 강제 컴팩트)로 사용자 확인

## P38-3. 창 동작 (`csharp-wpf-developer`, **Opus**, P38-2와 한 번에)

| 파일(제안) | 내용 |
|---|---|
| `Views/Dialogs/AlertDialog.xaml.cs` | 코드비하인드는 창 핸들에 묶인 것만: `DragMove`(버튼 위 제외), 기본 버튼 포커스, Enter/ESC/Alt+F4 → `AlertBehavior` 결과, Ctrl+C(제목 + 빈 줄 + 본문을 클립보드, 실패해도 예외 미전파), 열릴 때 알림음(`SystemSounds`, `AlertBehavior.SoundFor`), 진입 애니메이션(D4, `SystemParameters.ClientAreaAnimation == false`면 생략) |
| `Views/Dialogs/AlertDialog.cs`(정적 헬퍼, 파일명 조정 가능) | `AlertResult Show(Window? owner, string text, string caption, AlertKind kind, AlertButtons buttons = OK)` — PRD §3.4 표 전부: UI 스레드 확인(`Dispatcher.CheckAccess`, 아니면 `Invoke`), owner가 닫혔거나 닫히는 중이면 띄우지 않고 `ResultForEscape` 반환, owner가 최소화·숨김이면 화면 중앙, `ShowInTaskbar = owner == null`, `Title = caption`, `Topmost` 설정 안 함 |

**완료 조건**
- [x] OK/OKCancel/YesNo 각각 Enter·ESC·Alt+F4·버튼 클릭 결과가 PRD §3.4와 같다(갤러리 결과 표시로 확인)
- [x] 경고·오류만 알림음 — 에이전트는 청취 불가, P38-5 사용자 확인("다 잘 나온다")
- [x] 백그라운드 스레드에서 `Show` 호출해도 예외 없이 뜬다
- [x] owner를 닫은 뒤 호출 → 창이 뜨지 않고 기본 결과 반환


**검증 기록(2026-10-01, P38-2+3)** — `csharp-wpf-developer`(Opus) 구현. 원본 문구 4종을 띄워 카드 기준 픽셀 대조: 카드 높이(196/195), 제목·본문 글자 y, 버튼 윗변 y, 텍스트 x=81, 아이콘 (24,24)이 원본과 1px 이내. Success는 D8(줄 높이 20)로 236(원본 226, 의도). owner 중앙 정렬 실측 일치. Opus가 diff 직접 읽음(`AlertDialog.Show.cs`·`AlertDialog.xaml.cs`·`AlertDialogViewModel`) + 캡처 열람 + `dotnet build` 재실행(경고 0·오류 0) + exe 임베딩 매니페스트 `requireAdministrator` 확인. 결함 없음.
- **PRD 수치와 다르게 맞춘 값**(원본 캡처 정렬 목적, P38-5에서 PRD 반영): 카드 안쪽 여백 `23,23,23,25`(테두리 포함 24/26), 텍스트 묶음 위쪽 -2 보정, 텍스트→버튼 64(본문 있음)/65(제목만), 아이콘 선 두께 2.0(원본 ×·✓ 약 2px, !·?는 원본이 약 3px로 더 굵음 — P38-5 판단), 제목 줄 높이 20, 그림자 여백 `32,24,32,40`, 버튼 좌우 여백 24(원본 캡처 26 → 버튼 폭 3px 넓음).
- **구현 중 결정 → 사용자 확정(2026-10-01, PRD §3.4 반영)**: ① owner 최소화·숨김이면 다른 창 뒤에 뜸 → 작업 표시줄 깜빡임(`FlashWindowEx`), 판정은 `GetForegroundWindow() != hwnd`(WPF `IsActive`는 포그라운드 거부에도 true라 쓸 수 없음 — 실측) ② Enter = 포커스된 버튼 ③ owner `Closing` 처리기 안에서는 띄움 ④ 최소화·숨김 owner → owner 미지정 + 작업 표시줄 표시.
- **남은 확인**: 깜빡임 단독 재현(검증 중 다른 갤러리 인스턴스가 함께 떠 있어 어느 창의 깜빡임인지 분리 못 함) → P38-5.

### Opus 확인 포인트 (P38-2+3)
- 캡처를 직접 열어 원본 캡처와 나란히 비교(간격·글자 크기·색).
- 투명 여백 포함 창 크기 때문에 owner 중앙 배치가 카드 기준으로 맞는지.
- `ViewModels/Alerts/AlertDialogViewModel`에 WPF 타입이 없는지.
- 코드비하인드에 비즈니스 판단(결과 매핑)이 들어가지 않고 `AlertBehavior`를 호출만 하는지.
- `DropShadowEffect`가 카드 `Border`에만 걸려 텍스트까지 비트맵 효과로 흐려지지 않는지(WPF는 Effect가 걸린 요소 하위 텍스트 렌더링이 흐려질 수 있음 — 그림자를 별도 배경 `Border`에 두는 패턴인지).

## P38-4. 진단 하네스 (`csharp-wpf-developer`, **Sonnet**)

`App.xaml.cs` 진단 인자 체인에 추가:

| 인자 | 동작 |
|---|---|
| `--alert-gallery` | 갤러리 창 하나: 버튼 목록(5종 × {제목만, 제목+본문 1줄, 제목+본문 여러 줄(리더기 결과 문구 형식), 아주 긴 본문} × {OK, OKCancel, YesNo})을 누르면 해당 알림창을 갤러리 창을 owner로 띄우고, 반환된 `AlertResult`를 갤러리 화면 하단에 표시. "백그라운드 스레드에서 띄우기", "owner 없이 띄우기" 버튼 포함. 문구는 PRD §4.2 실제 문구를 쓴다 |
| `--alert-dialog-test self` | P38-1 완료 조건 전부를 `Check(...)`로 자동 판정(기존 `*SelfTest` 출력 방식, FileLogger 통과/실패). 창을 띄우지 않는다 |

**완료 조건**
- [x] `self` 전부 통과(에이전트 보고 + Opus 재실행)
- [x] 갤러리에서 모든 조합이 뜨고 결과가 표시된다
- [x] 인자 오류 → 사용법 로그 후 정상 종료

**검증 기록(2026-10-01, P38-4)** — `csharp-wpf-developer`(Sonnet) 구현: `AlertDialogSelfTest`(검사 30개 + 종합 1줄, CP1 L-1 후 33개), 갤러리에 조합 선택(종류·본문·버튼 콤보)과 PRD §4.2 실제 문구 14행 버튼 추가. Opus가 diff 직접 읽음(`App.xaml.cs` 변경은 병합 1줄 + 진단 인자 2개) + 로그에서 `[Phase38 알림창] 완료 … 종합=통과`, 실패(★) 0건 확인.

---

## CP1 확인 항목 (`checkpoint-reviewer`에 그대로 전달)

1. `dotnet build` 성공, `--alert-dialog-test self` 전부 통과를 직접 재실행으로 확인.
2. 계층: `ViewModels/Alerts/`가 `System.Windows`·`System.Media`를 참조하지 않음.
3. Enter/ESC/Alt+F4/버튼 → 결과 매핑이 PRD §3.4 표와 일치(특히 YesNo의 ESC = `No`).
4. `AlertDialog.Show`: UI 스레드 마샬링, owner 닫힘 처리, Topmost 미사용, owner 없음 처리.
5. 리소스 병합 순서가 `StaticResource` 해석 순서를 지키는지(Alert.xaml이 참조하는 키가 앞에서 병합됨).
6. 색·치수가 PRD §3.2·§3.3(확정분)과 일치, XAML 리터럴 산재 여부.
7. 기존 화면·ViewModel·하네스 diff 없음(`App.xaml.cs` 진단 인자·병합 추가 제외).
8. 예외 경로: 클립보드 실패, 알림음 실패가 창 동작을 깨지 않음.

---

## CP1 결과 (2026-10-01, `checkpoint-reviewer` 독립 검증 → Opus 직접 수정·재확인)

판정: **통과**(H·M 0건). 리뷰어가 빌드(일반·`--no-incremental`), x86 리플렉션으로 `AlertDialogSelfTest.RunAll`, 추가 Split 경우,
exe 임베딩 매니페스트를 직접 재실행했다. 앱 실행 GUI 동작(키보드·Alt+F4·Closing 안 표시·깜빡임·알림음·컴팩트·Win7)은 UAC/UIPI로
실측 못 함 → P38-5.

| # | 지적 | 조치(Opus 직접, 몇 줄 수정) |
|---|---|---|
| L-1 | self에 `

`/`
` 혼합·`
`만 입력 케이스 없음 | `AlertDialogSelfTest`에 3케이스 추가 — 재실행 통과(14:55) |
| L-2 | 종료 중 Dispatcher의 `Invoke`가 `default`=OK 반환 가능, Dispatcher 없는 MTA 스레드에서 STA 예외 | `AlertDialog.Show`: `HasShutdownStarted`면 ESC 결과, Dispatcher 없고 STA 아니면 WARN 로그 + ESC 결과 |
| L-3 | 포커스 링 바깥 반지름 10(버튼 8 + 3 = 11이어야 동심) | `Alert.xaml` 11로 수정. 나머지 리터럴(그림자 방향·스크롤바 치수)은 토큰 파일 안이라 유지 |
| L-4 | `--alert-gallery` 주석이 끝난 확장을 앞일로 서술, 검사 수 표기, `AlertBehavior` 주석의 `cref="System.Media"` | 주석 정정, `<c>System.Media</c>`로 교체(grep 오탐 제거) |

**Phase 39 참고(리뷰어)**: owner 핸들이 0이면 "닫힘"으로 판정하므로 **한 번도 표시되지 않은 owner**에서 호출하면 알림 없이 기본
결과가 나온다 — Phase 39에서 창 생성자·표시 전 단계에서 `Show`를 부르는 호출부가 없는지 확인한다.

---

## P38-5. 원본 캡처 대조 · 확정 (Opus 직접 + 사용자)

1. 갤러리 캡처(Error·Warning·Success·Question, 원본 캡처와 같은 문구)를 원본 캡처와 나란히 놓고 Opus가 차이 목록 작성
   (의도한 개선 D1~D8 / 의도하지 않은 차이 구분).
2. 사용자 육안 확인 — 의도하지 않은 차이는 P38-2 에이전트에 SendMessage로 조정.
3. 문서 갱신: PRD §2(캡처 기준 수치), §3.2·§3.3(확정값, "제안" 제거), §7 #8·#11 해소, ROADMAP Phase 38 체크.
4. `app.manifest` = `requireAdministrator` 확인(임베딩 확인 포함).


**실행 결과(2026-10-01, Opus + 사용자)** — 사용자가 관리자 권한 갤러리를 직접 조작해 확인, Opus는 전체 화면 캡처로 판독하고
수정은 몇 줄이면 직접, 구조 변경은 P38-2+3 에이전트(Opus)에 이어서 맡겼다. 사용자 피드백과 조치:

| # | 사용자 피드백 | 원인 | 조치 |
|---|---|---|---|
| 1 | "글자가 깨진 느낌" | ① 투명 창(`AllowsTransparency`)에서 WPF가 ClearType을 끔 ② 알림창에만 지정한 `TextFormattingMode=Display`가 획을 가늘게 붙이고 색 번짐을 키움 | ① 흰 카드·본문에 `ClearTypeHint=Enabled`, 짧은 본문은 스크롤 영역 밖(스크롤 클리핑이 ClearType 약화) ② 변형 4종(A 현재 / B 투명+Ideal / C 일반창+Ideal / D 일반창+Display)을 사용자가 비교 → **B 확정**, `Display` 제거. 수치: 제목 가장자리 폭 원본 1.45px / A 0.86 / B 1.41 |
| 2 | 제목이 한 줄을 넘으면 안 됨, 말줄임표 쓰지 않음 | 긴 첫 줄 문구 | 제목 `NoWrap`(말줄임 없음), 문구를 짧은 제목 + 본문으로(PRD §4.4) — Phase 39 하네스가 제목·본문 줄 폭 검증 |
| 3 | #12·#14 본문이 문장 중간에서 넘어감 | WPF가 한글을 글자 단위로 줄바꿈 | 어절 단위 줄바꿈(`AlertWordWrapper`, keep-all, self 13케이스) + #12·#14 문구 축약 |
| 4 | 컴팩트 모드에서 열릴 때 글자가 잠깐 흐림 | 진입 애니메이션(반투명·확대) 동안 ClearType 불가 | **D4 철회**(애니메이션 제거, 원본처럼 즉시 표시) |
| 5 | #3 문구가 개발자스러움 | 예외 메시지를 그대로 표시 | `설정 저장 실패
설정을 저장하지 못했습니다.
다시 시도하고, 반복되면 담당자에게
문의해주세요.` + 예외는 로그 ERROR(Phase 39) |
| — | "닫힌 owner로 호출"이 무엇인지 | 갤러리 특수 버튼 | 설명(결제창이 닫힌 뒤 도착한 전표 출력 실패 경고 상황 — 창 없이 기본 결과) |

- 진단용 추가: `--alert-gallery compact`(화면 높이와 무관하게 컴팩트 리소스, `App.xaml.cs`).
- 사용자 최종 확인: "다 잘 나온다"(일반·컴팩트 두 모드). 아이콘 `!`·`?` 굵기(원본보다 가늚)는 별도 지적 없어 선 두께 2.0 유지.
- **미확인**: Win7 실기(없음), 작업 표시줄 깜빡임 단독 재현은 사용자 조작으로만 확인 가능(별도 보고 없음 — "다 잘 나온다"에 포함된 것으로 기록).

**Phase 39로 넘기는 것**: 제목·본문 줄 폭 자동 검증(Pretendard·Malgun Gothic 두 글꼴), §4.2 문구 반영(#1·#3·#6·#12·#14),
#3 저장 실패 예외 로그 ERROR, receipt_print PRD §5 문구 갱신, 표시 전 owner에서 `Show` 호출부 금지(CP1 참고).

**완료 조건**
- [x] 사용자 확인 완료
- [x] PRD 확정 반영
- [x] 매니페스트 원복 확인

---

## Phase 38 진행 순서

**(착수 전)** PRD §7 #8 사용자 확정 + 원본 캡처 도착 → **P38-1**(Sonnet) → Opus 검증 → **P38-2+3**(Opus 에이전트)
→ Opus 검증 → **P38-4**(Sonnet) → Opus 검증 → **CP1**(`checkpoint-reviewer`) → 결함 수정·재확인 → **P38-5**(Opus + 사용자).

---

# Phase 39 — 기존 호출부 교체

**이 Phase가 끝나면**: 앱 안의 `MessageBox.Show`가 0건이고, PRD §4.2의 14행이 표에 적힌 종류·버튼·문구로 실제 화면에 뜬다.
세 ViewModel은 `AlertRequested`(`EventHandler<AlertMessage>`) 하나로 알리고, 모든 고정 문구의 제목·본문 줄 폭을 하네스가
자동으로 검증한다.

> **실수가 나기 쉬운 곳**: ① 이벤트 시그니처를 바꾸면 VM·View·기존 하네스가 **한 컴파일 단위로 같이 깨진다** — 나눠 맡기면
> 중간 빌드가 실패한다(그래서 P39-1~3을 한 위임으로 묶었다). ② 기존 하네스의 문구 규칙 `!reprint.Contains("\n")`(재출력 문구는 한 줄)이
> 새 형식(`제목\n사유`)과 정면으로 충돌한다 — 기대값만 바꾸지 말고 **규칙 자체를 다시 쓴다**. ③ 하네스의 문구 기대값은 구현 상수를
> 참조하지 않고 **PRD 문구를 리터럴로 적는다**(기존 원칙 — 상수가 틀려도 잡히게). 반대로 P39-4 줄 폭 검증은 **실제 상수·빌더를 열거**한다
> (실제로 화면에 나가는 문구를 재야 하므로). 두 원칙을 섞지 않는다. ④ 리더기 결과의 "처리 종료" 로그를 알림 **전에** 남기는 순서(R-9,
> `ReaderSetupViewModel.cs:228` 등 4곳)를 이벤트 교체 중에 뒤집지 않는다. ⑤ `--alert-dialog-test self`는 `Task.Run`에서 돈다 —
> `FormattedText` 측정과 `PretendardFontFamily` 리소스 조회는 UI 스레드(`Application.Current.Dispatcher.Invoke`)에서 한다. ⑥ 컴팩트 모드는
> `PretendardFontFamily` 리소스 자체를 Malgun Gothic으로 바꿔 끼우므로, 일반 실행 중의 리소스만 재면 Malgun Gothic 폭은 검증되지 않는다 —
> 두 글꼴을 **명시적으로** 각각 잰다.

## 착수 전 확정 사항

1. **PRD §4.2 재분류 14행, §4.3 이벤트 통합(`AlertRequested`), §4.4 문구(#1·#3·#6·#12·#14)** — 2026-10-01 사용자 확정.
2. **사유 문장이 없는 전표 출력 실패**(`PrinterError`·`CompositionFailed`·`Unexpected`) = **제목만** — 2026-10-02 사용자 확정(PRD §4.4).
   #6 `전표 출력 실패`, #14 `전표 출력 실패\n가맹점 설정에서 다시 출력하세요.`
3. **문구 출처** = 각 화면 ViewModel. View에 리터럴로 있던 #7·#12·#13 문구를 VM `internal const`로 옮긴다 — 2026-10-02 사용자 확정(PRD §4.2).
   중앙 문구 카탈로그는 두지 않는다.
4. **E2E** = `asInvoker` 일시 해제 + 에이전트 자동 조작·캡처, 리더기 실기, **#10 키다운로드 성공 포함**(IPEK 1회 소모 수용, 실행 직전
   재확인) — 2026-10-02 사용자 확정(PRD §6).
5. **#3 저장 실패 예외 로그** — 카테고리 `SETTINGS`, 레벨 ERROR, **code는 `null`**(기존 `가맹점 설정 저장` INFO 로그와 같음). 새 code 슬롯을
   만들면 `docs/operations/fault_alert_catalog.md`부터 고쳐야 하는 별도 결정이라 이 Phase에서는 만들지 않는다(Opus 기본값 — 사용자가 다르게
   원하면 착수 전 변경).

## 착수 전 전제 (코드 실측, 2026-10-02)

- **`MessageBox.Show` 7곳** — `ShopSetupWindow.xaml.cs:85`(Warning 고정)·`:90`(Info)·`:183`(질문), `ReaderSetupWindow.xaml.cs:117`(Info 고정)·
  `:210`(#12)·`:285`(질문), `PaymentScreenWindow.xaml.cs:87`(Warning). KioskSim 3곳은 범위 밖.
- **이벤트** — `ShopSetupViewModel.ResultMessageReady`(`:97`)·`InfoMessageReady`(`:102`), `ReaderSetupViewModel.ResultMessageReady`(`:209`),
  `PaymentScreenViewModel.ReceiptPrintWarningRequested`(`:108`). 모두 `EventHandler<string>`.
- **문구 빌더** — `PaymentScreenViewModel.BuildReceiptPrintFailureMessage`/`BuildReceiptPrintWarningMessage`/`ReprintHintLine`(`:172-202`, `internal`),
  `ReaderSetupViewModel.BuildMessage`/`BuildKeyDownloadMessage`(`:374`, `:346`, `private static`, `string` 반환).
- **하네스 구독처** — `LastReceiptReprintSelfTest.cs:293`(④)·`:410-431`(⑥)·`:454-525`(⑦), `NationalTaxReceiptAssemblerSelfTest.cs:245`(기대 안내 줄 상수)·
  `:248-270`(⑧)·`:331-362`(⑪). 기대 문구는 PRD 리터럴.
- **`ShopSetupViewModel` 저장** — `_settingsService = new()`(`:41`)가 필드에 고정돼 저장 실패를 주입할 수 없다(레지스트리는 HKCU라 `asInvoker`로도
  실패하지 않는다). #3을 self로 검증하려면 주입점이 필요하다.
- **알림창 측정 기준** — 본문 줄바꿈 측정은 `AlertDialog.ApplyKeepAllWrap`(`AlertDialog.xaml.cs:48-67`)이 본문 `TextBlock`과 같은 글꼴·크기·
  `TextFormattingMode`의 `FormattedText.WidthIncludingTrailingWhitespace`로, 폭 `ActualWidth − 1`. 제목 15 Bold·본문 13 Regular
  (`Themes/Alert.xaml:42-47`), 텍스트 영역 폭 272(= 376 − 80 − 24, PRD §4.4).
- **결제창 VAN 스텁** — `Services/Van/StubVanRelayService.cs`(Phase 37). #14 E2E에서 902614 승인을 받는 경로.
- **`app.manifest` = `requireAdministrator`**.

## 이 Phase에서 손대지 않는 것

- 알림창 컴포넌트 자체(`Views/Dialogs/AlertDialog*`, `AlertIcon`, `Themes/Alert.xaml`, `ViewModels/Alerts/` 기존 타입) — Phase 38에서 사용자 확인 완료.
  결함이 보이면 고치지 말고 Opus에 보고한다(사용자 확인을 다시 받아야 하므로).
- 결제 알림창(`PaymentNoticeWindow`), 홈 화면의 거래 중 설정 진입 "조용히 무시"(PRD §1.2), KioskSim.
- 문구 뜻 — PRD §4.4에 적힌 것 외에는 바꾸지 않는다.

## 위험 · 미확정

| # | 항목 | 대응 |
|---|---|---|
| 1 | 이벤트 시그니처 변경이 VM·View·하네스를 동시에 깬다 | P39-1~3을 한 위임으로. 위임 결과 보고 전에 빌드 성공을 확인 |
| 2 | 줄 폭 측정값(`FormattedText`)과 실제 `TextBlock` 배치가 1px 안팎 다를 수 있다 | 본문 한도는 줄바꿈 래퍼와 같은 `폭 − 1`, 제목은 272. 한도 2px 이내로 붙는 문구는 로그에 "경계" 표시 → Opus가 E2E 캡처로 확인 |
| 3 | 리더기 결과 본문에 길이를 모르는 값(DLL 오류 `detail`, 통신 오류 상세, 예외 메시지)이 들어간다 | 고정 부분·길이가 정해진 값(응답코드 2자리 등)만 실패로 판정. 길이 미정 값이 든 줄은 **측정값만 로그(정보)** — 어절 단위 줄바꿈이 안전장치 |
| 4 | #3(저장 실패)·#5(직전 영수증 없음)는 실화면 재현 수단이 마땅치 않다 | #3: VM 주입점으로 self 검증 + 갤러리, 실화면은 "재현 불가"로 기록. #5: 저장된 영수증이 이미 있으면 **DB를 지우거나 옮기지 않고** self(⑦b) + 갤러리로 대체 |
| 5 | #10 키다운로드 성공은 IPEK을 소모하고, VAN은 mode=R에서만 응답한다(OT 미도달) | 실행 직전 사용자에게 다시 확인. 실패 시 재시도하지 않고 결과만 보고(재시도 = IPEK 추가 소모 가능) |
| 6 | `asInvoker`로 낮춘 매니페스트를 원복하지 않은 채 커밋 | P39-6 체크 항목 + exe 임베딩 매니페스트 확인. 실행 중인 `KFTCTaxCAP.exe`가 사용자 세션 것인지 확인 없이 종료하지 않는다 |
| 7 | CP1(Phase 38) 참고 — 한 번도 표시되지 않은 owner로 `AlertDialog.Show`를 부르면 알림 없이 기본 결과 | 세 화면의 알림 발생 시점(버튼·명령·출력 완료)이 모두 창 표시 뒤인지 Opus가 diff로 확인. 리더기 설정 워밍업 인스턴스는 알림을 올리지 않는지 확인 |

## 체크포인트

| 체크포인트 | Task | 성격 |
|---|---|---|
| **CP1** | P39-1 ~ P39-4 | 코드 변경 전체. 이벤트 계약·종류 재분류·문구·호출부·하네스·줄 폭 검증. 빌드 + `--alert-dialog-test self` + `--receipt-print-test self` 재실행 |
| — | P39-5 | 실제 화면 E2E(에이전트 자동 조작, Opus 캡처 판독) |
| — | P39-6 | 문서·매니페스트 마무리(Opus 직접) |

## Task별 담당 · 모델

| Task | 담당 | 모델 | 위임 단위 | 판단 이유 / 위임 프롬프트에 넣을 근거 |
|---|---|---|---|---|
| P39-0 receipt_print PRD §5 갱신 | **Opus 직접** | Opus | — | 문서 몇 줄. "PRD를 먼저 고친다" — 코드 착수 전에 끝낸다 |
| P39-1 VM 이벤트 계약·문구·종류 | **`csharp-wpf-developer`** | **Sonnet** | **P39-1+2+3 한 번에** | 입력(PRD §4.2 표)·출력(이벤트 계약)이 명세로 완결. 근거: PRD §4 전체, 이 절 P39-1~3, `ViewModels/Alerts/AlertMessage.cs` |
| P39-2 View 7곳 교체 | 〃 | **Sonnet** | (위와 함께) | 공통 규칙 표는 이 Task를 Haiku 예로 들었지만, **이벤트 시그니처 변경이 VM·View·하네스를 한 컴파일 단위로 묶어** 나누면 위임 사이 빌드가 깨진다. View 쪽은 7곳·소량이라 Haiku로 떼어 얻는 이득이 별도 위임·검증 비용보다 작다 |
| P39-3 기존 하네스 갱신 | 〃 | **Sonnet** | (위와 함께) | 같은 이유(하네스가 바뀐 이벤트를 구독). 규칙 재작성(⑥)이 있어 단순 치환이 아니다 |
| P39-4 줄 폭·종류 자동 검증 + 갤러리 실문구 | **`csharp-wpf-developer`**(새 에이전트) | **Sonnet** | 단독 | 측정 방식은 `ApplyKeepAllWrap` 재사용, 대상 목록·판정 규칙은 이 절로 완결. 근거: PRD §4.4·§6, 이 절 P39-4, P39-1~3 결과 요약(상수·빌더 이름) |
| CP1 | **`checkpoint-reviewer`** | Opus | P39-1~4 | 중립 프롬프트: diff 범위(Phase 39 착수 커밋..HEAD + 미커밋), PRD §4·§6, receipt_print PRD §5, 이 절 CP1 항목 |
| CP1 결함 수정 | 해당 Task 에이전트(SendMessage) → Opus 재확인 | Task 모델 그대로 | — | 리뷰어 지적 원문. 몇 줄이면 Opus 직접 |
| P39-5 실제 화면 E2E | **`csharp-wpf-developer`**(새 에이전트) | **Sonnet** | 단독 | 화면 조작·캡처는 절차가 이 절 표로 정해져 있고, 판정(종류·문구가 맞는지)은 Opus가 캡처를 직접 열어 한다. 근거: 이 절 P39-5 표, PRD §4.2, 공통 규칙 "관리자 권한" |
| P39-6 마무리 | **Opus 직접** | Opus | — | 매니페스트 원복 확인, PRD·ROADMAP 체크, 문서 확정 |

---

## P39-0. receipt_print PRD §5 문구 갱신 (Opus 직접, 코드 착수 전)

- `docs/receipt_print/PRD.md` §5 표의 표시 문구를 `전표 출력 실패\n{사유}.` 형식으로(사유 문장 = 지금 괄호 안 문구 + 마침표).
- 표 아래에 "표에 행이 없는 사유(`PrinterError` 등)는 제목만" 한 줄.
- 재출력 안내 줄을 `가맹점 설정에서 다시 출력하세요.`로, "`MessageBoxImage.Warning`으로 띄운다"를 "알림창 Warning(docs/alert_dialog)"으로.
- 근거로 `docs/alert_dialog/PRD.md` §4.4를 가리킨다.

**완료 조건**
- [x] receipt_print PRD §5와 alert_dialog PRD §4.2·§4.4의 문구가 글자 단위로 같다

**검증 기록(2026-10-02, Opus 직접)** — receipt_print PRD §5 표 6행을 `전표 출력 실패\n{사유}.`로, 표 아래에 형식·"행 없는 사유 = 제목만" 2줄,
경고창 문단을 알림창 Warning + 안내 줄 `가맹점 설정에서 다시 출력하세요.`(자동 출력 예시 2개 포함)로 고쳤다. grep으로 옛 문구
(`전표 출력에 실패`·`'직전거래 전표출력'으로`·`MessageBoxImage`)가 경위 설명 2곳 외에 남지 않음을 확인.

## P39-1. ViewModel 이벤트 계약 · 문구 · 종류 (`csharp-wpf-developer`, **Sonnet**, P39-1+2+3 한 번에)

| 파일 | 내용 |
|---|---|
| `ViewModels/ShopSetupViewModel.cs` | `ResultMessageReady`·`InfoMessageReady` 삭제 → `public event EventHandler<AlertMessage>? AlertRequested`. 문구를 `internal const`로: `TimeoutInvalidMessage`(#1 `입력값을 확인해주세요.\n카드입력 타임아웃은 30초 이상 입력해주세요.`), `InvalidInputMessage`(#2), `SaveFailedMessage`(#3 `설정 저장 실패\n설정을 저장하지 못했습니다.\n다시 시도하고, 반복되면 담당자에게\n문의해주세요.`), `PrinterPortRequiredMessage`(#4), `NoLastReceiptMessage`(#5), `DiscardChangesQuestion`(#7). 종류: #1·#2·#4·#6 Warning, #3 **Error**, #5 Info. #6은 `PaymentScreenViewModel.BuildReceiptPrintFailureMessage` 그대로 + Warning |
| 〃 (#3 로그) | `catch`에서 알림 **전에** `FileLogger.Error(LogCategory.Settings, $"가맹점 설정 저장 실패 — {ex.GetType().Name}: {ex.Message}", code: null, transactionId: null)`. 설정값·예외 스택은 남기지 않는다 |
| 〃 (주입점) | 저장 실패를 self에서 재현할 주입점 — 기존 `internal` 생성자 패턴을 따라 저장 동작(`Action<ShopSettings>` 등)을 받는 `internal` 생성자 오버로드. 운영 경로(기본 생성자)는 지금과 같은 `ShopSettingsService.Save` |
| `ViewModels/ReaderSetupViewModel.cs` | `ResultMessageReady` → `AlertRequested`(`EventHandler<AlertMessage>`). `BuildMessage`·`BuildKeyDownloadMessage`가 **`AlertMessage`를 반환**하고 종류를 결과 객체로 정한다: `ReaderCommandOutcomeKind.Success` → Success, 그 외 → Error / `KeyDownloadOutcome.IsSuccess` → Success, 그 외 → Error. 두 메서드를 `internal static`으로(P39-4가 호출). 문구 텍스트는 바꾸지 않는다. `internal const`: `DuplicatePortMessage`(#12 `리더기 설정 실패\n리더기 포트가 동일합니다.\n서로 다른 포트를 선택해주세요.`), `DiscardChangesQuestion`(#13). **R-9 순서(`LogActionBoundary` → 알림) 유지** |
| `ViewModels/Payment/PaymentScreenViewModel.cs` | `ReceiptPrintWarningRequested` → `AlertRequested`(Warning). `BuildReceiptPrintFailureMessage` = `전표 출력 실패` + (사유가 있으면 `\n{사유}.`). 사유 문장: InvalidPort `프린터 포트 설정 확인`, PortOpenFailed `프린터 포트를 열 수 없습니다`, NoResponse `프린터 응답 없음`, PaperEnd `용지 없음`, CoverOpen `프린터 덮개 열림`, WriteFailed `전송 오류`(+ 마침표), 그 외 제목만. `ReprintHintLine` = `가맹점 설정에서 다시 출력하세요.` `BuildReceiptPrintWarningMessage` = 실패 문구 + `\n` + 안내 줄 |
| 위 세 파일의 주석 | `MessageBox`·`ResultMessageReady`·`InfoMessageReady`를 언급하는 XML 주석·`cref`를 새 계약에 맞게 고친다(남은 `cref`는 빌드 경고) |

**규칙**
- ViewModel은 계속 WPF를 모른다 — `AlertMessage`/`AlertKind`(WPF 비참조, Phase 38)만 쓴다.
- 이벤트 발생 시점·순서(검증 실패 → 알림 → 포커스 요청, R-9 로그 순서, 출력 완료 후 continuation에서 알림)를 바꾸지 않는다.

**완료 조건**
- [x] 빌드 경고 0 증가(P39-1~3 합쳐서) — 전·후 모두 경고 0·오류 0
- [x] `grep "ResultMessageReady\|InfoMessageReady\|ReceiptPrintWarningRequested" src/KFTCTaxCAP` 0건
- [x] PRD §4.2 각 행의 종류가 위 표대로(Opus가 diff로 대조)

**검증 기록(2026-10-02, P39-1+2+3)** — `csharp-wpf-developer`(Sonnet)가 8개 파일 구현. 앱이 `requireAdministrator`라 exe 대신
x86 리플렉션 하네스(scratchpad의 `SelfTestHarness.exe`, 빌드 출력 사본 + `.exe.config` 바인딩 리다이렉트)로 `AlertDialogSelfTest.RunAll`·
`ReceiptPrintSelfTest.RunAll`을 실행. **Opus 확인**: diff 직접 읽음 — 리더기 4경로 R-9 순서 유지, 키다운로드 예외 catch → `ReaderFailure` → Error,
#3 로그(`SETTINGS`/ERROR, 예외 타입·메시지만, 알림 전), 저장 주입점 기본값 = 기존 `_settingsService.Save`, 결제창 `_isClosed || !IsLoaded` 가드 유지,
질문창 `== AlertResult.Yes`, 하네스 기대 문구 리터럴·⑥ 규칙 재작성(두 줄/한 줄/접미사) 확인. `ShopSetupViewModel.AlertRequested` 요약 주석의
틀린 문장("View는 MessageBox를…") 1줄 Opus 직접 수정. 재빌드(경고 0·오류 0) 후 리플렉션 하네스 **Opus 재실행**(09:41) — 로그
`KFTCTaxCAP261002.log`에서 alert-dialog 종합=통과, receipt-print 종합=통과(⑥ 18건·⑦·⑧ 9건·⑪ 포함), 실패(★) 0건. grep(옛 이벤트·`MessageBox*`) 0건.
작업본 줄바꿈은 전부 CRLF로 일관(`autocrlf=true` 경고는 정상).

### Opus 확인 포인트 (P39-1)
- 리더기 4개 명령 경로(초기화·상태체크·무결성 체크·키다운로드) 모두 `LogActionBoundary`가 알림보다 먼저인지.
- `BuildKeyDownloadMessage`의 예외 경로(`KeyDownloadOutcome.ReaderFailure`로 떨어뜨리는 catch)도 Error로 가는지.
- #3 로그에 설정값(KIOSK_ID 등)·영수증 값이 섞이지 않았는지, 로그가 알림 **전**인지.
- 주입점이 운영 경로 동작을 바꾸지 않는지(기본 생성자 → 같은 `ShopSettingsService` 인스턴스).

## P39-2. View 7곳 교체 (`csharp-wpf-developer`, **Sonnet**, P39-1과 함께)

| 파일 | 내용 |
|---|---|
| `Views/ShopSetupWindow.xaml.cs` | 처리기 두 개 → `ViewModel_AlertRequested(object?, AlertMessage m) => AlertDialog.Show(this, m.Text, "가맹점 설정", m.Kind)`. 질문 → `AlertDialog.Show(this, ShopSetupViewModel.DiscardChangesQuestion, "가맹점 설정", AlertKind.Question, AlertButtons.YesNo) == AlertResult.Yes` |
| `Views/ReaderSetupWindow.xaml.cs` | `ViewModel_AlertRequested` → `"리더기 설정"`, `m.Kind`. #12 → `ReaderSetupViewModel.DuplicatePortMessage`, Warning, OK. 질문 → `DiscardChangesQuestion`, Question, YesNo, `== AlertResult.Yes` |
| `Views/PaymentScreenWindow.xaml.cs` | 구독·해제 대상만 `AlertRequested`로. **기존 가드(`_isClosed || !IsLoaded` → 띄우지 않음)와 `Dispatcher.BeginInvoke` 경로를 그대로 둔다** — `AlertDialog.Show`도 닫힌 owner를 걸러내지만 ROADMAP 완료 기준("닫힌 뒤 도착한 경고는 띄우지 않는다")의 1차 근거는 이 가드다. 캡션 `"전표 출력"` |
| 세 파일의 주석 | 클래스 요약·처리기 주석의 `MessageBox` 언급을 알림창으로 |

**완료 조건**
- [x] `grep -E "MessageBox\.Show\(|MessageBoxImage|MessageBoxResult|MessageBoxButton" src/KFTCTaxCAP` 0건
- [x] 캡션 3종(`가맹점 설정`·`리더기 설정`·`전표 출력`)이 바뀌지 않음

### Opus 확인 포인트 (P39-2)
- 질문창 결과 비교가 `== AlertResult.Yes`인지(ESC·Alt+F4 = No → 창 유지).
- 세 화면 모두 알림이 창 표시 뒤에만 발생하는지(위험 #7) — 생성자·`SourceInitialized`·워밍업 인스턴스 경로에서 알림이 올라가지 않는지.
- `ShopSetupWindow`에서 #2 알림을 닫은 뒤 포트 입력칸 포커스 요청이 그대로 동작하는 순서인지.

## P39-3. 기존 하네스 갱신 (`csharp-wpf-developer`, **Sonnet**, P39-1과 함께)

| 파일 | 내용 |
|---|---|
| `Services/Diagnostics/NationalTaxReceiptAssemblerSelfTest.cs` | `ExpectedReprintHintSuffix` = `"\n가맹점 설정에서 다시 출력하세요."`. ⑧ 기대값을 새 형식으로 — 사유 6종 `전표 출력 실패\n{사유}.` + 안내, **`PrinterError`·`CompositionFailed`·`Unexpected`는 `전표 출력 실패` + 안내**(행 추가). ⑪ 구독을 `AlertRequested`로, 판정에 **`Kind == AlertKind.Warning`** 추가 |
| `Services/Diagnostics/LastReceiptReprintSelfTest.cs` | ④ 구독 교체(경고 0회 판정 유지). ⑥ **규칙 재작성**: 재출력 문구는 `전표 출력 실패`로 시작하고 안내 줄이 없으며, 사유가 있는 6종은 정확히 두 줄 / 없는 3종은 한 줄, 자동 = 재출력 + 안내 접미사. ⑦ `NewShopSetup`이 `List<AlertMessage>` 하나를 모으고 판정에 종류 포함 — (a) Warning `프린터 포트번호를 입력해주세요.` (b) **Info** `출력할 직전 거래가 없습니다.` (d) Warning `전표 출력 실패\n프린터 포트를 열 수 없습니다.` |

**규칙**
- 기대 문구는 **PRD 리터럴**로 적는다(구현 상수 참조 금지 — 위 "실수가 나기 쉬운 곳" ③).
- 검사 항목을 지우지 않는다. 바뀐 규칙은 로그 문구도 새 규칙을 말하게 고친다.

**완료 조건**
- [x] `--receipt-print-test self` 전부 통과(로그의 종합 줄 + 실패(★) 0건) — 리플렉션 실행, 위 P39-1 검증 기록
- [x] `--alert-dialog-test self` 기존 33개 그대로 통과

### Opus 확인 포인트 (P39-3)
- ⑥이 "기대값만 바꿔서 통과"가 아니라 새 규칙(두 줄/한 줄, 안내 없음)을 실제로 검사하는지 — 일부러 틀린 문구를 넣으면 실패할 구조인지 diff로 판단.
- 하네스에 구현 상수(`ReprintHintLine` 등) 참조가 새로 생기지 않았는지.
- 재실행: `--receipt-print-test self` 1회(Opus 직접).

## P39-4. 줄 폭 · 종류 자동 검증 + 갤러리 실문구 (`csharp-wpf-developer` 새 에이전트, **Sonnet**)

`--alert-dialog-test self`에 두 묶음을 추가한다(`AlertDialogSelfTest` 확장 또는 같은 인자에서 함께 도는 새 `*SelfTest` — 에이전트 판단).

**(A) PRD §4.2 종류·문구 대조** — VM이 올리는 행을 실제 VM으로 실행해 이벤트를 받는다. 기대값은 PRD 리터럴.

| 행 | 실행 방법 | 기대 |
|---|---|---|
| #1 | `ShopSetupViewModel`에 타임아웃 `"10"` → `TryConfirm()` | false, Warning, #1 문구 |
| #2 | 타임아웃 정상 + 전표 인쇄 ON + 포트 `""` → `TryConfirm()` | false, Warning, `입력값을 확인해주세요.`, 포커스 요청 1회 |
| #3 | 저장 주입점이 예외를 던지게 → `TryConfirm()` | false, **Error**, #3 문구(예외 메시지가 문구에 **없음**) |
| #8·#9 | `ReaderSetupViewModel.BuildMessage` — 명령 3종(`초기화`·`상태체크`·`무결성 체크`) × `ReaderCommandOutcomeKind` 전부 | Success일 때만 Success, 나머지 Error. 성공 제목 `리더기 {명령} 성공` |
| #10·#11 | `BuildKeyDownloadMessage` — 성공 1 + 실패(단계 5종 × {응답코드, 단말기 교체, 응답코드 없음}) | 성공만 Success, 나머지 Error |

(#4·#5·#6·#14는 P39-3 하네스가, #7·#12·#13은 View가 띄우므로 Opus diff 확인 + P39-5 E2E가 맡는다.) **`TryConfirm`은 실패 경로만** 돌린다 — 성공 경로는 실제 레지스트리에 쓴다.

**(B) 제목·본문 줄 폭** — 실제 상수·빌더를 열거해 잰다(PRD §4.4).

- **대상**: `ShopSetupViewModel`·`ReaderSetupViewModel`의 문구 상수 전부, `BuildReceiptPrintFailureMessage`·`BuildReceiptPrintWarningMessage`(`None` 제외 사유 전부),
  `BuildMessage`(명령 3종 × 종류 전부)·`BuildKeyDownloadMessage`(위 (A)와 같은 조합). 값 자리에는 대표값을 넣는다 — 응답코드 2자리, 리더기 인증 식별번호·
  모듈 ID는 **파서·SPEC이 정한 최대 길이**(에이전트가 `Services/Reader/` 파서에서 찾아 근거 위치를 보고, 못 찾으면 추측하지 말고 Opus에 질문).
- **측정**: `AlertTextSplitter.Split`으로 제목·본문 분리, 본문은 `\n`으로 줄 단위. `FormattedText`(`ko-KR`, `TextFormattingMode.Ideal`, pixelsPerDip 1.0,
  `WidthIncludingTrailingWhitespace`) — `ApplyKeepAllWrap`과 같은 방식. 제목 15 Bold, 본문 13 Regular. 글꼴 2종: 앱 리소스 `PretendardFontFamily`
  (일반 모드 값)와 `Malgun Gothic`(컴팩트 모드 — 이름으로 직접). **UI 스레드에서** 잰다.
- **판정**: 제목 ≤ 272, 본문 각 줄 ≤ 271(래퍼와 같은 `272 − 1`). 고정 문구 줄과 길이가 정해진 값만 든 줄은 넘으면 **실패**. 길이 미정 값(DLL `detail`, 통신 오류 상세)이
  든 줄은 **측정값만 정보 로그**. 한도까지 2px 이내인 줄은 "경계"로 표시. 줄마다 `글꼴/제목·본문/폭/한도`를 로그에 남긴다.
- **검출력 확인**: Phase 38 이전 #12 문구(`리더기1과 리더기2에 같은 COM 포트를 지정할 수 없습니다.`)를 제목으로 재면 **넘침으로 판정**되는지 한 건 넣는다.

**(C) 갤러리 실문구 연결** — `AlertGalleryWindow`의 `RealPhraseCases`(지금 리터럴 14행)를 VM 상수·빌더 호출로 바꿔, 갤러리가 실제 화면 문구와 어긋나지 않게 한다.
#8~#11은 대표 결과 객체로 빌더를 부른다. 다른 갤러리 항목(원본 문구·넘침 확인)은 그대로.

**완료 조건**
- [x] `--alert-dialog-test self` 전부 통과(기존 33개 + 추가분), 종합=통과
- [x] 로그에 Pretendard·Malgun Gothic 각각 전 대상의 측정값이 있고, 검출력 확인 건이 "넘침"으로 판정됨
- [x] 갤러리 §4.2 14행이 실제 상수·빌더 문구로 뜬다 — 사용자가 `asInvoker` 일시 해제를 허용(2026-10-02)해 이 Task에서 #3·#12·#14 캡처

**검증 기록(2026-10-02, P39-4)** — `csharp-wpf-developer`(Sonnet) 구현: 신규 `Services/Diagnostics/AlertFixedPhraseSelfTest.cs`((A)(B)),
`App.xaml.cs`(`--alert-dialog-test self`가 Phase38·Phase39 묶음을 함께 돌리고 `[전체]` 종합 줄), `AlertGalleryWindow.RealPhraseCases`를 VM 상수·빌더로.
`app.manifest`를 `asInvoker`로 낮춰 exe를 직접 실행 → 원복(주석 기록). 리더기 ID 대표값 근거: 인증 식별번호 16자·모듈 ID 10자
(`Protocol/Reader/StatusResponseParser.cs:53-54`, 모듈 ID는 `KeyDownloadStartResponseParser.cs:47`·`KeyDownloadUsingKeyResponseParser.cs:35`도 10).
- (A) 34건 통과 — #1·#2(포커스 요청 1회)·#3(Error, 예외 문구 미노출), `BuildMessage` 3명령×5종, `BuildKeyDownloadMessage` 성공 1 + 실패 15.
- (B) 57개 문구 × 2글꼴, 판정 268줄 실패 0. 한도에 가장 가까운 줄: **`TimeoutInvalidMessage` 본문(#1 둘째 줄) Malgun Gothic 269.4/271 — "경계"** →
  P39-5에서 컴팩트 모드 캡처로 줄바꿈 여부 확인. 다음은 상태체크·무결성 체크 성공 본문 252.7(Malgun). 길이 미정 줄(DLL·통신 오류 상세, 키다운로드 Detail) 54건은 정보 로그.
  검출력: 옛 #12 제목 Pretendard 352.2·Malgun 408.9 > 272로 검출.
- **Opus 확인**: 새 하네스 코드 직접 읽음 — 측정 설정이 `Alert.xaml`(제목 Bold 15·본문 Normal 13, 글자 모드 미지정 = Ideal)·`ApplyKeepAllWrap`과 같음,
  Pretendard는 리소스·Malgun Gothic은 이름 직접, 측정은 `Dispatcher.Invoke`, (A) 기대 문구 리터럴·(B) 실제 상수·빌더 열거. 로그 직접 확인(09:53 `[전체] 완료 — Phase38=통과,
  Phase39=통과, 종합=통과`, 09:54 receipt-print 종합=통과, ★ 0건). 캡처 3장 열람(#3 Error 4줄, #12 Warning 3줄, #14 Warning 사유+안내, 제목 모두 한 줄).
  매니페스트 소스·exe 임베딩 모두 `requireAdministrator` 재확인. 사소한 점(수정 안 함): 갤러리 #8~#11은 빌더의 `.Text`만 쓰고 종류는 리터럴로 지정 — 종류 판정은 (A)가 맡으므로 기능 영향 없음.

### Opus 확인 포인트 (P39-4)
- 측정 설정(글꼴·크기·굵기·`TextFormattingMode`·폭 한도)이 `AlertDialog`/`Alert.xaml`과 같은지 — 다르면 통과해도 의미가 없다.
- 대상 열거에서 빠진 고정 문구가 없는지(VM 상수 grep과 대조).
- Malgun Gothic을 리소스가 아니라 이름으로 직접 재는지.
- 재실행: `--alert-dialog-test self` 1회(Opus 직접), 측정 로그에서 한도에 가장 가까운 줄 3개 확인.

---

## CP1 확인 항목 (`checkpoint-reviewer`에 그대로 전달)

1. `dotnet build` 성공(경고 증가 없음), `--alert-dialog-test self`·`--receipt-print-test self` 전부 통과를 직접 재실행으로 확인.
2. `src/KFTCTaxCAP`에서 `MessageBox.Show(`·`MessageBoxImage`·`MessageBoxResult`·`MessageBoxButton` 0건, 옛 이벤트 이름 0건.
3. PRD §4.2 14행 — 각 행의 문구·종류·버튼·캡션이 코드와 일치(View가 띄우는 #7·#12·#13 포함).
4. receipt_print PRD §5와 `PaymentScreenViewModel` 문구가 글자 단위로 일치, 사유 없는 실패 = 제목만.
5. 계층: 변경된 ViewModel이 WPF 타입을 새로 참조하지 않음. 하네스가 View를 참조하지 않음.
6. 동작 보존: 리더기 R-9 로그 순서, 결제창 닫힌 뒤 경고 미표시 가드, 질문창 Yes만 닫힘, #2 포커스 요청, 리더기 워밍업 인스턴스 무알림.
7. #3 저장 실패: ERROR 로그(`SETTINGS`)가 알림 전에 남고 민감값이 없음, 화면 문구에 예외 메시지 없음, 운영 경로의 저장 동작 불변.
8. 하네스 신뢰성: 문구 기대값이 PRD 리터럴인지(P39-3), 줄 폭 측정 설정이 실제 알림창과 같은지·검출력 확인 건이 있는지(P39-4).

## CP1 결과 (2026-10-02, `checkpoint-reviewer` 독립 검증 → Opus 직접 수정·재확인)

판정: **통과**(H·M 0건). 리뷰어가 빌드(`--no-incremental`, HEAD 대비 경고 증가 없음 — HEAD를 임시 worktree에서 빌드해 비교)와
x86 STA 호스트(Application·Dispatcher·리소스 8개 병합)로 `--alert-dialog-test self`·`--receipt-print-test self`를 직접 재실행(★ 0건),
항목 1~8 전부 통과. §4.2 14행을 코드 위치와 대조, receipt_print PRD §5와 글자 단위 일치, R-9·결제창 가드·질문창·#2 포커스·워밍업 무알림 확인.

| # | 지적 | 조치(Opus 직접) |
|---|---|---|
| L-1 | `AlertFixedPhraseSelfTest`가 앱 리소스 `PretendardFontFamily`를 쓰므로, Pretendard 로드에 실패하면(리뷰어 호스트에서 `pack://application`이 다른 어셈블리로 해석돼 재현) Malgun Gothic으로 조용히 재고도 "Pretendard" 라벨로 통과. 컴팩트 모드에서도 같은 일 | 글꼴을 어셈블리 명시 pack URI(`pack://application:,,,/KFTCTaxCAP;component/` + `./Assets/Fonts/#Pretendard, Malgun Gothic`)로 직접 만들고, 측정 전 `VerifyPretendardLoaded`가 Bold·Normal의 `GlyphTypeface.FontUri`에 Pretendard가 없으면 ★ 실패. `asInvoker` 일시 해제로 exe 직접 재실행(10:05) — `pretendard-bold.ttf`·`pretendard-regular.ttf` 실제 로드, 종합=통과, ★ 0건, 측정값 동일(검출력 352.2/408.9). 원복·exe 임베딩 확인 |
| L-2 | `MessageBox` 언급 주석 잔재 3곳(`ReaderSetupViewModel.cs:235`, `ReaderSetupWindow.xaml.cs:31`·`:189`) | "알림창(AlertDialog.Show)"으로 수정. 남은 `MessageBox` 문자열은 `AlertDialog.Show.cs` 요약(대체 대상 설명)·`HomeWindow.xaml.cs:124`(과거 경위)·`CModernMessageBox`(원본 이름)뿐 |

- 참고(리뷰어, 결함 아님): (B)는 길이 미정 여부를 문구 단위로 매겨 해당 문구의 고정 제목 줄도 정보 로그가 되지만, 같은 줄이 다른 조합에서
  판정되므로 빠지는 검사는 없다. #3 하네스는 실행마다 운영 로그에 `[ERROR] [SETTINGS] … TEST — 레지스트리 접근 거부(가상)`을 남긴다(code null, 알림 대상 아님).
- 정리: P39-4 중 `app.manifest` 1행에 BOM이 붙은 것을 발견해 제거(diff는 원복 기록 주석만 남음). 다른 변경 파일의 BOM 상태는 HEAD와 같음.
- **P39-5로 넘기는 것**: #1 본문 둘째 줄 Malgun Gothic 269.4/271 "경계" — 컴팩트 모드 실화면에서 줄바꿈 여부 확인.

---

## P39-5. 실제 화면 E2E (`csharp-wpf-developer` 새 에이전트, **Sonnet** — 판독은 Opus)

**준비**: 바꿀 설정(전표 인쇄·프린터 포트·리더기 포트)의 시작 전 값을 기록 → `app.manifest`를 `asInvoker`로(주석에 날짜·사유) → 빌드 → 실행.
사용자 세션의 `KFTCTaxCAP.exe`가 떠 있으면 종료하지 말고 Opus에 보고. 리더기 실기 연결, 가맹점 설정의 금융결제원 서버 = 실제 거래 서버(R).

| 행 | 화면 | 띄우는 방법 | 확인 |
|---|---|---|---|
| #1 | 가맹점 설정 | 카드입력 타임아웃 `10` → 확인 | Warning, 두 줄 문구, 창 유지 |
| #2 | 가맹점 설정 | 전표 인쇄 ON + 포트 비움 → 확인 | Warning, 닫은 뒤 포트 칸 포커스 |
| #3 | 가맹점 설정 | 실화면 재현 불가(HKCU 쓰기 실패 유도 수단 없음) | **"실화면 미재현 — P39-4 (A) + 갤러리로 대체"로 기록** |
| #4 | 가맹점 설정 | 포트 비움 → 직전거래 전표출력 | Warning |
| #5 | 가맹점 설정 | 저장 영수증이 없을 때만 직전거래 전표출력 | Info. 이미 있으면 **DB를 건드리지 않고** 생략 기록 |
| #6 | 가맹점 설정 | 저장 영수증 있음 + 없는 포트(예 `250`) → 직전거래 전표출력 | Warning, `전표 출력 실패\n프린터 포트를 열 수 없습니다.`(또는 실제 사유 문구) |
| #7 | 가맹점 설정 | 값 변경 → 취소 / X / Alt+F4 | Question [예][아니요] — 예 → 닫힘, 아니요·ESC → 창 유지, Enter(초기 포커스) = 예 |
| #8 | 리더기 설정 | 실기 리더기 포트에서 초기화·상태체크·무결성 체크 | Success(3건), 무결성 체크 후 목록 새로고침 그대로 |
| #9 | 리더기 설정 | 리더기 없는 포트에서 초기화 | Error |
| #10 | 리더기 설정 | 실기 키다운로드 — **실행 직전 Opus가 사용자에게 재확인**(IPEK 1회) | Success, 모듈 ID 줄. 실패하면 재시도 없이 보고 |
| #11 | 리더기 설정 | 리더기 없는 포트에서 키다운로드 | Error, 단계 줄 |
| #12 | 리더기 설정 | 리더기1·2 같은 포트 → 확인 | Warning, 세 줄 문구, 저장 안 됨 |
| #13 | 리더기 설정 | 포트 변경 → 취소 / X | Question, #7과 같은 키 동작 |
| #14 | 결제창 | 전표 인쇄 ON + 없는 포트 저장 → 결제창 스텁 경로로 501008·800000·902614 전송(승인) | Warning, 세 줄(사유 + `가맹점 설정에서 다시 출력하세요.`), 결제창 위 중앙 |
| 갤러리 | `--alert-gallery` | §4.2 14행 버튼 | P39-4 (C) 확인 — 실제 상수·빌더 문구 |

- 각 행 캡처 경로와 실제 문구를 표로 보고. 캡션은 작업 표시줄/Alt+Tab에서 확인 가능하면 기록(필수 아님).
- 끝나면 **즉시 `requireAdministrator`로 원복** → 빌드 → exe 임베딩 매니페스트 확인. 바꾼 설정은 **시작 전 값으로 되돌린다**.

**완료 조건**
- [x] 14행 중 #3(재현 불가)·#5(조건부)를 뺀 나머지가 표의 종류·버튼·문구로 뜬 캡처 — #11·#14는 아래 예외 기록
- [x] 질문창 키 동작(예/아니요/ESC/Enter) 확인 — #7에서 확인, #13은 같은 처리기(`ConfirmDiscardIfDirty`)라 표시만 확인
- [x] 매니페스트 원복·설정값 원복 확인

**실행 결과(2026-10-02, `csharp-wpf-developer`(Sonnet) 조작 → Opus 판독·마무리)** — `asInvoker`로 낮춰 scratchpad 출력 경로(`scratchpad\p39-5\build`)로 빌드해
조작·캡처(`scratchpad\p39-5\*.png`). 진행 중 두 번 끊겼다: ① 사용자 세션 앱(PID 37608)이 리더기 COM5를 잡고 있어 #8이 `READER_ERR_PORT_ALREADY_OPEN`
(Error 알림 자체는 정상) → 사용자가 앱 종료 후 재개 ② 에이전트가 사용량 한도로 #14 도중 중단 → **#14·컴팩트 #1·원복은 Opus가 직접**(`mcp__windows__*`) 마무리.

| 행 | 띄운 방법 | 결과(Opus 캡처 판독) |
|---|---|---|
| #1 | 타임아웃 `10` → 확인 | Warning `입력값을 확인해주세요.` / `카드입력 타임아웃은 30초 이상 입력해주세요.` ✔ |
| #1 컴팩트 | `--alert-gallery compact` #1 | Malgun Gothic에서 본문 둘째 줄(측정 269.4/271 "경계")이 **한 줄 유지** ✔ (`compact_row01.png`) |
| #2 | 전표 인쇄 ON + 포트 비움 → 확인 | Warning `입력값을 확인해주세요.`, 닫은 뒤 포트 칸 포커스 ✔ |
| #3 | — | **실화면 미재현**(HKCU 쓰기 실패 유도 수단 없음) — P39-4 (A) + 갤러리 캡처로 대체 |
| #4 | 포트 비움 → 직전거래 전표출력 | Warning `프린터 포트번호를 입력해주세요.` ✔ |
| #5 | — | **생략** — 직전 영수증이 이미 저장돼 있어 조건 미충족(DB 미변경). self ⑦(b) + 갤러리로 대체 |
| #6 | 포트 250 → 직전거래 전표출력 | Warning `전표 출력 실패` / `프린터 포트를 열 수 없습니다.` ✔ (S12 `PortOpenFailed`) |
| #7 | 타임아웃 변경 → X | Question [예][아니요] ✔. ESC·Alt+F4 → 창 유지, Enter(초기 포커스 예) → 닫힘(저장 안 함) ✔ |
| #8 | COM5 실기 초기화·상태체크·무결성 체크 | Success 3건 ✔ (상태체크: `리더기 인증 식별번호 : ####SPD-800F1011` / `모듈 ID : C160390003`) |
| #9 | COM6(리더기 없음) 초기화 | Error `리더기 초기화 실패` / `응답 시간 초과` ✔ |
| #10 | COM5 실기 키다운로드 — **1회만**(10:44:26, mode=R, 0130 응답 00, ⑤ 완료) | Success `리더기 키다운로드 성공` / `모듈 ID : C160390003` ✔ — IPEK 1회 소모 |
| #11 | 리더기 없는 포트 키다운로드(10:45:32, ① [73] 앱 타임아웃) | 캡처가 모두 다른 창(뉴스 위젯)에 가려 **아이콘·제목 미확인**. 보이는 본문 일부(`…콜백 없음 — 명령이` / `…오래 걸렸을 가능성.`)가 어절 단위로 줄바꿈됨을 확인. 종류(Error)·제목은 P39-4 (A) `ReaderFailure` 조합으로 갈음 |
| #12 | 리더기1·2 같은 포트 → 확인 | Warning `리더기 설정 실패` / `리더기 포트가 동일합니다.` / `서로 다른 포트를 선택해주세요.` ✔ |
| #13 | 포트 변경 → 취소 | Question [예][아니요] ✔(키 동작은 #7과 같은 `ConfirmDiscardIfDirty` — 별도 실측 없음) |
| #14 | 결제창(스텁 VAN, `App.xaml.cs:199`) 902614 + 사용자 핀패드 PIN(11:12:52) → `000` 승인 → S12 `PortOpenFailed`(COM250) | **사용자 육안 확인**(경고창이 결제창 위에 떴고 사용자가 닫음 — 캡처 전에 닫혀 캡처 없음). 문구는 갤러리 #14 캡처(P39-4)와 같은 빌더. 앞선 10:50 시도는 PIN 대기 시간 초과(E02) |

- **설정 원복**: 시작 전 값(HKCU `KFTC_VAN\KFTCTaxCAP` — TIMEOUT=110, PRINTER_CHECK=1, PRINTER_SPEED=115200, PRINTER=6, COMPORT1=COM 05, COMPORT2=미사용, VAN_MODE=R)과
  같음을 Opus가 레지스트리로 확인. 바뀐 것은 PRINTER(6→250) 하나였고 문자열로 되돌림(`ShopSettingsService.cs:163`과 같은 형식).
- **매니페스트**: `requireAdministrator` 원복(BOM 없음, 주석 기록 추가), 재빌드 경고 0·오류 0, bin exe 임베딩 `requireAdministrator` 확인. 검증용 인스턴스(PID 24228·54204·갤러리)는 모두 종료.
- **알림 위치·포커스 이상 없음**: 모든 캡처에서 알림창이 owner 창 중앙에 뜸.
- **미확인**: #3·#5 실화면, #11 아이콘·제목 캡처, #13 키 동작 실측, #14 캡처, 알림음(P38-5에서 확인), Win7.

### Opus 확인 포인트 (P39-5)
- 캡처를 직접 열어 행마다 아이콘 종류·문구·버튼을 PRD §4.2와 대조(에이전트 판정을 그대로 쓰지 않는다).
- 제목이 한 줄인지, 본문이 문장 중간에서 넘어가지 않는지(P39-4의 "경계" 줄 우선).
- 매니페스트: 소스 파일과 빌드된 exe 임베딩 둘 다.

## P39-6. 마무리 (Opus 직접)

1. PRD §4.1·§4.2를 "교체 완료" 상태로 갱신(현재 위치 줄번호 → 새 상수·이벤트 이름), §6 검증 결과 기록(Win7 미확인 포함).
2. ROADMAP Phase 39 체크·상태 ✅, 이 절 체크박스·검증 기록.
3. CLAUDE.md 5차 범위 문단에 Phase 39 완료 한 줄(사용자 확인 후).
4. `app.manifest` = `requireAdministrator` 최종 확인.

**완료 조건**
- [x] 문서 갱신 — PRD §4.1(교체 후 상태)·§6.1(검증 결과), ROADMAP Phase 39 ✅·작업 항목 체크, CLAUDE.md 5차 범위 문단, receipt_print PRD §5(P39-0)
- [x] 매니페스트 최종 확인 — 소스·bin exe 임베딩 모두 `requireAdministrator`(2026-10-02)

---

## Phase 39 진행 순서

**P39-0**(Opus) → **P39-1+2+3**(Sonnet, 한 위임) → Opus 검증(Task별 확인 포인트 + `--receipt-print-test self` 재실행) → **P39-4**(Sonnet)
→ Opus 검증(`--alert-dialog-test self` 재실행) → **CP1**(`checkpoint-reviewer`) → 결함 수정·재확인 → **P39-5**(Sonnet 조작 + Opus 판독, #10 직전 사용자 재확인)
→ **P39-6**(Opus) → (사용자 요청 시) 커밋.
