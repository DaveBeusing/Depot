// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public enum FinanceBankStatementFormat
{
	Csv = 1,
	Iso20022Camt053 = 2
}

public enum FinanceBankReconciliationTargetKind
{
	ReceivablePayment = 1,
	PayablePayment = 2,
	GeneralLedgerEntry = 3
}

public enum FinancePaymentRunStatus
{
	Draft = 1,
	Approved = 2,
	PartiallyExecuted = 3,
	Executed = 4,
	Cancelled = 5
}

public enum FinancePaymentRunLineStatus
{
	Proposed = 1,
	Executed = 2,
	Cancelled = 3
}

public sealed record FinanceBankAccount
{
	public long Id { get; init; }
	public long Version { get; init; } = 1;
	public required Guid LegalEntityId { get; init; }
	public required Guid AccountingBookId { get; init; }
	public required Guid GeneralLedgerAccountId { get; init; }
	public required CurrencyCode Currency { get; init; }
	public required string Name { get; init; }
	public string? BankName { get; init; }
	public string? Iban { get; init; }
	public string? Bic { get; init; }
	public string? LocalAccountNumber { get; init; }
	public bool IsActive { get; init; }
}

public sealed record FinanceBankStatement
{
	public long Id { get; init; }
	public required Guid OperationId { get; init; }
	public long BankAccountId { get; init; }
	public required FinanceBankStatementFormat Format { get; init; }
	public required string StatementReference { get; init; }
	public required string ImportHash { get; init; }
	public string? SourceFileName { get; init; }
	public required CurrencyCode Currency { get; init; }
	public required DateOnly FromDate { get; init; }
	public required DateOnly ToDate { get; init; }
	public decimal OpeningBalance { get; init; }
	public decimal ClosingBalance { get; init; }
	public DateTime ImportedAtUtc { get; init; }
	public long ImportedByUserId { get; init; }
	public IReadOnlyList<FinanceBankStatementLine> Lines { get; init; } = [];
}

public sealed record FinanceBankStatementLine
{
	public long Id { get; init; }
	public long StatementId { get; init; }
	public int LineNumber { get; init; }
	public required DateOnly BookingDate { get; init; }
	public DateOnly? ValueDate { get; init; }
	public decimal Amount { get; init; }
	public required CurrencyCode Currency { get; init; }
	public string? ExternalId { get; init; }
	public string? Reference { get; init; }
	public string? CounterpartyName { get; init; }
	public string? BankTransactionCode { get; init; }
	public bool IsReconciled { get; init; }
	public long? ReconciliationId { get; init; }
}

public sealed record FinanceBankStatementImportRequest
{
	public required Guid OperationId { get; init; }
	public long BankAccountId { get; init; }
	public FinanceBankStatementFormat Format { get; init; }
	public required string Content { get; init; }
	public string? SourceFileName { get; init; }
	public string? StatementReference { get; init; }
	public DateOnly? FromDate { get; init; }
	public DateOnly? ToDate { get; init; }
	public decimal? OpeningBalance { get; init; }
	public decimal? ClosingBalance { get; init; }
}

public sealed record FinanceBankReconciliation
{
	public long Id { get; init; }
	public required Guid OperationId { get; init; }
	public long StatementLineId { get; init; }
	public FinanceBankReconciliationTargetKind TargetKind { get; init; }
	public long TargetId { get; init; }
	public long TargetJournalEntryId { get; init; }
	public decimal MatchedAmount { get; init; }
	public DateTime CreatedAtUtc { get; init; }
	public long CreatedByUserId { get; init; }
	public Guid? ReversalOperationId { get; init; }
	public DateTime? ReversedAtUtc { get; init; }
	public long? ReversedByUserId { get; init; }
	public bool IsReversed => ReversedAtUtc is not null;
}

public sealed record FinanceBankReconciliationRequest
{
	public required Guid OperationId { get; init; }
	public long StatementLineId { get; init; }
	public FinanceBankReconciliationTargetKind TargetKind { get; init; }
	public long TargetId { get; init; }
}

public sealed record FinancePaymentRun
{
	public long Id { get; init; }
	public long Version { get; init; } = 1;
	public required Guid OperationId { get; init; }
	public long BankAccountId { get; init; }
	public required DateOnly PaymentDate { get; init; }
	public required CurrencyCode Currency { get; init; }
	public required string Description { get; init; }
	public FinancePaymentRunStatus Status { get; init; }
	public DateTime CreatedAtUtc { get; init; }
	public long CreatedByUserId { get; init; }
	public DateTime? ApprovedAtUtc { get; init; }
	public long? ApprovedByUserId { get; init; }
	public string? ApprovalComment { get; init; }
	public DateTime? CompletedAtUtc { get; init; }
	public IReadOnlyList<FinancePaymentRunLine> Lines { get; init; } = [];
}

public sealed record FinancePaymentRunLine
{
	public long Id { get; init; }
	public long PaymentRunId { get; init; }
	public long PayableOpenItemId { get; init; }
	public long SupplierId { get; init; }
	public decimal Amount { get; init; }
	public string? Reference { get; init; }
	public FinancePaymentRunLineStatus Status { get; init; }
	public required Guid ExecutionOperationId { get; init; }
	public long? PayablePaymentId { get; init; }
	public DateTime? ExecutedAtUtc { get; init; }
	public long? ExecutedByUserId { get; init; }
	public string? ExecutionReference { get; init; }
}

public sealed record FinancePaymentRunRequest
{
	public required Guid OperationId { get; init; }
	public long BankAccountId { get; init; }
	public required DateOnly PaymentDate { get; init; }
	public required CurrencyCode Currency { get; init; }
	public required string Description { get; init; }
	public IReadOnlyList<FinancePaymentRunLineRequest> Lines { get; init; } = [];
}

public sealed record FinancePaymentRunLineRequest(long PayableOpenItemId, decimal Amount, string? Reference);

public sealed record FinanceCashPosition
{
	public long BankAccountId { get; init; }
	public required string BankAccountName { get; init; }
	public required CurrencyCode Currency { get; init; }
	public DateOnly? StatementDate { get; init; }
	public decimal StatementBalance { get; init; }
	public decimal GeneralLedgerBalance { get; init; }
	public decimal Difference => StatementBalance - GeneralLedgerBalance;
	public int UnreconciledLineCount { get; init; }
}

public sealed record FinanceParsedBankStatement(
	string StatementReference,
	CurrencyCode Currency,
	DateOnly FromDate,
	DateOnly ToDate,
	decimal OpeningBalance,
	decimal ClosingBalance,
	IReadOnlyList<FinanceParsedBankStatementLine> Lines);

public sealed record FinanceParsedBankStatementLine(
	DateOnly BookingDate,
	DateOnly? ValueDate,
	decimal Amount,
	CurrencyCode Currency,
	string? ExternalId,
	string? Reference,
	string? CounterpartyName,
	string? BankTransactionCode);
