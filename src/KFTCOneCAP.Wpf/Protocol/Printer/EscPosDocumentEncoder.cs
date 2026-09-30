using System;
using System.IO;

namespace KFTCOneCAP.Wpf.Protocol.Printer;

/// <summary>
/// <see cref="PrintDocument"/> → ESC/POS <c>byte[]</c> 인코더(docs/receipt_print/development_plan.md
/// P33-1). 순서: <c>ESC @</c> → (<see cref="PrinterProfile.SendTwoByteCharacterModeOn"/>이면 <c>FS &amp;</c>)
/// → 줄마다 <c>ESC a</c> + <c>GS !</c> + CP949 바이트 + <c>LF</c> → <c>GS ! 00</c> 복귀 →
/// (<see cref="PrinterProfile.UseCutting"/>이면 <c>ESC d n</c> → <c>GS V 42 n</c>).
///
/// <b>이 인코더는 줄바꿈하지 않는다</b> — 줄바꿈은 문서를 만드는 쪽(<see cref="ReceiptTextLayout.Wrap"/>)의
/// 책임이다. 폭을 넘는 줄을 받으면 <see cref="ArgumentException"/>을 던진다(Protocol 계층은 예외를
/// 허용한다 — 포트를 여는 Services 계층이 그 앞에서 미리 막는다). I/O 없음, 순수 함수.
/// </summary>
public static class EscPosDocumentEncoder
{
    public static byte[] Encode(PrintDocument document)
    {
        if (document is null)
            throw new ArgumentNullException(nameof(document));

        using var stream = new MemoryStream();

        void Write(byte[] bytes) => stream.Write(bytes, 0, bytes.Length);

        Write(EscPosCommands.Initialize());

        if (PrinterProfile.SendTwoByteCharacterModeOn)
            Write(EscPosCommands.TwoByteCharacterModeOn());

        int lineIndex = 0;
        foreach (PrintLine line in document.Lines)
        {
            int widthBytes = line.Size == PrintSize.DoubleSize
                ? PrinterProfile.LineWidth / 2
                : PrinterProfile.LineWidth;

            int lineBytes = ReceiptTextLayout.ByteWidth(line.Text);
            if (lineBytes > widthBytes)
            {
                // 2026-09-30 CP1 리뷰 L-4 — 예외 메시지에 줄 본문(영수증 텍스트, 개인정보 포함 가능)을
                // 넣지 않는다. 줄 인덱스·바이트 수·허용 폭만 남긴다(예외가 로그로 새 나가도 안전하게).
                throw new ArgumentException(
                    $"줄 폭 초과: 줄 인덱스={lineIndex}, {lineBytes}바이트 > 허용 폭 {widthBytes}바이트" +
                    $"(크기={line.Size})",
                    nameof(document));
            }

            Write(EscPosCommands.Align(line.Alignment));
            Write(EscPosCommands.CharacterSize(line.Size == PrintSize.DoubleSize));
            Write(ReceiptEncoding.Value.GetBytes(line.Text));

            // 확장 지점(2026-09-30, 아직 구현하지 않음) — 실기 결과에 따라 "폭을 꽉 채운 줄 뒤 LF 생략"
            // 옵션이 PrinterProfile에 들어올 수 있다(일부 기종이 폭이 꽉 찬 줄에서 자동 줄바꿈하며 LF까지
            // 받으면 빈 줄이 하나 더 생기는 경우 대비). 그때 여기 한 줄만 조건부로 바꾸면 되도록, 줄마다
            // LF를 개별적으로 쓰는 이 구조(줄들을 먼저 다 이어붙인 뒤 한꺼번에 처리하지 않는 구조)를
            // 유지한다.
            stream.WriteByte(EscPosCommands.LineFeed);
            lineIndex++;
        }

        // 문서 끝에서 배율을 보통 크기로 되돌린다 — 이 인코더 호출 자체는 매번 ESC @로 시작하므로
        // 다음 문서에 영향을 주지는 않지만, 프린터가 이 명령 스트림 뒤에 다른 스트림(상태 조회 응답 등)을
        // 처리할 때 서식이 남아있지 않게 하기 위한 방어적 복귀다.
        Write(EscPosCommands.CharacterSize(doubleSize: false));

        if (PrinterProfile.UseCutting)
        {
            Write(EscPosCommands.FeedLines(PrinterProfile.FeedLinesBeforeCut));
            Write(EscPosCommands.PartialCut(PrinterProfile.CutFeedDots));
        }

        return stream.ToArray();
    }
}
