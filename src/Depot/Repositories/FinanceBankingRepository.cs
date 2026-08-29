// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;
using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class FinanceBankingRepository : DatabaseRepository
{
	public FinanceBankingRepository(DatabaseAccess database) : base(database) { }

	public Task<IReadOnlyList<FinanceBankAccount>> GetBankAccountsAsync(CancellationToken cancellationToken = default) =>
		Database.QueryAsync("SELECT Id,Version,LegalEntityId,AccountingBookId,GeneralLedgerAccountId,CurrencyCode,Name,BankName,Iban,Bic,LocalAccountNumber,IsActive FROM FinanceBankAccounts ORDER BY Name,Id;", ReadBankAccount, cancellationToken);

	public Task<FinanceBankAccount?> GetBankAccountAsync(long id, CancellationToken cancellationToken = default) =>
		Database.QuerySingleOrDefaultAsync("SELECT Id,Version,LegalEntityId,AccountingBookId,GeneralLedgerAccountId,CurrencyCode,Name,BankName,Iban,Bic,LocalAccountNumber,IsActive FROM FinanceBankAccounts WHERE Id=$Id;", ReadBankAccount, cancellationToken, Parameter("$Id", id));

	public async Task<IReadOnlyList<FinanceBankStatement>> GetStatementsAsync(long? bankAccountId = null, int count = 100, CancellationToken cancellationToken = default)
	{
		var sql = bankAccountId.HasValue
			? "SELECT Id,OperationId,BankAccountId,Format,StatementReference,ImportHash,SourceFileName,CurrencyCode,FromDate,ToDate,OpeningBalance,ClosingBalance,ImportedAtUtc,ImportedByUserId FROM FinanceBankStatements WHERE BankAccountId=$Account ORDER BY ToDate DESC,Id DESC;"
			: "SELECT Id,OperationId,BankAccountId,Format,StatementReference,ImportHash,SourceFileName,CurrencyCode,FromDate,ToDate,OpeningBalance,ClosingBalance,ImportedAtUtc,ImportedByUserId FROM FinanceBankStatements ORDER BY ToDate DESC,Id DESC;";
		var rows = bankAccountId.HasValue
			? await Database.QueryAsync(sql, ReadStatement, cancellationToken, Parameter("$Account", bankAccountId.Value))
			: await Database.QueryAsync(sql, ReadStatement, cancellationToken);
		return rows.Take(Math.Clamp(count, 1, 500)).ToArray();
	}

	public async Task<FinanceBankStatement?> GetStatementAsync(long id, CancellationToken cancellationToken = default)
	{
		var statement = await Database.QuerySingleOrDefaultAsync("SELECT Id,OperationId,BankAccountId,Format,StatementReference,ImportHash,SourceFileName,CurrencyCode,FromDate,ToDate,OpeningBalance,ClosingBalance,ImportedAtUtc,ImportedByUserId FROM FinanceBankStatements WHERE Id=$Id;", ReadStatement, cancellationToken, Parameter("$Id", id));
		if (statement is null) return null;
		var lines = await Database.QueryAsync(LineSelect + " WHERE l.StatementId=$Statement ORDER BY l.LineNumber;", ReadLine, cancellationToken, Parameter("$Statement", id));
		return statement with { Lines = lines };
	}

	public Task<IReadOnlyList<FinanceBankStatementLine>> GetUnreconciledLinesAsync(long? bankAccountId = null, CancellationToken cancellationToken = default)
	{
		var filter = bankAccountId.HasValue ? " AND s.BankAccountId=$Account" : string.Empty;
		var sql = LineSelect + " INNER JOIN FinanceBankStatements s ON s.Id=l.StatementId WHERE NOT EXISTS (SELECT 1 FROM FinanceBankReconciliations r WHERE r.StatementLineId=l.Id AND r.ReversedAtUtc IS NULL)" + filter + " ORDER BY l.BookingDate,l.Id;";
		return bankAccountId.HasValue
			? Database.QueryAsync(sql, ReadLine, cancellationToken, Parameter("$Account", bankAccountId.Value))
			: Database.QueryAsync(sql, ReadLine, cancellationToken);
	}

	public async Task<IReadOnlyList<FinancePaymentRun>> GetPaymentRunsAsync(int count = 100, CancellationToken cancellationToken = default)
	{
		var rows = await Database.QueryAsync(RunSelect + " ORDER BY PaymentDate DESC,Id DESC;", ReadRun, cancellationToken);
		return rows.Take(Math.Clamp(count, 1, 500)).ToArray();
	}

	public async Task<FinancePaymentRun?> GetPaymentRunAsync(long id, CancellationToken cancellationToken = default)
	{
		var run = await Database.QuerySingleOrDefaultAsync(RunSelect + " WHERE Id=$Id;", ReadRun, cancellationToken, Parameter("$Id", id));
		if (run is null) return null;
		var lines = await Database.QueryAsync(RunLineSelect + " WHERE PaymentRunId=$Run ORDER BY Id;", ReadRunLine, cancellationToken, Parameter("$Run", id));
		return run with { Lines = lines };
	}

	internal Task<FinanceBankAccount?> GetBankAccountAsync(DatabaseTransactionContext transaction, long id, CancellationToken cancellationToken) =>
		transaction.Session.QuerySingleOrDefaultAsync("SELECT Id,Version,LegalEntityId,AccountingBookId,GeneralLedgerAccountId,CurrencyCode,Name,BankName,Iban,Bic,LocalAccountNumber,IsActive FROM FinanceBankAccounts WHERE Id=$Id;", ReadBankAccount, cancellationToken, Parameter("$Id", id));

	internal Task<long> CreateBankAccountAsync(DatabaseTransactionContext transaction, FinanceBankAccount account, CancellationToken cancellationToken) =>
		transaction.Session.InsertAsync("INSERT INTO FinanceBankAccounts (Version,LegalEntityId,AccountingBookId,GeneralLedgerAccountId,CurrencyCode,Name,BankName,Iban,Bic,LocalAccountNumber,IsActive) VALUES (1,$Entity,$Book,$Gl,$Currency,$Name,$Bank,$Iban,$Bic,$Local,$Active);", cancellationToken, AccountParameters(account));

	internal Task<int> UpdateBankAccountAsync(DatabaseTransactionContext transaction, FinanceBankAccount account, long expectedVersion, CancellationToken cancellationToken) =>
		transaction.Session.ExecuteAsync("UPDATE FinanceBankAccounts SET Version=Version+1,LegalEntityId=$Entity,AccountingBookId=$Book,GeneralLedgerAccountId=$Gl,CurrencyCode=$Currency,Name=$Name,BankName=$Bank,Iban=$Iban,Bic=$Bic,LocalAccountNumber=$Local,IsActive=$Active WHERE Id=$Id AND Version=$Version;", cancellationToken, AccountParameters(account).Append(Parameter("$Id", account.Id)).Append(Parameter("$Version", expectedVersion)).ToArray());

	internal async Task<bool> ValidateBankAccountConfigurationAsync(DatabaseTransactionContext transaction, FinanceBankAccount account, CancellationToken cancellationToken)
	{
		var value = await transaction.Session.ExecuteScalarAsync("SELECT COUNT(*) FROM FinanceAccountingBooks b INNER JOIN FinanceAccounts a ON a.ChartOfAccountsId=b.ChartOfAccountsId WHERE b.Id=$Book AND b.LegalEntityId=$Entity AND b.IsActive=1 AND a.Id=$Gl AND a.IsActive=1 AND a.AllowDirectPosting=1;", cancellationToken, Parameter("$Book", account.AccountingBookId.ToString("D")), Parameter("$Entity", account.LegalEntityId.ToString("D")), Parameter("$Gl", account.GeneralLedgerAccountId.ToString("D")));
		return Convert.ToInt64(value ?? 0, CultureInfo.InvariantCulture) == 1;
	}

	internal Task<FinanceBankStatement?> FindStatementByOperationAsync(DatabaseTransactionContext transaction, Guid operationId, CancellationToken cancellationToken) =>
		transaction.Session.QuerySingleOrDefaultAsync("SELECT Id,OperationId,BankAccountId,Format,StatementReference,ImportHash,SourceFileName,CurrencyCode,FromDate,ToDate,OpeningBalance,ClosingBalance,ImportedAtUtc,ImportedByUserId FROM FinanceBankStatements WHERE OperationId=$Operation;", ReadStatement, cancellationToken, Parameter("$Operation", operationId.ToString("D")));

	internal Task<FinanceBankStatement?> FindStatementByHashAsync(DatabaseTransactionContext transaction, string importHash, CancellationToken cancellationToken) =>
		transaction.Session.QuerySingleOrDefaultAsync("SELECT Id,OperationId,BankAccountId,Format,StatementReference,ImportHash,SourceFileName,CurrencyCode,FromDate,ToDate,OpeningBalance,ClosingBalance,ImportedAtUtc,ImportedByUserId FROM FinanceBankStatements WHERE ImportHash=$Hash;", ReadStatement, cancellationToken, Parameter("$Hash", importHash));

	internal Task<long> CreateStatementAsync(DatabaseTransactionContext transaction, FinanceBankStatement value, CancellationToken cancellationToken) =>
		transaction.Session.InsertAsync("INSERT INTO FinanceBankStatements (OperationId,BankAccountId,Format,StatementReference,ImportHash,SourceFileName,CurrencyCode,FromDate,ToDate,OpeningBalance,ClosingBalance,ImportedAtUtc,ImportedByUserId) VALUES ($Operation,$Account,$Format,$Reference,$Hash,$File,$Currency,$From,$To,$Opening,$Closing,$At,$User);", cancellationToken,
			Parameter("$Operation", value.OperationId.ToString("D")), Parameter("$Account", value.BankAccountId), Parameter("$Format", (int)value.Format), Parameter("$Reference", value.StatementReference), Parameter("$Hash", value.ImportHash), Parameter("$File", value.SourceFileName), Parameter("$Currency", value.Currency.Value), Parameter("$From", value.FromDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), Parameter("$To", value.ToDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), Parameter("$Opening", value.OpeningBalance), Parameter("$Closing", value.ClosingBalance), Parameter("$At", value.ImportedAtUtc.ToString("O", CultureInfo.InvariantCulture)), Parameter("$User", value.ImportedByUserId));

	internal Task<long> CreateStatementLineAsync(DatabaseTransactionContext transaction, FinanceBankStatementLine value, CancellationToken cancellationToken) =>
		transaction.Session.InsertAsync("INSERT INTO FinanceBankStatementLines (StatementId,LineNumber,BookingDate,ValueDate,Amount,CurrencyCode,ExternalId,Reference,CounterpartyName,BankTransactionCode) VALUES ($Statement,$Line,$Booking,$Value,$Amount,$Currency,$External,$Reference,$Counterparty,$Code);", cancellationToken,
			Parameter("$Statement", value.StatementId), Parameter("$Line", value.LineNumber), Parameter("$Booking", value.BookingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), Parameter("$Value", value.ValueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), Parameter("$Amount", value.Amount), Parameter("$Currency", value.Currency.Value), Parameter("$External", value.ExternalId), Parameter("$Reference", value.Reference), Parameter("$Counterparty", value.CounterpartyName), Parameter("$Code", value.BankTransactionCode));

	internal Task<FinanceBankStatementLineContext?> GetStatementLineContextAsync(DatabaseTransactionContext transaction, long lineId, CancellationToken cancellationToken) =>
		transaction.Session.QuerySingleOrDefaultAsync("SELECT l.Id,l.StatementId,l.LineNumber,l.BookingDate,l.ValueDate,l.Amount,l.CurrencyCode,l.ExternalId,l.Reference,l.CounterpartyName,l.BankTransactionCode,s.BankAccountId,a.AccountingBookId,a.GeneralLedgerAccountId,a.CurrencyCode FROM FinanceBankStatementLines l INNER JOIN FinanceBankStatements s ON s.Id=l.StatementId INNER JOIN FinanceBankAccounts a ON a.Id=s.BankAccountId WHERE l.Id=$Id;", reader => new FinanceBankStatementLineContext(ReadLineBase(reader), reader.GetInt64(11), Guid.Parse(reader.GetString(12)), Guid.Parse(reader.GetString(13)), new CurrencyCode(reader.GetString(14))), cancellationToken, Parameter("$Id", lineId));

	internal Task<FinanceBankReconciliation?> FindReconciliationByOperationAsync(DatabaseTransactionContext transaction, Guid operationId, CancellationToken cancellationToken) =>
		transaction.Session.QuerySingleOrDefaultAsync(ReconciliationSelect + " WHERE OperationId=$Operation;", ReadReconciliation, cancellationToken, Parameter("$Operation", operationId.ToString("D")));

	internal Task<FinanceBankReconciliation?> GetActiveReconciliationForLineAsync(DatabaseTransactionContext transaction, long lineId, CancellationToken cancellationToken) =>
		transaction.Session.QuerySingleOrDefaultAsync(ReconciliationSelect + " WHERE StatementLineId=$Line AND ReversedAtUtc IS NULL ORDER BY Id DESC;", ReadReconciliation, cancellationToken, Parameter("$Line", lineId));

	internal Task<FinanceBankReconciliation?> GetReconciliationAsync(DatabaseTransactionContext transaction, long id, CancellationToken cancellationToken) =>
		transaction.Session.QuerySingleOrDefaultAsync(ReconciliationSelect + " WHERE Id=$Id;", ReadReconciliation, cancellationToken, Parameter("$Id", id));

	internal Task<long> CreateReconciliationAsync(DatabaseTransactionContext transaction, FinanceBankReconciliation value, CancellationToken cancellationToken) =>
		transaction.Session.InsertAsync("INSERT INTO FinanceBankReconciliations (OperationId,StatementLineId,TargetKind,TargetId,TargetJournalEntryId,MatchedAmount,CreatedAtUtc,CreatedByUserId) VALUES ($Operation,$Line,$Kind,$Target,$Journal,$Amount,$At,$User);", cancellationToken,
			Parameter("$Operation", value.OperationId.ToString("D")), Parameter("$Line", value.StatementLineId), Parameter("$Kind", (int)value.TargetKind), Parameter("$Target", value.TargetId), Parameter("$Journal", value.TargetJournalEntryId), Parameter("$Amount", value.MatchedAmount), Parameter("$At", value.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture)), Parameter("$User", value.CreatedByUserId));

	internal Task<int> ReverseReconciliationAsync(DatabaseTransactionContext transaction, long id, Guid operationId, DateTime atUtc, long userId, CancellationToken cancellationToken) =>
		transaction.Session.ExecuteAsync("UPDATE FinanceBankReconciliations SET ReversalOperationId=$Operation,ReversedAtUtc=$At,ReversedByUserId=$User WHERE Id=$Id AND ReversedAtUtc IS NULL;", cancellationToken, Parameter("$Operation", operationId.ToString("D")), Parameter("$At", atUtc.ToString("O", CultureInfo.InvariantCulture)), Parameter("$User", userId), Parameter("$Id", id));

	internal Task<FinancePaymentEvidence?> GetReceivablePaymentEvidenceAsync(DatabaseTransactionContext transaction, long id, CancellationToken cancellationToken) =>
		transaction.Session.QuerySingleOrDefaultAsync("SELECT p.Id,p.Amount,p.CurrencyCode,p.JournalEntryId,p.IsReversed,o.AccountingBookId FROM FinanceReceivablePayments p INNER JOIN FinanceReceivableOpenItems o ON o.Id=p.OpenItemId WHERE p.Id=$Id;", reader => new FinancePaymentEvidence(reader.GetInt64(0), ReadDecimal(reader, 1), new CurrencyCode(reader.GetString(2)), reader.GetInt64(3), ReadBool(reader, 4), Guid.Parse(reader.GetString(5))), cancellationToken, Parameter("$Id", id));

	internal Task<FinancePaymentEvidence?> GetPayablePaymentEvidenceAsync(DatabaseTransactionContext transaction, long id, CancellationToken cancellationToken) =>
		transaction.Session.QuerySingleOrDefaultAsync("SELECT p.Id,p.Amount,p.CurrencyCode,p.JournalEntryId,p.IsReversed,o.AccountingBookId FROM FinancePayablePayments p INNER JOIN FinancePayableOpenItems o ON o.Id=p.OpenItemId WHERE p.Id=$Id;", reader => new FinancePaymentEvidence(reader.GetInt64(0), ReadDecimal(reader, 1), new CurrencyCode(reader.GetString(2)), reader.GetInt64(3), ReadBool(reader, 4), Guid.Parse(reader.GetString(5))), cancellationToken, Parameter("$Id", id));

	internal async Task<FinanceGeneralLedgerBankEvidence?> GetGeneralLedgerBankEvidenceAsync(DatabaseTransactionContext transaction, long journalEntryId, Guid bankGlAccountId, CancellationToken cancellationToken)
	{
		var row = await transaction.Session.QuerySingleOrDefaultAsync("SELECT e.Id,e.AccountingBookId,e.TransactionCurrencyCode,COALESCE(SUM(l.TransactionDebit-l.TransactionCredit),0) FROM FinanceJournalEntries e INNER JOIN FinanceJournalEntryLines l ON l.JournalEntryId=e.Id WHERE e.Id=$Entry AND l.AccountId=$Account GROUP BY e.Id,e.AccountingBookId,e.TransactionCurrencyCode;", reader => new FinanceGeneralLedgerBankEvidence(reader.GetInt64(0), Guid.Parse(reader.GetString(1)), new CurrencyCode(reader.GetString(2)), ReadDecimal(reader, 3)), cancellationToken, Parameter("$Entry", journalEntryId), Parameter("$Account", bankGlAccountId.ToString("D")));
		return row;
	}

	internal Task<FinancePaymentRun?> FindPaymentRunByOperationAsync(DatabaseTransactionContext transaction, Guid operationId, CancellationToken cancellationToken) =>
		transaction.Session.QuerySingleOrDefaultAsync(RunSelect + " WHERE OperationId=$Operation;", ReadRun, cancellationToken, Parameter("$Operation", operationId.ToString("D")));

	internal Task<FinancePaymentRun?> LockPaymentRunAsync(DatabaseTransactionContext transaction, long id, CancellationToken cancellationToken) =>
		LockAndReadRunAsync(transaction, id, cancellationToken);

	private async Task<FinancePaymentRun?> LockAndReadRunAsync(DatabaseTransactionContext transaction, long id, CancellationToken cancellationToken)
	{
		await transaction.Session.ExecuteAsync("UPDATE FinancePaymentRuns SET Version=Version WHERE Id=$Id;", cancellationToken, Parameter("$Id", id));
		var run = await transaction.Session.QuerySingleOrDefaultAsync(RunSelect + " WHERE Id=$Id;", ReadRun, cancellationToken, Parameter("$Id", id));
		if (run is null) return null;
		var lines = await transaction.Session.QueryAsync(RunLineSelect + " WHERE PaymentRunId=$Run ORDER BY Id;", ReadRunLine, cancellationToken, Parameter("$Run", id));
		return run with { Lines = lines };
	}

	internal Task<long> CreatePaymentRunAsync(DatabaseTransactionContext transaction, FinancePaymentRun value, CancellationToken cancellationToken) =>
		transaction.Session.InsertAsync("INSERT INTO FinancePaymentRuns (Version,OperationId,BankAccountId,PaymentDate,CurrencyCode,Description,Status,CreatedAtUtc,CreatedByUserId) VALUES (1,$Operation,$Account,$Date,$Currency,$Description,$Status,$At,$User);", cancellationToken,
			Parameter("$Operation", value.OperationId.ToString("D")), Parameter("$Account", value.BankAccountId), Parameter("$Date", value.PaymentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), Parameter("$Currency", value.Currency.Value), Parameter("$Description", value.Description), Parameter("$Status", (int)value.Status), Parameter("$At", value.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture)), Parameter("$User", value.CreatedByUserId));

	internal Task<long> CreatePaymentRunLineAsync(DatabaseTransactionContext transaction, FinancePaymentRunLine value, CancellationToken cancellationToken) =>
		transaction.Session.InsertAsync("INSERT INTO FinancePaymentRunLines (PaymentRunId,PayableOpenItemId,SupplierId,Amount,Reference,Status,ExecutionOperationId) VALUES ($Run,$OpenItem,$Supplier,$Amount,$Reference,$Status,$Operation);", cancellationToken,
			Parameter("$Run", value.PaymentRunId), Parameter("$OpenItem", value.PayableOpenItemId), Parameter("$Supplier", value.SupplierId), Parameter("$Amount", value.Amount), Parameter("$Reference", value.Reference), Parameter("$Status", (int)value.Status), Parameter("$Operation", value.ExecutionOperationId.ToString("D")));

	internal Task<int> ApprovePaymentRunAsync(DatabaseTransactionContext transaction, long id, long version, DateTime atUtc, long userId, string? comment, CancellationToken cancellationToken) =>
		transaction.Session.ExecuteAsync("UPDATE FinancePaymentRuns SET Version=Version+1,Status=$Status,ApprovedAtUtc=$At,ApprovedByUserId=$User,ApprovalComment=$Comment WHERE Id=$Id AND Version=$Version AND Status=$Draft;", cancellationToken, Parameter("$Status", (int)FinancePaymentRunStatus.Approved), Parameter("$At", atUtc.ToString("O", CultureInfo.InvariantCulture)), Parameter("$User", userId), Parameter("$Comment", comment), Parameter("$Id", id), Parameter("$Version", version), Parameter("$Draft", (int)FinancePaymentRunStatus.Draft));

	internal Task<int> MarkPaymentRunLineExecutedAsync(DatabaseTransactionContext transaction, long lineId, long paymentId, DateTime atUtc, long userId, string? executionReference, CancellationToken cancellationToken) =>
		transaction.Session.ExecuteAsync("UPDATE FinancePaymentRunLines SET Status=$Status,PayablePaymentId=$Payment,ExecutedAtUtc=$At,ExecutedByUserId=$User,ExecutionReference=$Reference WHERE Id=$Id AND Status=$Proposed;", cancellationToken, Parameter("$Status", (int)FinancePaymentRunLineStatus.Executed), Parameter("$Payment", paymentId), Parameter("$At", atUtc.ToString("O", CultureInfo.InvariantCulture)), Parameter("$User", userId), Parameter("$Reference", executionReference), Parameter("$Id", lineId), Parameter("$Proposed", (int)FinancePaymentRunLineStatus.Proposed));

	internal Task<int> UpdatePaymentRunExecutionStatusAsync(DatabaseTransactionContext transaction, long runId, long expectedVersion, FinancePaymentRunStatus status, DateTime? completedAtUtc, CancellationToken cancellationToken) =>
		transaction.Session.ExecuteAsync("UPDATE FinancePaymentRuns SET Version=Version+1,Status=$Status,CompletedAtUtc=$Completed WHERE Id=$Id AND Version=$Version;", cancellationToken, Parameter("$Status", (int)status), Parameter("$Completed", completedAtUtc?.ToString("O", CultureInfo.InvariantCulture)), Parameter("$Id", runId), Parameter("$Version", expectedVersion));

	internal Task<FinancePayableProposalItem?> GetPayableProposalItemAsync(DatabaseTransactionContext transaction, long openItemId, CancellationToken cancellationToken) =>
		transaction.Session.QuerySingleOrDefaultAsync("SELECT Id,SupplierId,CurrencyCode,RemainingAmount,AccountingBookId,IsVoided,Kind FROM FinancePayableOpenItems WHERE Id=$Id;", reader => new FinancePayableProposalItem(reader.GetInt64(0), reader.GetInt64(1), new CurrencyCode(reader.GetString(2)), ReadDecimal(reader, 3), Guid.Parse(reader.GetString(4)), ReadBool(reader, 5), (FinancePayableOpenItemKind)Convert.ToInt32(reader.GetValue(6), CultureInfo.InvariantCulture)), cancellationToken, Parameter("$Id", openItemId));

	internal async Task<FinanceBankCashPositionRow> GetCashPositionAsync(DatabaseTransactionContext transaction, FinanceBankAccount account, CancellationToken cancellationToken)
	{
		var statement = await transaction.Session.QuerySingleOrDefaultAsync("SELECT ToDate,ClosingBalance FROM FinanceBankStatements WHERE BankAccountId=$Account ORDER BY ToDate DESC,Id DESC;", reader => new FinanceLatestStatement(DateOnly.FromDateTime(Convert.ToDateTime(reader.GetValue(0), CultureInfo.InvariantCulture)), ReadDecimal(reader, 1)), cancellationToken, Parameter("$Account", account.Id));
		var asOf = statement?.Date ?? DateOnly.FromDateTime(DateTime.UtcNow);
		var glValue = await transaction.Session.ExecuteScalarAsync("SELECT COALESCE(SUM(l.TransactionDebit-l.TransactionCredit),0) FROM FinanceJournalEntryLines l INNER JOIN FinanceJournalEntries e ON e.Id=l.JournalEntryId WHERE e.AccountingBookId=$Book AND l.AccountId=$Gl AND e.PostingDate<=$Date AND e.TransactionCurrencyCode=$Currency;", cancellationToken, Parameter("$Book", account.AccountingBookId.ToString("D")), Parameter("$Gl", account.GeneralLedgerAccountId.ToString("D")), Parameter("$Date", asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), Parameter("$Currency", account.Currency.Value));
		var unreconciled = await transaction.Session.ExecuteScalarAsync("SELECT COUNT(*) FROM FinanceBankStatementLines l INNER JOIN FinanceBankStatements s ON s.Id=l.StatementId WHERE s.BankAccountId=$Account AND NOT EXISTS (SELECT 1 FROM FinanceBankReconciliations r WHERE r.StatementLineId=l.Id AND r.ReversedAtUtc IS NULL);", cancellationToken, Parameter("$Account", account.Id));
		return new FinanceBankCashPositionRow(statement?.Date, statement?.Balance ?? 0m, Convert.ToDecimal(glValue ?? 0m, CultureInfo.InvariantCulture), Convert.ToInt32(unreconciled ?? 0, CultureInfo.InvariantCulture));
	}

	private static DatabaseParameter[] AccountParameters(FinanceBankAccount value) =>
	[
		Parameter("$Entity", value.LegalEntityId.ToString("D")), Parameter("$Book", value.AccountingBookId.ToString("D")), Parameter("$Gl", value.GeneralLedgerAccountId.ToString("D")), Parameter("$Currency", value.Currency.Value), Parameter("$Name", value.Name), Parameter("$Bank", value.BankName), Parameter("$Iban", value.Iban), Parameter("$Bic", value.Bic), Parameter("$Local", value.LocalAccountNumber), Parameter("$Active", value.IsActive)
	];

	private const string LineSelect = "SELECT l.Id,l.StatementId,l.LineNumber,l.BookingDate,l.ValueDate,l.Amount,l.CurrencyCode,l.ExternalId,l.Reference,l.CounterpartyName,l.BankTransactionCode,CASE WHEN EXISTS (SELECT 1 FROM FinanceBankReconciliations r WHERE r.StatementLineId=l.Id AND r.ReversedAtUtc IS NULL) THEN 1 ELSE 0 END,(SELECT MAX(r.Id) FROM FinanceBankReconciliations r WHERE r.StatementLineId=l.Id AND r.ReversedAtUtc IS NULL) FROM FinanceBankStatementLines l";
	private const string ReconciliationSelect = "SELECT Id,OperationId,StatementLineId,TargetKind,TargetId,TargetJournalEntryId,MatchedAmount,CreatedAtUtc,CreatedByUserId,ReversalOperationId,ReversedAtUtc,ReversedByUserId FROM FinanceBankReconciliations";
	private const string RunSelect = "SELECT Id,Version,OperationId,BankAccountId,PaymentDate,CurrencyCode,Description,Status,CreatedAtUtc,CreatedByUserId,ApprovedAtUtc,ApprovedByUserId,ApprovalComment,CompletedAtUtc FROM FinancePaymentRuns";
	private const string RunLineSelect = "SELECT Id,PaymentRunId,PayableOpenItemId,SupplierId,Amount,Reference,Status,ExecutionOperationId,PayablePaymentId,ExecutedAtUtc,ExecutedByUserId,ExecutionReference FROM FinancePaymentRunLines";

	private static FinanceBankAccount ReadBankAccount(DbDataReader reader) => new() { Id=reader.GetInt64(0),Version=reader.GetInt64(1),LegalEntityId=Guid.Parse(reader.GetString(2)),AccountingBookId=Guid.Parse(reader.GetString(3)),GeneralLedgerAccountId=Guid.Parse(reader.GetString(4)),Currency=new CurrencyCode(reader.GetString(5)),Name=reader.GetString(6),BankName=reader.IsDBNull(7)?null:reader.GetString(7),Iban=reader.IsDBNull(8)?null:reader.GetString(8),Bic=reader.IsDBNull(9)?null:reader.GetString(9),LocalAccountNumber=reader.IsDBNull(10)?null:reader.GetString(10),IsActive=ReadBool(reader,11) };
	private static FinanceBankStatement ReadStatement(DbDataReader reader) => new() { Id=reader.GetInt64(0),OperationId=Guid.Parse(reader.GetString(1)),BankAccountId=reader.GetInt64(2),Format=(FinanceBankStatementFormat)Convert.ToInt32(reader.GetValue(3),CultureInfo.InvariantCulture),StatementReference=reader.GetString(4),ImportHash=reader.GetString(5),SourceFileName=reader.IsDBNull(6)?null:reader.GetString(6),Currency=new CurrencyCode(reader.GetString(7)),FromDate=ReadDate(reader,8),ToDate=ReadDate(reader,9),OpeningBalance=ReadDecimal(reader,10),ClosingBalance=ReadDecimal(reader,11),ImportedAtUtc=Convert.ToDateTime(reader.GetValue(12),CultureInfo.InvariantCulture),ImportedByUserId=reader.GetInt64(13) };
	private static FinanceBankStatementLine ReadLine(DbDataReader reader) => ReadLineBase(reader) with { IsReconciled=ReadBool(reader,11),ReconciliationId=reader.IsDBNull(12)?null:reader.GetInt64(12) };
	private static FinanceBankStatementLine ReadLineBase(DbDataReader reader) => new() { Id=reader.GetInt64(0),StatementId=reader.GetInt64(1),LineNumber=Convert.ToInt32(reader.GetValue(2),CultureInfo.InvariantCulture),BookingDate=ReadDate(reader,3),ValueDate=reader.IsDBNull(4)?null:ReadDate(reader,4),Amount=ReadDecimal(reader,5),Currency=new CurrencyCode(reader.GetString(6)),ExternalId=reader.IsDBNull(7)?null:reader.GetString(7),Reference=reader.IsDBNull(8)?null:reader.GetString(8),CounterpartyName=reader.IsDBNull(9)?null:reader.GetString(9),BankTransactionCode=reader.IsDBNull(10)?null:reader.GetString(10) };
	private static FinanceBankReconciliation ReadReconciliation(DbDataReader reader) => new() { Id=reader.GetInt64(0),OperationId=Guid.Parse(reader.GetString(1)),StatementLineId=reader.GetInt64(2),TargetKind=(FinanceBankReconciliationTargetKind)Convert.ToInt32(reader.GetValue(3),CultureInfo.InvariantCulture),TargetId=reader.GetInt64(4),TargetJournalEntryId=reader.GetInt64(5),MatchedAmount=ReadDecimal(reader,6),CreatedAtUtc=Convert.ToDateTime(reader.GetValue(7),CultureInfo.InvariantCulture),CreatedByUserId=reader.GetInt64(8),ReversalOperationId=reader.IsDBNull(9)?null:Guid.Parse(reader.GetString(9)),ReversedAtUtc=reader.IsDBNull(10)?null:Convert.ToDateTime(reader.GetValue(10),CultureInfo.InvariantCulture),ReversedByUserId=reader.IsDBNull(11)?null:reader.GetInt64(11) };
	private static FinancePaymentRun ReadRun(DbDataReader reader) => new() { Id=reader.GetInt64(0),Version=reader.GetInt64(1),OperationId=Guid.Parse(reader.GetString(2)),BankAccountId=reader.GetInt64(3),PaymentDate=ReadDate(reader,4),Currency=new CurrencyCode(reader.GetString(5)),Description=reader.GetString(6),Status=(FinancePaymentRunStatus)Convert.ToInt32(reader.GetValue(7),CultureInfo.InvariantCulture),CreatedAtUtc=Convert.ToDateTime(reader.GetValue(8),CultureInfo.InvariantCulture),CreatedByUserId=reader.GetInt64(9),ApprovedAtUtc=reader.IsDBNull(10)?null:Convert.ToDateTime(reader.GetValue(10),CultureInfo.InvariantCulture),ApprovedByUserId=reader.IsDBNull(11)?null:reader.GetInt64(11),ApprovalComment=reader.IsDBNull(12)?null:reader.GetString(12),CompletedAtUtc=reader.IsDBNull(13)?null:Convert.ToDateTime(reader.GetValue(13),CultureInfo.InvariantCulture) };
	private static FinancePaymentRunLine ReadRunLine(DbDataReader reader) => new() { Id=reader.GetInt64(0),PaymentRunId=reader.GetInt64(1),PayableOpenItemId=reader.GetInt64(2),SupplierId=reader.GetInt64(3),Amount=ReadDecimal(reader,4),Reference=reader.IsDBNull(5)?null:reader.GetString(5),Status=(FinancePaymentRunLineStatus)Convert.ToInt32(reader.GetValue(6),CultureInfo.InvariantCulture),ExecutionOperationId=Guid.Parse(reader.GetString(7)),PayablePaymentId=reader.IsDBNull(8)?null:reader.GetInt64(8),ExecutedAtUtc=reader.IsDBNull(9)?null:Convert.ToDateTime(reader.GetValue(9),CultureInfo.InvariantCulture),ExecutedByUserId=reader.IsDBNull(10)?null:reader.GetInt64(10),ExecutionReference=reader.IsDBNull(11)?null:reader.GetString(11) };
	private static DateOnly ReadDate(DbDataReader reader,int ordinal)=>DateOnly.FromDateTime(Convert.ToDateTime(reader.GetValue(ordinal),CultureInfo.InvariantCulture));
	private static decimal ReadDecimal(DbDataReader reader,int ordinal)=>Convert.ToDecimal(reader.GetValue(ordinal),CultureInfo.InvariantCulture);
	private static bool ReadBool(DbDataReader reader,int ordinal)=>Convert.ToBoolean(reader.GetValue(ordinal),CultureInfo.InvariantCulture);
}

internal sealed record FinanceBankStatementLineContext(FinanceBankStatementLine Line,long BankAccountId,Guid AccountingBookId,Guid GeneralLedgerAccountId,CurrencyCode BankCurrency);
internal sealed record FinancePaymentEvidence(long Id,decimal Amount,CurrencyCode Currency,long JournalEntryId,bool IsReversed,Guid AccountingBookId);
internal sealed record FinanceGeneralLedgerBankEvidence(long JournalEntryId,Guid AccountingBookId,CurrencyCode Currency,decimal BankSignedAmount);
internal sealed record FinancePayableProposalItem(long OpenItemId,long SupplierId,CurrencyCode Currency,decimal RemainingAmount,Guid AccountingBookId,bool IsVoided,FinancePayableOpenItemKind Kind);
internal sealed record FinanceLatestStatement(DateOnly Date,decimal Balance);
internal sealed record FinanceBankCashPositionRow(DateOnly? StatementDate,decimal StatementBalance,decimal GeneralLedgerBalance,int UnreconciledLineCount);
