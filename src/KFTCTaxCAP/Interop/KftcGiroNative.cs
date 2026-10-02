using System.Runtime.InteropServices;

namespace KFTCTaxCAP.Interop;

/// <summary>
/// 네이티브 경계 — <c>KFTC_GIRO.dll</c>의 Export 함수 <c>FNAISCRDVAN</c> P/Invoke 선언과 버퍼 상수만.
/// 업무 로직 없음(docs/payment_relay/ROADMAP.md "계층 구조" — Interop은 P/Invoke 선언만 담당,
/// <see cref="NativeLibrary"/>가 세운 규칙 그대로).
///
/// <b>계약 출처</b>: <c>KFTC_GIRO.dll</c>은 별도 SPEC 문서·샘플 소스가 없다 — <c>docs/payment_relay/PRD.md</c>
/// §2.3이 현재 확보된 유일한 계약 정보다(development_plan.md Phase 20 실행계획서).
///
/// <b>문자열 인자를 <see cref="string"/>이 아니라 <see cref="byte"/>[]로 선언한 이유</b>: <c>char*</c>를
/// <c>string</c> + <c>CharSet.Ansi</c>로 마샬링하면 마샬러가 **프로세스의 ANSI 코드페이지**로 변환한다.
/// 한국어 Windows에서는 우연히 949(CP949)와 일치하지만, 로캘이 다른 PC에서는 조용히 깨진다(한글 필드가
/// <c>?</c>로 치환). 전문 본문은 이미 <c>PosMessageEncoding.Value</c>(CP949 고정)로 인코딩된 바이트이므로,
/// 그걸 <c>string</c>으로 되돌렸다가 마샬러가 다시 인코딩하게 두는 것은 불필요한 왕복이자 손실 지점이다.
/// <c>byte[]</c>는 마샬러가 손대지 않고 고정(pinned)해서 포인터만 넘기므로 코드페이지 변환이 개입할
/// 여지가 없다. <c>CharSet</c>을 지정하지 않는 것도 같은 이유.
///
/// <b>32bit 전용</b>: <c>KFTC_GIRO.dll</c>은 32bit(x86) DLL이다 — <c>KFTCTaxCAP.csproj</c>의
/// <c>PlatformTarget</c>이 <c>x86</c>으로 설정돼 있어야 한다(이미 설정됨, Phase 8).
///
/// <b>2026-09-29(Phase 32) DLL 교체 — 6번째 인자 <c>in_hostCode</c> 추가.</b> 새 빌드(PE 타임스탬프
/// 2026-09-14)로 <c>FNAISCRDVAN</c> 시그니처가 5인자→6인자로 바뀌었다. stdcall은 피호출자가 스택을
/// 정리하므로(구 <c>ret 0x14</c> → 신 <c>ret 0x18</c>) 인자 개수가 하나라도 틀리면 호출 직후 스택이
/// 깨진다 — 이 선언은 새 DLL 전용이며 구 DLL(5인자)과는 더 이상 호환되지 않는다.
/// 계약 출처: <c>docs/payment_relay/PRD.md</c> §2.3 + 2026-09-29 역어셈블.
///
/// <c>in_hostCode</c> 제약(DLL이 검사하지 않으므로 호출자가 반드시 지킨다, PRD §2.3):
/// <list type="bullet">
/// <item>DLL은 이 값을 <b>길이 검사 없이</b> 13바이트 스택 버퍼에 복사한다 → ASCII 12바이트 이하 +
/// NUL 종단이어야 한다. 넘으면 DLL 내부 스택 오버플로.</item>
/// <item><b>NULL을 넘기면 DLL 안에서 AV(크래시)</b> — NULL 검사가 없다.</item>
/// <item><b>빈 문자열을 넘기면 DLL이 조용히 <see cref="HostCodeKeyDownload"/>로 대체</b>한다 → 결제
/// 전문이 키다운로드 호스트로 잘못 라우팅된다.</item>
/// <item>ASCII 범위(0x20~0x7E) 밖의 문자가 섞이면 이 값을 ASCII 바이트로 인코딩하는 과정에서
/// <c>?</c>로 조용히 치환되거나 의도치 않은 바이트가 13바이트 버퍼에 실릴 수 있다.</item>
/// </list>
/// 위 네 위반(빈 값/NULL/12바이트 초과/비ASCII)은 <see cref="Services.Van.FnaisCrdVanInvoker"/>가
/// DLL 호출 전에 검증해서 막는다(P32-3) — 이 파일은 P/Invoke 선언과 상수만 담당한다.
/// </summary>
internal static class KftcGiroNative
{
    /// <summary><c>outData</c> 버퍼 크기(바이트). ROADMAP 확정값 — SPEC 최대 전문 길이(902614, 1500바이트)의
    /// 2.7배 여유.</summary>
    internal const int OutDataBufferSize = 4096;

    /// <summary><c>out_szRetCode</c> 버퍼 크기(바이트). <b>검증되지 않은 가정</b> — PRD §2.3에 "DLL 처리
    /// 결과코드가 반환된다"고만 적혀 있고 길이 언급이 없다. 결과코드가 256바이트를 넘을 개연성은 사실상
    /// 없다고 보고 넉넉히 잡은 값이며, 실서버 연동 후 실측으로 검증해야 한다.</summary>
    internal const int RetCodeBufferSize = 256;

    /// <summary><c>FNAISCRDVAN</c> 호출 타임아웃(초). PRD §2.3 사용 예(<c>FNAISCRDVAN("OT", input, data,
    /// ret_code, 60)</c>)를 그대로 따른 값.</summary>
    internal const int DefaultTimeoutSeconds = 60;

    /// <summary><c>in_szMode</c> — 외부망 테스트 서버. PRD §4.10 / 2026-08-18 확정. 운영("R")/내부망
    /// 테스트("IT")는 아직 쓰지 않는다.</summary>
    internal const string ModeExternalTest = "OT";

    /// <summary><c>in_hostCode</c> ASCII 최대 길이(NUL 제외, 바이트 수). PRD §2.3 — 12바이트를 넘으면
    /// DLL 내부 13바이트 스택 버퍼가 오버플로한다.</summary>
    internal const int HostCodeMaxLength = 12;

    /// <summary><c>in_hostCode</c> — 501008 국고 상세 고지내역 조회. PRD §2.3 표.</summary>
    internal const string HostCodeNoticeInquiry = "GIROINQUIRY";

    /// <summary><c>in_hostCode</c> — 800000 카드 정보 조회. PRD §2.3 표.</summary>
    internal const string HostCodeCardInfo = "GIROCARDINFO";

    /// <summary><c>in_hostCode</c> — 902614 국고 신용카드 승인 요청(보안단말기 거래용). PRD §2.3 표.</summary>
    internal const string HostCodeCardApproval = "GIROCARDAPRV";

    /// <summary><c>in_hostCode</c> — 키 다운로드/번들링(운영 기능 §3, docs/operations/PRD.md). PRD §2.3
    /// 표. 빈 문자열을 넘겼을 때 DLL이 조용히 대체하는 값과 동일 — 그래서 빈 값이 위험하다.</summary>
    internal const string HostCodeKeyDownload = "GIROKEYDOWN";

    /// <summary>
    /// VAN 서버에 결제 전문을 중계한다.
    /// </summary>
    /// <param name="in_szMode">"R"(운영)/"IT"(내부망테스트)/"OT"(외부망테스트)의 ASCII 바이트 + NUL 종단.</param>
    /// <param name="inData">CP949로 인코딩된 요청 전문 본문 + NUL 종단.</param>
    /// <param name="outData">응답 전문을 받을 버퍼(<see cref="OutDataBufferSize"/>바이트로 호출자가 할당).</param>
    /// <param name="out_szRetCode">DLL 처리 결과코드를 받을 버퍼(<see cref="RetCodeBufferSize"/>바이트로
    /// 호출자가 할당).</param>
    /// <param name="int_iTimeout">타임아웃(초).</param>
    /// <param name="in_hostCode">전문별 호스트 코드(<see cref="HostCodeNoticeInquiry"/> 등)의 ASCII 바이트 +
    /// NUL 종단. <see cref="HostCodeMaxLength"/>바이트 이하 + NUL 종단이어야 한다(클래스 주석 제약 참고) —
    /// 이 검증은 호출자(<see cref="Services.Van.FnaisCrdVanInvoker"/>)의 책임이며 이 P/Invoke 선언 자체는
    /// 검사하지 않는다.</param>
    /// <returns><c>0</c>: DLL 통신 성공(실제 승인/거절은 <paramref name="outData"/>를 봐야 앎).
    /// <c>-1</c>: DLL 통신 실패.</returns>
    [DllImport("KFTC_GIRO.dll", CallingConvention = CallingConvention.StdCall)]
    internal static extern int FNAISCRDVAN(
        byte[] in_szMode, byte[] inData, byte[] outData, byte[] out_szRetCode, int int_iTimeout,
        byte[] in_hostCode);
}
