using System;
using System.Globalization;
using System.Text;
using System.Threading;
using KFTCTaxCAP.Protocol.Printer;
using KFTCTaxCAP.Services.Printer;

namespace KFTCTaxCAP.Services.Diagnostics;

/// <summary>
/// <c>--receipt-print-test</c>의 <c>dump</c>/<c>print</c>/<c>status</c> 모드(docs/receipt_print/
/// development_plan.md P33-4). <c>self</c> 모드는 <see cref="ReceiptPrintSelfTest"/>가 계속 담당한다.
///
/// 여기서 조립하는 문서는 PRD.md §3.2 그림을 그대로 줄 목록으로 옮긴 <b>진단용 샘플</b>이다 —
/// Phase 34의 실제 영수증 조립 코드가 아니다(전문 필드를 전혀 읽지 않는다). 납세자명·납세자번호·
/// 전자납부번호·카드번호는 착수 전 확정 사항 5에 따라 <b>사진 실물 값을 전혀 쓰지 않고</b> 모두
/// 명백한 더미 값(홍길동, 800101*******, 전부 0인 전자납부번호 등)이다(2026-09-30 CP1 리뷰 H-1 —
/// 이전 버전에 사진 실물 전자납부번호가 그대로 남아 있던 것을 고쳤다).
/// </summary>
internal static class ReceiptPrintDiagnosticHarness
{
    /// <summary>PRD §3.2 그림 순서의 진단용 샘플 문서(더미 데이터). 폭은 <see cref="PrinterProfile.LineWidth"/>를
    /// 그대로 쓴다(2026-09-30 CP1 리뷰 M-2 — 이전엔 42를 이 클래스에 따로 하드코딩해 기종 상수를
    /// 한 곳에 모은다는 <see cref="PrinterProfile"/>의 존재 이유와 어긋났다).</summary>
    internal static PrintDocument BuildSampleDocument()
    {
        int width = PrinterProfile.LineWidth;
        var doc = new PrintDocument();

        doc.AddLine("종합소득세 납부확인증", PrintAlignment.Center, PrintSize.DoubleSize);
        doc.AddLine(ReceiptTextLayout.Separator(width));
        doc.AddLines(ReceiptTextLayout.LabelValue("전자납부번호", "0000000000000000000", width)); // 더미(19자리, 전부 0)
        doc.AddLines(ReceiptTextLayout.LabelValue("납세자명", "홍길동", width));
        doc.AddLines(ReceiptTextLayout.LabelValue("납세자번호", "800101*******", width));
        doc.AddLines(ReceiptTextLayout.LabelValue("세입징수관서", "국세청 서울지방국세청 강서세무서 징수관", width));
        doc.AddLines(ReceiptTextLayout.LabelValue("세입징수관계좌번호", "012027", width));
        doc.AddLine(ReceiptTextLayout.Separator(width));
        doc.AddLines(ReceiptTextLayout.LabelValue("납기내기한", "2026.08.31", width));
        doc.AddLines(ReceiptTextLayout.LabelValue("납기내금액(원)", "100,000", width));
        doc.AddLines(ReceiptTextLayout.LabelValue("납기후기한", "2026.08.31", width));
        doc.AddLines(ReceiptTextLayout.LabelValue("납기후금액(원)", "100,000", width));
        doc.AddLine(ReceiptTextLayout.Separator(width));
        doc.AddLines(ReceiptTextLayout.LabelValue("납부세액(원)", "100,000", width));
        doc.AddLines(ReceiptTextLayout.LabelValue("납부대행수수료(원)", "700", width));
        doc.AddLines(ReceiptTextLayout.LabelValue("총 납부금액(원)", "100,700", width));
        doc.AddLines(ReceiptTextLayout.LabelValue("선불카드잔액(원)", "0", width));
        doc.AddLine(ReceiptTextLayout.Separator(width));
        doc.AddLines(ReceiptTextLayout.LabelValue("납부카드", "신한카드", width));
        doc.AddLines(ReceiptTextLayout.LabelValue("할부개월수", "일시불", width));
        doc.AddLines(ReceiptTextLayout.LabelValue("카드번호", "1234-5678-****-000*", width)); // 더미(§2.4 필드 확장 전)
        doc.AddLines(ReceiptTextLayout.LabelValue("납부일자", "2026.08.19", width));
        doc.AddLine(ReceiptTextLayout.Separator(width));
        doc.AddBlankLine();
        doc.AddLines(ReceiptTextLayout.Wrap(
            "1.신용카드로 납부한 국세는 할부거래계약서 및 약관 등에도 불구하고 납부를 취소할 수 없습니다.",
            width));
        doc.AddBlankLine();
        doc.AddLines(ReceiptTextLayout.Wrap(
            "2.신용카드로 국세를 납부할 경우 납부세액의 0.8%인 납부대행수수료는 납세자가 부담합니다.",
            width));

        return doc;
    }

    /// <summary><c>dump</c> 모드 — 16진 덤프 + 줄 미리보기(경계 칸 수마다 <c>|</c>).</summary>
    internal static void RunDump()
    {
        try
        {
            FileLogger.Info("[receipt-print-test dump] 시작");

            int width = PrinterProfile.LineWidth;
            PrintDocument document = BuildSampleDocument();
            byte[] bytes = EscPosDocumentEncoder.Encode(document);

            FileLogger.Info($"[receipt-print-test dump] 인코딩 완료 — {bytes.Length}바이트");
            FileLogger.Info("[receipt-print-test dump] 16진 덤프:");
            for (int offset = 0; offset < bytes.Length; offset += 16)
            {
                int count = Math.Min(16, bytes.Length - offset);
                FileLogger.Info($"[receipt-print-test dump]   {offset:D4}: {ToHex(bytes, offset, count)}");
            }

            FileLogger.Info("[receipt-print-test dump] 줄 미리보기(경계 칸 수마다 | 표시):");
            FileLogger.Info($"[receipt-print-test dump]   {BuildRuler(width)}  (보통 폭 {width}칸 기준자)");
            foreach (PrintLine line in document.Lines)
            {
                int effectiveWidth = line.Size == PrintSize.DoubleSize ? width / 2 : width;
                int byteWidth = ReceiptTextLayout.ByteWidth(line.Text);
                int padding = Math.Max(0, effectiveWidth - byteWidth);
                string preview = line.Text + new string(' ', padding) + "|";
                string sizeTag = line.Size == PrintSize.DoubleSize ? "2배" : "보통";
                FileLogger.Info($"[receipt-print-test dump]   {preview}  ({sizeTag}, {byteWidth}바이트)");
            }

            FileLogger.Info("[receipt-print-test dump] 완료");
        }
        catch (Exception ex)
        {
            // 2026-09-30 CP1 리뷰 M-2 — 인코딩 실패(문서 조립 버그 등)가 조용히 프로세스를 죽이지
            // 않고 ERROR 로그로 남게 한다.
            FileLogger.Error($"[receipt-print-test dump] 예외로 중단: {ex}");
        }
    }

    /// <summary><c>print &lt;포트번호&gt; &lt;속도&gt;</c> 모드 — 샘플 문서를 실제로 출력 시도한다.</summary>
    internal static void RunPrint(string portArg, string speedArg)
    {
        try
        {
            if (!TryParseBaudRate(speedArg, out int baudRate))
            {
                FileLogger.Info($"[receipt-print-test print] 잘못된 속도 인자: \"{speedArg}\" — 사용법: --receipt-print-test print <포트번호> <속도>");
                return;
            }

            FileLogger.Info($"[receipt-print-test print] 시작 — 포트=\"{portArg}\", 속도={baudRate}bps");

            PrintDocument document = BuildSampleDocument();
            byte[] payload = EscPosDocumentEncoder.Encode(document);

            var printer = new SerialReceiptPrinter();
            var connection = new PrinterConnection(portArg, baudRate);
            PrintResult result = printer.PrintAsync(payload, connection, CancellationToken.None)
                .GetAwaiter().GetResult();

            FileLogger.Info(
                $"[receipt-print-test print] 결과 — IsSuccess={result.IsSuccess}, Failure={result.Failure}, " +
                $"PaperNearEnd={result.PaperNearEnd}");
        }
        catch (Exception ex)
        {
            FileLogger.Error($"[receipt-print-test print] 예외로 중단: {ex}");
        }
    }

    /// <summary><c>status &lt;포트번호&gt; &lt;속도&gt;</c> 모드 — 인쇄 없이 상태 조회만 한다.</summary>
    internal static void RunStatus(string portArg, string speedArg)
    {
        try
        {
            if (!TryParseBaudRate(speedArg, out int baudRate))
            {
                FileLogger.Info($"[receipt-print-test status] 잘못된 속도 인자: \"{speedArg}\" — 사용법: --receipt-print-test status <포트번호> <속도>");
                return;
            }

            FileLogger.Info($"[receipt-print-test status] 시작 — 포트=\"{portArg}\", 속도={baudRate}bps");

            var printer = new SerialReceiptPrinter();
            var connection = new PrinterConnection(portArg, baudRate);
            PrintResult result = printer.CheckStatusOnlyAsync(connection, CancellationToken.None)
                .GetAwaiter().GetResult();

            FileLogger.Info(
                $"[receipt-print-test status] 결과 — IsSuccess={result.IsSuccess}, Failure={result.Failure}, " +
                $"PaperNearEnd={result.PaperNearEnd}");
        }
        catch (Exception ex)
        {
            FileLogger.Error($"[receipt-print-test status] 예외로 중단: {ex}");
        }
    }

    private static bool TryParseBaudRate(string? text, out int baudRate)
        => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out baudRate) && baudRate > 0;

    private static string BuildRuler(int width)
    {
        var sb = new StringBuilder();
        for (int i = 1; i <= width; i++)
        {
            sb.Append((i % 10).ToString(CultureInfo.InvariantCulture));
        }

        sb.Append('|');
        return sb.ToString();
    }

    private static string ToHex(byte[] bytes, int offset, int count)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < count; i++)
        {
            if (i > 0)
            {
                sb.Append(' ');
            }

            sb.Append(bytes[offset + i].ToString("X2", CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }
}
