# 실행계획서: 알림창(메시지 박스) UI/UX 개선 (5차 범위)

> `PRD.md`(무엇을) → `ROADMAP.md`(순서) → **이 문서(Task 단위 지시·완료 조건)**. 각 Phase의 절은 그 Phase
> **착수 직전**에 작성한다. 현재 작성된 절: **Phase 38(작성 2026-10-01, 완료 2026-10-01)**. Phase 39는 Phase 38 완료 후 추가.

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
매니페스트 주석에 기록하고, 각 Phase 마지막 항목에서 원복을 확인한다. 실행 중인 `KFTCOneCAP.Wpf.exe`가 사용자
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
