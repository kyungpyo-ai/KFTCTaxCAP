using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using KFTCOneCAP.Wpf.Protocol.Printer;
using KFTCOneCAP.Wpf.Services.Printer;
using KFTCOneCAP.Wpf.Services.Receipt;
using KFTCOneCAP.Wpf.Services.Settings;
using KFTCOneCAP.Wpf.Services.Storage;
using KFTCOneCAP.Wpf.ViewModels;
using KFTCOneCAP.Wpf.ViewModels.Payment;

namespace KFTCOneCAP.Wpf.Services.Diagnostics;

/// <summary>
/// Phase 35 P35-1(docs/receipt_print/development_plan.md "P35-1" 6번) — 직전 영수증 저장소(<see cref="LastReceiptStore"/>),
/// 저장 시점(<see cref="PaymentScreenViewModel.PrintReceiptIfApprovedAsync"/>), 재출력(<see cref="ReceiptPrintService.ReprintAsync"/>),
/// 가맹점 설정 화면 버튼(<see cref="ShopSetupViewModel.RequestLastSlipPrintCommand"/>), 경고 문구를 검증한다.
///
/// <b>모든 케이스가 케이스별 임시 DB 파일</b>(<see cref="Path.GetTempPath"/>)을 쓴다 — 운영 DB
/// (<see cref="IntegrityCheckStore.DefaultDatabasePath"/>)를 건드리지 않는다. 프린터는 전부 Fake(포트 미사용).
/// 납세자번호 등은 더미값이다. <see cref="ReceiptPrintSelfTest.RunAll"/>이 호출한다(<c>--receipt-print-test self</c>).
/// </summary>
internal static class LastReceiptReprintSelfTest
{
    private const string Tag = "[receipt-print-test self] [Phase35 재출력";

    /// <summary>원문 13자리(더미). DB 파일 바이트에서 이 문자열이 나오면 실패다.</summary>
    private const string RawTaxpayerNumber = "8001011234567";
    private const string MaskedTaxpayerNumber = "800101*******";

    internal static bool RunAll()
    {
        bool c1 = RunRoundTripCase();
        bool c2 = RunOverwriteCase();
        bool c3 = RunEmptyStoreCase();
        bool c4 = RunAutoPathSavesWhenOffCase();
        bool c5 = RunReprintIgnoresSettingCase();
        bool c6 = RunMessageCases();
        bool c7 = RunShopSetupButtonCases();

        bool all = c1 && c2 && c3 && c4 && c5 && c6 && c7;
        FileLogger.Info(
            $"{Tag}] 완료 — ①저장/읽기 왕복·원문 미저장={Mark(c1)}, ②덮어쓰기={Mark(c2)}, ③저장 없음→null={Mark(c3)}, " +
            $"④자동 출력 OFF여도 저장={Mark(c4)}, ⑤재출력 설정 무시·(재출력) 줄={Mark(c5)}, ⑥경고 문구={Mark(c6)}, " +
            $"⑦설정 화면 버튼={Mark(c7)}, 종합={Mark(all)}");
        return all;
    }

    private static string Mark(bool ok) => ok ? "통과" : "실패";

    // ------------------------------------------------------------------
    // 공용
    // ------------------------------------------------------------------

    private static string NewTempDbPath(string caseKey) =>
        Path.Combine(Path.GetTempPath(), $"p35-{caseKey}-{Guid.NewGuid():N}.db");

    private static void DeleteTempDb(string path)
    {
        SqliteConnection.ClearAllPools();
        foreach (string p in new[] { path, path + "-journal", path + "-wal", path + "-shm" })
        {
            try { if (File.Exists(p)) File.Delete(p); } catch (Exception) { /* 임시 파일 — 판정과 무관 */ }
        }
    }

    /// <summary>모든 항목에 서로 다른 값(일부는 SPEC 원문처럼 뒤 공백 패딩 포함, 카드번호는 null)을 넣은 더미 영수증.</summary>
    private static NationalTaxReceipt Sample(string electronicPaymentNumber = "0000000000000000001", string taxpayerName = "홍길동") => new()
    {
        ElectronicPaymentNumber = electronicPaymentNumber,
        TaxpayerName = taxpayerName + "    ",
        TaxpayerNumber = RawTaxpayerNumber + "   ",
        CollectingAgency = "국세청 서울지방국세청 강서세무서 징수관",
        AccountNumber = "012027",
        DueDateWithinPeriod = "20260831",
        AmountWithinPeriod = "000000000100000",
        DueDateAfterPeriod = "20260930",
        AmountAfterPeriod = "000000000103000",
        TaxAmount = "000000000100000",
        Fee = "000000000000800",
        TotalAmount = "000000000100800",
        PrepaidCardBalance = "            ",
        CardCompanyName = "신한카드",
        InstallmentMonths = "00",
        CardNumber = null,
        PaymentDate = "20261001",
        FeeRatePercentRaw = "0080",
        IsReprint = false,
    };

    private static Dictionary<string, string?> Values(NationalTaxReceipt r) => new()
    {
        ["ElectronicPaymentNumber"] = r.ElectronicPaymentNumber,
        ["TaxpayerName"] = r.TaxpayerName,
        ["TaxpayerNumber"] = r.TaxpayerNumber,
        ["CollectingAgency"] = r.CollectingAgency,
        ["AccountNumber"] = r.AccountNumber,
        ["DueDateWithinPeriod"] = r.DueDateWithinPeriod,
        ["AmountWithinPeriod"] = r.AmountWithinPeriod,
        ["DueDateAfterPeriod"] = r.DueDateAfterPeriod,
        ["AmountAfterPeriod"] = r.AmountAfterPeriod,
        ["TaxAmount"] = r.TaxAmount,
        ["Fee"] = r.Fee,
        ["TotalAmount"] = r.TotalAmount,
        ["PrepaidCardBalance"] = r.PrepaidCardBalance,
        ["CardCompanyName"] = r.CardCompanyName,
        ["InstallmentMonths"] = r.InstallmentMonths,
        ["CardNumber"] = r.CardNumber,
        ["PaymentDate"] = r.PaymentDate,
        ["FeeRatePercentRaw"] = r.FeeRatePercentRaw,
    };

    /// <summary>저장 전 모델과 읽은 모델 비교 — 납세자번호만 "마스킹된 값"을 기대한다. 불일치 시 항목 이름만 로그(값은 더미지만 습관상 남기지 않는다).</summary>
    private static List<string> Diff(NationalTaxReceipt saved, NationalTaxReceipt loaded)
    {
        Dictionary<string, string?> expected = Values(saved);
        expected["TaxpayerNumber"] = saved.TaxpayerNumber is null ? null : ReceiptValueFormatter.MaskTaxpayerNumber(saved.TaxpayerNumber);
        Dictionary<string, string?> actual = Values(loaded);
        var diffs = expected.Where(kv => actual[kv.Key] != kv.Value).Select(kv => kv.Key).ToList();
        if (loaded.IsReprint)
            diffs.Add("IsReprint");
        return diffs;
    }

    private static bool FileContainsAscii(string path, string text)
    {
        SqliteConnection.ClearAllPools();
        byte[] haystack = File.ReadAllBytes(path);
        byte[] needle = Encoding.ASCII.GetBytes(text);
        for (int i = 0; i <= haystack.Length - needle.Length; i++)
        {
            int j = 0;
            while (j < needle.Length && haystack[i + j] == needle[j]) j++;
            if (j == needle.Length) return true;
        }
        return false;
    }

    private static void LogCheck(string name, bool ok, string? detail = null)
    {
        if (ok)
            FileLogger.Info($"{Tag} — {name}] 통과");
        else
            FileLogger.Error(LogCategory.App, $"{Tag} — {name}] ★ 실패{(detail is null ? "" : " — " + detail)}");
    }

    private static int CountRows(string dbPath)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM last_receipt;";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static ShopSettings Settings(bool slipPrintEnabled) =>
        new() { SlipPrintEnabled = slipPrintEnabled, PrinterPort = "5", PrinterSpeed = 115200 };

    // ------------------------------------------------------------------
    // ① 저장/읽기 왕복 — 모든 항목 일치(납세자번호는 마스킹 값), null 유지, 원문 13자리가 DB 파일에 없음
    // ------------------------------------------------------------------
    private static bool RunRoundTripCase()
    {
        string dbPath = NewTempDbPath("roundtrip");
        try
        {
            var store = new LastReceiptStore(dbPath);
            NationalTaxReceipt original = Sample();
            bool saved = store.Save(original, new DateTime(2026, 10, 1, 9, 30, 0));
            NationalTaxReceipt? loaded = store.TryLoad();

            List<string> diffs = loaded is null ? new List<string> { "TryLoad=null" } : Diff(original, loaded);
            bool fieldsOk = saved && diffs.Count == 0;
            LogCheck("① 저장/읽기 왕복 18개 항목 일치(뒤 공백 보존·null 유지·납세자번호 마스킹)", fieldsOk,
                fieldsOk ? null : $"Save={saved}, 불일치 항목={string.Join(",", diffs)}");

            bool maskedOk = loaded?.TaxpayerNumber == MaskedTaxpayerNumber;
            LogCheck("① 읽은 납세자번호 = 앞 6자리 + *", maskedOk);

            bool originalUntouched = original.TaxpayerNumber == RawTaxpayerNumber + "   " && !original.IsReprint;
            LogCheck("① Save가 호출자 모델을 바꾸지 않음", originalUntouched);

            bool rawAbsent = !FileContainsAscii(dbPath, RawTaxpayerNumber);
            bool maskedPresent = FileContainsAscii(dbPath, MaskedTaxpayerNumber);
            LogCheck("① DB 파일 바이트에 납세자번호 원문 13자리 없음(마스킹 값은 있음)", rawAbsent && maskedPresent,
                $"원문 없음={rawAbsent}, 마스킹 값 있음={maskedPresent}");

            return fieldsOk && maskedOk && originalUntouched && rawAbsent && maskedPresent;
        }
        finally
        {
            DeleteTempDb(dbPath);
        }
    }

    // ------------------------------------------------------------------
    // ② 덮어쓰기 — 2건 저장 → 마지막 것, 행은 1개, 첫 거래 값이 파일에 남지 않음
    // ------------------------------------------------------------------
    private static bool RunOverwriteCase()
    {
        string dbPath = NewTempDbPath("overwrite");
        try
        {
            var store = new LastReceiptStore(dbPath);
            NationalTaxReceipt first = Sample("1111111111111111111", "가나다");
            NationalTaxReceipt second = Sample("2222222222222222222", "라마바");
            second.TaxpayerNumber = "9001012345678";
            second.CardNumber = "11112222****444*";

            bool saves = store.Save(first, DateTime.Now) & store.Save(second, DateTime.Now);
            NationalTaxReceipt? loaded = store.TryLoad();
            List<string> diffs = loaded is null ? new List<string> { "TryLoad=null" } : Diff(second, loaded);
            int rows = CountRows(dbPath);
            bool rawAbsent = !FileContainsAscii(dbPath, "9001012345678") && !FileContainsAscii(dbPath, RawTaxpayerNumber);

            bool ok = saves && diffs.Count == 0 && rows == 1 && rawAbsent;
            LogCheck("② 2건 저장 → 마지막 것만, 행 1개, 두 원문 모두 파일에 없음", ok,
                $"Save={saves}, 불일치={string.Join(",", diffs)}, 행={rows}, 원문 없음={rawAbsent}");
            return ok;
        }
        finally
        {
            DeleteTempDb(dbPath);
        }
    }

    // ------------------------------------------------------------------
    // ③ 저장 없음 → TryLoad null (새 파일 / 존재하지 않는 디렉터리 아래 경로 모두)
    // ------------------------------------------------------------------
    private static bool RunEmptyStoreCase()
    {
        string dbPath = NewTempDbPath("empty");
        try
        {
            bool nullOk = new LastReceiptStore(dbPath).TryLoad() is null;
            LogCheck("③ 저장 없음 → TryLoad null", nullOk);

            // 공개 메서드 예외 미전파 — 열 수 없는 경로(디렉터리 자체를 DB 파일로 지정).
            string dirPath = Path.Combine(Path.GetTempPath(), $"p35-dir-{Guid.NewGuid():N}");
            Directory.CreateDirectory(dirPath);
            bool noThrow;
            try
            {
                var bad = new LastReceiptStore(dirPath);
                noThrow = !bad.Save(Sample(), DateTime.Now) && bad.TryLoad() is null;
            }
            catch (Exception)
            {
                noThrow = false;
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                try { Directory.Delete(dirPath, recursive: true); } catch (Exception) { }
            }
            LogCheck("③' 열 수 없는 경로 → Save=false·TryLoad=null, 예외 미전파", noThrow);
            return nullOk && noThrow;
        }
        finally
        {
            DeleteTempDb(dbPath);
        }
    }

    // ------------------------------------------------------------------
    // ④ 자동 출력 경로 — 전표 인쇄 사용 OFF여도 승인되면 저장(출력은 0회), ON+저장 실패여도 출력은 진행
    // ------------------------------------------------------------------
    private static Func<int, string?> Fake902614(string taxpayerNumber) => n => n switch
    {
        7 => "000",
        14 => taxpayerNumber,
        15 => "EPN000000000000035",
        _ => $"C{n:D2}",
    };

    private static bool RunAutoPathSavesWhenOffCase()
    {
        bool ok = true;

        string dbPath = NewTempDbPath("auto-off");
        try
        {
            var fake = new FakeReceiptPrinter(PrintResult.Success());
            var service = new ReceiptPrintService(() => Settings(slipPrintEnabled: false), fake);
            var store = new LastReceiptStore(dbPath);
            var viewModel = new PaymentScreenViewModel(service, store);
            int warnings = 0;
            viewModel.ReceiptPrintWarningRequested += (_, _) => warnings++;

            viewModel.PrintReceiptIfApprovedAsync(Fake902614(RawTaxpayerNumber)).GetAwaiter().GetResult();

            NationalTaxReceipt? loaded = store.TryLoad();
            bool saved = loaded is not null
                && loaded.ElectronicPaymentNumber == "EPN000000000000035"
                && loaded.TaxpayerNumber == MaskedTaxpayerNumber;
            bool notPrinted = fake.CallCount == 0 && warnings == 0;
            bool rawAbsent = !FileContainsAscii(dbPath, RawTaxpayerNumber);
            bool one = saved && notPrinted && rawAbsent;
            LogCheck("④ OFF + 902614 승인 → 저장됨(마스킹)·출력 0회·경고 0회·원문 없음", one,
                $"저장={saved}, 출력 호출={fake.CallCount}, 경고={warnings}, 원문 없음={rawAbsent}");
            ok &= one;

            // 승인이 아니면(#7≠000) 저장하지 않는다 — 직전 저장분이 그대로 남는다.
            viewModel.PrintReceiptIfApprovedAsync(n => n == 7 ? "111" : n == 15 ? "EPN999999999999999" : "X")
                .GetAwaiter().GetResult();
            bool kept = store.TryLoad()?.ElectronicPaymentNumber == "EPN000000000000035";
            LogCheck("④' 미승인(#7=111) → 저장하지 않음(이전 저장분 유지)", kept);
            ok &= kept;
        }
        finally
        {
            DeleteTempDb(dbPath);
        }

        // 저장 실패(열 수 없는 경로)여도 출력은 진행된다.
        string dirPath = Path.Combine(Path.GetTempPath(), $"p35-auto-dir-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dirPath);
        try
        {
            var fake = new FakeReceiptPrinter(PrintResult.Success());
            var service = new ReceiptPrintService(() => Settings(slipPrintEnabled: true), fake);
            var viewModel = new PaymentScreenViewModel(service, new LastReceiptStore(dirPath));
            viewModel.PrintReceiptIfApprovedAsync(Fake902614(RawTaxpayerNumber)).GetAwaiter().GetResult();
            bool printed = fake.CallCount == 1;
            LogCheck("④'' ON + 저장 실패 → 출력은 그대로 1회", printed, $"출력 호출={fake.CallCount}");
            ok &= printed;
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(dirPath, recursive: true); } catch (Exception) { }
        }

        return ok;
    }

    // ------------------------------------------------------------------
    // ⑤ ReprintAsync — 설정 OFF여도(설정 로더 호출 0회) 출력, 첫 줄 "(재출력)", 넘긴 포트·속도 사용
    // ------------------------------------------------------------------
    private static bool RunReprintIgnoresSettingCase()
    {
        bool ok = true;
        int settingsLoads = 0;
        NationalTaxReceipt receipt = Sample();
        receipt.TaxpayerNumber = MaskedTaxpayerNumber; // 저장소에서 읽은 값과 같은 형태

        var fake = new FakeReceiptPrinter(PrintResult.Success());
        var service = new ReceiptPrintService(() => { settingsLoads++; return Settings(slipPrintEnabled: false); }, fake);
        ReceiptPrintOutcome outcome = service.ReprintAsync(receipt, new PrinterConnection("7", 9600), CancellationToken.None)
            .GetAwaiter().GetResult();

        var expectedReceipt = Sample();
        expectedReceipt.TaxpayerNumber = MaskedTaxpayerNumber;
        expectedReceipt.IsReprint = true;
        PrintDocument expectedDoc = NationalTaxReceiptComposer.Compose(expectedReceipt);
        byte[] expectedPayload = EscPosDocumentEncoder.Encode(expectedDoc);

        bool printed = outcome.Status == ReceiptPrintStatus.Printed && fake.CallCount == 1 && settingsLoads == 0;
        LogCheck("⑤ 설정 OFF여도 재출력 1회(설정 로더 호출 0회)", printed,
            $"Status={outcome.Status}, 출력 호출={fake.CallCount}, 설정 로드={settingsLoads}");
        ok &= printed;

        bool firstLine = expectedDoc.Lines.Count > 0 && expectedDoc.Lines[0].Text == "(재출력)"
            && expectedDoc.Lines[0].Alignment == PrintAlignment.Center && expectedDoc.Lines[0].Size == PrintSize.Normal
            && fake.LastPayload is not null && fake.LastPayload.SequenceEqual(expectedPayload);
        LogCheck("⑤ payload = (재출력) 첫 줄(가운데·보통) 붙은 양식 그대로", firstLine);
        ok &= firstLine;

        bool connectionOk = fake.LastConnection?.PortNumberText == "7" && fake.LastConnection?.BaudRate == 9600;
        LogCheck("⑤ 넘긴 포트·속도(7/9600) 사용 — 설정값(5/115200) 아님", connectionOk,
            $"port={fake.LastConnection?.PortNumberText}, baud={fake.LastConnection?.BaudRate}");
        ok &= connectionOk;

        bool inputUntouched = !receipt.IsReprint;
        LogCheck("⑤ 호출자 모델의 IsReprint는 그대로 false", inputUntouched);
        ok &= inputUntouched;

        // 실패 사유 매핑·예외 미전파.
        var failing = new FakeReceiptPrinter(PrintResult.Fail(PrintFailureReason.PortOpenFailed));
        ReceiptPrintOutcome failed = new ReceiptPrintService(() => Settings(false), failing)
            .ReprintAsync(Sample(), new PrinterConnection("4", 57600), CancellationToken.None).GetAwaiter().GetResult();
        bool failOk = failed.Status == ReceiptPrintStatus.Failed && failed.Failure == ReceiptPrintFailureReason.PortOpenFailed;
        LogCheck("⑤ 프린터 실패(PortOpenFailed) → Failed/PortOpenFailed", failOk, $"{failed.Status}/{failed.Failure}");
        ok &= failOk;

        ReceiptPrintOutcome nullReceipt = new ReceiptPrintService(() => Settings(false), failing)
            .ReprintAsync(null!, new PrinterConnection("4", 57600), CancellationToken.None).GetAwaiter().GetResult();
        bool noThrow = nullReceipt.Status == ReceiptPrintStatus.Failed;
        LogCheck("⑤ receipt=null → 예외 없이 Failed", noThrow, $"{nullReceipt.Status}/{nullReceipt.Failure}");
        ok &= noThrow;

        // 실제 SerialReceiptPrinter + 포트 빈 값/범위 밖 → InvalidPort(포트를 열지 않음).
        ReceiptPrintOutcome invalid = new ReceiptPrintService(() => Settings(false), new SerialReceiptPrinter())
            .ReprintAsync(Sample(), new PrinterConnection("256", 57600), CancellationToken.None).GetAwaiter().GetResult();
        bool invalidOk = invalid.Failure == ReceiptPrintFailureReason.InvalidPort;
        LogCheck("⑤ 실제 프린터 + 포트 256 → InvalidPort", invalidOk, $"{invalid.Status}/{invalid.Failure}");
        ok &= invalidOk;

        return ok;
    }

    // ------------------------------------------------------------------
    // ⑥ 경고 문구 — 자동 출력은 재출력 안내 포함(마지막 줄), 재출력 실패 문구는 미포함
    // ------------------------------------------------------------------
    private static bool RunMessageCases()
    {
        bool ok = true;
        foreach (ReceiptPrintFailureReason reason in new[]
                 {
                     ReceiptPrintFailureReason.InvalidPort, ReceiptPrintFailureReason.PortOpenFailed,
                     ReceiptPrintFailureReason.NoResponse, ReceiptPrintFailureReason.PaperEnd,
                     ReceiptPrintFailureReason.CoverOpen, ReceiptPrintFailureReason.WriteFailed,
                     ReceiptPrintFailureReason.Unexpected,
                 })
        {
            string auto = PaymentScreenViewModel.BuildReceiptPrintWarningMessage(reason);
            string reprint = PaymentScreenViewModel.BuildReceiptPrintFailureMessage(reason);
            bool one = auto == reprint + NationalTaxReceiptAssemblerSelfTest.ExpectedReprintHintSuffix
                && reprint.StartsWith("전표 출력에 실패했습니다.", StringComparison.Ordinal)
                && !reprint.Contains("직전거래 전표출력")
                && !reprint.Contains("\n");
            LogCheck($"⑥ {reason} — 자동=안내 포함 / 재출력=안내 없음", one);
            ok &= one;
        }
        return ok;
    }

    // ------------------------------------------------------------------
    // ⑦ 가맹점 설정 화면 버튼(ShopSetupViewModel.RequestLastSlipPrintCommand)
    // ------------------------------------------------------------------

    /// <summary>호출되면 <see cref="Release"/>까지 완료하지 않는 프린터 — 실행 중 버튼 비활성 확인용.</summary>
    private sealed class BlockingReceiptPrinter : IReceiptPrinter
    {
        private readonly TaskCompletionSource<PrintResult> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly ManualResetEventSlim Entered = new(false);
        public int CallCount;

        public Task<PrintResult> PrintAsync(byte[] payload, PrinterConnection connection, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref CallCount);
            Entered.Set();
            return _tcs.Task;
        }

        public void Release() => _tcs.TrySetResult(PrintResult.Success());
    }

    private static (ShopSetupViewModel Vm, List<string> Warnings, List<string> Infos) NewShopSetup(
        IReceiptPrinter printer, LastReceiptStore store, string port, string speedDisplay)
    {
        // 설정 로더는 OFF — 재출력이 설정을 보지 않음을 화면 경로에서도 확인한다. 실제 레지스트리 값은
        // ShopSetupViewModel.Load()가 화면 초기값으로만 읽고(쓰기 없음), 아래에서 화면 값을 덮어쓴다.
        var service = new ReceiptPrintService(() => Settings(slipPrintEnabled: false), printer);
        var vm = new ShopSetupViewModel(service, store)
        {
            SlipPrintEnabled = false,
            PrinterPort = port,
            PrinterSpeedSelection = speedDisplay,
        };
        var warnings = new List<string>();
        var infos = new List<string>();
        vm.ResultMessageReady += (_, m) => { lock (warnings) warnings.Add(m); };
        vm.InfoMessageReady += (_, m) => { lock (infos) infos.Add(m); };
        return (vm, warnings, infos);
    }

    private static bool RunShopSetupButtonCases()
    {
        bool ok = true;
        string dbPath = NewTempDbPath("shopsetup");
        try
        {
            var store = new LastReceiptStore(dbPath);

            // (a) 포트 빈 값 → 기존 오류창, 저장소·프린터 미호출.
            var fakeA = new FakeReceiptPrinter(PrintResult.Success());
            var a = NewShopSetup(fakeA, store, port: "", speedDisplay: "9600bps");
            a.Vm.RequestLastSlipPrintCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            bool aOk = a.Warnings.SequenceEqual(new[] { "프린터 포트번호를 입력해주세요." }) && a.Infos.Count == 0 && fakeA.CallCount == 0;
            LogCheck("⑦ 포트 빈 값 → \"프린터 포트번호를 입력해주세요.\"", aOk,
                $"경고=[{string.Join("|", a.Warnings)}], 안내=[{string.Join("|", a.Infos)}], 출력={fakeA.CallCount}");
            ok &= aOk;

            // (b) 저장 없음 → 정보창, 출력 0회.
            var fakeB = new FakeReceiptPrinter(PrintResult.Success());
            var b = NewShopSetup(fakeB, store, port: "7", speedDisplay: "9600bps");
            b.Vm.RequestLastSlipPrintCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            bool bOk = b.Infos.SequenceEqual(new[] { "출력할 직전 거래가 없습니다." }) && b.Warnings.Count == 0 && fakeB.CallCount == 0;
            LogCheck("⑦ 저장 없음 → \"출력할 직전 거래가 없습니다.\"(정보)", bOk,
                $"경고=[{string.Join("|", b.Warnings)}], 안내=[{string.Join("|", b.Infos)}], 출력={fakeB.CallCount}");
            ok &= bOk;

            store.Save(Sample(), DateTime.Now);

            // (c) 성공 → 안내창 없음, 화면 포트·속도(7 / "9600bps"→9600) 사용, (재출력) payload.
            var fakeC = new FakeReceiptPrinter(PrintResult.Success());
            var c = NewShopSetup(fakeC, store, port: "7", speedDisplay: "9600bps");
            c.Vm.RequestLastSlipPrintCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            var expectedReceipt = Sample();
            expectedReceipt.TaxpayerNumber = MaskedTaxpayerNumber;
            expectedReceipt.IsReprint = true;
            byte[] expectedPayload = EscPosDocumentEncoder.Encode(NationalTaxReceiptComposer.Compose(expectedReceipt));
            bool cOk = c.Warnings.Count == 0 && c.Infos.Count == 0 && fakeC.CallCount == 1
                && fakeC.LastConnection?.PortNumberText == "7" && fakeC.LastConnection?.BaudRate == 9600
                && fakeC.LastPayload is not null && fakeC.LastPayload.SequenceEqual(expectedPayload);
            LogCheck("⑦ 저장 있음 + 성공 → 안내창 없음, 화면 포트·속도(7/9600), (재출력) 영수증", cOk,
                $"경고={c.Warnings.Count}, 안내={c.Infos.Count}, 출력={fakeC.CallCount}, " +
                $"port={fakeC.LastConnection?.PortNumberText}, baud={fakeC.LastConnection?.BaudRate}");
            ok &= cOk;

            // (d) 실패 → 경고창(PRD §5 문구, 재출력 안내 없음).
            var fakeD = new FakeReceiptPrinter(PrintResult.Fail(PrintFailureReason.PortOpenFailed));
            var d = NewShopSetup(fakeD, store, port: "4", speedDisplay: "115200bps");
            d.Vm.RequestLastSlipPrintCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            bool dOk = d.Warnings.SequenceEqual(new[] { "전표 출력에 실패했습니다. (프린터 포트를 열 수 없습니다)" })
                && d.Infos.Count == 0 && fakeD.LastConnection?.BaudRate == 115200;
            LogCheck("⑦ 실패 → \"전표 출력에 실패했습니다. (프린터 포트를 열 수 없습니다)\"(재출력 안내 없음)", dOk,
                $"경고=[{string.Join("|", d.Warnings)}], 안내={d.Infos.Count}");
            ok &= dOk;

            // (e) 실행 중 버튼 비활성(CanExecute=false) → 완료 후 다시 활성. 진행 중 재실행 요청은 무시.
            var blocking = new BlockingReceiptPrinter();
            var e = NewShopSetup(blocking, store, port: "7", speedDisplay: "9600bps");
            bool before = e.Vm.RequestLastSlipPrintCommand.CanExecute(null);
            Task running = e.Vm.RequestLastSlipPrintCommand.ExecuteAsync(null);
            bool entered = blocking.Entered.Wait(TimeSpan.FromSeconds(10));
            bool during = e.Vm.RequestLastSlipPrintCommand.CanExecute(null);
            bool runningFlag = e.Vm.RequestLastSlipPrintCommand.IsRunning;
            blocking.Release();
            bool finished = running.Wait(TimeSpan.FromSeconds(10));
            bool after = e.Vm.RequestLastSlipPrintCommand.CanExecute(null);
            bool eOk = before && entered && !during && runningFlag && finished && after && blocking.CallCount == 1;
            LogCheck("⑦ 재출력 중 CanExecute=false(버튼 비활성)·IsRunning=true·완료 후 true", eOk,
                $"전={before}, 진입={entered}, 중={during}, IsRunning={runningFlag}, 완료={finished}, 후={after}, 출력 호출={blocking.CallCount}");
            ok &= eOk;
        }
        catch (Exception ex)
        {
            LogCheck("⑦ 설정 화면 버튼 — 예외 미전파", false, $"{ex.GetType().Name}: {ex.Message}");
            ok = false;
        }
        finally
        {
            DeleteTempDb(dbPath);
        }
        return ok;
    }
}
