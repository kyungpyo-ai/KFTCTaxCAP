# 실행계획서: 국세 납부확인증 영수증 출력 (4차 범위)

> `PRD.md`(무엇을) → `ROADMAP.md`(순서) → **이 문서(Task 단위 지시·완료 조건)**. 각 Phase의 절은 그 Phase
> **착수 직전**에 작성한다. 현재 작성된 절: **Phase 33(완료, 2026-09-30)**. Phase 34~36은 착수 직전에 추가.

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
| 전원 끔 | **사용자 결정으로 건너뜀** — 상태 조회 없이 성공으로 기록될 가능성(미실측) |
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
