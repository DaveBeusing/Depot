// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;
using Depot.Services;
using Xunit;
using static Depot.Tests.FinancePaymentIntegrityFixture;

namespace Depot.Tests;

public sealed class FinancePaymentIntegrityTests
{
	[Fact]
	public async Task MatchingAggregatesCurrentLinesAndKeepsReceiptScopesIndependent()
	{
		using var fixture = CreateSqlite();
		await VerifyMatchingAsync(fixture);
	}

	[Fact]
	public async Task ConcurrentApprovalsCannotReserveTheSameReceivedQuantityTwice()
	{
		using var fixture = CreateSqlite();
		await VerifyConcurrentMatchingAsync(fixture);
	}

	[Fact]
	public async Task PaymentRunPostsTheSelectedBankAndRejectsChangedProfiles()
	{
		using var fixture = CreateSqlite();
		await VerifySelectedBankAsync(fixture);
	}

	[Fact]
	public async Task ReconciliationRejectsWrongBankAndPaymentJournalAliases()
	{
		using var fixture = CreateSqlite();
		await VerifyReconciliationAsync(fixture);
	}

	[Fact]
	public async Task TransferBankSidesRemainIndependentForReceivableAndJournalTargets()
	{
		using var fixture = CreateSqlite();
		await VerifyTransferAsync(fixture);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task NewSepaExportRevalidatesFullAndPartialSettlement(bool partial)
	{
		using var fixture = CreateSqlite();
		var bank = await fixture.CreateBankAsync();
		var invoice = await fixture.CreateInvoiceAsync();
		var run = await fixture.CreateRunAsync(bank.Id, invoice.OpenItemId.GetValueOrDefault());
		await fixture.PayAsync(invoice.OpenItemId.GetValueOrDefault(), partial ? 40m : 100m);
		Assert.False((await fixture.Sepa.PreviewAsync(run.Id)).IsValid);
		await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Sepa.GenerateAsync(run.Id));
		Assert.Equal(0L, fixture.Scalar("SELECT COUNT(*) FROM FinanceSepaPaymentExports WHERE PaymentRunId=$Run;", P("$Run", run.Id)));
	}

	[Fact]
	public async Task ReversedPayableCannotGenerateANewSepaInstruction()
	{
		using var fixture = CreateSqlite();
		var bank = await fixture.CreateBankAsync();
		var invoice = await fixture.CreateInvoiceAsync();
		var run = await fixture.CreateRunAsync(bank.Id, invoice.OpenItemId.GetValueOrDefault());
		await fixture.Payables.ReverseDocumentAsync(invoice.Id, new FinancePayableReversalRequest { OperationId=Guid.NewGuid(),PostingDate=fixture.PostingDate,Reason="Cancelled supplier invoice" });
		await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Sepa.GenerateAsync(run.Id));
	}

	[Fact]
	public async Task SepaReservationsReleaseOnCancelledAndRejectedOutcomesAndKeepHistoricalBytes()
	{
		using var fixture = CreateSqlite();
		await VerifySepaLifecycleAsync(fixture);
	}

	[Fact]
	public async Task ConcurrentSepaRunsCannotOverInstructOneOpenBalance()
	{
		using var fixture = CreateSqlite();
		await VerifyConcurrentSepaAsync(fixture);
	}

	internal static async Task VerifyMatchingAsync(FinancePaymentIntegrityFixture fixture)
	{
		var combined = await fixture.CreateReceiptsAsync(10);
		var excessive = await fixture.Payables.SaveDraftAsync(fixture.Draft(MatchLine(combined.LineId, 8m), MatchLine(combined.LineId, 8m)));
		Assert.Equal(6m, excessive.Lines.Sum(line => line.QuantityVariance));
		await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.ApproveAsync(excessive));

		var split = await fixture.CreateReceiptsAsync(5, 5);
		var first = await fixture.Payables.SaveDraftAsync(fixture.Draft(MatchLine(split.LineId, 5m, split.ReceiptIds[0])));
		await fixture.ApproveAsync(first);
		var second = await fixture.Payables.SaveDraftAsync(fixture.Draft(MatchLine(split.LineId, 5m, split.ReceiptIds[1])));
		Assert.Equal(FinancePayableMatchStatus.Matched, Assert.Single(second.Lines).MatchStatus);
		await fixture.ApproveAsync(second);
		var duplicate = await fixture.Payables.SaveDraftAsync(fixture.Draft(MatchLine(split.LineId, 1m, split.ReceiptIds[1])));
		Assert.Equal(1m, Assert.Single(duplicate.Lines).QuantityVariance);

		var mixed = await fixture.CreateReceiptsAsync(5, 5);
		var mixedDraft = await fixture.Payables.SaveDraftAsync(fixture.Draft(MatchLine(mixed.LineId, 6m), MatchLine(mixed.LineId, 5m, mixed.ReceiptIds[0])));
		Assert.Equal(1m, mixedDraft.Lines.Sum(line => line.QuantityVariance));
		Assert.Equal(0m, mixedDraft.Lines[1].QuantityVariance);
		var historicalUnscoped = await fixture.Payables.SaveDraftAsync(fixture.Draft(MatchLine(mixed.LineId, 5m)));
		await fixture.ApproveAsync(historicalUnscoped);
		var explicitRemainder = await fixture.Payables.SaveDraftAsync(fixture.Draft(MatchLine(mixed.LineId, 5m, mixed.ReceiptIds[0])));
		Assert.Equal(FinancePayableMatchStatus.Matched, Assert.Single(explicitRemainder.Lines).MatchStatus);
	}

	internal static async Task VerifyConcurrentMatchingAsync(FinancePaymentIntegrityFixture fixture)
	{
		var receipt = await fixture.CreateReceiptsAsync(10);
		var first = await fixture.Payables.SaveDraftAsync(fixture.Draft(MatchLine(receipt.LineId, 8m)));
		var second = await fixture.Payables.SaveDraftAsync(fixture.Draft(MatchLine(receipt.LineId, 8m)));
		first = await fixture.Payables.SubmitAsync(first.Id, first.Version);
		second = await fixture.Payables.SubmitAsync(second.Id, second.Version);
		var conflict = await ProviderWriteOverlap.RunAsync(fixture.Factory, fixture.Transactions,
			() => fixture.ApproveSubmittedAsync(first),
			() => fixture.ApproveSubmittedAsync(second));
		Assert.IsType<InvalidOperationException>(conflict);
		Assert.Equal(8m, fixture.Amount("SELECT SUM(l.Quantity) FROM FinanceSupplierDocumentLines l INNER JOIN FinanceSupplierDocuments d ON d.Id=l.DocumentId WHERE l.PurchaseOrderLineId=$Line AND d.Status=2;", P("$Line", receipt.LineId)));
	}

	internal static async Task VerifySelectedBankAsync(FinancePaymentIntegrityFixture fixture)
	{
		var bankA = await fixture.CreateBankAsync();
		var bankB = await fixture.CreateBankAsync(second: true);
		var invoice = await fixture.CreateInvoiceAsync();
		var run = await fixture.CreateRunAsync(bankB.Id, invoice.OpenItemId.GetValueOrDefault());
		var line = Assert.Single(run.Lines);
		await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Banking.ExecutePaymentRunLineAsync(run.Id, line.Id, "Selected bank B"));
		Assert.Equal(100m, (await fixture.Payables.GetOpenItemAsync(invoice.OpenItemId.GetValueOrDefault()))?.RemainingAmount);
		var profileB = fixture.CreateBankBProfile();
		fixture.Transactions.FailNextCommit(new InvalidOperationException("Injected payment commit failure"));
		await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Banking.ExecutePaymentRunLineAsync(run.Id, line.Id, "Selected bank B"));
		Assert.Equal(0L, fixture.Scalar("SELECT COUNT(*) FROM FinancePayablePayments WHERE OperationId=$Operation;",P("$Operation",line.ExecutionOperationId.ToString("D"))));
		Assert.Equal(0L, fixture.Scalar("SELECT COUNT(*) FROM FinanceJournalEntries WHERE OperationId=$Operation;",P("$Operation",line.ExecutionOperationId.ToString("D"))));
		Assert.Equal(100m, (await fixture.Payables.GetOpenItemAsync(invoice.OpenItemId.GetValueOrDefault()))?.RemainingAmount);
		var executed = await fixture.Banking.ExecutePaymentRunLineAsync(run.Id, line.Id, "Selected bank B");
		var paymentId = Assert.Single(executed.Lines).PayablePaymentId.GetValueOrDefault();
		var journalId = fixture.Scalar("SELECT JournalEntryId FROM FinancePayablePayments WHERE Id=$Id;", P("$Id", paymentId));
		Assert.Equal(100m, fixture.Amount("SELECT COALESCE(SUM(TransactionCredit-TransactionDebit),0) FROM FinanceJournalEntryLines WHERE JournalEntryId=$Journal AND AccountId=$Account;", P("$Journal", journalId), P("$Account", fixture.BankGlB.ToString("D"))));
		Assert.Equal(0m, fixture.Amount("SELECT COALESCE(SUM(TransactionCredit),0) FROM FinanceJournalEntryLines WHERE JournalEntryId=$Journal AND AccountId=$Account;", P("$Journal", journalId), P("$Account", fixture.BankGlA.ToString("D"))));
		var replay = await fixture.Banking.ExecutePaymentRunLineAsync(run.Id, line.Id, "Selected bank B");
		Assert.Equal(paymentId, Assert.Single(replay.Lines).PayablePaymentId);
		await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Payables.PostPaymentAsync(new FinancePayablePaymentRequest
		{
			OperationId=line.ExecutionOperationId,BankAccountId=bankA.Id,SupplierId=line.SupplierId,PaymentDate=run.PaymentDate,Currency=run.Currency,Amount=line.Amount,Reference=line.Reference,Description=$"Payment run {run.Id}: {run.Description}",Allocations=[new FinancePayableAllocationRequest(line.PayableOpenItemId,line.Amount)]
		}));
		await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Banking.SaveBankAccountAsync(bankB with { GeneralLedgerAccountId=fixture.BankGlA }));
		await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Banking.SaveBankAccountAsync(bankB with { Iban=bankA.Iban }));

		var nextInvoice = await fixture.CreateInvoiceAsync();
		var nextRun = await fixture.CreateRunAsync(bankB.Id, nextInvoice.OpenItemId.GetValueOrDefault());
		fixture.Database.Execute("UPDATE FinancePostingProfileLines SET AccountId=$Account WHERE PostingProfileId=$Profile AND Direction=$Credit;", P("$Account",fixture.BankGlA.ToString("D")),P("$Profile",profileB),P("$Credit",(int)FinancePostingDirection.Credit));
		await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Banking.ExecutePaymentRunLineAsync(nextRun.Id, Assert.Single(nextRun.Lines).Id, "Changed profile"));
		Assert.Equal(100m, (await fixture.Payables.GetOpenItemAsync(nextInvoice.OpenItemId.GetValueOrDefault()))?.RemainingAmount);

		var interruptedInvoice = await fixture.CreateInvoiceAsync();
		var interruptedRun = await fixture.CreateRunAsync(bankA.Id,interruptedInvoice.OpenItemId.GetValueOrDefault());
		var interruptedLine = Assert.Single(interruptedRun.Lines);
		var legacyPayment = await fixture.PostLegacyRunPaymentAsync(interruptedRun);
		var resumed = await fixture.Banking.ExecutePaymentRunLineAsync(interruptedRun.Id,interruptedLine.Id,"Resume retained payment");
		Assert.Equal(legacyPayment.Id,Assert.Single(resumed.Lines).PayablePaymentId);
		Assert.Equal(1L,fixture.Scalar("SELECT COUNT(*) FROM FinancePayablePayments WHERE OperationId=$Operation;",P("$Operation",interruptedLine.ExecutionOperationId.ToString("D"))));
		Assert.Equal(1L,fixture.Scalar("SELECT COUNT(*) FROM FinanceJournalEntries WHERE OperationId=$Operation;",P("$Operation",interruptedLine.ExecutionOperationId.ToString("D"))));
		var wrongLegacyPayment = await fixture.PostLegacyRunPaymentAsync(nextRun);
		await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Banking.ExecutePaymentRunLineAsync(nextRun.Id,Assert.Single(nextRun.Lines).Id,"Wrong retained bank"));
		Assert.Null(Assert.Single((await fixture.Banking.GetPaymentRunAsync(nextRun.Id))?.Lines ?? []).PayablePaymentId);
		Assert.Equal(1L,fixture.Scalar("SELECT COUNT(*) FROM FinanceJournalEntries WHERE OperationId=$Operation;",P("$Operation",wrongLegacyPayment.OperationId.ToString("D"))));
	}

	internal static async Task VerifyReconciliationAsync(FinancePaymentIntegrityFixture fixture)
	{
		var bankA = await fixture.CreateBankAsync();
		var bankB = await fixture.CreateBankAsync(second: true);
		var invoice = await fixture.CreateInvoiceAsync();
		var payment = await fixture.PayAsync(invoice.OpenItemId.GetValueOrDefault());
		var statementA = await fixture.ImportAsync(bankA.Id, -100m, -100m, -100m);
		var statementB = await fixture.ImportAsync(bankB.Id, -100m);
		Assert.DoesNotContain(await fixture.Banking.GetReconciliationCandidatesAsync(statementB.Lines[0].Id), candidate => candidate.TargetId == payment.Id && candidate.TargetKind == FinanceBankReconciliationTargetKind.PayablePayment);
		await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Banking.ReconcileAsync(Reconcile(statementB.Lines[0].Id, FinanceBankReconciliationTargetKind.PayablePayment, payment.Id)));
		var request = Reconcile(statementA.Lines[0].Id, FinanceBankReconciliationTargetKind.PayablePayment, payment.Id);
		fixture.Transactions.FailNextCommit(new InvalidOperationException("Injected reconciliation commit failure"));
		await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Banking.ReconcileAsync(request));
		Assert.Equal(0L, fixture.Scalar("SELECT COUNT(*) FROM FinanceBankReconciliations WHERE OperationId=$Operation;",P("$Operation",request.OperationId.ToString("D"))));
		var first = await fixture.Banking.ReconcileAsync(request);
		var available = await fixture.Banking.GetReconciliationCandidatesAsync(statementA.Lines[1].Id);
		Assert.DoesNotContain(available, candidate => candidate.TargetKind == FinanceBankReconciliationTargetKind.PayablePayment && candidate.TargetId == payment.Id);
		Assert.DoesNotContain(available, candidate => candidate.TargetKind == FinanceBankReconciliationTargetKind.GeneralLedgerEntry && candidate.TargetId == payment.JournalEntryId);
		await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Banking.ReconcileAsync(Reconcile(statementA.Lines[1].Id, FinanceBankReconciliationTargetKind.GeneralLedgerEntry, payment.JournalEntryId)));
		await fixture.Banking.ReverseReconciliationAsync(first.Id, Guid.NewGuid(), "Correct bank line");
		var conflict = await ProviderWriteOverlap.RunAsync(fixture.Factory, fixture.Transactions,
			() => fixture.Banking.ReconcileAsync(Reconcile(statementA.Lines[1].Id, FinanceBankReconciliationTargetKind.GeneralLedgerEntry, payment.JournalEntryId)),
			() => fixture.Banking.ReconcileAsync(Reconcile(statementA.Lines[2].Id, FinanceBankReconciliationTargetKind.PayablePayment, payment.Id)));
		Assert.IsType<InvalidOperationException>(conflict);
		Assert.Equal(1L, fixture.Scalar("SELECT COUNT(*) FROM FinanceBankReconciliations WHERE TargetJournalEntryId=$Journal AND ReversedAtUtc IS NULL;", P("$Journal", payment.JournalEntryId)));
	}

	internal static async Task VerifyTransferAsync(FinancePaymentIntegrityFixture fixture)
	{
		var bankA = await fixture.CreateBankAsync();
		var bankB = await fixture.CreateBankAsync(second: true);
		var split = await fixture.SplitBankReceiptAsync();
		var receiptPaymentId = fixture.CreateReceivableEvidence(split.Id, 100m);
		var statementA = await fixture.ImportAsync(bankA.Id, 80m, 20m);
		var statementB = await fixture.ImportAsync(bankB.Id, 80m);
		Assert.DoesNotContain(await fixture.Banking.GetReconciliationCandidatesAsync(statementA.Lines[0].Id), candidate => candidate.TargetKind == FinanceBankReconciliationTargetKind.ReceivablePayment && candidate.TargetId == receiptPaymentId);
		await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Banking.ReconcileAsync(Reconcile(statementA.Lines[0].Id, FinanceBankReconciliationTargetKind.ReceivablePayment, receiptPaymentId)));
		var candidateB = Assert.Single(await fixture.Banking.GetReconciliationCandidatesAsync(statementB.Lines[0].Id), candidate => candidate.TargetKind == FinanceBankReconciliationTargetKind.ReceivablePayment && candidate.TargetId == receiptPaymentId);
		Assert.Equal(80m, candidateB.Amount);
		var receiptB = await fixture.Banking.ReconcileAsync(Reconcile(statementB.Lines[0].Id, FinanceBankReconciliationTargetKind.ReceivablePayment, receiptPaymentId));
		var receiptA = await fixture.Banking.ReconcileAsync(Reconcile(statementA.Lines[1].Id, FinanceBankReconciliationTargetKind.ReceivablePayment, receiptPaymentId));
		Assert.Equal(split.Id, receiptB.TargetJournalEntryId);
		Assert.Equal(split.Id, receiptA.TargetJournalEntryId);

		var transfer = await fixture.TransferAsync(50m);
		var transferA = await fixture.ImportAsync(bankA.Id, -50m);
		var transferB = await fixture.ImportAsync(bankB.Id, 50m);
		var debit = await fixture.Banking.ReconcileAsync(Reconcile(Assert.Single(transferA.Lines).Id, FinanceBankReconciliationTargetKind.GeneralLedgerEntry, transfer.Id));
		var credit = await fixture.Banking.ReconcileAsync(Reconcile(Assert.Single(transferB.Lines).Id, FinanceBankReconciliationTargetKind.GeneralLedgerEntry, transfer.Id));
		Assert.Equal(transfer.Id, debit.TargetJournalEntryId);
		Assert.Equal(transfer.Id, credit.TargetJournalEntryId);
		Assert.NotEqual(debit.Id, credit.Id);
	}

	internal static async Task VerifySepaLifecycleAsync(FinancePaymentIntegrityFixture fixture)
	{
		var bank = await fixture.CreateBankAsync();
		var invoice = await fixture.CreateInvoiceAsync();
		var firstRun = await fixture.CreateRunAsync(bank.Id, invoice.OpenItemId.GetValueOrDefault());
		var secondRun = await fixture.CreateRunAsync(bank.Id, invoice.OpenItemId.GetValueOrDefault());
		fixture.Transactions.FailNextCommit(new InvalidOperationException("Injected export commit failure"));
		await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Sepa.GenerateAsync(firstRun.Id));
		Assert.Equal(0L, fixture.Scalar("SELECT COUNT(*) FROM FinanceSepaPaymentExports WHERE PaymentRunId=$Run;",P("$Run",firstRun.Id)));
		var first = await fixture.Sepa.GenerateAsync(firstRun.Id);
		await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Sepa.GenerateAsync(secondRun.Id));
		await fixture.Sepa.UpdateStatusAsync(first.Id, new FinanceSepaPaymentExportStatusUpdate { Status=FinanceSepaPaymentExportStatus.Cancelled,EvidenceNote="Not submitted" });
		var second = await fixture.Sepa.GenerateAsync(secondRun.Id);
		await fixture.Sepa.UpdateStatusAsync(second.Id, new FinanceSepaPaymentExportStatusUpdate { Status=FinanceSepaPaymentExportStatus.SubmittedExternally,ExternalReference="Fixture external evidence" });
		await fixture.Sepa.UpdateStatusAsync(second.Id, new FinanceSepaPaymentExportStatusUpdate { Status=FinanceSepaPaymentExportStatus.Rejected,EvidenceNote="Rejected before payment" });
		var thirdRun = await fixture.CreateRunAsync(bank.Id, invoice.OpenItemId.GetValueOrDefault());
		var third = await fixture.Sepa.GenerateAsync(thirdRun.Id);
		Assert.NotEqual(first.Id, third.Id);
		await fixture.PayAsync(invoice.OpenItemId.GetValueOrDefault());
		Assert.Equal(first.XmlPayload, (await fixture.Sepa.DownloadAsync(first.Id)).XmlPayload);
		Assert.Equal(third.XmlPayload, (await fixture.Sepa.GenerateAsync(thirdRun.Id)).XmlPayload);
		await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Sepa.GenerateAsync(thirdRun.Id, supersedeExisting:true));
	}

	internal static async Task VerifyConcurrentSepaAsync(FinancePaymentIntegrityFixture fixture)
	{
		var bank = await fixture.CreateBankAsync();
		var invoice = await fixture.CreateInvoiceAsync();
		var first = await fixture.CreateRunAsync(bank.Id, invoice.OpenItemId.GetValueOrDefault(), 60m);
		var second = await fixture.CreateRunAsync(bank.Id, invoice.OpenItemId.GetValueOrDefault(), 60m);
		var conflict = await ProviderWriteOverlap.RunAsync(fixture.Factory, fixture.Transactions,
			() => fixture.Sepa.GenerateAsync(first.Id),
			() => fixture.Sepa.GenerateAsync(second.Id));
		Assert.IsType<InvalidOperationException>(conflict);
		Assert.Equal(60m, fixture.Amount("SELECT SUM(ControlSum) FROM FinanceSepaPaymentExports WHERE PaymentRunId IN ($First,$Second) AND CurrentStatus=1;", P("$First",first.Id),P("$Second",second.Id)));
	}

	private static FinanceSupplierDocumentLineDraft MatchLine(long purchaseOrderLineId, decimal quantity, long? receiptId = null) => new() { PurchaseOrderLineId=purchaseOrderLineId,GoodsReceiptLineId=receiptId,Description="Received items",Quantity=quantity,UnitPrice=10m };
	private static FinanceBankReconciliationRequest Reconcile(long lineId, FinanceBankReconciliationTargetKind kind, long targetId) => new() { OperationId=Guid.NewGuid(),StatementLineId=lineId,TargetKind=kind,TargetId=targetId };
}
