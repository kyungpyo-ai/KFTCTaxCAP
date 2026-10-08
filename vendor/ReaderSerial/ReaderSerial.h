#pragma once

#ifdef READERDLL_EXPORTS
#define READER_API extern "C" __declspec(dllexport)
#else
#define READER_API extern "C" __declspec(dllimport)
#endif

// CALLBACK eventType 값. (PRD §7.6)
// 2026-08-05 죽은 값(CONNECTED/DISCONNECTED/SEND_ERROR) 제거 및 재번호.
// 2026-08-12: 1부터 시작하던 것을 0부터 시작하도록 재번호(상대 순서는 불변) —
// 1차 개발(리더기) 때 만들어진 이 enum은 1부터, 2차 개발(핀패드) 때 새로 만들어진
// PinpadEventType은 관용적으로 0부터 시작해 서로 시작 번호가 어긋나 있었다.
// 두 enum의 공통 개념(RESPONSE/TIMEOUT)이 정확히 같은 값을 갖도록 사용자 요청으로
// 통일했다. 이후 값은 고정, 이전 값과 호환되지 않음.
enum ReaderEventType
{
    READER_EVENT_RESPONSE = 0,
    READER_EVENT_TIMEOUT,
    READER_EVENT_LRC_ERROR,
    READER_EVENT_RECEIVE_ERROR,
    READER_EVENT_UNSOLICITED, // POS 요청 없이 리더기가 자발적으로 보낸 전문(예: 0x76 카드 감지). operationState는 변경되지 않는다.

    // 누적 수신 버퍼에 미완성 프레임이 남아있는 채로 일정 시간(내부
    // TimeoutPolicy::FRAME_STALL_TIMEOUT_MS, 2026-07-30 확정 1000ms) 동안 추가
    // Byte가 도착하지 않아 그 미완성 프레임을 버렸을 때 전달된다(Inter-byte
    // Timeout, PRD §13). 명령 레벨 Response/Trade Timeout(READER_EVENT_TIMEOUT)과는
    // 독립적으로 동작하며, operationState/CommandStateManager는 건드리지 않는다 —
    // 순수하게 프레임 버퍼 재동기화 목적이다.
    READER_EVENT_FRAME_STALL,

    // 2026-10-08: 요청을 보내고 응답을 기다리는 중에 리더기가 응답 대신 NAK(전문 구분
    // 0x15) 프레임을 보냈을 때 전달된다. SPEC에는 리더기→POS 방향의 NAK가 정의돼
    // 있지 않지만 실장비에서 관측됐다(0x2B 거래정보 요청에 대한 응답 자리). 이 경우
    // 해당 요청은 응답을 받지 못한 것이 확실하므로 DLL이 즉시 IDLE로 되돌리며,
    // POS는 곧바로 다음 명령을 보낼 수 있다. data는 항상 nullptr/0이다. 요청 대기
    // 중이 아닐 때 도착한 NAK는 통지 없이 로그만 남는다. (commandCode 값은 위
    // READER_CALLBACK 설명의 이벤트별 규칙을 따른다.)
    READER_EVENT_NAK
};

// 리더기 응답/이벤트 CALLBACK. (PRD §8.2)
//
// data는 이 CALLBACK 호출이 실행되는 동안에만 유효하다. 호출이 반환된 직후
// DLL은 내부 임시 버퍼를 0으로 덮어쓰고 정리하므로, 이후에도 데이터가 필요하면
// CALLBACK 내부에서 별도 메모리로 복사해야 한다.
//
// SPEC 업무 응답 코드(00~23, ASCII 2byte)는 이 함수의 commandCode 파라미터가
// 아니라 data의 첫 2byte에 실려 온다 — DLL 자체의 ReaderResult(음수 오류코드)와는
// 완전히 별개 체계이며 DLL은 이 값을 해석하지 않고 그대로 전달만 한다. 코드표는
// DOC/개발문서/실행계획서.md Phase 7 P7-4 참조 (Phase 7, P7-4).
//
// 2026-08-12: resultCode 파라미터 제거 — eventType(ReaderEventType)과 1:1로
// 고정된 값이라 완전히 중복 정보였다(당시 RESPONSE/UNSOLICITED→READER_OK,
// TIMEOUT/LRC_ERROR/RECEIVE_ERROR/FRAME_STALL은 각각 전용 ReaderResult 값 —
// 그 전용 값들은 2026-08-13에 완전히 죽은 코드로 확인되어 ReaderErrors.h에서
// 제거됐다). 필요하면 POS 쪽에서 eventType으로부터 직접 유도할 수 있다.
//
// 2026-08-12 추가: 3번째 파라미터 이름을 responseCode에서 commandCode로
// 통일했다(값/채워지는 로직은 전혀 바뀌지 않은 순수 이름 변경) — PINPAD_CALLBACK의
// commandCode 파라미터와 동일한 어휘를 쓰기 위함이다.
//
// commandCode에 실리는 값 (이벤트별, 2026-10-08 정리):
//   - 응답/프레임을 "받은" 이벤트 - RESPONSE / UNSOLICITED / LRC_ERROR:
//       실제로 수신한 프레임의 CommandCode (LRC_ERROR는 손상됐더라도 받은 프레임의 코드).
//   - 응답을 "받지 못한" 이벤트 - TIMEOUT / FRAME_STALL / NAK / RECEIVE_ERROR:
//       요청 코드가 아니라 POS가 "지금 기다리고 있던 응답 코드"(예: 0x2B 요청이면 0x3B).
//       대기 중인 요청이 없으면 0. POS는 이 값으로 자기 요청의 실패를 식별할 수 있다.
//   (2026-10-08 이전에는 RECEIVE_ERROR만 항상 0이었으나 위 규칙에 맞춰 통일했다.)
typedef void (__stdcall *READER_CALLBACK)(
    int readerId,
    int eventType,
    unsigned char commandCode,
    const unsigned char* data,
    int dataLength,
    void* userContext
);

// 핀패드 CALLBACK eventType 값. (PRD_핀패드.md §8.3)
// 2026-08-12 전면 재설계: 과거에는 실패 원인을 PINPAD_EVENT_ERROR 하나로 뭉쳐
// failInfo(3byte: fc/subCode/reason) payload로 data에 실어 보냈으나, 리더기
// ReaderEventType(원인마다 최상위 이벤트)보다 다루기 어렵다는 사용자 판단에 따라
// 리더기와 동일하게 원인 하나하나를 최상위 eventType으로 승격했다. failInfo/
// PinpadFailReason 개념은 완전히 제거됐다 - eventType 자체가 그 정보를 대신하므로
// 완전히 중복이었다(리더기의 resultCode 제거와 동일한 논리). PINPAD_FAIL_TIMEOUT이
// 옛 failInfo에서 0x01이었던 것과 PINPAD_EVENT_TIMEOUT = 1이 겹치는 건 우연이며
// 의미 있는 매핑이 아니다 - 그냥 선언 순서대로 번호가 매겨진 것뿐이다.
enum PinpadEventType
{
    PINPAD_EVENT_RESPONSE      = 0,   // 조합 명령 정상 완료
    PINPAD_EVENT_TIMEOUT       = 1,   // 단계 응답 대기 시간 초과 (복구 처리 완료 후 발생)
    PINPAD_EVENT_NAK           = 2,   // NAK 수신 (0xF1/0xF3 단계는 1회 재전송 후에도 실패, 0xF7 단계는 즉시)
    PINPAD_EVENT_LRC_ERROR     = 3,   // 수신 전문 LRC 불일치
    PINPAD_EVENT_TAMPER        = 4,   // 0xFA 응답, Tamper 파손
    PINPAD_EVENT_SEND_FAIL     = 5,   // 포트 쓰기 실패
    PINPAD_EVENT_RECEIVE_ERROR = 6,   // 포트 물리 장애 (리더기 READER_EVENT_RECEIVE_ERROR와 동일 원인)
    PINPAD_EVENT_FRAME_STALL   = 7    // 미완성 프레임 정체 (리더기 READER_EVENT_FRAME_STALL과 동일 원칙)
};

// POS에 노출하는 핀패드 가상 명령 코드. (PRD_핀패드.md §7.3)
//
// SPEC이 정의하는 값이 아니라 DLL이 조합 시퀀스를 대신 수행하기 위해 자체적으로
// 부여한 값이다. 1Byte 코드 대역 배치(2026-09-07 Phase 18 재번호 기준):
//   - 리더기 SPEC 실제 명령 코드: 0x60~0x9F 부근 (CommandCodes.h)
//   - 핀패드 SPEC 실제 Fc 코드:   0xF0~0xFA, 0x06, 0x15 (PinpadCommandCodes.h)
//   - 핀패드 가상 명령 코드(본 enum): 0xD0~0xD4
//   - 사인패드 SPEC 실제 명령 코드: 0xA0~0xA8, 0xAC 등 (Phase 19+, SignpadCommandCode)
// 2026-09-07: 사인패드 SPEC이 실제 명령 코드로 0xA0~0xAC를 점유하고 있어
// 충돌하므로, 사인패드 코드를 작성하기 전에 핀패드 가상 코드를 0xA0~0xA4에서
// 0xD0~0xD4로 재번호했다(Phase 18). 0xD0~0xDF는 리더기/핀패드/사인패드 SPEC
// 어디에도 없는 완전 미사용 대역임을 확인했다 - 0xA0 대역으로 되돌리지 말 것.
enum PinpadCommandCode
{
    PINPAD_CMD_INIT         = 0xD0,   // 핀패드 초기화
    PINPAD_CMD_PIN_PASSWORD = 0xD1,   // 비밀번호 핀입력
    PINPAD_CMD_PIN_NUMBER   = 0xD2,   // 번호 핀입력
    PINPAD_CMD_PIN_DES      = 0xD3,   // DES 암호화 핀입력
    PINPAD_CMD_PIN_SEED     = 0xD4    // SEED 암호화 핀입력
};

// 핀패드 응답/이벤트 CALLBACK. (PRD_핀패드.md §8.3)
//
// data 포인터의 수명 규칙은 READER_CALLBACK과 동일하다 - CALLBACK 반환 즉시
// 0으로 덮어쓰고 해제하므로, 이후 필요한 데이터는 CALLBACK 내부에서 복사해야 한다.
// PINPAD_EVENT_RESPONSE일 때만 실제 핀패드 응답 데이터가 실리고, 그 외 모든
// 이벤트(TIMEOUT/NAK/LRC_ERROR/TAMPER/SEND_FAIL/RECEIVE_ERROR/FRAME_STALL)는
// data = nullptr, dataLength = 0이다 (READER_CALLBACK과 동일한 패턴).
//
// 2026-08-12 전면 재설계: 3번째 파라미터가 result(ReaderResult, 과거 항상
// READER_ERR_PINPAD_STEP_FAILED로 고정되어 정보량이 없었다)에서 commandCode로
// 바뀌었다 - Pinpad_SendCommand(readerId, commandCode, ...)에 POS가 넘긴 그
// PinpadCommandCode(0xD0~0xD4) 값 그대로다. DLL 사용자는 내부 핀패드 SPEC(Fc/
// subCode)을 모르는 상황이므로, 시퀀스가 내부적으로 몇 단계(0xF1->0xF3->0xF7 등)를
// 거치든 이 값은 항상 POS가 요청할 때 쓴 그 commandCode로 고정된다 - 리더기
// responseCode가 "그 이벤트가 어떤 요청/응답에 관한 것인지" 알려주는 것과 동일한
// 역할을, 내부 구현 디테일(Fc) 대신 POS가 이해하는 공개 어휘로 수행한다. subCode는
// 콜백에 실어 보내지 않는다 - POS가 이미 자신이 보낸 commandCode로 알고 있는
// 정보라 중복이기 때문이다.
typedef void (__stdcall *PINPAD_CALLBACK)(
    int readerId,
    int eventType,
    unsigned char commandCode,
    const unsigned char* data,
    int dataLength,
    void* userContext
);

// 사인패드 CALLBACK eventType 값. (PRD_사인패드.md §8.4, Phase 19)
//
// 0~7은 PinpadEventType과 정확히 같은 자리를 쓴다(4번만 REJECTED로 의미가
// 갈린다 — 사인패드는 TAMPER 개념이 없고, 대신 0xA0 요청에 ESC로 거부당하는
// 경우가 그 자리를 대신한다). POS가 세 장비의 실패 계열 이벤트를 같은
// switch/case로 처리할 수 있도록 하기 위한 의도적 슬롯 일치다. 8부터는
// 사인패드 전용(0xA1 진행 중 실시간 좌표 스트림) — 오류가 아니라 정상 경로다.
enum SignpadEventType
{
    SIGNPAD_EVENT_RESPONSE      = 0,  // 유일한 정상 완료. commandCode로 결과 구분(0xA0/0xB2/0xBC/0xB6)
    SIGNPAD_EVENT_TIMEOUT       = 1,
    SIGNPAD_EVENT_NAK           = 2,  // 서명 데이터 크기 초과(SPEC §5.3.2)
    SIGNPAD_EVENT_LRC_ERROR     = 3,
    SIGNPAD_EVENT_REJECTED      = 4,  // 0xA0 요청에 ESC 응답 — VAN사 식별코드 불일치
    SIGNPAD_EVENT_SEND_FAIL     = 5,
    SIGNPAD_EVENT_RECEIVE_ERROR = 6,
    SIGNPAD_EVENT_FRAME_STALL   = 7,
    SIGNPAD_EVENT_COORD         = 8,  // 0xA1 진행 중 0xB1 좌표. data = {x, y}, dataLength = 2. 상태 불변
    SIGNPAD_EVENT_COORD_CLEAR   = 9   // 0xA1 진행 중 0xB1 좌표가 0xEE/0xEE(전체 Clear). data = nullptr. 상태 불변
};

// POS에 노출하는 사인패드 명령 코드. (PRD_사인패드.md §8.3)
//
// 핀패드(PinpadCommandCode)가 DLL 자체 가상 코드를 쓰는 것과 달리, 사인패드는
// 명령 하나가 전문 하나에 정확히 대응해 감출 조합 시퀀스가 없으므로 SPEC 실제
// Command ID를 그대로 공개 명령 코드로 쓴다. 1Byte 대역 배치(Phase 18/19 재번호
// 확정 후):
//   0x00~0x9E   리더기 SPEC (미구현 명령 포함 예약, CommandCodes.h)
//   0xA0~0xBC   사인패드 SPEC 실제 코드 (본 enum, SignpadCommandCodes.h)
//   0xC0        사인패드 (카드정보, 범위 밖)
//   0xD0~0xD4   핀패드 가상 명령 코드 (PinpadCommandCode)
//   0xE0, 0xE1  사인패드 (한화 갤러리아, 범위 밖)
//   0xF0~0xFA   핀패드 SPEC 와이어 Fc (내부 전용, PinpadCommandCodes.h)
// SPEC 원문의 0xA2/0xAC는 0xA1 진행 중에만 허용되는 "찌르기" 명령이며 독립
// 사이클을 열지 않는다 — PRD_사인패드.md §7.2 참조. Phase 19는 타입만 정의하며
// 이 규칙의 강제는 Phase 20(SignpadSequence)에서 구현한다.
enum SignpadCommandCode
{
    SIGNPAD_CMD_INIT        = 0xA0,   // 초기화 요청
    SIGNPAD_CMD_SIGN_INPUT  = 0xA1,   // 서명 입력 요청
    SIGNPAD_CMD_SIGN_SEND   = 0xA2,   // 서명 전송 요청 (0xA1 진행 중에만 허용)
    SIGNPAD_CMD_SIGN_CANCEL = 0xAC,   // 서명 취소 요청 (0xA1 진행 중에만 허용)
    SIGNPAD_CMD_CASH_RANDOM = 0xA6    // 현금카드 입력 요청(난수)
};

// 사인패드 응답/이벤트 CALLBACK. (PRD_사인패드.md §8.4)
//
// data 포인터의 수명 규칙은 READER_CALLBACK/PINPAD_CALLBACK과 동일하다 -
// CALLBACK 반환 즉시 0으로 덮어쓰고 해제하므로, 이후 필요한 데이터는 CALLBACK
// 내부에서 별도 메모리로 복사해야 한다.
//
// commandCode의 의미는 PINPAD_CALLBACK과 다르다 — "요청한 가상 코드 고정"이
// 아니라 리더기 규약과 동일하게 **수신한 실제 응답 Command ID**다. 사인패드는
// SPEC 실제 코드를 그대로 공개하는 데다, 하나의 요청(0xA1)이 0xB2/0xBC 두
// 결과로 갈릴 수 있어 응답 코드가 실려야 POS가 구분할 수 있기 때문이다.
//
//   이벤트                    | commandCode              | data
//   ------------------------- | ------------------------- | -----------------------------
//   RESPONSE (0xA0 ACK)       | 0xA0 (요청 코드 그대로)   | nullptr — 응답이 제어 전문이라 Command ID 없음
//   RESPONSE (0xA2/0xAC 종결) | 0xB2 / 0xBC (수신한 응답) | 응답 Data. 0xBC는 Data가 없어 nullptr
//   RESPONSE (0xA6)           | 0xB6 (수신한 응답)        | 응답 Data
//   REJECTED (0xA0 ESC)       | 0xA0                      | nullptr
//   COORD / COORD_CLEAR       | 0xB1 (좌표 전문 코드)     | COORD만 {x, y} dataLength=2, CLEAR는 nullptr
//   그 외 실패 계열           | 기다리고 있던 응답 코드    | nullptr
//
// Phase 19는 타입 선언만 하며, 위 표의 실제 전달 로직은 Phase 20(SignpadSequence)
// 이후에 구현한다.
typedef void (__stdcall *SIGNPAD_CALLBACK)(
    int readerId,
    int eventType,
    unsigned char commandCode,
    const unsigned char* data,
    int dataLength,
    void* userContext
);

// 2026-09-07 (Phase 21): signpadCallback 인자 추가 (6 -> 7). 사인패드 미사용
// 포트는 nullptr을 넘긴다. 세 CALLBACK(readerCallback/pinpadCallback/
// signpadCallback)이 전부 nullptr이면 READER_ERR_INVALID_ARGUMENT를 반환한다
// (PRD_사인패드.md §8.1). 등록 함수를 별도로 두지 않는 이유는 §8.1 주석 참조 -
// "CALLBACK은 포트를 열 때 등록한다"는 기존 원칙을 유지한다.
READER_API int __stdcall Reader_OpenPort(
    int portNumber,
    int baudRate,
    READER_CALLBACK readerCallback,
    PINPAD_CALLBACK pinpadCallback,
    SIGNPAD_CALLBACK signpadCallback,
    void* userContext,
    int* outReaderId
);

READER_API int __stdcall Reader_ClosePort(
    int readerId
);

READER_API int __stdcall Reader_IsPortOpen(
    int readerId
);

READER_API int __stdcall Reader_SendCommand(
    int readerId,
    unsigned char commandCode,
    const unsigned char* data,
    int dataLength
);

// 핀패드 조합 명령 전송. (PRD_핀패드.md §8.2)
//
// commandCode는 PinpadCommandCode 값이다. 5종 명령(INIT/PIN_PASSWORD/PIN_NUMBER/
// PIN_DES/PIN_SEED) 모두 Phase 14부터 동작한다.
//
// 2026-08-13: 파라미터 타입을 int에서 unsigned char로 통일했다 — PinpadCommandCode
// 값(0xD0~0xD4)이 Reader_SendCommand의 commandCode(unsigned char)와 마찬가지로
// 1byte 범위 안에 들어가는데도 타입이 달랐던 것은 설계 근거 없는 우연한 불일치였다
// (Phase 14 최초 설계 당시 관례를 따르지 않은 것으로 추정 — 문서화된 이유는 없었다).
// 다시 int로 되돌리지 말 것.
//
// 이 포트에 pinpadCallback이 등록되지 않았으면(NULL) 타임아웃 없이 즉시
// READER_ERR_PINPAD_NOT_SUPPORTED를 반환한다.
READER_API int __stdcall Pinpad_SendCommand(
    int readerId,
    unsigned char commandCode,
    const unsigned char* data,
    int dataLength
);

// 사인패드 명령 전송. (PRD_사인패드.md §8.2)
//
// commandCode는 SignpadCommandCode 값이다(사인패드는 SPEC 실제 Command ID를
// 그대로 공개 명령 코드로 쓴다 - §8.3). 0xA0/0xA1/0xA6은 독립 명령 사이클을
// 새로 열고(SignpadSequence_Begin), 0xA2/0xAC는 0xA1 진행 중에만 허용되는
// "찌르기"다(SignpadSequence_Poke, 진행 중이 아니면 READER_ERR_COMMAND_NOT_ALLOWED).
// 그 외 값은 READER_ERR_INVALID_ARGUMENT.
//
// 이 포트에 signpadCallback이 등록되지 않았으면(nullptr) 타임아웃 없이 즉시
// READER_ERR_SIGNPAD_NOT_SUPPORTED를 반환한다.
//
// 서명 완료를 기다리지 않고 전문 하나만 전송한 뒤 즉시 반환한다 - 결과는
// SIGNPAD_CALLBACK으로 통지된다(PRD_사인패드.md §11-3).
READER_API int __stdcall Signpad_SendCommand(
    int readerId,
    unsigned char commandCode,
    const unsigned char* data,
    int dataLength
);
