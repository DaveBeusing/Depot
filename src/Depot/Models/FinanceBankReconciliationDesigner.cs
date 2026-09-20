// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed record FinanceBankReconciliationCandidate
{
	public required FinanceBankReconciliationTargetKind TargetKind { get; init; }
	public long TargetId { get; init; }
	public decimal Amount { get; init; }
	public required CurrencyCode Currency { get; init; }
	public required DateOnly Date { get; init; }
	public string? Reference { get; init; }
	public required string Counterparty { get; init; }
	public required string Evidence { get; init; }

	public string TargetDisplay => $"{TargetKind} #{TargetId}";

	public FinanceBankReconciliationRequest CreateRequest(Guid operationId, long statementLineId)
	{
		if (operationId == Guid.Empty) throw new ArgumentException("Operation ID is required.", nameof(operationId));
		if (statementLineId <= 0) throw new ArgumentOutOfRangeException(nameof(statementLineId));
		return new FinanceBankReconciliationRequest
		{
			OperationId = operationId,
			StatementLineId = statementLineId,
			TargetKind = TargetKind,
			TargetId = TargetId
		};
	}
}

public sealed record FinanceBankReconciliationMatchPreview(
	FinanceBankStatementLine StatementLine,
	FinanceBankReconciliationCandidate Candidate)
{
	public bool IsExactAmount => StatementLine.Amount == Candidate.Amount;
	public bool IsExactCurrency => StatementLine.Currency == Candidate.Currency;
	public string AmountEvidence => $"{StatementLine.Amount:N2} {StatementLine.Currency.Value} ↔ {Candidate.Amount:N2} {Candidate.Currency.Value}";
	public string ReferenceEvidence => $"{StatementLine.Reference ?? "No bank reference"} ↔ {Candidate.Reference ?? "No target reference"}";
}
