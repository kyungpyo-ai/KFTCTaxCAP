using System;
using System.Diagnostics;
using System.Globalization;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using KFTCOneCAP.Wpf.Protocol.Printer;
using KFTCOneCAP.Wpf.Services.Diagnostics;

namespace KFTCOneCAP.Wpf.Services.Printer;

/// <summary>
/// <see cref="IReceiptPrinter"/>의 실제 구현 — 시리얼 COM 포트를 열어 ESC/POS 바이트를 보낸다
/// (docs/receipt_print/PRD.md §4.3·§5, escpos_reference.md §2·§3, development_plan.md P33-3).
///
/// <b>동시성</b>: 프로세스 안에서 이 클래스로 향하는 모든 호출을 <see cref="Gate"/>(정적
/// <see cref="SemaphoreSlim"/>) 하나로 직렬화한다 — 인스턴스를 여러 개 만들어도(자동 출력·재출력이
/// 각자 새 인스턴스를 만들 가능성) 같은 프린터로 두 출력이 동시에 가지 않는다는 PRD §4.3 요구를
/// 인스턴스 개수와 무관하게 보장하기 위해 정적으로 뒀다.
///
/// <b>취소</b>: <see cref="CancellationToken"/>이 취소되면 <see cref="PrintFailureReason.WriteFailed"/>로
/// 변환한다 — PRD §5 실패 표에 "취소"가 별도 행으로 없고, 취소가 실제로 발생할 수 있는 지점(포트 대기
/// 큐, 상태 조회 대기, 쓰기 대기)이 전부 "무언가를 보내다 만" 상태라 <c>WriteFailed</c>가 가장 가깝다.
/// 별도 값이 필요하다고 판단되면 PRD §5를 먼저 갱신한 뒤 값을 추가한다(development_plan.md P33-3 지시).
///
/// <b>예외</b>: 공개 메서드(<see cref="PrintAsync"/>)는 어떤 예외도 밖으로 던지지 않는다 — 포트
/// 열기/상태 조회/쓰기 각 단계를 개별 try/catch로 감싸고, 예상 밖 예외까지 최상위 catch로 받아
/// <see cref="PrintFailureReason.WriteFailed"/>로 변환한다.
/// </summary>
public sealed class SerialReceiptPrinter : IReceiptPrinter
{
    /// <summary>상태 조회 응답 대기(escpos_reference.md §3, PRD §9 #3 — 제안값, 실기에서 조정 가능).</summary>
    private const int ReadTimeoutMs = 500;

    /// <summary>쓰기 타임아웃(escpos_reference.md §3 — 제안값).</summary>
    private const int WriteTimeoutMs = 3000;

    /// <summary>클래스 요약의 동시성 설명 참고.</summary>
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>
    /// <b>테스트 전용</b>(development_plan.md P33-4 "동시 호출 2건 직렬화" 확인). 0보다 크면
    /// <see cref="PrintOnBackgroundThread"/> 진입 직후(포트를 만들기 전) 이 길이만큼 동기적으로
    /// 멈춘다 — 250/251처럼 존재하지 않는 포트 번호로 검증할 때는 <c>SerialPort.Open()</c>이 거의
    /// 즉시 실패해서 두 동시 호출의 로그 타임스탬프만으로는 직렬화 여부를 신뢰성 있게 구분할 수 없다
    /// (병렬이든 직렬이든 둘 다 순식간에 실패해 버림). 이 지연으로 "직렬화됐다면 두 호출의 지연이
    /// 누적되고, 병렬이었다면 거의 동시에 끝난다"는 관찰 가능한 차이를 만든다. 기본값 0(운영 동작에
    /// 전혀 영향 없음) — 테스트가 끝나면 반드시 0으로 되돌린다(<c>ReceiptPrintSelfTest</c> 참고).
    /// </summary>
    internal static int TestOnlyCriticalSectionDelayMs { get; set; }

    public Task<PrintResult> PrintAsync(byte[] payload, PrinterConnection connection, CancellationToken cancellationToken)
    {
        if (payload is null)
        {
            FileLogger.Warn(LogCategory.Printer, "[SerialReceiptPrinter] payload가 null — 출력하지 않음");
            return Task.FromResult(PrintResult.Fail(PrintFailureReason.WriteFailed));
        }

        return ExecuteAsync(payload, connection, cancellationToken);
    }

    /// <summary>
    /// P33-4 <c>status</c> 모드 전용 — 포트를 열고 상태 조회(<c>PrinterProfile.UseStatusQuery</c>가
    /// true일 때)만 하고 아무것도 인쇄하지 않는다(용지·커버 재현 시험용, development_plan.md P33-4).
    /// </summary>
    internal Task<PrintResult> CheckStatusOnlyAsync(PrinterConnection connection, CancellationToken cancellationToken)
        => ExecuteAsync(payload: null, connection, cancellationToken);

    private static async Task<PrintResult> ExecuteAsync(byte[]? payload, PrinterConnection connection, CancellationToken cancellationToken)
    {
        int portNumber = ParsePortNumber(connection?.PortNumberText);
        if (portNumber < 1 || portNumber > 255)
        {
            FileLogger.Warn(LogCategory.Printer,
                $"[SerialReceiptPrinter] 잘못된 포트 번호(\"{connection?.PortNumberText}\") — 포트를 열지 않음");
            return PrintResult.Fail(PrintFailureReason.InvalidPort);
        }

        int baudRate = connection!.BaudRate;
        if (baudRate <= 0)
        {
            // 2026-09-30 CP1 리뷰 L-5 — InvalidPort는 "포트·속도 설정 오류" 전체를 가리킨다(PrintResult.cs
            // 주석 참고). 허용 속도 목록(9600/38400/57600/115200, ShopSettingsService.AllowedPrinterSpeeds)
            // 까지는 검사하지 않는다 — Services/Printer가 Services/Settings를 참조하면 "포트를 여는 계층은
            // 설정을 모른다"는 이 계층의 책임 경계(PRD §7.1)가 흐려지므로, 값이 있는지(양수인지)만 본다.
            FileLogger.Warn(LogCategory.Printer,
                $"[SerialReceiptPrinter] 잘못된 속도({baudRate}bps) — 포트를 열지 않음");
            return PrintResult.Fail(PrintFailureReason.InvalidPort);
        }

        string portName = "COM" + portNumber.ToString(CultureInfo.InvariantCulture);

        bool acquired = false;
        try
        {
            await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            acquired = true;

            return await Task.Run(
                () => PrintOnBackgroundThread(payload, portName, baudRate, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            FileLogger.Warn(LogCategory.Printer, $"[SerialReceiptPrinter] 취소됨({portName})");
            return PrintResult.Fail(PrintFailureReason.WriteFailed);
        }
        catch (Exception ex)
        {
            // 위 두 단계(세마포어 대기, Task.Run 스케줄링) 자체가 실패하는 것은 사실상 없지만, 공개
            // 메서드는 예외를 던지지 않는다는 계약을 무조건 지키기 위한 최후 방어선이다.
            FileLogger.Error(LogCategory.Printer,
                $"[SerialReceiptPrinter] 예상치 못한 예외({portName}): {ex.GetType().Name}: {ex.Message}");
            return PrintResult.Fail(PrintFailureReason.WriteFailed);
        }
        finally
        {
            if (acquired)
            {
                Gate.Release();
            }
        }
    }

    /// <summary>
    /// 포트 열기~상태 조회~(<paramref name="payload"/>가 있으면)송신~닫기 전체(<see
    /// cref="Task.Run(System.Func{PrintResult}, CancellationToken)"/> 안에서 동기적으로 실행됨 —
    /// <see cref="SerialPort"/>가 동기 API라 별도 스레드로 옮긴 것). <paramref name="payload"/>가
    /// <c>null</c>이면 <see cref="CheckStatusOnlyAsync"/>(상태 조회만, 인쇄 없음) 경로다.
    /// </summary>
    private static PrintResult PrintOnBackgroundThread(byte[]? payload, string portName, int baudRate, CancellationToken cancellationToken)
    {
        if (TestOnlyCriticalSectionDelayMs > 0)
        {
            // 클래스 요약 참고 — 테스트 전용 인위적 지연. 포트를 만들기도 전에 걸어 지연이 100%
            // 임계구역(Gate) 보유 시간에 포함되게 한다.
            Thread.Sleep(TestOnlyCriticalSectionDelayMs);
        }

        SerialPort? port = null;
        try
        {
            port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
            {
                Handshake = Handshake.None,
                ReadTimeout = ReadTimeoutMs,
                WriteTimeout = WriteTimeoutMs,
            };

            try
            {
                port.Open();
            }
            catch (Exception ex)
            {
                FileLogger.Error(LogCategory.Printer,
                    $"[SerialReceiptPrinter] 포트 열기 실패({portName}, {baudRate}bps): {ex.GetType().Name}: {ex.Message}");
                return PrintResult.Fail(PrintFailureReason.PortOpenFailed);
            }

            FileLogger.Info(LogCategory.Printer,
                payload is null
                    ? $"[SerialReceiptPrinter] 상태 조회 시작({portName}, {baudRate}bps, 인쇄 없음)"
                    : $"[SerialReceiptPrinter] 출력 시작({portName}, {baudRate}bps, {payload.Length}바이트)");

            cancellationToken.ThrowIfCancellationRequested();

            bool paperNearEnd = false;

            // P33-3 — PrinterProfile.UseStatusQuery == false면 상태 조회를 건너뛰고 바로 송신한다
            // (미지원 기종 대비, PrinterProfile 클래스 요약 참고).
            if (PrinterProfile.UseStatusQuery)
            {
                port.DiscardInBuffer();

                PrintResult? offlineFailure = CheckOfflineCause(port, portName);
                if (offlineFailure is not null)
                {
                    return offlineFailure;
                }

                cancellationToken.ThrowIfCancellationRequested();

                PaperSensorCheck paperCheck = CheckPaperSensor(port, portName);
                if (paperCheck.Failure is not null)
                {
                    return paperCheck.Failure;
                }

                paperNearEnd = paperCheck.PaperNearEnd;
            }

            if (payload is null)
            {
                // status 전용 — 인쇄하지 않는다(development_plan.md P33-4).
                FileLogger.Info(LogCategory.Printer,
                    $"[SerialReceiptPrinter] 상태 조회 완료({portName}, 용지부족={paperNearEnd})");
                return PrintResult.Success(paperNearEnd);
            }

            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                port.Write(payload, 0, payload.Length);
                if (!WaitForWriteDrain(port, out int remainingBytes))
                {
                    // 흐름제어/케이블 문제로 송신이 멈춘 경우 — WriteTimeoutMs를 넘겨도 송신 버퍼가
                    // 안 비었는데 그냥 반환해 "출력 완료"로 오판하면 안 된다(코디네이터 지적,
                    // 2026-09-30). 실제로는 프린터에 다 나가지 않았을 수 있으므로 실패로 처리한다.
                    FileLogger.Error(LogCategory.Printer,
                        $"[SerialReceiptPrinter] 쓰기 실패({portName}): 타임아웃({WriteTimeoutMs}ms) 안에 " +
                        $"송신 버퍼를 비우지 못함(남은 바이트={remainingBytes})");
                    return PrintResult.Fail(PrintFailureReason.WriteFailed);
                }
            }
            catch (Exception ex)
            {
                FileLogger.Error(LogCategory.Printer,
                    $"[SerialReceiptPrinter] 쓰기 실패({portName}): {ex.GetType().Name}: {ex.Message}");
                return PrintResult.Fail(PrintFailureReason.WriteFailed);
            }

            FileLogger.Info(LogCategory.Printer,
                $"[SerialReceiptPrinter] 출력 완료({portName}, {payload.Length}바이트, 용지부족={paperNearEnd})");
            return PrintResult.Success(paperNearEnd);
        }
        finally
        {
            if (port is not null)
            {
                try
                {
                    if (port.IsOpen)
                    {
                        port.Close();
                    }
                }
                catch (Exception ex)
                {
                    // 이미 실패 처리(또는 성공 처리)가 끝난 뒤의 자원 정리 시도라 결과에 영향을 주지
                    // 않는다 — 로그만 남긴다(포트 누수 자체는 Dispose가 최종적으로 막는다).
                    FileLogger.Warn(LogCategory.Printer,
                        $"[SerialReceiptPrinter] 포트 닫기 실패({portName}): {ex.GetType().Name}: {ex.Message}");
                }

                port.Dispose();
            }
        }
    }

    private readonly struct PaperSensorCheck
    {
        public PaperSensorCheck(PrintResult? failure, bool paperNearEnd)
        {
            Failure = failure;
            PaperNearEnd = paperNearEnd;
        }

        public PrintResult? Failure { get; }
        public bool PaperNearEnd { get; }
    }

    /// <summary><c>DLE EOT 2</c>(오프라인 원인) 조회. 실패가 없으면 <c>null</c>.</summary>
    private static PrintResult? CheckOfflineCause(SerialPort port, string portName)
    {
        byte? statusByte = ReadStatusByte(port, portName, EscPosCommands.RealtimeStatus(2), "DLE EOT 2(오프라인 원인)");
        if (statusByte is null)
        {
            return PrintResult.Fail(PrintFailureReason.NoResponse);
        }

        PrinterStatus status = PrinterStatusParser.ParseOfflineCause(statusByte.Value);
        if (!status.IsValid)
        {
            FileLogger.Error(LogCategory.Printer,
                $"[SerialReceiptPrinter] {portName} DLE EOT 2 알 수 없는 응답(0x{statusByte.Value:X2})");
            return PrintResult.Fail(PrintFailureReason.NoResponse);
        }

        if (status.CoverOpen)
        {
            FileLogger.Error(LogCategory.Printer, $"[SerialReceiptPrinter] {portName} 커버 열림");
            return PrintResult.Fail(PrintFailureReason.CoverOpen);
        }

        if (status.Error)
        {
            FileLogger.Error(LogCategory.Printer, $"[SerialReceiptPrinter] {portName} 프린터 오류");
            return PrintResult.Fail(PrintFailureReason.PrinterError);
        }

        return null;
    }

    /// <summary><c>DLE EOT 4</c>(용지 센서) 조회.</summary>
    private static PaperSensorCheck CheckPaperSensor(SerialPort port, string portName)
    {
        byte? statusByte = ReadStatusByte(port, portName, EscPosCommands.RealtimeStatus(4), "DLE EOT 4(용지 센서)");
        if (statusByte is null)
        {
            return new PaperSensorCheck(PrintResult.Fail(PrintFailureReason.NoResponse), paperNearEnd: false);
        }

        PrinterStatus status = PrinterStatusParser.ParsePaperSensor(statusByte.Value);
        if (!status.IsValid)
        {
            FileLogger.Error(LogCategory.Printer,
                $"[SerialReceiptPrinter] {portName} DLE EOT 4 알 수 없는 응답(0x{statusByte.Value:X2})");
            return new PaperSensorCheck(PrintResult.Fail(PrintFailureReason.NoResponse), paperNearEnd: false);
        }

        if (status.PaperEnd)
        {
            FileLogger.Error(LogCategory.Printer, $"[SerialReceiptPrinter] {portName} 용지 없음");
            return new PaperSensorCheck(PrintResult.Fail(PrintFailureReason.PaperEnd), paperNearEnd: false);
        }

        if (status.PaperNearEnd)
        {
            // PRD §5 — 용지 부족(near-end)은 실패가 아니다. 출력은 계속하고 WARN만 남긴다.
            FileLogger.Warn(LogCategory.Printer, $"[SerialReceiptPrinter] {portName} 용지 부족(계속 출력)");
        }

        return new PaperSensorCheck(null, status.PaperNearEnd);
    }

    private static byte? ReadStatusByte(SerialPort port, string portName, byte[] request, string label)
    {
        try
        {
            port.Write(request, 0, request.Length);
            int value = port.ReadByte();
            if (value < 0)
            {
                FileLogger.Error(LogCategory.Printer, $"[SerialReceiptPrinter] {portName} {label} 응답 스트림 종료");
                return null;
            }

            return (byte)value;
        }
        catch (TimeoutException)
        {
            FileLogger.Error(LogCategory.Printer,
                $"[SerialReceiptPrinter] {portName} {label} 응답 없음(타임아웃 {ReadTimeoutMs}ms)");
            return null;
        }
        catch (Exception ex)
        {
            FileLogger.Error(LogCategory.Printer,
                $"[SerialReceiptPrinter] {portName} {label} 조회 실패: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 위험 #3(development_plan.md) — 일부 USB-시리얼 변환기는 <c>Close()</c>가 송신 버퍼를 그대로
    /// 버린다. <c>BytesToWrite == 0</c>이 될 때까지(최대 <see cref="WriteTimeoutMs"/>) 대기한다.
    /// <b>2026-09-30 코디네이터 지적 수정</b>: 이전 버전은 타임아웃으로 빠져나온 경우와 실제로 다 비운
    /// 경우를 구분하지 않아, 흐름제어/케이블 문제로 송신이 멈춰도(<c>BytesToWrite &gt; 0</c>로 남아
    /// 있어도) 호출부가 그냥 "출력 완료"로 처리했다 — 이제 완전히 비웠는지를 <c>bool</c>로 돌려주고,
    /// 타임아웃 시점의 잔여 바이트 수를 <paramref name="remainingBytes"/>로 알려준다(호출부가 ERROR
    /// 로그와 <see cref="PrintFailureReason.WriteFailed"/>로 처리).
    /// </summary>
    private static bool WaitForWriteDrain(SerialPort port, out int remainingBytes)
    {
        var stopwatch = Stopwatch.StartNew();
        remainingBytes = port.BytesToWrite;
        while (remainingBytes > 0 && stopwatch.ElapsedMilliseconds < WriteTimeoutMs)
        {
            Thread.Sleep(10);
            remainingBytes = port.BytesToWrite;
        }

        return remainingBytes == 0;
    }

    /// <summary>
    /// 포트 번호 문자열 → 정수. 숫자(0-9)만으로 이뤄진 문자열만 허용한다 — 공백·부호(+/-)·소수점이
    /// 섞이면 형식 자체가 잘못된 것이므로 곧장 -1(호출부가 1~255 범위 검사로 InvalidPort 처리)로
    /// 취급한다(2026-09-30 CP1 리뷰 L-5 — 이전 버전은 <c>NumberStyles.Integer</c>를 써서 " 5"·"+5" 같은
    /// 값도 조용히 통과시켰다).
    /// </summary>
    private static int ParsePortNumber(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return -1;
        }

        string digitsOnly = text!; // IsNullOrEmpty 통과 후 non-null 확정(net48 NotNullWhen 미지원, ReceiptTextLayout.Wrap과 동일 패턴).

        foreach (char c in digitsOnly)
        {
            if (c < '0' || c > '9')
            {
                return -1;
            }
        }

        return int.TryParse(digitsOnly, NumberStyles.None, CultureInfo.InvariantCulture, out int parsedValue) ? parsedValue : -1;
    }
}
