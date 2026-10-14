using System;
using System.Collections.Generic;
using KFTCTaxCAP.Protocol.Pos;

namespace KFTCTaxCAP.Services.Diagnostics;

/// <summary>
/// 로그 줄의 코드 슬롯만 보고는 어떤 오류인지 알기 어려워, <see cref="FileLogSink.Write"/>가 코드가 있는 레코드마다
/// 다음 줄에 별도로 설명을 찍는다(<see cref="FileLogSink.BuildCodeNoteBytes"/> 참고).
///
/// Phase 45(PRD §17.3): 전문 응답코드 해설은 <see cref="PosResponseCodeCatalog"/>로 일원화했다 —
/// 형식 <c>{내용} · {출처}</c>. <c>000</c>은 설명 줄 없음, <c>S</c>계열은 출처 없이 설명만,
/// 정의되지 않은 코드는 <see langword="null"/>(추측 금지). KioskSim은 코드를 공유하지 않고 따로 옮겨 적는다(P19-2).
/// </summary>
internal static class LogCodeCatalog
{
    /// <summary>
    /// 전문이 나가지 않는 내부 사건(<see cref="InternalFaultCodes"/>, <c>S</c>계열)만 이 표가 맡는다 — 출처 없이 설명만.
    /// 전문 응답코드(공통·인터넷지로·KFTCVAN·원캡 E/R/D)는 Phase 45에서 <see cref="PosResponseCodeCatalog"/>로 일원화했다.
    /// </summary>
    private static readonly Dictionary<string, string> Descriptions = new(StringComparer.OrdinalIgnoreCase)
    {
        // 전문이 나가지 않는 내부 사건(InternalFaultCodes, fault_alert_catalog.md §2.2).
        ["S01"] = "POS 소켓 리스닝 실패",
        ["S02"] = "수락 루프 사망(이후 새 연결 영구 불가)",
        ["S03"] = "무결성 이력 저장·조회 실패",
        ["S04"] = "로그 보관 정리 실패(삭제 실패)",
        ["S05"] = "전역 키보드 훅 설치 실패",
        ["S06"] = "응답 전달 실패(완료 콜백 처리 중 예외 — POS가 결과 코드를 받지 못함)",
        ["S07"] = "응답 송신 실패(완성된 프레임 폐기)",
        ["S08"] = "응답 직렬화 실패(송신 시도조차 못 함)",
        ["S09"] = "동시 연결 상한 초과 거부",
        ["S10"] = "연결 처리 중 예외",
        ["S11"] = "DLL 로드 스모크 실패(기동 시점 사전 점검)",
        ["S12"] = "전표(영수증) 출력 실패",
    };

    /// <summary>
    /// <paramref name="code"/>에 대한 설명을 찾는다. 모르는 코드(장래 신설 코드가 아직 이 표에 반영되지
    /// 않은 경우 등)면 <see langword="null"/> — 호출부는 그냥 설명 없이 원본 메시지만 쓴다(추측 금지).
    /// </summary>
    internal static string? Describe(string? code)
    {
        if (string.IsNullOrEmpty(code))
        {
            return null;
        }

        string trimmed = code!.Trim();

        if (Descriptions.TryGetValue(trimmed, out string? exact))
        {
            return exact;
        }

        // 정상(000)은 설명 줄을 찍지 않는다 — 정상 거래마다 줄만 는다(PRD §17.3).
        if (trimmed == PosResponseCodeCatalog.SuccessCode)
        {
            return null;
        }

        PosResponseCodeInfo info = PosResponseCodeCatalog.Describe(trimmed);
        if (info.IsKnown)
        {
            return $"{info.Description} · {info.SourceLabel}";
        }

        return null;
    }
}
