using System.Text;

namespace KFTCOneCAP.Wpf.Protocol.Printer;

/// <summary>
/// 영수증 본문 바이트 인코딩을 정하는 단 하나의 지점(docs/receipt_print/PRD.md §3.1, §7.2).
/// CP949(<c>Encoding.GetEncoding(949)</c>)는 <c>net48</c>에서 별도 패키지 없이 바로 쓸 수 있다
/// (System.Text의 코드 페이지 지원이 .NET Framework 자체에 있음 — <see cref="Pos.PosMessageEncoding"/>과
/// 동일 근거). 이 클래스는 POS 소켓 전문(<see cref="Pos.PosMessageEncoding"/>)과 의도적으로 분리했다 —
/// 프린터 계층이 POS 프로토콜 계층에 의존하지 않게 하기 위해서다(CLAUDE.md 계층 규칙, 두 값이 같은 CP949여도
/// 각자의 이유로 바뀔 수 있다).
/// </summary>
internal static class ReceiptEncoding
{
    internal static readonly Encoding Value = Encoding.GetEncoding(949);
}
