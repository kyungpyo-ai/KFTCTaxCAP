namespace KFTCTaxCAP.ViewModels.Payment;

/// <summary>
/// Phase 45(PRD §17.2) — 결제창 응답 패널의 해설띠 표시 값(불변). 표시 전용이며 결과 판정(<see cref="TelegramOutcome"/>)에는
/// 쓰이지 않는다.
/// </summary>
public sealed class ResponseCodeBand
{
    public ResponseCodeBand(bool isSuccess, string code, string headline, string? detail, string sourceLabel)
    {
        IsSuccess = isSuccess;
        Code = code;
        Headline = headline;
        Detail = detail;
        SourceLabel = sourceLabel;
    }

    /// <summary><c>#7 == "000"</c>일 때만 true(<c>V00</c>은 아니다).</summary>
    public bool IsSuccess { get; }

    /// <summary>표시용 코드(빈 값이면 "(빈 값)").</summary>
    public string Code { get; }

    /// <summary>① 주 해설 — <c>{코드} · {내용}</c>.</summary>
    public string Headline { get; }

    /// <summary>② 800000 <c>M01</c>의 세부 해설. 해당 없으면 null.</summary>
    public string? Detail { get; }

    /// <summary>③ 출처 표시.</summary>
    public string SourceLabel { get; }
}
