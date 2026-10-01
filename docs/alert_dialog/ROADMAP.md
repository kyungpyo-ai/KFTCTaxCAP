# ROADMAP: 알림창(메시지 박스) UI/UX 개선 (5차 범위)

> `PRD.md`(무엇을)를 기준으로 **어떤 순서로, 어디까지 하면 끝인지**를 정의한다. Task 단위 지시·완료 조건은
> `development_plan.md`(각 Phase 착수 직전 작성)에서 다룬다.
>
> **Phase 번호는 앞 범위에서 이어진다** — 1차 0~6, 2차 7~21·26·29~30·32(`docs/payment_relay/`), 3차
> 22~25·27~28·31(`docs/operations/`), 4차 33~37(`docs/receipt_print/`). 이 범위는 **38부터**다.

## 작업 방식 (앞 범위와 동일 + 모델 선택 규칙)

1. Phase는 순서대로 진행한다. 완료 기준을 통과하지 못하면 다음으로 넘어가지 않는다.
2. 각 Phase 종료 시 `dotnet build` 성공 + 실제 실행 검증(화면 캡처). 확인한 것만 완료로 표시하고, 못 한 것은
   그 사실을 적는다(예: Win7 실기 미확인).
3. PRD와 구현이 어긋나면 PRD를 먼저 고친다. 원본 캡처와 PRD 수치가 다르면 캡처가 기준이다.
4. PRD에 없는 결정이 필요하면 즉시 사용자에게 묻는다(`PRD.md` §7 미확정 표를 먼저 갱신).
5. **메인 세션은 Opus**(설계·검증·문서·사용자와의 실측). 구현은 위임하되 **모델은 Task 성격에 맞춰 고른다**
   (2026-10-01 사용자 지시) — 시각 품질·디자인 판단이 핵심인 Task는 Opus, 명세로 완결되는 로직은 Sonnet,
   표대로 바꾸기만 하는 기계적 치환은 Haiku. 선택과 이유는 `development_plan.md` Task 표에 적는다.
6. Task 사이클: 구현(위임) → 자체 테스트 → Opus 검증(diff 직접 읽기 + 핵심 명령 1개 재실행) → 수정 → Opus
   재확인. 체크포인트마다 `checkpoint-reviewer`(같은 세션 서브에이전트, 중립 프롬프트).

## 진행 상태

| Phase | 내용 | 상태 |
|---|---|---|
| 38 | **알림창 컴포넌트** — 계약 타입·제목/본문 분리, 색 토큰·스타일, 창(XAML+VM)·벡터 아이콘·키보드·알림음, `--alert-gallery`·`--alert-dialog-test`, 원본 캡처 대조 | ✅ 완료(2026-10-01 — 사용자 확인, D4 애니메이션 철회·글자 모드 Ideal 확정) |
| 39 | **기존 호출부 교체** — ViewModel 이벤트 계약(`AlertMessage`), 종류 재분류(PRD §4.2), View 7곳 교체, 하네스 갱신, 실제 화면 E2E | ⬜ 대기 |

(상태 값: ⬜ 대기 / 🔄 진행중 / ✅ 완료 / ⏸ 보류)

## 계층 구조

`PRD.md` §5.1. 새 폴더는 `ViewModels/Alerts/`(WPF 타입 없음), `Views/Dialogs/`, 테마 파일 `Themes/Alert.xaml`.
의존 방향 `Views → ViewModels → Services → Protocol` — 매 Phase 완료 기준에 이 점검을 넣는다.

---

## Phase 38 — 알림창 컴포넌트 (PRD §2, §3, §5, §6)

**목표**: 기존 화면을 건드리지 않고, **진단 인자 하나로 5종 알림창을 모두 띄워 원본 캡처와 나란히 비교할 수 있는
상태**를 만든다.

**왜 먼저인가**: 호출부를 바꾸기 전에 모양·키보드·알림음을 한 곳에서 확정해야, 교체 후 화면마다 디자인을 다시
고치는 일이 생기지 않는다. 디자인 개선안(D1~D8)은 실제로 띄워 봐야 판단이 서므로 갤러리가 결정 도구 역할도 한다.

**착수 조건**: PRD §7 #8 확정(2026-10-01), 원본 캡처 4장(`screenshots/{Error,Warning,Success,Question}.png`) 도착(2026-10-01) — 충족.
캡처가 늦으면 소스 수치로 먼저 만들고 P38-5에서 대조한다(사용자 판단).

### 작업 항목

- [x] 계약 타입(`AlertKind`/`AlertButtons`/`AlertResult`/`AlertMessage`) + 제목/본문 분리 + 버튼·키·알림음 매핑(순수 로직)
- [x] 색 토큰(`Colors.xaml` `Alert*`) + `Themes/Alert.xaml`(치수·버튼 스타일) + App 병합 순서 추가
- [x] `AlertDialog` 창(XAML + `AlertDialogViewModel`) — 카드·그림자·벡터 아이콘·레이아웃(진입 애니메이션은 P38-5에서 철회)
- [x] 동작 — 모달·owner 중앙·Enter/ESC/Alt+F4·DragMove·Ctrl+C·알림음·UI 스레드 마샬링·owner 닫힘 처리
- [x] 진단 인자 `--alert-gallery`(5종 × 버튼 구성 × 본문 길이, 일반·컴팩트) + `--alert-dialog-test self`
- [x] 원본 캡처 대조 + 사용자 육안 확인 → PRD §2·§3 확정

### 완료 기준

- `--alert-dialog-test self` 전부 통과.
- 갤러리 캡처를 원본 캡처와 나란히 놓고 사용자가 "원본 기반 + 개선"으로 확인.
- Enter/ESC/Alt+F4 결과가 PRD §3.4 표와 같다(갤러리에서 결과를 화면에 표시해 확인).
- 경고·오류에서만 알림음.
- `ViewModels/Alerts/`가 `System.Windows`를 참조하지 않는다.
- 기존 화면·ViewModel diff 없음(`App.xaml.cs` 진단 인자·리소스 병합 추가 제외).

---

## Phase 39 — 기존 호출부 교체 (PRD §4)

**목표**: 앱 안의 모든 `MessageBox.Show`가 새 알림창으로 바뀌고, 리더기 설정의 성공/실패 등이 올바른 종류로 뜬다.

**착수 조건**: Phase 38 완료. (PRD §7 #9 문구 변경·#10 이벤트 `AlertRequested` 통합은 2026-10-01 확정.)

### 작업 항목

- [ ] ViewModel 이벤트 계약 변경 — `EventHandler<string>` → `EventHandler<AlertMessage>`(PRD §4.3), 문구를 만드는 쪽이 종류 결정
- [ ] 종류 재분류 — PRD §4.2 표 14행
- [ ] View 7곳 `MessageBox.Show` → `AlertDialog.Show` 교체(질문창 2곳 포함)
- [ ] 하네스 갱신 — `LastReceiptReprintSelfTest`, `NationalTaxReceiptAssemblerSelfTest`(문구 + 종류 검증)
- [ ] 실제 화면 E2E — PRD §4.2 각 행을 실제 화면에서 띄워 확인(사용자 조작 또는 `asInvoker` 일시 해제 후 원복)

### 완료 기준

- `src/KFTCOneCAP.Wpf`에서 `MessageBox.Show` grep 0건.
- PRD §4.2 14행이 표의 종류·버튼으로 뜬다(실제 화면 캡처 또는 사용자 확인).
- `--receipt-print-test self` 통과(재출력·조립 하네스 포함).
- 질문창: 예 → 닫힘, 아니오·ESC → 창 유지(기존 dirty-check 동작 유지).
- 결제창이 닫힌 뒤 도착한 전표 출력 경고는 띄우지 않는다(기존 동작 유지).
- ViewModel이 WPF를 참조하지 않는다(계층 규칙 유지). `app.manifest` = `requireAdministrator`.
