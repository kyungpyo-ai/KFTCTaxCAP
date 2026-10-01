namespace KFTCOneCAP.Wpf.Services.Diagnostics;

/// <summary>
/// Phase 27 P27-8(e) — "전문이 나가지 않는 내부 사건"에 부여하는 <c>S</c> 계열 코드
/// (<c>docs/operations/fault_alert_catalog.md</c> §2.2가 정본). <b>일부러
/// <see cref="Payment.PosResultCodeMapper"/>에 넣지 않는다</b> — 그 클래스의 계약은 "POS 응답 전문
/// <c>#7</c>에 실릴 값"인데, <c>S</c> 계열은 정의상 POS에 응답이 나가지 않는 사건에만 붙는 코드라
/// 전문에 실리지 않는다(§1.12.2 #2). 같은 클래스에 섞으면 그 계약이 흐려진다.
///
/// 호출부에 <c>"S01"</c> 같은 리터럴을 흩지 않기 위해 이 한 지점에 모은다. 다음 번호는
/// <c>S13</c>이다(§2.2, §4 — S12는 전표 출력 실패, docs/receipt_print Phase 34).
/// </summary>
internal static class InternalFaultCodes
{
    /// <summary>POS 소켓 리스닝 실패.</summary>
    internal const string ListenFailure = "S01";

    /// <summary>수락 루프 사망(이후 새 연결 영구 불가).</summary>
    internal const string AcceptLoopDied = "S02";

    /// <summary>무결성 이력 저장·조회 실패(저장/조회/금일조회 3곳 공유).</summary>
    internal const string IntegrityStoreFailure = "S03";

    /// <summary>로그 보관 정리 실패(삭제 실패).</summary>
    internal const string LogRetentionFailure = "S04";

    /// <summary>전역 키보드 훅 설치 실패.</summary>
    internal const string KeyboardHookFailure = "S05";

    /// <summary>응답 전달 실패(완료 콜백 처리 중 예외 — POS가 결과 코드를 받지 못함).</summary>
    internal const string ResponseDeliveryFailure = "S06";

    /// <summary>응답 송신 실패(stream.Write 실패로 완성된 프레임 폐기).</summary>
    internal const string ResponseSendFailure = "S07";

    /// <summary>응답 직렬화 실패(ToFrame() 예외로 송신 시도조차 못 함).</summary>
    internal const string ResponseSerializationFailure = "S08";

    /// <summary>동시 연결 상한 초과 거부.</summary>
    internal const string ConnectionLimitExceeded = "S09";

    /// <summary>연결 처리 중 예외(HandleConnection 최상위 catch).</summary>
    internal const string ConnectionHandlingException = "S10";

    /// <summary>DLL 로드 스모크 실패(기동 시점 사전 점검).</summary>
    internal const string DllLoadSmokeFailure = "S11";

    /// <summary>
    /// 전표(영수증) 출력 실패(docs/receipt_print/PRD.md §5, Phase 34 P34-2). 사유(포트 설정 오류/포트
    /// 열기 실패/전송 오류/양식 조립 오류 등)는 로그 본문에 적는다 — 코드는 <c>S12</c> 하나뿐이다.
    /// <b>S 계열의 유일한 예외로 장애 알림 대상이 아니다(N)</b> — <c>docs/operations/fault_alert_catalog.md</c>
    /// §2.2, <c>FaultAlertJudge</c>의 기본 분기가 모르는 코드를 <c>N</c>으로 판정하므로 이 코드 자체가
    /// <c>FaultAlertJudge</c> 수정 없이 <c>N</c>이 된다.
    /// </summary>
    internal const string ReceiptPrintFailure = "S12";
}
