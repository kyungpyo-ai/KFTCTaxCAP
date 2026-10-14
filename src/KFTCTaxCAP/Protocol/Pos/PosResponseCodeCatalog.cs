using System;
using System.Collections.Generic;

namespace KFTCTaxCAP.Protocol.Pos;

/// <summary>응답 코드(<c>#7</c>)의 출처 분류 — PRD §17.1.</summary>
internal enum PosResponseCodeSource
{
    Common,
    InternetGiro,
    ServerUnknown,
    KftcVanRelay,
    KftcVanApproval,
    OneCap,
    OneCapReader,
    OneCapVanDll,
    Unknown,
}

/// <summary>응답 코드 한 건의 해설 결과(불변).</summary>
internal sealed class PosResponseCodeInfo
{
    public PosResponseCodeInfo(string code, string? description, PosResponseCodeSource source, string sourceLabel)
    {
        Code = code;
        Description = description;
        Source = source;
        SourceLabel = sourceLabel;
    }

    /// <summary>Trim 결과. null/공백이면 빈 문자열.</summary>
    public string Code { get; }

    /// <summary>null = 정의되지 않은 코드.</summary>
    public string? Description { get; }

    public PosResponseCodeSource Source { get; }

    public string SourceLabel { get; }

    public bool IsKnown => Description != null;

    /// <summary>정상은 <c>000</c> 하나다(<c>V00</c>은 성공이 아니다).</summary>
    public bool IsSuccess => Code == PosResponseCodeCatalog.SuccessCode;

    public string DisplayCode => Code.Length == 0 ? "(빈 값)" : Code;

    public string DisplayDescription => Description ?? "정의되지 않은 코드";
}

/// <summary>
/// Phase 45(PRD §17.1) — <c>#7</c> 응답코드의 해설 표. 표시·기록용이며 relay 값, 결과 판정(<c>TelegramOutcome</c>),
/// <c>FaultAlertJudge</c>에는 영향을 주지 않는다. 코드 생성은 <c>PosResultCodeMapper</c>가 계속 맡는다.
/// 문구는 SPEC 20261013판 3장 원문 그대로(‘ ’ 따옴표 포함)다.
/// </summary>
internal static class PosResponseCodeCatalog
{
    internal const string SuccessCode = "000";

    /// <summary>800000 응답의 API 세부 응답코드 필드 번호(<c>#27</c>).</summary>
    internal const int CardInfoDetailFieldNumber = 27;

    private static readonly Dictionary<string, string> CommonTable = new(StringComparer.Ordinal)
    {
        ["000"] = "정상",
        ["030"] = "전문 오류 (FORMAT ERROR)",
        ["031"] = "전문 전송 일자 오류",
        ["091"] = "Time-Over",
        ["092"] = "서비스 시간 아님",
        ["093"] = "시스템 오류",
        ["094"] = "해당 기관 사정으로 조회 불가",
        ["201"] = "서비스 불가(유효한 사용자 정보가 아님)",
    };

    private static readonly Dictionary<string, string> InternetGiroTable = new(StringComparer.Ordinal)
    {
        ["311"] = "고지내역 없음",
        ["312"] = "납부내역 없음",
        ["313"] = "납부 처리 중 (납부 불가)",
        ["314"] = "취소 처리 중 (납부 불가)",
        ["315"] = "자동이체분 (납부 불가)",
        ["321"] = "조회 가능 기간 경과",
        ["324"] = "지로 번호 오류",
        ["325"] = "해당 이용기관 정보 없음",
        ["331"] = "기 납부 내역임",
        ["332"] = "(조회) 원거래 없음",
        ["334"] = "‘납부 처리 중’으로 납부 불가",
        ["335"] = "‘취소 처리 중’으로 납부 불가",
        ["336"] = "‘자동이체분’으로 납부 불가",
        ["337"] = "거래 일자(수납 일자) 틀림",
        ["338"] = "출금은행 점별 코드 틀림",
        ["339"] = "발행기관 분류코드 틀림",
        ["340"] = "주민(사업자)등록번호 틀림",
        ["341"] = "전자(인터넷)납부번호 틀림",
        ["342"] = "고객관리번호 틀림",
        ["343"] = "납부 금액 틀림",
        ["344"] = "‘장기 연체자(기타 사유 등)’ 납부 불가",
        ["345"] = "납부 금액 틀림 (원 단위 금액 포함)",
        ["360"] = "‘자진 납부 대상’ 아님",
        ["361"] = "‘징수관 계좌번호’ 틀림",
        ["362"] = "‘소계정’ 틀림",
        ["363"] = "‘회계 년도’ 틀림",
        ["364"] = "‘납부 기한’ 경과로 수납 불가",
        ["365"] = "‘세목’ 틀림",
        ["366"] = "입력 내역 불충분으로 수납 불가",
        ["367"] = "창구나 해당 기관을 이용하여 납부",
        ["368"] = "입력 내역 불충분으로 조회 불가(연대 납부)",
        ["369"] = "입력 내역 불충분으로 수납 불가(연대 납부)",
    };

    private static readonly Dictionary<string, string> KftcVanRelayTable = new(StringComparer.Ordinal)
    {
        ["M01"] = "API 오류 (세부 응답코드 필드 참조)",
        ["N01"] = "납부가능 시간 아님",
        ["N02"] = "납부 불가 카드사",
    };

    private static readonly Dictionary<string, string> OneCapTable = new(StringComparer.Ordinal)
    {
        ["E01"] = "사용자 취소",
        ["E02"] = "Timeout",
        ["E03"] = "설정 화면 사용 중",
        ["E04"] = "리더기 미설정(사용 가능한 리더기 없음)",
        ["E05"] = "리더기 무결성 체크 실패",
        ["E06"] = "키오스크 ID 불일치",
        ["E07"] = "조회한 원거래 없음(직전 전문 확인 요청 관련)",
        ["E40"] = "요청 전문 길이 불일치",
        ["E41"] = "알 수 없는 거래 구분 코드",
        ["E42"] = "전문 식별 불가(본문이 16바이트 미만이라 #4조차 읽지 못함)",
        ["E43"] = "전문 형식 오류(길이 필드가 깨져 프레이밍 자체가 불가)",
        ["E99"] = "원캡 내부 오류(그 외 처리 중 예외)",
    };

    // R00~R23: 리더기 업무 응답코드(docs/reader_dll/API명세서.md §9, 기존 LogCodeCatalog 문구 그대로).
    // R24~R32: SPEC 3장 3) 문구.
    private static readonly Dictionary<string, string> OneCapReaderTable = new(StringComparer.Ordinal)
    {
        ["R00"] = "리더기 상태 정상",
        ["R01"] = "리더기 무결성 오류",
        ["R02"] = "Reader Error(카드 리딩 도중 제거)",
        ["R03"] = "사용자 취소(단말기/멀티패드 종료 버튼)",
        ["R04"] = "거래요청 Timeout",
        ["R05"] = "금액 요청 IC",
        ["R06"] = "IC 카드 거래 불가(카드매체 불량)",
        ["R07"] = "FallBack(MS가능한 거래)",
        ["R08"] = "IC 카드 삽입되어있음(카드제거 요청)",
        ["R09"] = "상황에 맞지 않는 명령",
        ["R10"] = "상호인증오류(Key 상호인증 시)",
        ["R11"] = "암호화/복호화오류(Key 다운로드 시)",
        ["R12"] = "MS거래 불가! IC카드로 진행",
        ["R13"] = "리더기 KEY 다운로드 요망",
        ["R14"] = "MS카드를 넣어주세요(MS전용카드 시)",
        ["R15"] = "RF카드 리딩 에러",
        ["R16"] = "비정상 RF카드 접촉",
        ["R17"] = "음성/동영상 파일 번호 없음",
        ["R18"] = "현금IC 카드 복수 계좌 거래 불가",
        ["R19"] = "사용자 확인(입력 버튼)",
        ["R20"] = "2차 검증 데이터 오류(EMV 데이터 오류)",
        ["R21"] = "정의되지 않은 전문 코드",
        ["R22"] = "지원되지 않는 전문 코드(해당 리더기 미지원)",
        ["R23"] = "필드값 오류",
        ["R24"] = "리더기 포트가 열려 있지 않은 상태에서 명령 시도",
        ["R25"] = "리더기 명령 송신 실패",
        ["R26"] = "리더기가 다른 명령 처리 중",
        ["R27"] = "지정한 COM 포트를 찾을 수 없음",
        ["R28"] = "리더기 포트 오픈 실패",
        ["R29"] = "허용되지 않는 명령",
        ["R30"] = "리더기 응답 수신 중 통신 오류",
        ["R31"] = "그 외 리더기 DLL 연동 오류(위 항목에 해당하지 않는 경우)",
        ["R32"] = "리더기 실패 원인을 특정할 수 없는 경우",
    };

    private static readonly Dictionary<string, string> OneCapVanDllTable = new(StringComparer.Ordinal)
    {
        ["D01"] = "VAN DLL 로드 실패",
        ["D02"] = "VAN DLL 호출 자체 실패(서버 거절과는 다른, 통신 실패)",
    };

    // 800000 #27 고정 코드.
    private static readonly Dictionary<string, string> CardInfoDetailTable = new(StringComparer.Ordinal)
    {
        ["000"] = "정상 처리",
        ["R9999"] = "유효하지 않은 API",
        ["R2000"] = "JSON 파싱 오류",
        ["R2101"] = "요청 header 오류",
        ["R2102"] = "요청 body 오류",
        ["R3000"] = "복호화 오류",
        ["R4000"] = "인코딩 오류",
        ["R5000"] = "조회 내역 없음",
        ["R6000"] = "기타 오류",
        ["R8000"] = "포맷 오류",
        ["PAY001"] = "전자납부번호 형식 오류",
        ["PAY207"] = "타인내역 납부 불가",
        ["PAY501"] = "납부불가 금융기관",
        ["PAY502"] = "납부가능 일자 아님",
        ["PAY503"] = "납부가능 시간 경과",
        ["PAY504"] = "일괄납부 가능 건수 초과",
        ["M0089"] = "VAN 이용 불가",
        ["M1100"] = "존재하지 않는 지로번호 오류",
        ["M1101"] = "이용기관 정보 오류",
        ["M1102"] = "요금 종류 오류",
    };

    private const string DetailM22Description = "국세 자진납부 입력 항목 관련 오류";
    private const string DetailM23Description = "납부금액 검증 관련 오류";

    // 표 개수 검사용(자가진단).
    internal static int CommonCount => CommonTable.Count;
    internal static int InternetGiroCount => InternetGiroTable.Count;
    internal static int KftcVanRelayCount => KftcVanRelayTable.Count;
    internal static int OneCapCount => OneCapTable.Count;
    internal static int OneCapReaderCount => OneCapReaderTable.Count;
    internal static int OneCapVanDllCount => OneCapVanDllTable.Count;
    internal static int CardInfoDetailFixedCount => CardInfoDetailTable.Count;

    internal static PosResponseCodeInfo Describe(string? code)
    {
        string trimmed = code?.Trim() ?? string.Empty;

        if (trimmed.Length == 3)
        {
            char c0 = trimmed[0];
            if (IsAsciiDigit(c0) && IsAsciiDigit(trimmed[1]) && IsAsciiDigit(trimmed[2]))
            {
                if (CommonTable.TryGetValue(trimmed, out string? common))
                {
                    return new PosResponseCodeInfo(trimmed, common, PosResponseCodeSource.Common, "공통 응답");
                }

                if (InternetGiroTable.TryGetValue(trimmed, out string? giro))
                {
                    return new PosResponseCodeInfo(trimmed, giro, PosResponseCodeSource.InternetGiro, "인터넷지로 응답");
                }

                return new PosResponseCodeInfo(trimmed, null, PosResponseCodeSource.ServerUnknown, "서버 응답");
            }

            if ((c0 == 'M' || c0 == 'N') && IsUpperAlnum(trimmed[1]) && IsUpperAlnum(trimmed[2]))
            {
                KftcVanRelayTable.TryGetValue(trimmed, out string? relay);
                return new PosResponseCodeInfo(trimmed, relay, PosResponseCodeSource.KftcVanRelay, "KFTCVAN 중계 응답");
            }

            if (c0 == 'V' && IsUpperAlnum(trimmed[1]) && IsUpperAlnum(trimmed[2]))
            {
                KftcVanApprovalCodeTable.Entries.TryGetValue(trimmed.Substring(1), out string? approval);
                return new PosResponseCodeInfo(trimmed, approval, PosResponseCodeSource.KftcVanApproval, "KFTCVAN 승인 응답 (별첨1)");
            }

            if (c0 == 'E' && IsAsciiDigit(trimmed[1]) && IsAsciiDigit(trimmed[2]))
            {
                OneCapTable.TryGetValue(trimmed, out string? oneCap);
                return new PosResponseCodeInfo(trimmed, oneCap, PosResponseCodeSource.OneCap, "원캡 응답");
            }

            if (c0 == 'R' && IsAsciiDigit(trimmed[1]) && IsAsciiDigit(trimmed[2]))
            {
                OneCapReaderTable.TryGetValue(trimmed, out string? reader);
                return new PosResponseCodeInfo(trimmed, reader, PosResponseCodeSource.OneCapReader, "원캡 응답 · 리더기");
            }

            if (c0 == 'D' && IsAsciiDigit(trimmed[1]) && IsAsciiDigit(trimmed[2]))
            {
                OneCapVanDllTable.TryGetValue(trimmed, out string? vanDll);
                return new PosResponseCodeInfo(trimmed, vanDll, PosResponseCodeSource.OneCapVanDll, "원캡 응답 · VAN DLL");
            }
        }

        return new PosResponseCodeInfo(trimmed, null, PosResponseCodeSource.Unknown, "출처를 알 수 없음");
    }

    /// <summary>800000 응답이고 <c>#7 = M01</c>일 때만 <c>#27</c> 세부 코드를 해석한다.</summary>
    internal static bool HasCardInfoDetail(string? transactionTypeCode, string? responseCode) =>
        transactionTypeCode == "800000" && string.Equals(responseCode?.Trim(), "M01", StringComparison.Ordinal);

    /// <summary>세부 코드 내용. 없으면 null(공백 처리는 호출자).</summary>
    internal static string? DescribeCardInfoDetail(string? detailCode)
    {
        string trimmed = detailCode?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return null;
        }

        if (CardInfoDetailTable.TryGetValue(trimmed, out string? fixedText))
        {
            return fixedText;
        }

        if (trimmed.Length == 5)
        {
            if (trimmed.StartsWith("M22", StringComparison.Ordinal))
            {
                return DetailM22Description;
            }

            if (trimmed.StartsWith("M23", StringComparison.Ordinal))
            {
                return DetailM23Description;
            }
        }

        return null;
    }

    private static bool IsAsciiDigit(char c) => c >= '0' && c <= '9';

    private static bool IsUpperAlnum(char c) => IsAsciiDigit(c) || (c >= 'A' && c <= 'Z');
}
