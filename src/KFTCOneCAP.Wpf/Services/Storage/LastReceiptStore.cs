using System;
using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;
using KFTCOneCAP.Wpf.Services.Diagnostics;
using KFTCOneCAP.Wpf.Services.Receipt;

namespace KFTCOneCAP.Wpf.Services.Storage;

/// <summary>
/// Phase 35 P35-1(docs/receipt_print/PRD.md §6, development_plan.md Phase 35) — "직전거래 전표출력"이
/// 다시 뽑을 수 있도록 <b>가장 최근 승인 거래의 영수증 값(<see cref="NationalTaxReceipt"/>)</b>을 저장한다.
/// <see cref="LastTransactionResponseStore"/>와 같은 패턴이다:
///
/// - <see cref="IntegrityCheckStore.DefaultDatabasePath"/>와 같은 SQLite 파일, 별도 테이블 <c>last_receipt</c>.
/// - <b>이력을 쌓지 않는다</b> — 고정 키(<see cref="FixedRowId"/>) 1행 upsert.
/// - ESC/POS 바이트가 아니라 <b>항목 값</b>을 저장한다(양식을 고쳐도 재출력은 새 양식으로 나온다, PRD §6).
///   net48에 기본 JSON 직렬화기가 없어(새 패키지 추가 금지) 항목별 컬럼으로 둔다. 값은 SPEC 원문 그대로
///   (트림·패딩 제거 전) 저장하고, <c>null</c>은 SQL <c>NULL</c>로 저장해 왕복 시 null/빈 문자열 구분을 유지한다.
/// - <b>납세자번호는 저장 전에 <see cref="ReceiptValueFormatter.MaskTaxpayerNumber"/>로 마스킹한다</b> —
///   모델에는 원문 13자리가 들어 있으므로 그대로 쓰면 디스크에 남는다(PRD §6). 마스킹은 멱등이라 다시
///   읽어 조립기(Composer)가 한 번 더 마스킹해도 결과가 같다. 카드번호는 리더기가 이미 마스킹한 값이다.
/// - <see cref="NationalTaxReceipt.IsReprint"/>는 저장하지 않는다(출력 시점의 속성이지 거래 값이 아니다).
/// - 공개 메서드는 예외를 던지지 않는다(PRD §6 — 저장 실패는 WARN, 출력은 계속). 로그에는 영수증 값을
///   남기지 않는다(예외 타입·메시지만).
/// </summary>
public sealed class LastReceiptStore
{
    /// <summary>이력이 아니라 "가장 최근 1건"만 저장하므로 고정 키를 쓴다.</summary>
    private const int FixedRowId = 1;

    private readonly string _connectionString;

    public LastReceiptStore()
        : this(IntegrityCheckStore.DefaultDatabasePath())
    {
    }

    /// <summary>테스트/진단 하네스가 임시 경로를 지정할 수 있도록 내부 생성자를 열어 둔다
    /// (<see cref="LastTransactionResponseStore"/>와 동일한 패턴).</summary>
    internal LastReceiptStore(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
        }.ToString();
    }

    /// <summary>
    /// 영수증 값 1건을 upsert한다(고정 키, 이력 없음). 납세자번호는 마스킹 후 저장한다. 실패는 예외 없이
    /// <c>false</c>(WARN 로그) — 저장은 재출력 보조 수단이지 출력·거래의 성립 조건이 아니다.
    /// </summary>
    public bool Save(NationalTaxReceipt receipt, DateTime savedAt)
    {
        if (receipt is null)
        {
            FileLogger.Warn(LogCategory.Printer, "[LastReceiptStore] 직전 영수증 저장 실패: receipt가 null");
            return false;
        }

        try
        {
            using var connection = OpenConnection();
            EnsureSchema(connection);

            using var command = connection.CreateCommand();
            command.CommandText = @"
INSERT INTO last_receipt
    (id, electronic_payment_number, taxpayer_name, taxpayer_number_masked, collecting_agency, account_number,
     due_date_within_period, amount_within_period, due_date_after_period, amount_after_period,
     tax_amount, fee, total_amount, prepaid_card_balance, card_company_name, installment_months,
     card_number, payment_date, fee_rate_percent_raw, saved_at)
VALUES
    ($id, $electronicPaymentNumber, $taxpayerName, $taxpayerNumberMasked, $collectingAgency, $accountNumber,
     $dueDateWithinPeriod, $amountWithinPeriod, $dueDateAfterPeriod, $amountAfterPeriod,
     $taxAmount, $fee, $totalAmount, $prepaidCardBalance, $cardCompanyName, $installmentMonths,
     $cardNumber, $paymentDate, $feeRatePercentRaw, $savedAt)
ON CONFLICT(id) DO UPDATE SET
    electronic_payment_number = excluded.electronic_payment_number,
    taxpayer_name = excluded.taxpayer_name,
    taxpayer_number_masked = excluded.taxpayer_number_masked,
    collecting_agency = excluded.collecting_agency,
    account_number = excluded.account_number,
    due_date_within_period = excluded.due_date_within_period,
    amount_within_period = excluded.amount_within_period,
    due_date_after_period = excluded.due_date_after_period,
    amount_after_period = excluded.amount_after_period,
    tax_amount = excluded.tax_amount,
    fee = excluded.fee,
    total_amount = excluded.total_amount,
    prepaid_card_balance = excluded.prepaid_card_balance,
    card_company_name = excluded.card_company_name,
    installment_months = excluded.installment_months,
    card_number = excluded.card_number,
    payment_date = excluded.payment_date,
    fee_rate_percent_raw = excluded.fee_rate_percent_raw,
    saved_at = excluded.saved_at;";

            // PRD §6 — 원문 13자리가 디스크에 남지 않게 마스킹한 값만 바인딩한다. null은 null로 둔다.
            string? maskedTaxpayerNumber = receipt.TaxpayerNumber is null
                ? null
                : ReceiptValueFormatter.MaskTaxpayerNumber(receipt.TaxpayerNumber);

            command.Parameters.AddWithValue("$id", FixedRowId);
            AddText(command, "$electronicPaymentNumber", receipt.ElectronicPaymentNumber);
            AddText(command, "$taxpayerName", receipt.TaxpayerName);
            AddText(command, "$taxpayerNumberMasked", maskedTaxpayerNumber);
            AddText(command, "$collectingAgency", receipt.CollectingAgency);
            AddText(command, "$accountNumber", receipt.AccountNumber);
            AddText(command, "$dueDateWithinPeriod", receipt.DueDateWithinPeriod);
            AddText(command, "$amountWithinPeriod", receipt.AmountWithinPeriod);
            AddText(command, "$dueDateAfterPeriod", receipt.DueDateAfterPeriod);
            AddText(command, "$amountAfterPeriod", receipt.AmountAfterPeriod);
            AddText(command, "$taxAmount", receipt.TaxAmount);
            AddText(command, "$fee", receipt.Fee);
            AddText(command, "$totalAmount", receipt.TotalAmount);
            AddText(command, "$prepaidCardBalance", receipt.PrepaidCardBalance);
            AddText(command, "$cardCompanyName", receipt.CardCompanyName);
            AddText(command, "$installmentMonths", receipt.InstallmentMonths);
            AddText(command, "$cardNumber", receipt.CardNumber);
            AddText(command, "$paymentDate", receipt.PaymentDate);
            AddText(command, "$feeRatePercentRaw", receipt.FeeRatePercentRaw);
            command.Parameters.AddWithValue("$savedAt", savedAt.ToString(IntegrityCheckStore.TimestampFormat, CultureInfo.InvariantCulture));
            command.ExecuteNonQuery();

            return true;
        }
        catch (Exception ex)
        {
            // 영수증 값(납세자명 등)은 로그에 남기지 않는다 — 예외 타입·메시지만.
            FileLogger.Warn(LogCategory.Printer, $"[LastReceiptStore] 직전 영수증 저장 실패: {ex.GetType().Name} - {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 저장된 직전 영수증을 읽는다. 기록이 없거나 조회가 실패하면 <c>null</c>(예외 없음). 돌려주는 모델의
    /// <see cref="NationalTaxReceipt.TaxpayerNumber"/>는 이미 마스킹된 값이고 <see cref="NationalTaxReceipt.IsReprint"/>는
    /// <c>false</c>다(재출력 표시는 출력 서비스가 정한다).
    /// </summary>
    public NationalTaxReceipt? TryLoad()
    {
        try
        {
            using var connection = OpenConnection();
            EnsureSchema(connection);

            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT electronic_payment_number, taxpayer_name, taxpayer_number_masked, collecting_agency, account_number,
       due_date_within_period, amount_within_period, due_date_after_period, amount_after_period,
       tax_amount, fee, total_amount, prepaid_card_balance, card_company_name, installment_months,
       card_number, payment_date, fee_rate_percent_raw
FROM last_receipt
WHERE id = $id;";
            command.Parameters.AddWithValue("$id", FixedRowId);

            using SqliteDataReader reader = command.ExecuteReader();
            if (!reader.Read())
                return null;

            return new NationalTaxReceipt
            {
                ElectronicPaymentNumber = ReadText(reader, 0),
                TaxpayerName = ReadText(reader, 1),
                TaxpayerNumber = ReadText(reader, 2),
                CollectingAgency = ReadText(reader, 3),
                AccountNumber = ReadText(reader, 4),
                DueDateWithinPeriod = ReadText(reader, 5),
                AmountWithinPeriod = ReadText(reader, 6),
                DueDateAfterPeriod = ReadText(reader, 7),
                AmountAfterPeriod = ReadText(reader, 8),
                TaxAmount = ReadText(reader, 9),
                Fee = ReadText(reader, 10),
                TotalAmount = ReadText(reader, 11),
                PrepaidCardBalance = ReadText(reader, 12),
                CardCompanyName = ReadText(reader, 13),
                InstallmentMonths = ReadText(reader, 14),
                CardNumber = ReadText(reader, 15),
                PaymentDate = ReadText(reader, 16),
                FeeRatePercentRaw = ReadText(reader, 17),
                IsReprint = false,
            };
        }
        catch (Exception ex)
        {
            FileLogger.Warn(LogCategory.Printer, $"[LastReceiptStore] 직전 영수증 조회 실패: {ex.GetType().Name} - {ex.Message}");
            return null;
        }
    }

    private static void AddText(SqliteCommand command, string name, string? value) =>
        command.Parameters.AddWithValue(name, (object?)value ?? DBNull.Value);

    private static string? ReadText(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private SqliteConnection OpenConnection()
    {
        var builder = new SqliteConnectionStringBuilder(_connectionString);
        string? dir = Path.GetDirectoryName(builder.DataSource);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    /// <summary>최초 실행 시 테이블을 자동 생성한다("IF NOT EXISTS", 다른 스토어와 같은 파일·다른 테이블).
    /// 납세자번호 컬럼 이름에 <c>_masked</c>를 붙여 원문이 아님을 스키마에서 바로 드러낸다.</summary>
    private static void EnsureSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = @"
CREATE TABLE IF NOT EXISTS last_receipt (
    id INTEGER PRIMARY KEY,
    electronic_payment_number TEXT NULL,
    taxpayer_name TEXT NULL,
    taxpayer_number_masked TEXT NULL,
    collecting_agency TEXT NULL,
    account_number TEXT NULL,
    due_date_within_period TEXT NULL,
    amount_within_period TEXT NULL,
    due_date_after_period TEXT NULL,
    amount_after_period TEXT NULL,
    tax_amount TEXT NULL,
    fee TEXT NULL,
    total_amount TEXT NULL,
    prepaid_card_balance TEXT NULL,
    card_company_name TEXT NULL,
    installment_months TEXT NULL,
    card_number TEXT NULL,
    payment_date TEXT NULL,
    fee_rate_percent_raw TEXT NULL,
    saved_at TEXT NOT NULL
);";
        command.ExecuteNonQuery();
    }
}
