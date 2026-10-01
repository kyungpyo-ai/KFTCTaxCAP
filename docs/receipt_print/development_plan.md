# 실행계획서: 국세 납부확인증 영수증 출력 (4차 범위)

> `PRD.md`(무엇을) → `ROADMAP.md`(순서) → **이 문서(Task 단위 지시·완료 조건)**. 각 Phase의 절은 그 Phase
> **착수 직전**에 작성한다. 현재 작성된 절: **Phase 33(완료, 2026-09-30)**, **Phase 34(완료, 2026-10-01)**, **Phase 36(완료, 2026-10-01)**, **Phase 37(완료, 2026-10-01)**. Phase 34~36은 착수 직전에 추가.

## 공통 작업 규칙 (모든 Phase)

### 모델·에이전트

- **메인 세션 모델은 Opus 5.5로 고정**(사용자 지정). 설계·검증·실기 판독·문서는 메인이 직접 한다.
- 구현 위임 대상 에이전트의 모델은 에이전트 정의 frontmatter를 따른다.

| 에이전트 | 모델 | 이 범위에서 쓰는 곳 |
|---|---|---|
| (메인 세션, 직접) | **Opus 5.5** | 실기 확인, 설계 판단, Task 검증(diff 직접 읽기), 문서 갱신, 사용자와 함께하는 실측 |
| `receipt-printer-developer` (신설 2026-09-30) | Sonnet 5 | `Protocol/Printer/`, `Services/Printer/`, 프린터 진단 하네스 — 바이트가 프린터에 도달하는 계층 |
| `csharp-wpf-developer` | Sonnet 5(세션 기본 상속) | 결제창·가맹점 설정 ViewModel/XAML, 영수증 조립, SQLite 저장(Phase 34~35) |
| `pos-onecap-spec-expert` | Sonnet 5 | 전문 필드 출처 재확인(Phase 34 착수 전, Phase 36) |
| `reader-pinpad-spec-expert` | Sonnet 5 | 리더기 카드번호 형식 재확인(Phase 36) |
| `checkpoint-reviewer` | Opus 5.5 | 체크포인트 독립 검증(같은 세션 서브에이전트, 중립 프롬프트) |

**에이전트를 쓸지 판단 기준**: ① 여러 파일에 걸친 새 코드 작성이고 근거 문서로 지시를 완결할 수 있으면 위임,
② 사용자·실기와 주고받으며 판단해야 하거나(실기 확인), 몇 줄 수정·문서 작업이면 Opus 직접, ③ 스펙 해석이
걸리면 SPEC 전문가에게 먼저 묻고 그 결과를 위임 프롬프트에 넣는다.

### Task 사이클 (Phase 32 방식 그대로)

1. **구현(위임)** — 담당 에이전트가 구현 + Task 테스트 실행 + 결과 보고(변경 파일·실행 명령·못 한 것).
2. **검증(Opus)** — 보고를 그대로 믿지 않는다. diff를 직접 읽고 "Opus 확인 포인트"를 대조하며, Task 테스트의
   핵심 명령 1개를 직접 다시 실행한다.
3. **수정** — 결함은 같은 에이전트에 SendMessage로 이어서 맡기고, Opus는 수정분만 재확인한다.
4. 체크포인트에서는 `checkpoint-reviewer`가 한 번 더 본다(Opus 검증을 대체하지 않음).
5. 체크박스 갱신 → (사용자 요청 시) 커밋.

**`checkpoint-reviewer` 프롬프트는 중립으로**: diff 범위, PRD/이 문서의 해당 절 경로, CP 확인 항목만. Opus의
결론이나 "여기는 괜찮다"는 판단을 넣지 않는다.

---

# Phase 33 — 프린터 출력 기반

**이 Phase가 끝나면**: 진단 인자 하나로 사진 샘플과 같은 모양의 문서를 실제 프린터에 찍을 수 있고, 용지 없음·
커버 열림·무응답·없는 포트·속도 불일치가 각각 정확한 사유로 분류된다. 결제 흐름과 화면은 바뀌지 않는다.

> **이 Phase는 결제와 연결하지 않는다.** 새 코드는 진단 하네스에서만 호출된다. 실수가 나기 쉬운 곳은
> **① CP949 2바이트 글자를 줄 경계에서 반으로 자르는 것**, **② 포트를 닫지 않고 빠져나가는 예외 경로**,
> **③ 상태 바이트 비트 해석** 세 가지다.

## 착수 전 확정 사항

1. **연결** — 시리얼 COM + ESC/POS 직접(PRD §0, 사용자 확정).
2. **폭** — 42칸 가정(PRD §3.1). P33-0에서 실기로 먼저 확인하고, 다르면 P33-1 착수 전에 PRD·이 절을 고친다.
3. **상태 조회** — `DLE EOT 2`·`DLE EOT 4`, 대기 500ms(PRD §4.3, §9 #3 — P33-5에서 확정).
4. **로그 카테고리** — `PRINTER` 추가(2026-09-30 사용자 확정, 운영 PRD §1.3-b 갱신 완료). P33-3에서
   `LogCategory.Printer` + `LogCategoryText` 한 줄 추가. 렌더러 폭 8·파싱 정규식은 변경 없음.
6. **실기 정보는 테스트할 때 받는다**(2026-09-30 사용자 지시) — 모델명·COM 포트·속도는 P33-5 착수 시점에
   사용자에게 묻고 사용자가 세팅한다. 프린터는 일반 Epson 호환 전표 프린터이므로 **코드(P33-1~4)는 표준
   ESC/POS 가정으로 먼저 작성**한다. 단 기종마다 달라질 수 있는 4가지(한 줄 칸 수, `FS &` 필요 여부, 커팅
   명령, `DLE EOT` 지원)는 **상수 한 곳에 모아** 실기 결과가 다르면 그 한 곳만 고치게 한다.
5. **하네스 샘플 데이터의 개인정보** — 사진 샘플의 납세자명·납세자번호·전자납부번호·카드번호는 **더미 값으로
   바꿔** 코드에 넣는다(예: `홍길동`, `800101*******`). 줄바꿈 검증에 쓰는 세입징수관서 문자열과 하단 안내
   문구는 개인정보가 아니므로 샘플 그대로 쓴다.

## 착수 전 전제 (코드 실측, 2026-09-30)

- **설정** — `ShopSettingsService`(`Services/Settings/ShopSettingsService.cs`)가 `SlipPrintEnabled`/
  `PrinterSpeed`(int)/`PrinterPort`(숫자 문자열)을 이미 읽고 쓴다(Phase 31). 이 Phase에서는 **하네스가 인자로
  포트·속도를 받으므로** 설정을 읽지 않는다 — 설정 연동은 Phase 34.
- **로그** — `FileLogger.Info/Warn/Error(LogCategory, message, code, transactionId)`. 카테고리는
  `Services/Diagnostics/LogCategory.cs`의 8종 + 텍스트 변환 `LogCategoryText`.
- **진단 인자 패턴** — `App.xaml.cs`의 `else if (e.Args[0] == "--...")` 체인. 순수 로직 자체 검증은 홈 화면을
  띄운 채 `Task.Run(XxxSelfTest.RunAll)`로 돌리는 방식(`--field-chain-converter-test` 참고).
- **SQLite·결제창** — 이 Phase에서 건드리지 않는다.
- **`app.manifest`는 `requireAdministrator`** — 하네스 실행 시 UAC 승인은 사용자가 한다.
- **`System.IO.Ports`** — `net48` 기본 포함, 별도 패키지 불필요. `SerialPort` 사용처는 아직 없다(리더기는 DLL이
  포트를 연다).

## 이 Phase에서 손대지 않는 것

- 결제창, 가맹점 설정 화면, `PaymentOrchestrator`·`PosSocketServer`·`TransactionQueue`.
- 영수증 항목 조립·전문 필드 읽기(Phase 34). 하네스의 샘플 문서는 **줄 목록을 직접 적어** 만든다.
- 설정값 읽기(Phase 34).

## 위험 · 미확정

| # | 항목 | 대응 |
|---|---|---|
| 1 | 호환 기종이 `DLE EOT` 미지원 | P33-0에서 먼저 확인. 미지원이면 사용자에게 보고하고 상태 조회 방식을 다시 정한다 |
| 2 | 한국어 펌웨어가 아니거나 `FS &` 동작이 다름 | P33-0에서 `FS &` 유/무 두 가지로 한글 한 줄씩 찍어 비교 |
| 3 | USB-시리얼 변환기에서 `Close()`가 송신 버퍼를 버림 | 송신 후 `BytesToWrite == 0`까지 대기 후 닫기(P33-3). 실기에서 긴 문서 끝이 잘리는지 확인 |
| 4 | CP949 확장 완성형 글자 | P33-5에서 1~2자 출력 확인, 결과를 `escpos_reference.md` §1에 기록 |
| 5 | 관리자 권한 앱 | 하네스는 명령줄 실행이라 GUI 자동화가 필요 없다. UAC 승인만 사용자 |
| 6 | `Gate.WaitAsync` 무기한 대기 — `Open`/`Close`가 멈추면 이후 모든 출력이 대기 | P33-5 실기에서 `Close()` 지연 여부를 보고 대기 제한 시간을 둘지 결정(CP1 지적) |
| 7 | 닫은 직후 같은 포트를 다시 열 때 실패(.NET `SerialPort` 알려진 동작) | P33-5에서 `print` 연속 2회로 확인 |

## ⚠️ 진행 중 임시 조치 — `app.manifest` 관리자 권한 해제 (2026-09-30, 반드시 원복)

사용자 허용으로 Phase 33 검증 동안 `src/KFTCOneCAP.Wpf/app.manifest`의 `requestedExecutionLevel`을
`requireAdministrator` → `asInvoker`로 낮춘다(Phase 23·31과 같은 조치). 하네스 실행 후 뜨는 창을 에이전트가
닫지 못해 빌드가 막히는 문제(P33-1 첫 실행에서 발생) 때문이다.

**운영 방식(2026-09-30 P33-1+2에서 정착)**: 상시 낮춰 두지 않고, **검증 실행이 필요할 때만 낮춘 뒤 확인 즉시
`requireAdministrator`로 되돌린다**(Phase 31 선례와 동일). 되돌릴 때마다 매니페스트 주석에 기록을 남긴다.
P33-1+2 검증은 이 방식으로 끝났고 현재 `requireAdministrator` 상태다.

- **P33-5 마지막 항목으로 반드시 `requireAdministrator`로 되돌리고**, 빌드한 exe에 매니페스트가 임베딩됐는지
  확인한다. 원복하지 않으면 로그 경로(`C:\KFTC_PosAgent\KFTCTaxLog\`) 쓰기 권한 문제가 재발한다(P22-0).
- 이 기간 로그 파일 쓰기가 실패할 수 있다 — self 검증 결과가 로그에 안 남으면 콘솔/대체 출력으로 확인한다.

## 체크포인트

| 체크포인트 | Task | 성격 |
|---|---|---|
| — | P33-0 | 실기 사전 확인(Opus + 사용자) |
| **CP1** | P33-1 ~ P33-4 | 새 계층 전체. `checkpoint-reviewer`가 diff·빌드·`self` 하네스를 직접 재실행. 특히 2바이트 줄바꿈, 포트 닫기 경로, 상태 비트, 계층 규칙 |
| — | P33-5 | 실기 검증 · 문서 확정 |

## Task별 담당 · 모델

| Task | 담당 | 모델 | 위임 단위 | 판단 이유 / 위임 프롬프트에 넣을 근거 |
|---|---|---|---|---|
| P33-0 실기 사전 확인 | **Opus 직접 + 사용자** | Opus 5.5 | — | 실기·사용자와 주고받는 판단. PowerShell `SerialPort`로 몇 바이트 보내는 수준이라 위임 비용이 더 크다 |
| P33-1 명령·폭 계산·문서 인코더 | **`receipt-printer-developer`** | Sonnet 5 | P33-1+2 한 번에 | 순수 함수 신규 파일 여러 개, 근거 문서로 완결 가능. 근거: PRD §3.1~§3.3, `escpos_reference.md` 전체, 이 절의 P33-1/P33-2, P33-0 실기 결과 요약 |
| P33-2 상태 응답 파서 | 〃 | Sonnet 5 | (위와 함께) | P33-1과 같은 계층·같은 근거 문서 |
| P33-3 시리얼 출력 서비스 | **`receipt-printer-developer`**(SendMessage로 이어서) | Sonnet 5 | P33-3+4 한 번에 | 포트 생명주기·예외·직렬화 — 이 에이전트의 전문 영역. 근거: PRD §4.3, §5, §7, 이 절의 P33-3/P33-4, P33-1+2 결과 요약(공개 타입 이름) |
| P33-4 진단 하네스 | 〃 | Sonnet 5 | (위와 함께) | 서비스와 한 묶음으로 검증돼야 의미가 있음 |
| CP1 | **`checkpoint-reviewer`** | Opus 5.5 | P33-1~4 전체 | 중립 프롬프트: `git diff <Phase 33 착수 커밋>..HEAD`, PRD §3·§4.3·§5·§7, `escpos_reference.md`, 이 절의 CP1 항목 |
| CP1 결함 수정 | `receipt-printer-developer`(SendMessage) → Opus 재확인 | Sonnet 5 → Opus 5.5 | — | 리뷰어 지적 원문 |
| P33-5 실기 검증 · 문서 확정 | **Opus 직접 + 사용자** | Opus 5.5 | — | 출력물 육안 대조·장애 재현(용지 빼기 등)은 사용자 손이 필요, 판독·문서 갱신은 Opus |

---

## P33-0. 실기 사전 확인 (Opus 직접 + 사용자) — **P33-5 시작 시점으로 이동**

> 2026-09-30 사용자 지시: 실기 세팅은 실제 테스트할 때 한다. 따라서 이 확인은 코드 작성 전이 아니라
> **P33-5 첫 단계로 수행**하고, 코드(P33-1~4)는 착수 전 확정 사항 6의 표준 ESC/POS 가정으로 먼저 만든다.
> 실기 결과가 가정과 다르면 상수만 고치고 `self` 하네스를 다시 돌린다. 진행 순서 표기는 이력 보존을 위해
> 번호를 그대로 둔다.

프린터가 이 프로젝트의 가정대로 동작하는지 확인한다.

1. 사용자에게 받을 것: **모델명**, 연결 방식(RS-232 직결/USB 가상 COM), **COM 번호**, 프린터에 설정된 **속도**
   (셀프 테스트 출력물에 적혀 있음 — 전원 켤 때 FEED 버튼), 가능하면 명령어 매뉴얼 PDF.
2. PowerShell `System.IO.Ports.SerialPort`로 직접 확인(스크래치 스크립트, 저장소에 넣지 않음):
   - `ESC @` + ASCII 한 줄 + `LF` → 출력되는지
   - `FS &` 없이 CP949 한글 한 줄, `FS &` 켜고 한 줄 → 어느 쪽이 정상인지
   - `'-' × 42` + `LF`, `'-' × 43` + `LF` → 42칸에서 한 줄로 맞는지(43이면 1자 넘어감)
   - **42칸을 꽉 채운 줄 + `LF` 뒤에 빈 줄이 하나 더 생기는지**(일부 기종은 버퍼가 차면 자동 줄넘김 후 `LF`를
     또 처리한다). 생기면 인코더가 꽉 찬 줄 뒤 `LF`를 생략하도록 `PrinterProfile`에 옵션을 추가한다 — 구분선
     (`-`×42)과 2번 안내 문구 1·2행이 꽉 찬 줄이다
   - `GS V 42 n` → 커팅 동작
   - `DLE EOT 2`, `DLE EOT 4` → 1바이트 응답이 오는지, 값이 `(b & 0x93) == 0x12`를 만족하는지
3. 결과를 `escpos_reference.md` §5에 기록. 가정과 다르면 PRD §3.1/§9, 이 절을 먼저 고친다.

**결과(2026-09-30)** — `escpos_reference.md` §5 표. 모델명은 미확인(사용자가 기록하지 않음). COM6(USB-시리얼,
재연결 때 COM4→COM6), 115200bps. 한글·42칸·빈 줄 없음·2배·가운데·부분 커팅 정상. `DLE EOT` 무응답 → 상태 조회 끔.
오른쪽 여백 문제로 `GS L` 36 추가(사용자 확정).

**완료 조건**
- [x] 모델명·포트·속도 기록
- [x] 한글 출력 방식(`FS &` 필요 여부) 확정
- [x] 한 줄 칸 수 확정(42 또는 실측값)
- [x] 커팅·`DLE EOT` 지원 여부 확정

---

## P33-1. `Protocol/Printer/` — 명령 바이트 · 폭 계산 · 문서 인코더

**새 파일(제안 이름, 에이전트가 기존 명명 관례에 맞춰 조정 가능)**

| 파일 | 내용 |
|---|---|
| `Protocol/Printer/EscPosCommands.cs` | `escpos_reference.md` §1의 명령만 `static` 메서드/상수로. 매직 넘버를 다른 파일에 흩뿌리지 않는다 |
| `Protocol/Printer/ReceiptTextLayout.cs` | `LineWidth`(상수 42, P33-0 결과), `ByteWidth(string)`(CP949 바이트 수), `Wrap(string, int width)`, `LabelValue(label, value, width)`(= `Wrap(label + " : " + value)`), `Separator(width)`(`'-'` × width) |
| `Protocol/Printer/PrintDocument.cs` | 줄 목록 모델 — 각 줄 = 텍스트 + 정렬(왼/가운데) + 크기(보통/2배). 빈 줄 허용 |
| `Protocol/Printer/EscPosDocumentEncoder.cs` | `PrintDocument` → `byte[]`. 순서: `ESC @` → (P33-0 결과에 따라 `FS &`) → 줄마다 `ESC a` + `GS !` + CP949 바이트 + `LF` → `GS ! 00` 복귀 → `ESC d n` → `GS V 42 n` |

**규칙**
- `Wrap`: 바이트 폭을 채우다가 **다음 글자가 넘치면 그 글자부터 다음 줄**. 2바이트 글자를 쪼개지 않는다.
  공백도 1칸 글자로 취급해 그대로 옮긴다(샘플의 2번 안내 문구 둘째 줄 맨 앞 공백이 이 규칙의 결과 — PRD §3.3).
  **단, 줄바꿈으로 생긴 각 줄의 끝 공백은 제거한다**(2026-09-30 사용자 확정 — P33-1 첫 구현에서 1번 문구 1·2행이
  42번째 칸 공백으로 끝나는 것이 드러남. 인쇄 모양은 같고, 42칸을 꽉 채운 줄 뒤 `LF`가 빈 줄을 하나 더 만드는
  기종 위험을 줄인다). 줄 맨 앞 공백은 유지한다.
  빈 문자열 → 빈 줄 1개. `null` → 빈 줄 1개.
- 2배 크기 줄의 폭은 `LineWidth / 2`. **인코더는 줄바꿈하지 않는다** — 줄바꿈은 문서를 만드는 쪽 책임이고,
  인코더는 폭을 넘는 줄을 받으면 `ArgumentException`(Protocol 계층은 예외 허용, 서비스가 막는다).
- CP949로 표현 못 하는 문자는 인코더 기본 대체(`?`)로 두되, 하네스 `self`에서 그 동작을 확인한다.
- I/O·로그·WPF 참조 금지(순수 함수).

**검증 기록(2026-09-30)** — `receipt-printer-developer` 구현(24개 Check 통과 보고) → Opus가 diff 직접 읽고,
x86 PowerShell 리플렉션으로 빌드 결과물의 `ReceiptTextLayout.Wrap`/`PrinterStatusParser`를 독립 호출해 세입징수관서·
안내 문구 1·2의 줄바꿈이 PRD §3.2와 글자 단위로 같음, 경계 케이스, 상태 비트 7종을 재확인. 경미 지적 1건(기종
의존값이 `const bool`이라 `false`로 바꾸면 CS0162 경고) → P33-3+4에서 `static readonly`로 교체.

**완료 조건**
- [x] `ByteWidth("세입징수관서 : 국세청 서울지방국세청 강서") == 41`
- [x] `Wrap("세입징수관서 : 국세청 서울지방국세청 강서세무서 징수관", 42)` → 2줄, 둘째 줄 `세무서 징수관`
- [x] PRD §3.3 안내 문구 2개를 `Wrap(…, 42)` 한 결과가 PRD §3.2 그림의 줄과 글자 단위로 같다
- [x] 홀수 폭 경계에서 2바이트 글자가 쪼개지지 않는다(예: `"a" + "가"×21`, 폭 42 → 첫 줄 41바이트)
- [x] 인코더 출력이 `1B 40`으로 시작하고 `1D 56 42 ..`로 끝난다. 2배 줄 앞에 `1D 21 11`, 뒤 줄 앞에 `1D 21 00`
- [x] `Protocol/Printer/`가 `Services`·`System.Windows`·`FileLogger`를 참조하지 않는다

## P33-2. `Protocol/Printer/` — 상태 응답 파서

| 파일 | 내용 |
|---|---|
| `Protocol/Printer/PrinterStatusParser.cs` | `ParseOfflineCause(byte)`(`DLE EOT 2`), `ParsePaperSensor(byte)`(`DLE EOT 4`) → 결과 구조체: `IsValid`((b & 0x93) == 0x12), `CoverOpen`, `Error`, `PaperEnd`, `PaperNearEnd` |

**완료 조건**
- [x] `escpos_reference.md` §2 표의 비트마다 참/거짓 케이스 1개 이상(하네스 `self`)
- [x] 고정 비트가 틀린 바이트(`0x00`, `0xFF`) → `IsValid == false`

---

## P33-3. `Services/Printer/` — 시리얼 출력 서비스

| 파일 | 내용 |
|---|---|
| `Services/Printer/IReceiptPrinter.cs` | `Task<PrintResult> PrintAsync(byte[] payload, PrinterConnection connection, CancellationToken ct)` |
| `Services/Printer/PrinterConnection.cs` | 포트 번호(숫자 문자열) + 속도(int). **설정을 직접 읽지 않고 호출자가 넘긴다** — 자동 출력(저장된 설정)과 재출력(화면 입력값, PRD §4.2)이 다른 값을 쓰기 때문 |
| `Services/Printer/PrintResult.cs` | `IsSuccess`, `Failure`(enum: `None`/`InvalidPort`/`PortOpenFailed`/`NoResponse`/`PaperEnd`/`CoverOpen`/`PrinterError`/`WriteFailed`), `PaperNearEnd`(bool) |
| `Services/Printer/SerialReceiptPrinter.cs` | 실제 구현 |
| `Services/Printer/FakeReceiptPrinter.cs` | 받은 payload를 보관하고 지정한 결과를 돌려주는 테스트용(기존 `Services/Diagnostics/Fake*` 위치 관례를 따를지 에이전트가 판단해 보고) |

**`SerialReceiptPrinter.PrintAsync` 동작(PRD §4.3, §5)**
1. 포트 번호가 숫자 1~255가 아니면 `InvalidPort`(포트를 열지 않음).
2. `SemaphoreSlim(1,1)`로 직렬화 — 동시에 두 출력이 같은 포트를 열지 않는다.
3. 전부 `Task.Run` 안에서: `new SerialPort("COM"+n, baud, None, 8, One)`, `Handshake.None`,
   `ReadTimeout = 500`, `WriteTimeout = 3000`(PRD §9 #3, `escpos_reference.md` §3). `Open()` 예외 →
   `PortOpenFailed`.
4. `DiscardInBuffer()` → `DLE EOT 2` 쓰고 1바이트 읽기 → 타임아웃이면 `NoResponse`, `IsValid == false`면
   `NoResponse`(알 수 없는 응답 — 속도 불일치 시 흔함), 커버 열림 → `CoverOpen`, 오류 비트 → `PrinterError`.
5. `DLE EOT 4` 같은 방식 → 용지 끝 → `PaperEnd`, 거의 끝 → `PaperNearEnd = true`로 계속.
6. payload 쓰기 → `BytesToWrite == 0`까지 대기(최대 `WriteTimeout`) → 쓰기 예외/초과 → `WriteFailed`.
7. `finally`에서 `Close()`/`Dispose()`. **공개 메서드는 예외를 던지지 않는다**(취소 토큰 취소도 결과로 변환 —
   `WriteFailed` 또는 별도 값이 필요하면 에이전트가 제안하고 PRD를 갱신).
8. 로그: 시작(포트·속도·바이트 수), 결과(사유). **payload 내용은 로그에 남기지 않는다.** 카테고리는 착수 전
   확정 사항 4에 따른다. code 슬롯은 Phase 34에서 배정하므로 이 Phase는 `code: null`.

**완료 조건**
- [x] 없는 포트(`COM250` 등) → `PortOpenFailed`, 예외 전파 없음, 이후 같은 호출 반복해도 정상 반환(포트 누수 없음)
- [x] 포트 `"0"`/`"abc"`/`""` → `InvalidPort`, `SerialPort` 생성 안 함
- [x] 동시 호출 2건 → 순차 실행(로그 시각으로 확인)
- [x] `Services/Printer/`가 WPF 타입을 참조하지 않는다

## P33-4. 진단 하네스 `--receipt-print-test`

`App.xaml.cs` 진단 인자 체인에 추가. 모드는 두 번째 인자로:

| 모드 | 동작 |
|---|---|
| `self` | P33-1/P33-2 완료 조건 전부를 `Check(...)` 목록으로 자동 판정(기존 `*SelfTest` 출력 방식). 포트 불필요 |
| `dump` | 샘플 문서(아래)를 인코딩한 16진 덤프 + 줄별 텍스트 미리보기(`|` 로 42칸 경계 표시)를 로그/콘솔에 출력 |
| `print <포트번호> <속도>` | 샘플 문서를 `SerialReceiptPrinter`로 실제 출력하고 `PrintResult`를 출력 |
| `status <포트번호> <속도>` | 출력 없이 상태 조회만(용지·커버 재현 시험용) |

**샘플 문서**: PRD §3.2 그림을 그대로 줄 목록으로 적되 개인정보 항목은 더미(착수 전 확정 사항 5). 제목은
`종합소득세 납부확인증`(2배, 가운데). 이 문서는 Phase 34의 양식 코드가 아니다 — Phase 34는 이것과 같은 결과를
내는지 이 하네스로 대조한다.

**완료 조건**
- [x] `self` 전부 통과(에이전트 보고 + Opus 재실행)
- [x] `dump`의 줄 미리보기가 PRD §3.2 그림과 같은 위치에서 줄바꿈
- [x] 인자 누락·잘못된 모드 → 사용법 출력 후 정상 종료(예외 없음)

**검증 기록(2026-09-30)** — `receipt-printer-developer` 구현, self 27개 통과 보고(`print 0/abc`→`InvalidPort`,
`print/status 250`→`PortOpenFailed`, 동시 2건은 테스트 전용 지연 300ms로 합계 621ms → 직렬화 확인). Opus가
`SerialReceiptPrinter` 전체를 직접 읽고 결함 1건 지적: `WaitForWriteDrain`이 타임아웃 후 남은 바이트를 확인하지
않아 성공으로 처리 → `bool` 반환 + `WriteFailed`로 수정, 재빌드 경고 0 확인. **이 드레인 실패 분기는 실제 열린
포트가 있어야 도달하므로 self로 검증 불가 — P33-5 실기에서 확인.** 취소는 `WriteFailed`로 변환(PRD §5에 별도 행
없음). `PrinterProfile`의 분기용 값 5개는 `static readonly`로 교체(CS0162 방지), `LineWidth`만 `const` 유지.

### Opus 확인 포인트 (P33-1~4 공통)

- `Wrap`이 문자(char) 단위가 아니라 CP949 **바이트** 폭으로 자르는지 — `string.Length` 기반 계산이 섞여 있지 않은지.
- `SerialPort`가 모든 경로(`Open` 예외, 상태 타임아웃, 쓰기 예외, 취소)에서 닫히는지 — `using`/`finally` 확인.
- 상태 바이트 마스크가 `escpos_reference.md` §2와 같은지(비트 번호 하나 어긋나면 용지 없음이 커버 열림으로 나옴).
- `SemaphoreSlim`이 예외 경로에서도 `Release`되는지.
- 로그에 payload·영수증 텍스트가 들어가지 않는지.
- `self` 하네스를 직접 1회 재실행.

---

## CP1 확인 항목 (`checkpoint-reviewer`에 그대로 전달)

1. `dotnet build` 성공, `--receipt-print-test self` 전부 통과를 직접 재실행으로 확인.
2. 계층: `Protocol/Printer/` → `Services`/WPF/`FileLogger` 참조 없음, `Services/Printer/` → WPF 참조 없음.
3. CP949 폭 계산·줄바꿈이 바이트 기준이며 2바이트 글자를 쪼개지 않음(경계 케이스 직접 추가 실행 가능).
4. `SerialReceiptPrinter`: 모든 경로에서 포트 닫힘, 세마포어 해제, 공개 메서드 예외 미전파.
5. 상태 비트 해석이 `escpos_reference.md` §2와 일치.
6. `escpos_reference.md`에 없는 명령 바이트가 코드에 없음.
7. 로그·하네스 샘플에 실제 개인정보가 없음.
8. 결제·화면 코드(`Services/Payment/`, `Services/Pos/`, `ViewModels/`, `Views/`) diff 없음(`App.xaml.cs` 진단 인자 추가 제외).

---

## CP1 결과 (2026-09-30, `checkpoint-reviewer` 독립 검증 → 수정 → Opus 재확인)

판정: 수정 후 재검증 필요 → **수정 완료, Opus 재확인 통과**. 리뷰어가 직접 빌드·self 2회·상태 파서 0x00~0xFF 전수·
경계 케이스·diff 범위를 재실행했다.

| # | 지적 | 조치 |
|---|---|---|
| H-1 | 하네스 샘플의 전자납부번호가 사진 실물 값 | 더미(`0000000000000000000`)로 교체, `src/` grep 잔여 없음. PRD §3.2 그림의 실물 값 4개(Opus가 문서 작성 때 옮긴 것)도 더미로 교체. 오늘자 로그의 해당 줄·루트 사진은 사용자 결정으로 그대로 둠(사진은 그대로 커밋) |
| M-1 | 동시성 판정 기준 250ms < 지연 300ms라 병렬이어도 통과 | 기준 450ms(1.5×지연) — 실측 621ms |
| M-2 | 하네스가 폭 42 하드코딩, 예외 시 조용히 종료 | `PrinterProfile.LineWidth` 사용, dump/print/status 전체 try/catch + ERROR 로그 |
| L-1 | 서로게이트 쌍이 줄 경계에서 쪼개짐 | 쌍을 한 글자로 묶어 폭 계산 |
| L-2 | 줄 끝 공백 제거가 ASCII 공백만 | `TrimEnd()`(전각 공백·탭 포함) |
| L-3 | 본문 제어문자가 그대로 프린터로 나감 | **Phase 34 설계 결정으로 이월**(PRD §9 #14) |
| L-4 | 인코더 예외 메시지에 줄 본문 포함 | 줄 인덱스·바이트 수·허용 폭만 |
| L-5 | 속도 0 이하가 전송 오류로 분류, 포트 파싱이 공백·부호 허용 | 숫자만 허용, 속도 0 이하 → `InvalidPort`(포트·속도 설정 오류) |
| L-6 | 주석·cref 정리 | Protocol의 Services cref 제거, 로그 카테고리 "9종", 무의미한 검사 제거 |

리뷰어가 실측하지 못한 항목(실기 필요) → P33-5에서 확인: 드레인 실패 분기, 실제 NoResponse/CoverOpen/PaperEnd/
PaperNearEnd, USB-시리얼 `Close()` 동작, 연속 출력 시 포트 재열기 실패, `Gate.WaitAsync` 무기한 대기(아래 위험 #6).

---

## P33-5. 실기 검증 · 문서 확정 (Opus 직접 + 사용자)

1. `print` 모드로 샘플 문서 출력 → 사용자가 사진 샘플과 나란히 비교(제목 크기·가운데 정렬, 구분선 길이,
   `세입징수관서` 줄바꿈 위치, 안내 문구 줄바꿈, 커팅 위치).
2. 실패 재현(사용자가 조작, Opus가 결과 판독):

| 시나리오 | 조작 | 기대 결과 |
|---|---|---|
| 용지 없음 | 롤 제거 | `PaperEnd`, 아무것도 인쇄 안 됨 |
| 커버 열림 | 커버 열기 | `CoverOpen` |
| 무응답 | 프린터 전원 끔 | `NoResponse`(약 0.5초 내) |
| 없는 포트 | 존재하지 않는 COM 번호 | `PortOpenFailed` |
| 속도 불일치 | 프린터와 다른 속도 지정 | `NoResponse`(알 수 없는 응답 포함) |
| 용지 부족 | (가능하면) 거의 끝난 롤 | 출력 성공 + `PaperNearEnd` |

3. 확장 완성형 글자 1~2자 출력 결과 기록(위험 #4).
4. 문서 갱신: `escpos_reference.md` §1 근거 등급 (E)→(실), §5 기록 / PRD §9 #1~#3 확정 / ROADMAP Phase 33 체크.

**실행 결과(2026-09-30, Opus + 사용자)** — 출력은 x86 PowerShell 리플렉션으로 `ReceiptPrintDiagnosticHarness.RunPrint`
를 직접 호출(매니페스트를 낮추지 않음).

| 시나리오 | 결과 |
|---|---|
| 샘플 영수증 | 성공(1078→여백 추가 후 1082바이트). 사진과 줄바꿈·제목·커팅 동일, 여백 36으로 가운데(사용자 사진 확인) |
| 용지 없음 / 커버 열림 / 무응답 / 용지 부족 | **해당 없음** — 상태 조회를 끔(이 프린터는 `DLE EOT` 무응답). 감지 불가를 PRD §0·§5에 한계로 기록 |
| 전원 끔 | **실측(2026-09-30 17:12)** — 프린터 전원을 끈 채 출력: COM6 열기 성공, 1082바이트 송신 완료, **`IsSuccess=True`로 기록**(실제 출력 없음). USB-시리얼 변환기가 PC 전원으로 살아 있어 데이터를 받아 버리고, 상태 조회를 꺼서 감지 수단이 없다 |
| 없는 포트(COM4) | `PortOpenFailed`(31ms) |
| 포트 `0` | `InvalidPort`, 포트 열지 않음 |
| 속도 불일치 | 미실시(상태 조회가 없어 판정 수단 없음 — 깨진 글자 출력 여부로만 알 수 있음) |
| 연속 2회(위험 #7) | 둘 다 성공(각 약 0.24초), 닫은 직후 재열기 문제 없음, 2장 모두 끝까지 출력(사용자 확인) |
| 드레인 실패 분기 | 미재현(정상 출력에서 송신 버퍼가 항상 즉시 비워짐) |
| `Gate.WaitAsync` 무기한 대기(위험 #6) | `Close()` 지연 관찰 안 됨(한 장 0.24초) → 대기 제한 시간 추가하지 않음 |
| 확장 완성형 글자(위험 #4) | 미실시 |

**완료 조건**
- [x] 샘플 문서 실기 출력이 사진과 같은 줄바꿈(사용자 확인)
- [x] 실패 시나리오 결과 표 채움(재현 못 한 항목은 사유 기록)
- [x] 문서 갱신 완료
- [x] `app.manifest` = `requireAdministrator` 확인(P33-5는 매니페스트를 낮추지 않았음)

---

## Phase 33 진행 순서

**P33-1+2**(`receipt-printer-developer`, 표준 ESC/POS 가정) → Opus 검증 → **P33-3+4**(같은 에이전트
SendMessage) → Opus 검증 → **CP1**(`checkpoint-reviewer`) → 결함 수정·재확인 → **(사용자에게 실기 세팅 요청)** →
**P33-0**(Opus+사용자, 실기 기본 동작 확인 — 다르면 상수 수정 후 `self` 재실행) → **P33-5**(Opus+사용자, 실기 검증).

## Phase 33 — 완료 선언 (2026-09-30)

P33-1~P33-4(CP1 통과) + P33-0/P33-5(실기) 완료. 진단 인자 하나로 사진 샘플과 같은 줄바꿈의 영수증이 실제
프린터(COM6, 115200bps)에 가운데 정렬로 출력된다. 실기에서 바뀐 것: 상태 조회 끔(`UseStatusQuery=false`),
왼쪽 여백 36 추가(`LeftMarginDots`, `GS L` — `escpos_reference.md`에 먼저 등재). 결제 흐름·화면 변경 없음.
**남은 한계**: 이 프린터에서는 용지 없음·커버 열림·전원 꺼짐을 감지하지 못한다(PRD §5).

---

# Phase 34 — 납부확인증 자동 출력

**이 Phase가 끝나면**: 결제창에서 902614를 보내 승인(`#7=000`)되면, 가맹점 설정의 전표 인쇄 사용이 ON일 때
세 탭의 응답값으로 조립한 납부확인증이 자동으로 출력된다. 실패하면 결제창에 경고창이 뜨고 `S12` 로그가 남는다.

> **실수가 나기 쉬운 곳**: ① 전문 필드 번호(501008/800000/902614가 같은 이름 필드를 다른 번호로 가진다 —
> PRD §2.2 표가 유일한 기준), ② 501008 짝 검사(전자납부번호 불일치 시 501008 값을 **전부** 버린다),
> ③ 원캡 중계 경로를 건드리는 것(이 Phase의 diff에 `Services/Payment/`·`Services/Pos/`가 있으면 설계 오류).

## 착수 전 확정 사항 (2026-09-30 사용자 확정, PRD 반영 완료)

1. **출력 중 다음 전송 허용** — 출력은 백그라운드, 직렬화는 출력 서비스(`SerialReceiptPrinter.Gate`)가 맡는다(PRD §9 #4).
2. **빈 값** — 선불카드잔액 공백 → `0`, 그 외 빈 값 → 라벨만(`라벨 : `)(PRD §9 #7).
3. **제어문자** — 영수증에 들어가는 모든 전문 값에서 0x00~0x1F·0x7F를 공백으로 치환(PRD §9 #14).
4. **로그 코드 `S12` 하나**(전표 출력 실패, 사유는 본문), 장애 알림 `N`(`fault_alert_catalog.md` §2.2 예외 행 추가 완료).
5. **수수료율** — 800000 `#26` N4, 소수점 2자리(`0050`=0.5%), 끝 0 제거. 값 없으면 요율 없는 문구(PRD §3.3).
6. **할부** — `00`/`01` → 일시불, 그 외 `N개월`(PRD §2.2 #15).
7. **카드번호 줄** — 800000 응답 `#14`(마스킹 카드번호) 트림 → 4자리마다 `-`. 800000 응답 없으면 빈칸(2026-09-30 Phase 36 선행으로 변경).
8. **경고창의 재출력 안내 문구는 Phase 35에서 붙인다**(PRD §5).
9. **승인 판정** — 902614 응답 `#7 == "000"`만(PRD §2.3).

## 착수 전 전제 (코드 실측, 2026-09-30)

- **결제창 구조** — `PaymentScreenViewModel.Tabs`(501008/800000/902614 순, `TransactionTypeCode`로 찾음).
  탭 VM `PaymentTelegramTabViewModel.SendAsync`가 응답을 받으면 `_lastResponseTelegram`을 채운 **뒤**
  `HasResponse = true`로 바꾸고, `PaymentScreenViewModel.OnTabPropertyChanged`가 그 전이를 받아
  연쇄(`ApplyChainMappingsFrom`)를 적용한다. 전송 직전 `HasResponse=false`로 떨어뜨려 매 응답마다 false→true
  전이가 보장된다(P30 CP2 M-1). 응답 파싱 실패 시에도 `HasResponse=true`가 되지만 `_lastResponseTelegram`은
  `null` → `TryReadResponseField`가 `null`.
- **값 읽기** — `tab.TryReadResponseField(n)` = `PosTelegram.Read(n)`(고정 길이 원문, 패딩 포함 — 트림은 호출자 몫).
  902614 응답은 요청 필드를 그대로 되돌려 준다(스텁 VAN이 clone 후 `#3/#6/#7/#8`만 덮고 kiosk 외 소유 필드는
  임의값으로 채움 — P30-1).
- **VAN** — 메인 앱 `App.Orchestrator`는 `StubVanRelayService`(항상 `#7=000`). 따라서 결제창 E2E로 자동 출력을
  재현할 수 있다. 800000 `#26`은 스텁이 임의 숫자 4자리로 채우므로 요율이 비현실적일 수 있다(표시 형식만 확인).
- **View 알림 경로** — `PaymentScreenWindow.xaml.cs`에는 아직 메시지 박스 경로가 없다. `ShopSetupViewModel`의
  `ResultMessageReady`(string 이벤트 → View가 `MessageBox`) 패턴을 따른다.
- **설정** — `ShopSettingsService.Load()`(예외 없음, 폴백): `SlipPrintEnabled`, `PrinterPort`(숫자 문자열),
  `PrinterSpeed`(int). 매 출력 직전에 읽는다(캐시 금지).
- **출력 계층(Phase 33)** — `ReceiptTextLayout.Wrap/LabelValue/Separator`, `PrintDocument`, `EscPosDocumentEncoder.Encode`,
  `IReceiptPrinter.PrintAsync(payload, PrinterConnection, ct)` → `PrintResult`. 샘플 문서 조립 예시는
  `ReceiptPrintDiagnosticHarness.BuildSampleDocument`.
- **로그 코드** — `InternalFaultCodes`(S01~S11), `LogCodeCatalog.Descriptions`. `FaultAlertJudge`의 기본 분기
  (`_ => ClassifyRBusinessFailure`)가 모르는 코드를 `N`으로 판정하므로 S12는 **Judge 수정 없이** `N`이 된다.
- **관리자 권한** — 결제창 E2E는 GUI 조작이 필요하고 UIPI 때문에 에이전트 자동화가 안 된다 → 사용자가 조작.

## 이 Phase에서 손대지 않는 것

- 원캡 중계 경로(`Services/Payment/`, `Services/Pos/`, `Services/Van/`), 전문 스키마(`Protocol/Pos/`).
- 가맹점 설정 화면·직전거래 전표출력(Phase 35), SQLite 저장(Phase 35).
- 카드번호 값(Phase 36). `FaultAlertJudge`.

## 위험 · 미확정

| # | 항목 | 대응 |
|---|---|---|
| 1 | 탭 값의 필드 번호 혼동 | 조립기를 PRD §2.2 표와 1:1로 대조하는 self 검증(필드마다 다른 표식값을 넣은 가짜 탭으로) |
| 2 | 출력 대기가 UI를 막음 | 포트 I/O는 Phase 33 서비스가 `Task.Run`으로 처리 — 전송 버튼 잠금과 무관하게 진행 |
| 3 | 결제창을 닫는 중 출력 진행 | 출력은 창과 무관하게 끝까지 진행(창이 닫혔으면 경고창은 띄우지 않고 로그만) |
| 4 | 상태 조회 꺼짐 → 프린터 꺼져도 성공 | Phase 33 한계 그대로(PRD §5). 이 Phase에서 해결하지 않음 |

## 체크포인트

| 체크포인트 | Task | 성격 |
|---|---|---|
| **CP1** | P34-1 ~ P34-4 | 양식·조립·트리거 전체. 필드 매핑 정확성, 501008 짝 검사, 계층, 중계 경로 무변경 |
| — | P34-5 | 결제창 E2E 실기(사용자 조작) · 문서 |

## Task별 담당 · 모델

| Task | 담당 | 모델 | 위임 단위 | 판단 이유 / 위임 프롬프트에 넣을 근거 |
|---|---|---|---|---|
| P34-1 표시값 변환·양식 조립 | **`receipt-printer-developer`** | Sonnet 5 | P34-1+2 한 번에 | 출력 계층(Phase 33) API를 만든 에이전트라 `PrintDocument` 구성에 맥락이 가장 많다. 근거: PRD §3 전체·§2.2 "표시 변환" 열·§9 확정값, 이 절 P34-1/P34-2 |
| P34-2 출력 서비스(설정 확인·S12) | 〃 | Sonnet 5 | (위와 함께) | Services 계층, 전문 필드를 모름 |
| P34-3 결제창 조립·트리거·경고창 | **`csharp-wpf-developer`** | Sonnet 5 | P34-3+4 한 번에 | ViewModel/View(MVVM) 작업, 전문 필드 번호를 아는 유일한 계층. 근거: PRD §2 전체(특히 §2.2 표·§2.3)·§4.1·§5, 이 절 P34-3/P34-4, P34-1+2 결과 요약(공개 타입) |
| P34-4 조립 self 검증 | 〃 | Sonnet 5 | (위와 함께) | 조립기와 한 묶음 |
| CP1 | **`checkpoint-reviewer`** | Opus 5.5 | P34-1~4 | 중립 프롬프트: diff 범위(`91f57e9..HEAD` + 미커밋), PRD §2·§3·§4.1·§5, 이 절 CP1 항목 |
| CP1 결함 수정 | 해당 에이전트(SendMessage) → Opus 재확인 | Sonnet 5 → Opus 5.5 | — | 리뷰어 지적 원문 |
| P34-5 결제창 E2E | **Opus 직접 + 사용자** | Opus 5.5 | — | 카드·PIN·화면 조작은 사용자, 로그·출력물 판독과 문서는 Opus |

---

## P34-1. `Services/Receipt/` — 표시값 모델 · 변환 · 양식 조립

| 파일(제안) | 내용 |
|---|---|
| `Services/Receipt/NationalTaxReceipt.cs` | 이름 붙은 표시용 값 모델(전문 필드 번호를 모른다): 세목명, 전자납부번호, 납세자명, 납세자번호(원문), 세입징수관서, 계좌번호, 납기내기한/금액, 납기후기한/금액(원문), 납부세액, 수수료, 총 납부금액, 선불카드잔액, 카드사명, 할부(원문 2자리), 카드번호, 납부일자, 수수료율(원문 4자리), `IsReprint`(Phase 35용, 기본 false). 전부 `string?` 원문을 받고 변환은 아래가 한다 |
| `Services/Receipt/ReceiptValueFormatter.cs` | 순수 함수: `Amount`(N 0패딩 → `100,000`, 전부 0 → `0`, 공백 → 빈값), `Date`(`YYYYMMDD` → `YYYY.MM.DD`, 형식 아니면 트림 원문), `Installment`(`00`/`01` → 일시불, 숫자 → `N개월`, 그 외 트림 원문), `MaskTaxpayerNumber`(트림 후 앞 6 + 나머지 `*`, 6자 이하 그대로), `PrepaidBalance`(공백 → `0`), `FeeRatePercent`(N4 소수점 2자리 → `0.5`/`0.75`/`1`, 공백·비숫자 → null), `CardNumber`(4자리마다 `-`, 공백 → 빈값), `Sanitize`(제어문자 → 공백, 뒤 공백 트림) |
| `Services/Receipt/NationalTaxReceiptComposer.cs` | `NationalTaxReceipt` → `PrintDocument`. PRD §3.2 순서·구분선·빈 줄·하단 안내 1·2(요율 유무 분기)·제목 2배 가운데(폭 `LineWidth/2`로 Wrap). `IsReprint`면 제목 위에 `(재출력)` 가운데 보통 크기 1줄. 모든 값은 `Sanitize` 후 `LabelValue`로 |

**완료 조건**
- [x] 변환 함수마다 정상·공백·비정상 입력 케이스(self)
- [x] 더미 샘플 모델 → 조립 결과 줄 목록이 PRD §3.2 그림과 글자 단위로 같다(요율 `0080` = 0.8%일 때)
- [x] 요율 없음 → 2번 문구가 `2.신용카드로 국세를 납부할 경우 납부대행수수료는 납세자가 부담합니다.`의 Wrap 결과
- [x] `IsReprint` → 첫 줄 `(재출력)`
- [x] 제어문자 포함 값(예: 납세자명에 ESC `@` 삽입) → 인코딩 결과의 값 영역에 `1B 40`이 없다
- [x] `Services/Receipt/`가 WPF·`Protocol/Pos`(전문)를 참조하지 않는다

## P34-2. `Services/Receipt/ReceiptPrintService` — 설정 확인 · 출력 · S12

- `Task<ReceiptPrintOutcome> PrintAsync(NationalTaxReceipt receipt, CancellationToken ct)`:
  1. `ShopSettingsService.Load()`(매번). `SlipPrintEnabled == false` → INFO `전표 인쇄 사용 안 함 — 출력 생략` → `Skipped`.
  2. 조립 → 인코딩(예외 → `Failed` + S12 ERROR, 사유 "양식 조립 오류", 본문 로그 금지).
  3. `IReceiptPrinter.PrintAsync(payload, new PrinterConnection(settings.PrinterPort, settings.PrinterSpeed), ct)`.
  4. 실패 → `FileLogger.Error(LogCategory.Printer, "전표 출력 실패 — 사유=…", code: "S12")` → `Failed(reason)`. 성공 → INFO → `Printed`.
- 공개 메서드는 예외를 던지지 않는다. `IReceiptPrinter`와 설정 로더는 생성자 주입(기본값 = 실제 구현) — self에서 Fake로 교체.
- `InternalFaultCodes.ReceiptPrintFailure = "S12"`, `LogCodeCatalog`에 `["S12"] = "전표(영수증) 출력 실패"`.
- 결과 → 경고창 문구 변환은 PRD §5 표(상태 조회 관련 행은 현재 발생하지 않지만 enum에 있으므로 매핑은 둔다).

**완료 조건**
- [x] 설정 OFF → 프린터 호출 0회, `Skipped`
- [x] Fake 프린터 실패 → `Failed` + 로그에 `[PRINTER ] [S12]`
- [x] 설정 ON + Fake 성공 → payload가 P34-1 조립 결과의 인코딩과 같다
- [x] 예외 미전파(설정 로드·조립·출력 어디서 던져도)

**검증 기록(2026-09-30)** — `receipt-printer-developer` 구현, self `[Phase34 조립] 종합=통과` 보고. Opus가
`ReceiptValueFormatter`/`NationalTaxReceiptComposer`/`ReceiptPrintService`를 직접 읽고, x86 리플렉션으로
`FaultAlertJudge.Classify("S12")` → `Verdict=None`(S11은 Immediate), 요율(`0050`→0.5, `0005`→0.05, `1000`→10,
공백·`12a4`→null), 금액(전부 0→`0`, 공백→빈값) 재확인. 경미 관찰: 요율 `0000` → `0%인` 문구(CP1에서 판단).

### Opus 확인 포인트 (P34-1+2)
- 금액 변환이 선행 0만 제거하고 `0` 금액을 `0`으로 남기는지.
- 수수료율 소수점 2자리 해석(`0050`→`0.5`, `0005`→`0.05`, `1000`→`10`).
- 납세자번호 마스킹이 트림 후 길이 기준인지.
- 설정을 캐시하지 않는지(필드에 `ShopSettings` 보관 금지).
- `S12` 로그에 영수증 값이 없는지.

---

## P34-3. 결제창 — 조립 · 승인 판정 · 트리거 · 경고창

- `ViewModels/Payment/NationalTaxReceiptAssembler.cs`(전문 필드 번호를 아는 유일한 곳): 세 탭의 읽기 함수
  (`Func<int, string?>`)를 받아 `NationalTaxReceipt`를 만든다. PRD §2.2 표 그대로:
  - 501008 1순위 조건: 501008 응답 있음 **그리고** `501008 #14`(트림) == `902614 #15`(트림). 아니면 501008 값을 **하나도** 쓰지 않고 대체 출처/빈값.
  - 800000: 응답 있으면 `#18` 카드사명, `#26` 요율. 없으면 null.
  - 902614: `#15/#19/#27/#28/#29/#52/#34/#32`, 대체 출처 `#21/#37/#14/#20`.
  - 카드번호 = 800000 응답 `#14` 트림(Phase 36이 먼저 끝나 있으므로 연결한다 — 2026-09-30 순서 변경).
- `PaymentScreenViewModel.OnTabPropertyChanged` — 902614 탭의 `HasResponse` false→true 전이에서(연쇄 처리 **뒤**)
  `TryReadResponseField(7)` 트림 == `"000"`이면 조립 → `ReceiptPrintService.PrintAsync` 호출. `async void` 핸들러
  안에서 try/catch로 감싼 await — 예외가 창을 죽이지 않게. 결과가 `Failed`면 `ReceiptPrintWarningRequested`(string) 이벤트.
- `PaymentScreenWindow.xaml.cs` — 이벤트 구독 → 창이 아직 열려 있으면 `MessageBox`(Warning, 제목 `전표 출력`).
  문구 = PRD §5 표(재출력 안내 문구는 붙이지 않음 — 확정 사항 8). 창이 닫혔으면 무시.
- 출력 중에도 전송 버튼은 막지 않는다(확정 사항 1).

**완료 조건**
- [x] `Services/Payment/`, `Services/Pos/`, `Services/Van/`, `Protocol/Pos/` diff 없음
- [x] 902614 응답 `#7 != 000` / 파싱 실패 / 다른 탭 응답 → 출력 호출 없음
- [x] 조립기가 PRD §2.2 표와 1:1(P34-4 self로 판정)

## P34-4. 조립 self 검증

`--receipt-print-test self`에서 함께 실행되도록 이어 붙인다.

- 가짜 탭: 필드마다 **다른 표식값**을 넣어 어느 필드가 어느 항목으로 갔는지 판정.
- 케이스: ① 세 탭 모두 + 전자납부번호 일치 → 1순위 전부 ② 501008 없음 → 대체 출처 + 납기 4줄 빈값 ③ 501008 있으나
  전자납부번호 불일치 → ②와 같음 ④ 800000 없음 → 카드사명 빈값·요율 null ⑤ `#7`이 `000`이 아닐 때 트리거 판정이 false.

**완료 조건**
- [x] 위 5 케이스 전부 통과, P34-1/P34-2 self와 합쳐 `종합=통과`

**검증 기록(2026-09-30)** — `csharp-wpf-developer` 구현(Phase 36에 이어서). self `[Phase34 결제창 조립기]` ①~⑧ 통과
(⑦ 빈 짝키 → 501008 미사용, ⑧ 경고 문구), `--payment-flow-test` 192건 통과, 빌드 0/0. Opus가 조립기 필드 상수 28개를
PRD §2.2와 한 줄씩 대조(전부 일치), 짝 검사가 읽기 함수를 통째로 끊어 부분 적용 불가, 트리거가 연쇄 뒤·try/catch·
취소 토큰 없음(창 닫혀도 출력 완료)을 직접 확인. 에이전트 판단 2건 수용: ① 짝 키가 비어 있으면 불일치 처리
② PRD §5에 없는 사유(PrinterError/CompositionFailed/Unexpected)는 괄호 없이 공통 문장. **실제 트리거 경로(결제창 →
출력 → 경고창)는 P34-5 실기로 확인.**

### Opus 확인 포인트 (P34-3+4)
- 필드 번호를 PRD §2.2 표와 한 줄씩 대조(특히 501008 `#25~#28`, 902614 `#14/#15/#19/#20/#21`).
- 짝 검사가 부분 적용되지 않는지.
- 연쇄 적용 뒤에 트리거가 도는지, 파싱 실패 경로에서 null 안전한지.
- `async void` 핸들러의 예외 처리, 창 닫힘 후 경고창 미표시.

---

## CP1 확인 항목 (`checkpoint-reviewer`에 그대로 전달)

1. `dotnet build` 성공, `--receipt-print-test self` 종합 통과를 직접 재실행(매니페스트는 건드리지 말고 x86 PowerShell 리플렉션으로 대체 가능).
2. 조립기 필드 매핑이 PRD §2.2 표와 1:1, 501008 짝 검사가 전부/전무로 동작.
3. 표시 변환(금액·날짜·할부·마스킹·선불잔액·요율·제어문자)이 PRD §2.2·§3.3·§3.4·§9와 일치.
4. 설정 OFF → 미출력, 승인 아님 → 미출력, 실패 → S12 + 경고 이벤트, 공개 메서드 예외 미전파.
5. 계층: `Services/Receipt/`는 WPF·`Protocol/Pos`를 모름, 전문 필드 번호는 ViewModel에만.
6. 원캡 중계 경로·스키마 diff는 **Phase 36의 800000 `#14` 한 지점(스키마·`HandleCardInfoInquiryAsync` 채움·KioskSim)뿐**. 새 POSITION이 SPEC 20260930 p.13과 일치.
7. 로그·코드에 실제 개인정보 없음, S12 로그에 영수증 본문 없음.
8. `fault_alert_catalog.md` S12 행과 코드(`InternalFaultCodes`, `LogCodeCatalog`)가 일치하고 `FaultAlertJudge`가 S12를 `N`으로 판정.

---

## CP1 결과 (2026-09-30, `checkpoint-reviewer` — Phase 34 P34-1~4 + Phase 36)

판정: **수정 후 재검증 필요**(M-1). 리뷰어가 빌드(`--no-incremental` 포함), `ReceiptPrintSelfTest`·`PaymentFlowTestScenarios`(192)·
`MemoryClearTestScenarios`(8) 재실행, `FaultAlertJudge` S12=None, KioskSim 800000 리플렉션, SPEC p.13 200dpi 렌더 대조를 직접 수행.

| # | 지적 | 조치 |
|---|---|---|
| M-1 | InvalidPort·조립/설정 예외처럼 **동기로 끝나는 실패**면 경고창이 `HasResponse=true` 처리 중(=`IsSending=false` 전)에 모달로 떠 전송 잠금이 안 풀림(P34-5 시나리오 4의 "없는 포트"는 비동기 경로라 재현 못 함) | 출력 호출을 항상 `Task.Run`으로 UI 흐름 밖에서 시작 |
| L-1 | InvalidPort의 S12가 ERROR — PRD §5는 WARN | InvalidPort만 WARN |
| L-2 | 오래된 주석 2곳 | 갱신 |
| (개선) | 501008 응답이 실패여도 번호만 같으면 사용 / 1순위가 공백이면 빈칸 | 501008 `#7=000` 조건 추가, 공백이면 대체 출처(PRD §2.2 갱신) — Opus 결정 |

**재검증(2026-09-30, `checkpoint-reviewer` 새 세션) — 통과, 확정 결함 0건.** M-1 수정(동기 조립 → `Task.Yield()` → `Task.Run` →
이벤트)이 `DispatcherSynchronizationContext`에서 경고를 `SendAsync`의 `IsSending=false` 이후로 미룸을 코드 흐름으로 확인, self ⑪이
수정 전 코드라면 실패하는 검증임을 실측(포트 빈 값 → `PrintAsync`가 동기 완료), `ReceiptPrintSelfTest` ①~⑪·payment-flow 192·
memory-clear 8 재실행 통과, 빌드 `--no-incremental` 0/0. 경미 관찰 1건(주석 부정확)은 Opus가 수정. **실제 UI에서 경고창이 떠 있는 동안
전송 버튼·배지가 풀려 있는지는 P34-5 시나리오 4-b로 확인.** 참고: payment-flow 중 SQLite `readonly database` WARN 1회는 이번 변경 전부터
있던 로그(원인 미조사, 범위 밖).

**범위 밖 후속(기존 불일치, 결제 흐름 영향 없음)**: SPEC 20260930 p.13 렌더에서 800000 `#19`의 ○가 VAN 열, 공통부 `#5`·`#7` VAN 단독 /
`#6`·`#8` VAN+kiosk로 보이나 코드는 다름, KioskSim 800000 `#11`/`#12`가 여전히 Kiosk — `pos-onecap-spec-expert` 확인 후 별도 정리.
운영 로그 16:24:40 줄의 실물 전자납부번호는 사용자 결정으로 유지.

## P34-5. 결제창 E2E (Opus 직접 + 사용자)

준비: 가맹점 설정에서 전표 인쇄 사용 ON, 포트 `6`, 속도 `115200bps`. 리더기·카드 준비.

| # | 시나리오 | 기대 |
|---|---|---|
| 1 | 501008 → 800000 → 902614(카드·PIN) | 자동 출력, 영수증 값 = 탭 응답값(PRD §2.2 변환 적용). 요율은 스텁 임의값이라 형식만 확인 |
| 2 | 전표 인쇄 사용 OFF 후 902614 | 출력 없음, 로그 INFO "출력 생략" |
| 3 | 902614만(501008 없이) | 납기 4줄 빈칸, 납세자명·세입징수관서는 902614 값 |
| 4 | 포트를 없는 번호(예: 4)로 저장 후 902614 | 결제창 경고창(포트 열기 실패 문구), 로그 `S12`, 탭 결과는 그대로 |
| 4-b | 전표 인쇄 사용 ON + **포트 빈 값**(동기 실패 경로, CP1 M-1) — 가맹점 설정 저장이 막히면 레지스트리 `PRINTER`를 직접 비움 | 경고창 `(프린터 포트 설정 확인)`, **경고창이 떠 있는 동안에도 전송 버튼·배지가 이미 풀려 있음** |
| 5 | 출력 중 다른 탭 전송 | 막히지 않음 |

**실행 결과(2026-10-01, 사용자 카드·PIN + Opus 로그 판독/화면 자동화)**

| # | 결과 |
|---|---|
| 1 | **통과**(09:17~09:18) — 501008·800000·902614 모두 `000`, 800000 응답 `#14` = `35641514****706*`(실카드, 리더기 마스킹 그대로), 응답 0.06초 뒤 COM6 1236바이트 출력. 로그 원문으로 같은 조립 코드를 돌린 기대 영수증과 출력물 일치(사용자 확인). 값 대부분이 스텁 임의 문자열 — 별도 Phase로 현실화 예정(아래) |
| 2 | **통과**(10:06) — OFF 저장 후 902614 승인, `전표 인쇄 사용 안 함 — 출력 생략`, 프린터 호출 없음 |
| 3 | **통과**(10:13) — 재시작 후 902614만, 1014바이트 출력(납기 4줄·카드사명·카드번호 빈칸으로 짧아짐). 직전 10:12 `E01`은 카드 대기 중 사용자 취소(출력 호출 없음, 정상) |
| 4 | **통과**(10:22) — 포트 4, `포트 열기 실패(COM4)` + `[ERROR] [S12] 사유=PortOpenFailed`, 경고창 |
| 4-b | **통과**(10:27) — 레지스트리 `PRINTER` 비움, `asInvoker` 임시 해제 후 Opus가 결제창 전송 클릭(카드·PIN은 사용자). 경고창 `(프린터 포트 설정 확인)`이 떠 있는 동안 배지 "응답 수신"·상태 "전송 완료 — 응답 수신됨"·전송 버튼 활성(UIA 비활성 표시 없음, 스크린숏 확인) → **CP1 M-1 해결 실측**. 로그 `[WARN ] [S12] 사유=InvalidPort`(PRD §5대로 WARN). 끝난 뒤 매니페스트 원복(커밋본과 동일)·포트 `6` 원복 |
| 5 | 4-b에서 경고창이 떠 있는 동안 전송 버튼이 이미 활성임을 확인 — 출력 결과와 전송 잠금이 분리됨. 출력 자체는 0.1초 내 끝나 "출력 도중" 수동 재현은 하지 않음 |

진행 중 변경: 영수증 제목 고정(위 절), 스텁 값 현실화 요청(별도 Phase — ROADMAP 참고).

**완료 조건**
- [x] 1~5 결과 기록(사용자 확인)
- [x] 문서 갱신(ROADMAP Phase 34 체크, 이 절 완료 선언)

## Phase 34 — 완료 선언 (2026-10-01)

P34-1~4(CP1 + 재검증 통과) + P34-5(결제창 실기) 완료. 결제창에서 902614가 승인되면 전표 인쇄 사용 설정에 따라
`종합소득세 납부확인증`이 자동 출력되고, 카드번호 줄은 Phase 36의 800000 `#14` 마스킹 카드번호로 채워진다. 실패는
결제창 경고창 + `S12` 로그로 남고 거래 결과·전송 잠금과 분리된다. Phase 36도 이 실기(시나리오 1)로 끝에서 끝까지 확인됐다.

**P34-5 진행 중 변경(2026-10-01, 사용자 확정)** — 영수증 제목을 `종합소득세 납부확인증` **고정**으로 바꿨다(이 프로그램은 종합소득세
전용). 실기 중 스텁 임의값 `#21 징수 과목명`이 제목 위에 1~2줄로 찍히는 것이 발단. `NationalTaxReceipt.TaxItemName` 속성과 조립기의
501008/902614 `#21` 매핑 삭제, Composer 상수화(Opus 직접 — 몇 줄 수정). PRD §0·§2.2 T·§3.2 갱신.

## Phase 34 진행 순서

**P34-1+2**(`receipt-printer-developer`) → Opus 검증 → **Phase 36**(800000 `#14`, 아래 절 — 2026-09-30 SPEC 개정으로 끼워 넣음) → **P34-3+4**(`csharp-wpf-developer`, 카드번호 줄 = 800000 응답 `#14` 연결 포함) → Opus 검증 →
**CP1**(`checkpoint-reviewer`) → 수정·재확인 → **P34-5**(Opus + 사용자, 결제창 E2E).

---

# Phase 36 — 800000 `#14` 마스킹 카드번호 (SPEC 20260930) — Phase 34 중간(P34-3 전)에 진행

**이 Phase가 끝나면**: 800000 스키마가 SPEC 20260930과 일치하고(`#14` AN19, `#15~#28` +11, `#28` AN205, 총 500),
원캡은 800000에서 리더기 카드번호의 구분자 앞 부분(이미 마스킹됨)을 `#14`에 채운다. 키오스크 시뮬레이터도 같은
스키마를 쓴다. 영수증 카드번호 줄 연결은 P34-3(결제창 조립)에서 한다.

> **왜 Phase 34 중간인가**(2026-09-30 사용자 확정): 스키마가 SPEC과 어긋난 채로 P34-3이 800000 응답 `#18`/`#26`을
> 읽으면 11바이트 밀린 값을 읽는다. 스키마부터 맞추고, P34-5 실기에서 카드번호 줄까지 한 번에 확인한다.
>
> **원캡 중계 경로를 건드리는 유일한 Phase다** — 변경은 800000 `#14` 채움 한 지점으로 제한한다. 902614·501008
> 경로와 응답 삭제(P26-1)·메모리 클리어(Phase 25) 규칙은 그대로 유지돼야 한다.

## 착수 전 확정 사항 (2026-09-30)

1. **SPEC 정본** — `docs/payment_relay/spec/국세 베리어프리 키오스크용 전산설계서(POS-원캡)_20260930.pdf`. 변경은 800000뿐
   (pos-onecap-spec-expert 두 판 대조): `#14` `마스킹 카드번호` AN19 POSITION 70 원캡 SET, `#15~#28` POSITION +11,
   `#28` 예비 정보 FIELD AN216→AN205, 총 길이 500. `#18` 카드사명 POSITION 108, `#26` 수수료율 POSITION 285.
2. **채움 규칙**(사용자 확정) — 리더기 `CardReadData.CardNumber`에서 **맨 앞부터 숫자(`0-9`)·`*`가 이어지는 부분만**
   (구분자 `D`/`=` 등 다른 문자가 나오면 거기서 끊음), 19자 초과면 앞 19자, **왼쪽 정렬 + 공백 채움**.
   결과가 8자 미만이면 기존 "카드번호 8자리 미만" 방어 경로 그대로(`ReaderNoCardDataDefensiveCode`, `SendInvalidationInit`).
3. **마스킹** — 원캡은 추가 마스킹하지 않는다(리더기가 이미 9~12번째·마지막 자리 `*`). 전문 로그(`TelegramLogRedactor`)도
   변경 없음 — 리더기 마스킹 그대로 남김(사용자 확정).
4. **메모리 클리어** — `#14`용 임시 `char[]`는 기존 BIN 버퍼처럼 쓰고 바로 `SecureClear.Clear`(Phase 25 원칙).
5. **응답 삭제(P26-1)** — 800000 `#14`는 대상이 아니다(카드 정보 조회 전문의 목적이 이 값을 돌려주는 것). 그대로.
6. **결제창 표시** — 800000 `#14`는 원캡 담당이지만 응답 패널에 보여 주는 예외(Phase 29) 그대로. 라벨만 새 이름.

## 착수 전 전제 (코드 실측, 2026-09-30)

- `Protocol/Pos/Schemas/CardInfoInquirySchema.cs` — `new(14, "BIN", PosFieldType.AN, 8, 70, PosFieldOwner.OneCap)`,
  `#28 … 216, 284`. `PosTelegramSchema` 생성자가 POSITION 연속성·총 길이를 자체 검증한다.
- `Services/Payment/PaymentOrchestrator.cs` `HandleCardInfoInquiryAsync` — `fillOneCapFields`에서 `cardNumber.Length < 8`
  방어 후 앞 8자리를 `char[8]`에 복사해 `request.Telegram.Write(14, bin)`, `finally`에서 클리어. 로그 "BIN 채움 완료".
- `src/KFTCOneCAP.KioskSim/Protocol/TelegramSchemas.cs` — 시뮬레이터(Phase 19)의 별도 스키마 사본. 같은 변경 필요.
- 테스트: `Services/Diagnostics/PaymentFlowTestScenarios.cs:239` `Read(14) == "94123456"`(BIN 기대) 등. 가짜 리더기
  카드번호 픽스처(`FakeReaderEndpoint` 등)가 구분자를 포함하는지 확인 필요.
- 주석에 "BIN"이 남은 곳: `PosResponseTelegram.cs:100`, `StubVanRelayService.cs:113`, `PaymentTelegramTabViewModel.cs:71`,
  `TelegramLogRedactor.cs:20,98`, `CardInfoInquirySchema.cs:8,48`, `PosSchemaRegistry` 등 — 의미가 바뀐 곳만 갱신.

## 이 Phase에서 손대지 않는 것

- 501008/902614/999999 스키마와 처리, `TelegramLogRedactor`의 마스킹 대상 목록, P26-1 응답 삭제 목록.
- 영수증 카드번호 줄 연결(P34-3에서), 결제창 연쇄 맵(800000 `#14`는 연쇄 대상이 아님 — 확인만).

## Task별 담당 · 모델

| Task | 담당 | 모델 | 위임 단위 | 판단 이유 / 근거 |
|---|---|---|---|---|
| P36-1 문서 정본 갱신 | **Opus 직접** | Opus 5.5 | — | 완료(2026-09-30): CLAUDE.md, `pos-onecap-spec-expert.md`, payment_relay PRD §3.3 표·개정 이력, ROADMAP 참고 문서, `spec_open_questions.md`, receipt PRD §2.4 |
| P36-2 스키마·원캡 채움·KioskSim·테스트 | **`csharp-wpf-developer`** | Sonnet 5 | 한 번에 | 스키마(Protocol)·오케스트레이터(Services)·시뮬레이터·하네스가 한 변경의 여러 면이라 한 에이전트가 일관되게. 근거: 이 절 전체, payment_relay PRD §3.3 개정 이력 행, `spec_open_questions.md` 마지막 행, SPEC PDF p.13~14 |
| P36-3 Opus 검증 | **Opus 직접** | Opus 5.5 | — | diff 직접 읽기 + 회귀 하네스 재실행 |
| CP | Phase 34 CP1에 합친다 | Opus 5.5 | — | 규모가 작아 별도 체크포인트 없이 Phase 34 CP1 범위에 포함(중계 경로 변경이 이 한 지점뿐인지 확인 항목 추가) |
| 실기 | Phase 34 P34-5에 합친다 | Opus 5.5 + 사용자 | — | 실제 카드로 800000 → `#14` 값 확인 |

## P36-2. 스키마 · 원캡 채움 · 시뮬레이터 · 테스트

1. `CardInfoInquirySchema` — `#14` 이름 `마스킹 카드번호`, AN 19, POSITION 70. `#15~#27` POSITION +11,
   `#28` AN 205 POSITION 295. 주석의 SPEC 판·페이지 인용 갱신(20260930 p.13~14).
2. `PaymentOrchestrator` 800000 채움 — 확정 사항 2의 규칙으로 `char[19]`(공백 초기화) 버퍼를 만들어 `Write(14, …)`,
   `finally`에서 클리어. 방어 조건은 "추출 결과 8자 미만". 로그 문구 "마스킹 카드번호 채움 완료"(값은 로그에 넣지 않음 —
   전문 로그는 `TelegramLogRedactor` 경로가 따로 남긴다).
   추출 로직은 테스트 가능하도록 순수 함수로 분리해도 좋다(위치는 에이전트 판단, Protocol/Pos 또는 Services/Payment).
3. `KioskSim` `TelegramSchemas` 800000 동일 변경. 시뮬레이터 UI에 `#14` 길이·라벨이 박혀 있으면 함께.
4. 테스트 — `PaymentFlowTestScenarios` 800000 기대값을 새 규칙으로(예: 픽스처 카드번호 `9412345678901234`처럼 구분자
   없는 값 → `"9412345678901234   "`; 가능하면 `11112222****444*D****123…` 같은 구분자 포함 픽스처 케이스 추가).
   추출 함수 경계 케이스(구분자 `D`/`=`, 19자 초과, 8자 미만, 빈 값)를 self로.
5. 주석의 "BIN" 갱신(전제의 목록).

**완료 조건**
- [x] `dotnet build`(루트 — KioskSim 포함) 경고·오류 0
- [x] 스키마 자체 검증 통과(앱 기동 또는 스키마 생성 시 예외 없음), 800000 총 길이 500
- [x] `--payment-flow-test`(또는 800000을 다루는 기존 회귀 하네스) 통과 — 기대값 갱신 포함
- [x] 추출 경계 케이스 self 통과
- [x] 501008/902614 스키마·`TelegramLogRedactor` 대상 목록·P26-1 삭제 목록 diff 없음
- [x] 메모리 클리어 하네스(`--memory-clear-test`) 회귀 통과

**검증 기록(2026-09-30)** — `csharp-wpf-developer` 구현. 새 POSITION 15행을 SPEC 20260930 p.13과 대조(전부 일치,
총 500), `MaskedCardNumberExtractor.ExtractInto` 경계 14건, `--payment-flow-test` 192건·`--memory-clear-test` 8건·
난수/연쇄 self 통과(Debug·Release, x86 리플렉션 — SQLite 바인딩 리디렉트를 위해 스크립트에 AssemblyResolve 추가).
KioskSim 스키마도 리플렉션으로 생성 확인(total=500), `tools/spec_client.ps1`의 `#15` 89·`#16` 104·`#14` 19바이트 출력
수정(사용자 지적으로 범위 추가). Opus가 추출 함수·오케스트레이터 diff·spec_client diff를 직접 읽어 확인 — 구분자 뒤가
섞이는 경로 없음, 버퍼 클리어가 방어 경로 포함 전 경로, 902614 채움 코드 무변경.

**후속 확인(범위 밖, 에이전트 보고)**: ① SPEC 20260930 p.13 800000 `#19` 체크카드 여부의 SET 체크가 VAN 열로 보인다(코드는
InternetGiro) — pos-onecap-spec-expert로 확인 필요 ② KioskSim 800000 `#11`/`#12`가 여전히 Kiosk로 분류(20260922 개정 미반영).
둘 다 표시·분류 문제로 결제 흐름에는 영향 없음.

### Opus 확인 포인트
- 새 POSITION이 SPEC 20260930 p.13 표와 한 칸씩 일치(특히 `#18`=108, `#26`=285, `#28`=295/205).
- 추출이 "숫자·`*` 연속 구간"이고 구분자 뒤(유효기간 등)가 절대 섞이지 않는지.
- 버퍼 클리어가 모든 경로(방어 실패 포함)에서 일어나는지.
- 902614 원캡 8필드 채움 코드에 diff가 없는지.

---

# Phase 37 — 결제창·VAN 스텁 테스트값 현실화 (2026-10-01)

**이 Phase가 끝나면**: 결제창(kiosk 역할 대행)의 요청값과 VAN 스텁의 응답값이 실제와 비슷한 테스트값으로 채워져, 영수증에
`김테스트`, `테스트세무서 징수관`, 현실적인 금액·날짜·카드사명·수수료율이 찍힌다. 금액은 서로 맞는다(본세+농특세+교육세=납부세액,
수수료=납부세액×요율, 합계 일치). 실 VAN이 붙으면 스텁 쪽은 사라진다(P30-1과 같은 개발용 장치).

> **실수가 나기 쉬운 곳**: ① 필드 길이(AHN은 CP949 바이트 — `902614 #37`은 10바이트, `#20`/`#21`은 20바이트) ② 연쇄(Phase 30)와의
> 정합 — 결제창은 501008 응답값을 800000/902614 요청에 복사하므로, 스텁이 만든 값이 연쇄를 거쳐도 의미가 유지돼야 한다
> ③ 기존 회귀 하네스(`PosRandomValueGenerator`를 쓰는 소켓 클라이언트·연쇄 변환 테스트)를 깨지 않는 것.

## 착수 전 확정 사항 (2026-10-01)

1. **범위** — 결제창 요청값 + VAN 스텁 응답값 둘 다(사용자 확정). 키오스크 시뮬레이터(KioskSim)는 범위 밖.
2. **값 방식** — 이름류는 "테스트" 표시, 금액·날짜·번호는 현실적 범위에서 거래마다 변동(사용자 확정).
3. **수수료율** — 스텁이 체크카드 여부(`800000 #19`)를 정하고 신용 `0080`(0.8%) / 체크 `0050`(0.5%)(사용자 확정, SPEC은 형식만).
4. **세목 코드** — 종합소득세 `10` 가정(사용자 확정, SPEC 형식: 세목년월 YYMM + 결정구분 1(`5`=고지분) + 세목 2). 코드 주석에 "추정 테스트값" 명시.
5. **영수증 제목은 이미 고정**(Phase 34) — 징수 과목명은 영수증에 안 쓰지만 전문에는 `종합소득세`로 채운다.
6. **새 생성기를 만든다** — 기존 `PosRandomValueGenerator`는 그대로 둔다(소켓 클라이언트·연쇄 변환 회귀 테스트가 사용). 결제창
   (`PaymentTelegramTabViewModel`의 생성·"임의값 재생성")과 `StubVanRelayService`만 새 생성기로 바꾼다.
7. **표에 없는 필드는 공백** — kiosk/디지털예산/인터넷지로 담당 필드는 아래 표에 전부 값 규칙을 둔다. 의미 있는 값이 없는 필드는 공백
   (난수 문자열을 다시 넣지 않는다). 원캡 담당 필드는 지금처럼 건드리지 않는다.

근거: SPEC 20260930(pos-onecap-spec-expert 확인, 2026-10-01 — 공통부 p.5~7, 501008 p.8~11, 800000 p.13~14 카드사 코드표,
902614 p.15~19), `docs/payment_relay/spec_open_questions.md`(연쇄 확정값), KioskSim `PresetStore.cs` 기본값.

## 값 표

표기: `R(a~b)` = 범위 난수, `today` = 거래 시각 날짜. "연쇄"는 결제창 Phase 30 연쇄가 덮어쓰는 필드(요청 생성 시에는 그럴듯한 초기값만).

### 공통부 (요청 — 결제창)

| 필드 | 값 | 근거 |
|---|---|---|
| #1 업무 구분 | `IGN` | SPEC p.5 고정 |
| #2 요청기관 코드 | `095` | SPEC p.5 고정 |
| #3 전문 종별 코드 | `0200` | SPEC p.5 |
| #4 거래 구분 코드 | 전문 코드 | — |
| #5 상태 코드(kiosk 소유인 전문만) | `000` | 정상 |
| #6 송·수신 FLAG | `G` | SPEC p.6 |
| #8 전송 일시 | `now` `yyMMddHHmmss` | — |
| #9 전문 관리 번호 | `0EC` + `0` + 숫자 8자리 R | SPEC p.6 |
| #11/#12 | 501008: `01`/`2600000`(국세청), 902614: 연쇄(초기값 동일), 800000: 공백 | SPEC p.7, Q9 |

스텁 응답의 공통부 `#3 0210 / #6 C / #7 000 / #8 now`는 지금 그대로.

### 501008

| 필드 | 요청(결제창) / 응답(스텁) | 값 |
|---|---|---|
| #14 전자납부번호 | 요청 | `0126` + `yyMMdd` + 숫자 9자리 R (19자리) |
| #15 납부 순번 | 응답 | `001` |
| #16 실 납부자번호 | 요청(D\|Kiosk) | 공백(연대납부 아님) |
| #17 납세 의무자 번호 | 응답 | `900101` + `1` + 숫자 6자리 R (13자리, 테스트 주민번호) |
| #18 납세 의무자 명 | 응답 | `김테스트` (902614 `#37` 10바이트로 연쇄돼도 잘리지 않는 길이) |
| #19 징수 기관명 | 응답 | `테스트세무서 징수관` (19바이트 — 902614 `#20` 20바이트에 들어감) |
| #20 징수 과목 코드 | 응답 | `yyMM`(이번 달) + `5` + `10` (7자리, 추정) |
| #21 징수 과목명 | 응답 | `종합소득세` |
| #22 징수관 계좌번호 | 응답 | 숫자 6자리 R |
| #23 소계정 | 응답 | `0` |
| #24 회계 년도 | 응답 | 올해 `yyyy` |
| #30 본세 | 응답 | R(10,000~1,000,000), 10원 단위 |
| #31 농어촌특별세 / #32 교육세 | 응답 | `0` / `0` (종합소득세 — 부가세목 없음으로 단순화) |
| #25 납기내 금액 | 응답 | #30+#31+#32 |
| #26 납기일(납기내) | 응답 | today + R(7~30)일 |
| #27 납기후 금액 / #28 납기일(납기후) | 응답 | #25 / #26과 같게(샘플 영수증과 동일) |
| #29 고지서 유형 | 응답 | `2` (본세+농특세+교육세+가산금 표시) |
| #33~#39 | 응답 | 전부 `0` |
| #40 회계명 / #41 소관명 | 응답 | `일반회계` / `국세청` |
| #42 납부자 주소 | 응답 | `서울특별시 테스트구 테스트로 1` |
| #43 수입 신고 번호 | 응답 | 공백 |
| #44 납기 내후 구분 | 응답 | `1` (납기내) |
| #45 / #46 | 응답 | `N` / `N` |
| #47 / #48 | 응답 | `0` / `0` (미납 상태) |
| #49 고지 일자 | 응답 | today − R(5~20)일 |
| #50 대리 납부 허용 | 응답 | `1` |
| #51 기 납부 금액 / #52 잔여 납부할 금액 | 응답 | `0` / #25 |
| #53 분야 코드 / #56 예비 | 응답 | 공백 |
| #54 신용카드 납부 가능 | 응답 | `Y` |
| #55 납세자 유형 | 응답 | `10` |

### 800000

| 필드 | 요청 / 응답 | 값 |
|---|---|---|
| #15 납부세액 | 요청 | 연쇄(501008 #30+#31+#32). 초기값 R(10,000~1,000,000) |
| #16 납세자 유형 | 요청 | `10` |
| #17 카드사 코드 / #18 카드사명 | 응답 | SPEC p.14 코드표 12개 중 R 하나(코드·이름 쌍) |
| #19 체크카드 여부 | 응답 | `N`(80%) / `Y`(20%) |
| #20 / #21 | 응답 | `Y` / `N` |
| #22 카드 할부개월 LIST | 응답 | 신용: `020304050607080910111224`, 체크: 공백 |
| #23 포인트 할부개월 LIST | 응답 | 공백 |
| #26 수수료율 | 응답 | 신용 `0080` / 체크 `0050` |
| #24 수수료 금액 | 응답 | floor(요청 #15 × #26 / 10000), 원 단위 |
| #25 합계금액 | 응답 | 요청 #15 + #24 |
| #27 API 세부 응답코드 / #28 | 응답 | 공백 |

### 902614

| 필드 | 요청 / 응답 | 값 |
|---|---|---|
| #14 / #36 주민번호 | 요청 | 연쇄(501008 #17). 초기값 `900101` + `1` + 6자리 R(둘 같게) |
| #15 전자납부번호 | 요청 | 연쇄. 초기값은 501008 #14와 같은 규칙 |
| #16 납부 순번 | 요청 | `001` |
| #18 징수 과목 코드 | 요청 | 연쇄. 초기값 501008 #20 규칙 |
| #19 계좌번호 / #20 징수 기관명 / #21 징수 과목명 | 요청 | 연쇄. 초기값 숫자 6 R / `테스트세무서 징수관` / `종합소득세` |
| #22 소계정 / #23 회계년도 | 요청 | `0` / 올해 |
| #24 본세 / #25 교육세 / #26 농특세 | 요청 | 연쇄. 초기값 R / `0` / `0` |
| #27 납부 세액 / #28 수수료 / #29 총액 | 요청 | 연쇄(#27=본세+교육+농특, #28=800000 #24, #29=#27+#28). 초기값도 같은 식으로 일관되게(#28 = #27 × 0.8% 내림) |
| #30 납기 내후 구분 | 요청 | `1` |
| #31 납기 일자 | 요청 | 연쇄(501008 #26). 초기값 today + 14일 |
| #32 납부 일자 | 요청 | today |
| #33 카드사 코드 | 요청 | 연쇄(800000 #17). 초기값 `66` |
| #34 할부 개월 수 | 요청 | `00` |
| #35 연락 전화 번호 | 요청 | `010` + 숫자 8자리 R |
| #37 납부자 성명 | 요청 | `김테스트` |
| #39 / #41 / #49 | 요청 | `O` / `Q` / `0` (SPEC p.18) |
| #42 키오스크 고유번호 | 요청 | 지금처럼 설정값 |
| #38 카드소유주 주민번호 | 응답 | 요청 #36과 같게 |
| #40 기 납부 이용 시스템 | 응답 | 공백(기 납부 아님) |
| #52 선불카드 잔액 | 응답 | 공백(선불카드 아님) |

## 착수 전 전제 (코드 실측)

- 결제창 요청 생성: `PaymentTelegramTabViewModel.cs:247` `PosRandomValueGenerator.GenerateRandomRequest(_schema)`(생성자·"임의값 재생성").
  `#42`는 그 뒤 `_kioskIdProvider`로 덮는다(P30-7).
- 스텁: `StubVanRelayService.BuildFakeSuccess` — kiosk·원캡·None이 아닌 필드를 `PosRandomValueGenerator.GenerateValue`(금액 4개만 자릿수
  제한)로 채운 뒤 공통부 4개를 덮는다.
- 연쇄: `TelegramFieldChainMap`/`PaymentScreenViewModel.ApplyChainMappingsFrom`(응답 수신 시 다른 탭 요청 갱신). 연쇄 결과가 이 표의
  요청 초기값을 덮는다 — 이 Phase는 연쇄 맵을 바꾸지 않는다.
- `PosRandomValueGenerator` 사용처: `PosClientTestScenarios`, `PosRandomValueGeneratorSelfTest`, `TelegramFieldChainConverterSelfTest` — 그대로.

## 이 Phase에서 손대지 않는 것

연쇄 맵·변환기, 스키마, 원캡 중계 경로(`Services/Payment/`, `Services/Pos/`), `VanService`(실 VAN), KioskSim, 영수증 코드(`Services/Receipt/`, 조립기).

## Task별 담당 · 모델

| Task | 담당 | 모델 | 판단 이유 / 근거 |
|---|---|---|---|
| P37-1 테스트값 생성기 + 결제창·스텁 배선 + self | **`csharp-wpf-developer`**(새 세션) | Sonnet 5 | Protocol(생성기)·Services(스텁)·ViewModel(결제창)에 걸친 한 변경. 근거: 이 절 전체, PRD §2.2, `spec_open_questions.md` |
| P37-2 Opus 검증 | **Opus 직접** | Opus 5.5 | 값 표 대조 + 생성 전문 실제 덤프 + 기존 회귀 재실행 |
| P37-3 실기 확인 | **Opus 직접 + 사용자** | Opus 5.5 | 결제창 501008→800000→902614 후 영수증이 현실적으로 찍히는지(사용자 카드·PIN, 클릭은 Opus 가능) |

규모가 작고 원캡 중계 경로를 건드리지 않아 별도 체크포인트는 두지 않는다(P37-2에서 Opus 검증으로 대체).

## P37-1. 테스트값 생성기 · 배선 · self

- 새 클래스(제안: `Protocol/Pos/RealisticTestValueGenerator.cs` — 순수 함수, `Random`·`DateTime` 주입):
  - `PosTelegram GenerateRequest(PosTelegramSchema schema, Random random, DateTime now)` — 위 표의 요청 열.
  - `void FillStubResponse(PosTelegram response, PosTelegram request, Random random, DateTime now)` — 위 표의 응답 열(요청 값을
    참조하는 계산 포함: 800000 #24/#25, 902614 #38).
  - 전문 종류별로 필드 번호 → 값 규칙. **kiosk·디지털예산·인터넷지로 담당 필드가 전부 규칙에 있는지** self에서 스키마와 대조
    (빠진 필드가 있으면 실패).
- 배선: `PaymentTelegramTabViewModel`(요청 생성·재생성) → 새 생성기. `StubVanRelayService.BuildFakeSuccess` → `FillStubResponse` 후 공통부 4개
  덮기(순서 유지). 기존 금액 자릿수 제한 코드(`IsRealisticAmountField` 등)는 새 규칙에 흡수되면 삭제.
- self(`--receipt-print-test self`처럼 기존 하네스에 붙이거나 새 클래스): 고정 `Random(seed)`·고정 날짜로
  ① 각 전문 요청/응답의 모든 필드가 `PosField.Pad`를 통과(길이 초과 없음) ② 금액 정합(501008 #25=#30+#31+#32, 800000 #24=floor(#15×#26/10000),
  #25=#15+#24, 902614 초기값 #29=#27+#28) ③ 날짜 형식·범위(#26 > today, #49 < today) ④ 카드사 코드·이름이 SPEC 표 쌍과 일치 ⑤ 체크카드면
  #26=0050·할부 LIST 공백 ⑥ AHN 필드에 한글 "테스트" 표시 ⑦ 501008 응답 → 연쇄 → 902614 요청 → Phase 34 영수증 조립까지 돌려
  영수증 줄에 의미 없는 문자열이 없음(줄 미리보기를 로그에 출력).

**완료 조건**
- [x] 빌드 0/0, 새 self 통과
- [x] 기존 회귀 통과: `PaymentFlowTestScenarios`, `ReceiptPrintSelfTest`, `TelegramFieldChainConverterSelfTest`, `PosRandomValueGeneratorSelfTest`
- [x] 연쇄 맵·스키마·`Services/Payment/`·`Services/Pos/`·`Services/Receipt/` diff 없음

**검증 기록(2026-10-01)** — `csharp-wpf-developer` 구현(`Protocol/Pos/RealisticTestValueGenerator.cs`, self `--realistic-test-value-test`,
스텁·결제창 배선). self ①~⑦ 통과(600회: 날짜 3종 × 시드 200, 12개 카드사 전부 등장, 체크카드 20.5%), ⑦은 실제 스텁 → 연쇄 → 영수증 조립까지.
Opus가 생성기 규칙을 값 표와 대조하고, x86 리플렉션으로 새 self·연쇄 변환·payment-flow 192·영수증 self를 독립 재실행해 전부 통과 확인.
에이전트 판단(수용): 501008/800000 응답 `#5`=`000`, 응답 공통부 `#10/#13` 공백, 902614 `#11/#12`는 연쇄 맵에 없어 초기값 `01`/`2600000`이
그대로 전송됨(맵은 범위 밖), `#42` 생성기 규칙은 공백(결제창이 설정값으로 덮음), 요청 `#15`가 숫자가 아니면 800000 `#24/#25` 공백.
**후속(2026-10-01 사용자 지시)**: 902614 `#11`/`#12`를 연쇄 맵에 추가(501008 응답 `#11`/`#12` Direct, SPEC p.7 명문 근거) — Opus 직접(2건 추가),
연쇄 변환·Phase 37 self·payment-flow 192·영수증 self 재실행 통과. payment_relay PRD §3.3.2 표 갱신.

## P37-3. 실기 확인

결제창 501008 → 800000(카드) → 902614(카드·PIN) → 영수증. 납세자명 `김테스트`, 징수기관 `테스트세무서 징수관`, 금액·수수료·총액 정합,
납기일이 오늘 이후, 카드사명이 코드표 이름, 하단 수수료율 `0.8%`(또는 체크 `0.5%`)인지 확인.

**실행 결과(2026-10-01 10:51~10:53)** — `asInvoker` 임시 해제 후 Opus가 결제창 전송 클릭, 카드·PIN은 사용자. 501008·800000·902614 모두
`000`, 1059바이트 출력. 로그 원문으로 만든 기대 영수증: `김테스트` / `900101*******` / `테스트세무서 징수관` / 납기 2026.10.21 / 170,790원 · 수수료 1,366(0.8% 내림) ·
총 172,156 / `롯데카드`(스텁 무작위) / 실카드 `3564-1514-****-706*`. 이후 앱 종료·매니페스트 원복(커밋본과 동일). **출력물이 기대 영수증과 같음(사용자 확인).**

- [x] 실기 확인 완료

## Phase 37 — 완료 선언 (2026-10-01)

결제창 요청값과 VAN 스텁 응답값이 "테스트" 표시가 붙은 현실적인 값으로 바뀌어, 영수증이 실제와 비슷하게 찍힌다(금액·수수료·총액 정합).
후속으로 902614 `#11`/`#12` 연쇄 추가.
