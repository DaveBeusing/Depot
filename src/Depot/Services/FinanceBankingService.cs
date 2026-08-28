// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Depot.Data;
using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class FinanceBankingService
{
	private readonly IDatabaseTransactionRunner _transactions;
	private readonly FinanceBankingRepository _banking;
	private readonly FinanceAccountsPayableService _accountsPayable;
	private readonly AuditRepository _auditEntries;
	private readonly AuditService _audit;
	private readonly IAuthorizationService _authorization;

	public FinanceBankingService(IDatabaseTransactionRunner transactions, FinanceBankingRepository banking, FinanceAccountsPayableService accountsPayable, AuditRepository auditEntries, AuditService audit, IAuthorizationService authorization)
	{
		_transactions = transactions;
		_banking = banking;
		_accountsPayable = accountsPayable;
		_auditEntries = auditEntries;
		_audit = audit;
		_authorization = authorization;
	}

	public bool CanView => _authorization.HasPermission(ApplicationPermission.FinanceBankingView);
	public bool CanManage => _authorization.HasPermission(ApplicationPermission.FinanceBankingManage);
	public bool CanImportStatements => _authorization.HasPermission(ApplicationPermission.FinanceBankStatementsCreate);
	public bool CanReconcile => _authorization.HasPermission(ApplicationPermission.FinanceBankReconciliationManage);
	public bool CanCreatePaymentRuns => _authorization.HasPermission(ApplicationPermission.FinancePaymentProposalsCreate);
	public bool CanApprovePaymentRuns => _authorization.HasPermission(ApplicationPermission.FinancePaymentProposalsApprove);
	public bool CanExecutePaymentRuns => _authorization.HasPermission(ApplicationPermission.FinancePaymentRunsPost);

	public Task<IReadOnlyList<FinanceBankAccount>> GetBankAccountsAsync(CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceBankingView);
		return _banking.GetBankAccountsAsync(cancellationToken);
	}

	public Task<IReadOnlyList<FinanceBankStatement>> GetStatementsAsync(long? bankAccountId = null, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceBankingView);
		return _banking.GetStatementsAsync(bankAccountId, cancellationToken: cancellationToken);
	}

	public Task<FinanceBankStatement?> GetStatementAsync(long id, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceBankingView);
		return _banking.GetStatementAsync(id, cancellationToken);
	}

	public Task<IReadOnlyList<FinanceBankStatementLine>> GetUnreconciledLinesAsync(long? bankAccountId = null, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceBankingView);
		return _banking.GetUnreconciledLinesAsync(bankAccountId, cancellationToken);
	}

	public Task<IReadOnlyList<FinancePaymentRun>> GetPaymentRunsAsync(CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceBankingView);
		return _banking.GetPaymentRunsAsync(cancellationToken: cancellationToken);
	}

	public Task<FinancePaymentRun?> GetPaymentRunAsync(long id, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceBankingView);
		return _banking.GetPaymentRunAsync(id, cancellationToken);
	}

	public async Task<FinanceBankAccount> SaveBankAccountAsync(FinanceBankAccount account, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(account);
		_authorization.RequirePermission(ApplicationPermission.FinanceBankingManage);
		RequireUser();
		var normalized = NormalizeAccount(account);
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			if (!await _banking.ValidateBankAccountConfigurationAsync(transaction, normalized, token)) throw new InvalidOperationException("Bank account legal entity, accounting book and General Ledger account are incompatible or inactive.");
			if (normalized.Id == 0)
			{
				var id = await _banking.CreateBankAccountAsync(transaction, normalized, token);
				var created = normalized with { Id = id, Version = 1 };
				await _auditEntries.CreateAsync(transaction, _audit.CreateCreatedEntry(id, created), token);
				return created;
			}
			var before = await _banking.GetBankAccountAsync(transaction, normalized.Id, token) ?? throw new InvalidOperationException("Bank account was not found.");
			if (before.Version != normalized.Version) throw new ConcurrencyConflictException("bank account");
			if (await _banking.UpdateBankAccountAsync(transaction, normalized, before.Version, token) != 1) throw new ConcurrencyConflictException("bank account");
			var after = normalized with { Version = before.Version + 1 };
			await _auditEntries.CreateAsync(transaction, _audit.CreateUpdatedEntry(after.Id, before, after), token);
			return after;
		}, cancellationToken);
	}

	public async Task<FinanceBankStatement> ImportStatementAsync(FinanceBankStatementImportRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		_authorization.RequirePermission(ApplicationPermission.FinanceBankStatementsCreate);
		var user = RequireUser();
		if (request.OperationId == Guid.Empty || request.BankAccountId <= 0) throw new ArgumentException("Operation ID and bank account are required.", nameof(request));
		var importHash = HashImport(request);
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var previous = await _banking.FindStatementByOperationAsync(transaction, request.OperationId, token);
			if (previous is not null)
			{
				if (!string.Equals(previous.ImportHash, importHash, StringComparison.Ordinal)) throw new InvalidOperationException("The statement operation ID is already assigned to different import content.");
				return (await _banking.GetStatementAsync(previous.Id, token)) ?? previous;
			}
			var duplicate = await _banking.FindStatementByHashAsync(transaction, importHash, token);
			if (duplicate is not null) return (await _banking.GetStatementAsync(duplicate.Id, token)) ?? duplicate;
			var bank = await _banking.GetBankAccountAsync(transaction, request.BankAccountId, token) ?? throw new InvalidOperationException("Bank account was not found.");
			if (!bank.IsActive) throw new InvalidOperationException("Bank account is inactive.");
			var parsed = FinanceBankStatementParser.Parse(request, bank.Currency);
			if (parsed.ClosingBalance != parsed.OpeningBalance + parsed.Lines.Sum(line => line.Amount)) throw new InvalidDataException("Statement opening balance plus transaction amounts must equal closing balance. Depot does not apply an implicit reconciliation tolerance.");
			var now = DateTime.UtcNow;
			var value = new FinanceBankStatement { OperationId=request.OperationId,BankAccountId=bank.Id,Format=request.Format,StatementReference=parsed.StatementReference,ImportHash=importHash,SourceFileName=Clean(request.SourceFileName,260),Currency=parsed.Currency,FromDate=parsed.FromDate,ToDate=parsed.ToDate,OpeningBalance=parsed.OpeningBalance,ClosingBalance=parsed.ClosingBalance,ImportedAtUtc=now,ImportedByUserId=user.Id };
			var id = await _banking.CreateStatementAsync(transaction, value, token);
			var lines = new List<FinanceBankStatementLine>(parsed.Lines.Count);
			for (var index = 0; index < parsed.Lines.Count; index++)
			{
				var parsedLine = parsed.Lines[index];
				var line = new FinanceBankStatementLine { StatementId=id,LineNumber=index+1,BookingDate=parsedLine.BookingDate,ValueDate=parsedLine.ValueDate,Amount=parsedLine.Amount,Currency=parsedLine.Currency,ExternalId=parsedLine.ExternalId,Reference=parsedLine.Reference,CounterpartyName=parsedLine.CounterpartyName,BankTransactionCode=parsedLine.BankTransactionCode };
				var lineId = await _banking.CreateStatementLineAsync(transaction, line, token);
				lines.Add(line with { Id=lineId });
			}
			var created = value with { Id=id, Lines=lines };
			await _auditEntries.CreateAsync(transaction, _audit.CreateCreatedEntry(id, created), token);
			return created;
		}, cancellationToken);
	}

	public async Task<FinanceBankReconciliation> ReconcileAsync(FinanceBankReconciliationRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		_authorization.RequirePermission(ApplicationPermission.FinanceBankReconciliationManage);
		var user = RequireUser();
		if (request.OperationId == Guid.Empty || request.StatementLineId <= 0 || request.TargetId <= 0) throw new ArgumentException("Operation ID, statement line and target are required.", nameof(request));
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var previous = await _banking.FindReconciliationByOperationAsync(transaction, request.OperationId, token);
			if (previous is not null)
			{
				if (previous.StatementLineId != request.StatementLineId || previous.TargetKind != request.TargetKind || previous.TargetId != request.TargetId) throw new InvalidOperationException("The reconciliation operation ID is already assigned to different content.");
				return previous;
			}
			if (await _banking.GetActiveReconciliationForLineAsync(transaction, request.StatementLineId, token) is not null) throw new InvalidOperationException("Bank statement line is already reconciled.");
			var context = await _banking.GetStatementLineContextAsync(transaction, request.StatementLineId, token) ?? throw new InvalidOperationException("Bank statement line was not found.");
			if (context.Line.Currency != context.BankCurrency) throw new InvalidOperationException("Statement line currency differs from bank-account currency.");
			long journalEntryId;
			switch (request.TargetKind)
			{
				case FinanceBankReconciliationTargetKind.ReceivablePayment:
				{
					var payment = await _banking.GetReceivablePaymentEvidenceAsync(transaction, request.TargetId, token) ?? throw new InvalidOperationException("Receivable payment was not found.");
					if (payment.IsReversed) throw new InvalidOperationException("A reversed receivable payment cannot be reconciled.");
					ValidatePaymentTarget(context, payment, payment.Amount);
					journalEntryId = payment.JournalEntryId;
					break;
				}
				case FinanceBankReconciliationTargetKind.PayablePayment:
				{
					var payment = await _banking.GetPayablePaymentEvidenceAsync(transaction, request.TargetId, token) ?? throw new InvalidOperationException("Payable payment was not found.");
					if (payment.IsReversed) throw new InvalidOperationException("A reversed payable payment cannot be reconciled.");
					ValidatePaymentTarget(context, payment, -payment.Amount);
					journalEntryId = payment.JournalEntryId;
					break;
				}
				case FinanceBankReconciliationTargetKind.GeneralLedgerEntry:
				{
					var journal = await _banking.GetGeneralLedgerBankEvidenceAsync(transaction, request.TargetId, context.GeneralLedgerAccountId, token) ?? throw new InvalidOperationException("General Ledger entry does not contain the configured bank account.");
					if (journal.AccountingBookId != context.AccountingBookId || journal.Currency != context.BankCurrency || journal.BankSignedAmount != context.Line.Amount) throw new InvalidOperationException("General Ledger bank amount, accounting book or currency does not match the statement line exactly.");
					journalEntryId = journal.JournalEntryId;
					break;
				}
				default: throw new ArgumentOutOfRangeException(nameof(request), "Unsupported reconciliation target kind.");
			}
			var value = new FinanceBankReconciliation { OperationId=request.OperationId,StatementLineId=request.StatementLineId,TargetKind=request.TargetKind,TargetId=request.TargetId,TargetJournalEntryId=journalEntryId,MatchedAmount=context.Line.Amount,CreatedAtUtc=DateTime.UtcNow,CreatedByUserId=user.Id };
			var id = await _banking.CreateReconciliationAsync(transaction, value, token);
			var created = value with { Id=id };
			await _auditEntries.CreateAsync(transaction, _audit.CreateCreatedEntry(id, created), token);
			return created;
		}, cancellationToken);
	}

	public async Task<FinanceBankReconciliation> ReverseReconciliationAsync(long reconciliationId, Guid operationId, string reason, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceBankReconciliationManage);
		var user = RequireUser();
		if (reconciliationId <= 0 || operationId == Guid.Empty || string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Reconciliation, operation ID and reason are required.");
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var value = await _banking.GetReconciliationAsync(transaction, reconciliationId, token) ?? throw new InvalidOperationException("Reconciliation was not found.");
			if (value.IsReversed)
			{
				if (value.ReversalOperationId == operationId) return value;
				throw new InvalidOperationException("Reconciliation has already been reversed.");
			}
			var now = DateTime.UtcNow;
			if (await _banking.ReverseReconciliationAsync(transaction, reconciliationId, operationId, now, user.Id, token) != 1) throw new ConcurrencyConflictException("bank reconciliation");
			var after = value with { ReversalOperationId=operationId,ReversedAtUtc=now,ReversedByUserId=user.Id };
			await _auditEntries.CreateAsync(transaction, _audit.CreateActionEntry(value.Id, $"Reversed: {reason.Trim()}", value, after), token);
			return after;
		}, cancellationToken);
	}

	public async Task<FinancePaymentRun> CreatePaymentRunAsync(FinancePaymentRunRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		_authorization.RequirePermission(ApplicationPermission.FinancePaymentProposalsCreate);
		var user = RequireUser();
		if (request.OperationId == Guid.Empty || request.BankAccountId <= 0 || request.Lines.Count == 0) throw new ArgumentException("Operation ID, bank account and at least one payable item are required.", nameof(request));
		if (request.Lines.GroupBy(line => line.PayableOpenItemId).Any(group => group.Count() > 1)) throw new ArgumentException("A payable open item may occur only once in a payment proposal.", nameof(request));
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var previous = await _banking.FindPaymentRunByOperationAsync(transaction, request.OperationId, token);
			if (previous is not null) return previous;
			var bank = await _banking.GetBankAccountAsync(transaction, request.BankAccountId, token) ?? throw new InvalidOperationException("Bank account was not found.");
			if (!bank.IsActive || bank.Currency != request.Currency) throw new InvalidOperationException("Payment run bank account is inactive or uses a different currency.");
			var proposed = new List<(FinancePayableProposalItem Item, FinancePaymentRunLineRequest Request)>();
			foreach (var line in request.Lines)
			{
				if (line.PayableOpenItemId <= 0 || line.Amount <= 0m) throw new ArgumentException("Payment proposal lines require an open item and positive amount.", nameof(request));
				var item = await _banking.GetPayableProposalItemAsync(transaction, line.PayableOpenItemId, token) ?? throw new InvalidOperationException($"Payable open item '{line.PayableOpenItemId}' was not found.");
				if (item.IsVoided || item.Kind != FinancePayableOpenItemKind.Invoice || item.RemainingAmount <= 0m) throw new InvalidOperationException("Only active supplier invoice open items can be proposed for payment.");
				if (item.Currency != request.Currency || item.AccountingBookId != bank.AccountingBookId) throw new InvalidOperationException("Payment proposal item currency/accounting book does not match the bank account.");
				if (line.Amount > item.RemainingAmount) throw new InvalidOperationException("Payment proposal amount exceeds the supplier open-item balance.");
				proposed.Add((item,line));
			}
			var now = DateTime.UtcNow;
			var run = new FinancePaymentRun { OperationId=request.OperationId,BankAccountId=bank.Id,PaymentDate=request.PaymentDate,Currency=request.Currency,Description=Required(request.Description,500),Status=FinancePaymentRunStatus.Draft,CreatedAtUtc=now,CreatedByUserId=user.Id };
			var runId = await _banking.CreatePaymentRunAsync(transaction, run, token);
			var lines = new List<FinancePaymentRunLine>();
			foreach (var pair in proposed)
			{
				var executionOperation = DeterministicOperationId(request.OperationId, pair.Item.OpenItemId);
				var line = new FinancePaymentRunLine { PaymentRunId=runId,PayableOpenItemId=pair.Item.OpenItemId,SupplierId=pair.Item.SupplierId,Amount=pair.Request.Amount,Reference=Clean(pair.Request.Reference,200),Status=FinancePaymentRunLineStatus.Proposed,ExecutionOperationId=executionOperation };
				var lineId = await _banking.CreatePaymentRunLineAsync(transaction, line, token);
				lines.Add(line with { Id=lineId });
			}
			var created = run with { Id=runId,Version=1,Lines=lines };
			await _auditEntries.CreateAsync(transaction, _audit.CreateCreatedEntry(runId, created), token);
			return created;
		}, cancellationToken);
	}

	public async Task<FinancePaymentRun> ApprovePaymentRunAsync(long runId, long expectedVersion, string? comment, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinancePaymentProposalsApprove);
		var user = RequireUser();
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var before = await _banking.LockPaymentRunAsync(transaction, runId, token) ?? throw new InvalidOperationException("Payment run was not found.");
			if (before.Version != expectedVersion) throw new ConcurrencyConflictException("payment run");
			if (before.Status != FinancePaymentRunStatus.Draft) throw new InvalidOperationException("Only a draft payment run can be approved.");
			if (before.CreatedByUserId == user.Id) throw new InvalidOperationException("Payment-run creator cannot approve the same run.");
			var now = DateTime.UtcNow;
			if (await _banking.ApprovePaymentRunAsync(transaction, runId, expectedVersion, now, user.Id, Clean(comment,500), token) != 1) throw new ConcurrencyConflictException("payment run");
			var after = before with { Version=before.Version+1,Status=FinancePaymentRunStatus.Approved,ApprovedAtUtc=now,ApprovedByUserId=user.Id,ApprovalComment=Clean(comment,500) };
			await _auditEntries.CreateAsync(transaction, _audit.CreateActionEntry(runId, "Approved", before, after), token);
			return after;
		}, cancellationToken);
	}

	public async Task<FinancePaymentRun> ExecutePaymentRunLineAsync(long runId, long lineId, string? executionReference, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinancePaymentRunsPost);
		var user = RequireUser();
		var snapshot = await _banking.GetPaymentRunAsync(runId, cancellationToken) ?? throw new InvalidOperationException("Payment run was not found.");
		if (snapshot.Status is not (FinancePaymentRunStatus.Approved or FinancePaymentRunStatus.PartiallyExecuted)) throw new InvalidOperationException("Payment run must be approved before execution.");
		var line = snapshot.Lines.SingleOrDefault(value => value.Id == lineId) ?? throw new InvalidOperationException("Payment run line was not found.");
		if (line.Status == FinancePaymentRunLineStatus.Executed) return snapshot;
		if (line.Status != FinancePaymentRunLineStatus.Proposed) throw new InvalidOperationException("Only a proposed payment line can be executed.");
		var payment = await _accountsPayable.PostPaymentAsync(new FinancePayablePaymentRequest
		{
			OperationId=line.ExecutionOperationId,
			SupplierId=line.SupplierId,
			Currency=snapshot.Currency,
			PaymentDate=snapshot.PaymentDate,
			Amount=line.Amount,
			Reference=Clean(line.Reference ?? executionReference,200),
			Description=$"Payment run {snapshot.Id}: {snapshot.Description}",
			Allocations=[new FinancePayableAllocationRequest(line.PayableOpenItemId,line.Amount)]
		}, cancellationToken);

		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var current = await _banking.LockPaymentRunAsync(transaction, runId, token) ?? throw new InvalidOperationException("Payment run was not found after payment posting.");
			var currentLine = current.Lines.Single(value => value.Id == lineId);
			if (currentLine.Status == FinancePaymentRunLineStatus.Executed)
			{
				if (currentLine.PayablePaymentId == payment.Id) return current;
				throw new InvalidOperationException("Payment run line is already linked to a different payment.");
			}
			if (current.Status is not (FinancePaymentRunStatus.Approved or FinancePaymentRunStatus.PartiallyExecuted)) throw new InvalidOperationException("Payment run is no longer executable.");
			var now = DateTime.UtcNow;
			if (await _banking.MarkPaymentRunLineExecutedAsync(transaction, lineId, payment.Id, now, user.Id, Clean(executionReference,200), token) != 1) throw new ConcurrencyConflictException("payment run line");
			var remaining = current.Lines.Count(value => value.Id != lineId && value.Status == FinancePaymentRunLineStatus.Proposed);
			var status = remaining == 0 ? FinancePaymentRunStatus.Executed : FinancePaymentRunStatus.PartiallyExecuted;
			if (await _banking.UpdatePaymentRunExecutionStatusAsync(transaction, runId, current.Version, status, status == FinancePaymentRunStatus.Executed ? now : null, token) != 1) throw new ConcurrencyConflictException("payment run");
			var after = await _banking.LockPaymentRunAsync(transaction, runId, token) ?? throw new InvalidOperationException("Executed payment run could not be reloaded.");
			await _auditEntries.CreateAsync(transaction, _audit.CreateActionEntry(runId, "Payment executed", current, after), token);
			return after;
		}, cancellationToken);
	}

	public async Task<IReadOnlyList<FinanceCashPosition>> GetCashPositionAsync(CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceCashPositionView);
		var accounts = await _banking.GetBankAccountsAsync(cancellationToken);
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var result = new List<FinanceCashPosition>();
			foreach (var account in accounts.Where(value => value.IsActive))
			{
				var row = await _banking.GetCashPositionAsync(transaction, account, token);
				result.Add(new FinanceCashPosition { BankAccountId=account.Id,BankAccountName=account.Name,Currency=account.Currency,StatementDate=row.StatementDate,StatementBalance=row.StatementBalance,GeneralLedgerBalance=row.GeneralLedgerBalance,UnreconciledLineCount=row.UnreconciledLineCount });
			}
			return (IReadOnlyList<FinanceCashPosition>)result;
		}, cancellationToken);
	}

	private static void ValidatePaymentTarget(FinanceBankStatementLineContext context, FinancePaymentEvidence payment, decimal expectedSignedAmount)
	{
		if (payment.AccountingBookId != context.AccountingBookId || payment.Currency != context.BankCurrency || expectedSignedAmount != context.Line.Amount) throw new InvalidOperationException("Payment amount, accounting book or currency does not match the statement line exactly.");
	}

	private User RequireUser() => _authorization.CurrentUser ?? throw new UnauthorizedAccessException("An authenticated user is required.");
	private static FinanceBankAccount NormalizeAccount(FinanceBankAccount account) => account with { Name=Required(account.Name,200),BankName=Clean(account.BankName,200),Iban=Clean(account.Iban,64)?.Replace(" ",string.Empty,StringComparison.Ordinal).ToUpperInvariant(),Bic=Clean(account.Bic,32)?.Replace(" ",string.Empty,StringComparison.Ordinal).ToUpperInvariant(),LocalAccountNumber=Clean(account.LocalAccountNumber,100) };
	private static string Required(string? value,int maxLength) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A required text value is missing.") : value.Trim().Length <= maxLength ? value.Trim() : throw new ArgumentException($"Text exceeds {maxLength} characters.");
	private static string? Clean(string? value,int maxLength) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length <= maxLength ? value.Trim() : value.Trim()[..maxLength];
	private static string HashImport(FinanceBankStatementImportRequest request)
	{
		var canonical = string.Join("|",request.BankAccountId.ToString(CultureInfo.InvariantCulture),((int)request.Format).ToString(CultureInfo.InvariantCulture),request.SourceFileName?.Trim() ?? string.Empty,request.StatementReference?.Trim() ?? string.Empty,request.FromDate?.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture) ?? string.Empty,request.ToDate?.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture) ?? string.Empty,request.OpeningBalance?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,request.ClosingBalance?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,request.Content.Replace("\r\n","\n",StringComparison.Ordinal).Trim());
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
	}
	private static Guid DeterministicOperationId(Guid runOperationId,long openItemId)
	{
		var hash=SHA256.HashData(Encoding.UTF8.GetBytes($"Depot|Finance|PaymentRun|{runOperationId:D}|{openItemId}"));
		return new Guid(hash.AsSpan(0,16));
	}
}
