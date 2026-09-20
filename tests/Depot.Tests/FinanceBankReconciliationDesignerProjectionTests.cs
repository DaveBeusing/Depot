// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

using Xunit;

namespace Depot.Tests;

public sealed class FinanceBankReconciliationDesignerProjectionTests
{
	[Fact]
	public void CandidateCreatesExactExistingReconciliationRequest()
	{
		var operationId = Guid.NewGuid();
		var candidate = new FinanceBankReconciliationCandidate
		{
			TargetKind = FinanceBankReconciliationTargetKind.PayablePayment,
			TargetId = 42,
			Amount = -125m,
			Currency = new CurrencyCode("EUR"),
			Date = new DateOnly(2026, 9, 20),
			Reference = "PAY-42",
			Counterparty = "Supplier",
			Evidence = "Supplier payment"
		};

		var request = candidate.CreateRequest(operationId, 7);

		Assert.Equal(operationId, request.OperationId);
		Assert.Equal(7, request.StatementLineId);
		Assert.Equal(FinanceBankReconciliationTargetKind.PayablePayment, request.TargetKind);
		Assert.Equal(42, request.TargetId);
	}

	[Fact]
	public void MatchPreviewIsReadOnlyEvidenceAndDoesNotChangeEitherSide()
	{
		var line = new FinanceBankStatementLine
		{
			Id = 7,
			StatementId = 3,
			LineNumber = 1,
			BookingDate = new DateOnly(2026, 9, 20),
			Amount = 125m,
			Currency = new CurrencyCode("EUR"),
			Reference = "BANK-REF",
			CounterpartyName = "Customer"
		};
		var candidate = new FinanceBankReconciliationCandidate
		{
			TargetKind = FinanceBankReconciliationTargetKind.ReceivablePayment,
			TargetId = 9,
			Amount = 125m,
			Currency = new CurrencyCode("EUR"),
			Date = new DateOnly(2026, 9, 20),
			Reference = "BANK-REF",
			Counterparty = "Customer",
			Evidence = "Customer payment"
		};

		var preview = new FinanceBankReconciliationMatchPreview(line, candidate);

		Assert.True(preview.IsExactAmount);
		Assert.True(preview.IsExactCurrency);
		Assert.Equal(7, line.Id);
		Assert.False(line.IsReconciled);
		Assert.Equal(9, candidate.TargetId);
	}
}
