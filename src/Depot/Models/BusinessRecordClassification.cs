// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public enum BusinessRecordRetentionCategory
{
	Operational,
	BusinessTransaction,
	AccountingRelevant,
	AuditEvidence
}

public sealed record BusinessRecordClassification(
	string EntityType,
	BusinessRecordRetentionCategory RetentionCategory,
	string EditableState,
	string FinalState,
	string CorrectionMechanism,
	string NumberingRule,
	bool HistoricalSnapshotRequired);

public static class BusinessRecordCatalog
{
	private static readonly Dictionary<string, BusinessRecordClassification> Classifications =
		new(StringComparer.Ordinal)
		{
			[nameof(PurchaseOrder)] = Record(nameof(PurchaseOrder), BusinessRecordRetentionCategory.BusinessTransaction, "Draft", "Ordered / PartiallyReceived / Received / Closed / Cancelled", "Close or compensating receipt/return workflow; posted receipts are never deleted", "PO-{database identity:000000}"),
			[nameof(GoodsReceipt)] = Record(nameof(GoodsReceipt), BusinessRecordRetentionCategory.AccountingRelevant, "Draft preparation only", "Posted or reversed", "Explicit reversal with reason and linked compensating stock movements", "GR-{database identity:000000}"),
			[nameof(StockTransfer)] = Record(nameof(StockTransfer), BusinessRecordRetentionCategory.BusinessTransaction, "Draft", "Posted or reversed", "Explicit reversal with reason and linked compensating stock movements", "ST-{database identity:000000}"),
			[nameof(InventoryCount)] = Record(nameof(InventoryCount), BusinessRecordRetentionCategory.BusinessTransaction, "Draft / counting", "Posted or reversed", "Explicit reversal/correction workflow with reason", "IC-{database identity:000000}"),
			[nameof(MaterialIssue)] = Record(nameof(MaterialIssue), BusinessRecordRetentionCategory.BusinessTransaction, "Draft", "Posted or reversed", "Explicit material return or reversal with reason", "MI-{database identity:000000}"),
			[nameof(MaterialReturn)] = Record(nameof(MaterialReturn), BusinessRecordRetentionCategory.BusinessTransaction, "Draft", "Posted or reversed", "Explicit reversal with reason", "MR-{database identity:000000}"),
			[nameof(SupplierReturn)] = Record(nameof(SupplierReturn), BusinessRecordRetentionCategory.BusinessTransaction, "Draft", "Posted or reversed", "Explicit reversal with reason", "SR-{database identity:000000}"),
			[nameof(SalesOrder)] = Record(nameof(SalesOrder), BusinessRecordRetentionCategory.BusinessTransaction, "Draft", "Released / PartiallyShipped / Shipped / Completed / Cancelled", "Cancellation before fulfilment; shipment return/credit workflows after fulfilment", "SO-{database identity:000000}"),
			[nameof(Shipment)] = Record(nameof(Shipment), BusinessRecordRetentionCategory.BusinessTransaction, "Draft", "Posted or reversed", "Explicit reversal before invoicing; customer return after invoicing", "SH-{database identity:000000}"),
			[nameof(CustomerReturn)] = Record(nameof(CustomerReturn), BusinessRecordRetentionCategory.BusinessTransaction, "Draft", "Posted", "Separate corrective transaction; original shipment remains retained", "CR-{database identity:000000}"),
			[nameof(SalesInvoice)] = Record(nameof(SalesInvoice), BusinessRecordRetentionCategory.AccountingRelevant, "Draft", "Posted or cancelled draft", "Credit note for a posted invoice; posted invoice content remains immutable", "INV-{database identity:000000}"),
			[nameof(SalesCreditNote)] = Record(nameof(SalesCreditNote), BusinessRecordRetentionCategory.AccountingRelevant, "Draft", "Posted", "Additional correcting credit/debit workflow; posted credit note remains immutable", "CN-{database identity:000000}"),
			[nameof(FinanceJournalEntry)] = Record(nameof(FinanceJournalEntry), BusinessRecordRetentionCategory.AccountingRelevant, "None after posting", "Posted", "Explicit linked reversal journal entry; the original journal entry remains immutable", "Configured Finance General Ledger number sequence"),
			[nameof(FinanceReceivableOpenItem)] = Record(nameof(FinanceReceivableOpenItem), BusinessRecordRetentionCategory.AccountingRelevant, "Settlement state only", "Created from posted source/payment", "Allocations, source credit notes, write-offs and explicit payment/write-off reversals preserve the original open-item identity", "Database identity plus immutable source identity"),
			[nameof(FinanceReceivablePayment)] = Record(nameof(FinanceReceivablePayment), BusinessRecordRetentionCategory.AccountingRelevant, "None after posting", "Posted or reversed", "Explicit linked payment reversal restores allocations and creates a General Ledger reversal", "Database identity plus immutable operation ID"),
			[nameof(FinanceReceivableWriteOff)] = Record(nameof(FinanceReceivableWriteOff), BusinessRecordRetentionCategory.AccountingRelevant, "None after posting", "Posted or reversed", "Explicit linked write-off reversal restores the receivable and creates a General Ledger reversal", "Database identity plus immutable operation ID"),
			[nameof(FinanceDunningRun)] = Record(nameof(FinanceDunningRun), BusinessRecordRetentionCategory.AuditEvidence, "None after creation", "Immutable run snapshot", "Create a new dunning run for a later assessment date; historical run evidence is not rewritten", "Database identity plus immutable operation ID"),
			[nameof(FinanceSupplierDocument)] = Record(nameof(FinanceSupplierDocument), BusinessRecordRetentionCategory.AccountingRelevant, "Draft", "Posted or reversed", "Explicit reversal of an unsettled posted supplier document; settled documents require allocation correction first", "Supplier identity + supplier document number + document kind"),
			[nameof(FinancePayableOpenItem)] = Record(nameof(FinancePayableOpenItem), BusinessRecordRetentionCategory.AccountingRelevant, "Settlement state only", "Created from posted supplier document/payment", "Allocations, supplier credit notes and explicit payment/document reversals preserve the original open-item identity", "Database identity plus immutable source identity"),
			[nameof(FinancePayablePayment)] = Record(nameof(FinancePayablePayment), BusinessRecordRetentionCategory.AccountingRelevant, "None after posting", "Posted or reversed", "Explicit linked payment reversal restores allocations and creates a General Ledger reversal", "Database identity plus immutable operation ID"),
			[nameof(FinanceInventoryAccountingEvent)] = Record(nameof(FinanceInventoryAccountingEvent), BusinessRecordRetentionCategory.AccountingRelevant, "None after creation", "Posted accounting event", "Linked source movement reversal creates a compensating General Ledger entry and valuation restoration", "Immutable stock movement identity plus operation ID"),
			[nameof(FinanceInventoryPurchaseVariance)] = Record(nameof(FinanceInventoryPurchaseVariance), BusinessRecordRetentionCategory.AccountingRelevant, "None after creation", "Posted or reversed", "Explicit linked variance reversal; original variance evidence remains retained", "Supplier document identity plus immutable operation ID"),
			[nameof(FinanceInventoryLandedCostOperation)] = Record(nameof(FinanceInventoryLandedCostOperation), BusinessRecordRetentionCategory.AccountingRelevant, "None after allocation", "Posted or reversed", "Explicit linked landed-cost reversal restores layer cost only while the affected layers remain unconsumed", "Database identity plus immutable operation ID"),
			[nameof(FinanceInventoryReconciliationRun)] = Record(nameof(FinanceInventoryReconciliationRun), BusinessRecordRetentionCategory.AuditEvidence, "None after creation", "Immutable period-end snapshot", "Create a new reconciliation for a later assessment date; historical snapshots are never rewritten", "Database identity plus immutable operation ID"),
			[nameof(FinanceBankStatement)] = Record(nameof(FinanceBankStatement), BusinessRecordRetentionCategory.AccountingRelevant, "None after import", "Immutable imported bank statement", "Import corrected external evidence as a new statement; imported statement content is never rewritten", "Bank account + external statement reference + immutable import hash"),
			[nameof(FinanceBankReconciliation)] = Record(nameof(FinanceBankReconciliation), BusinessRecordRetentionCategory.AuditEvidence, "None after creation", "Matched or reversed", "Explicit reconciliation reversal preserves the original match and target evidence", "Database identity plus immutable operation ID"),
			[nameof(FinancePaymentRun)] = Record(nameof(FinancePaymentRun), BusinessRecordRetentionCategory.AccountingRelevant, "Draft proposal", "Approved / executed / cancelled", "Create a new run for changed payment intent; executed AP payments retain their own reversal workflow", "Database identity plus immutable operation ID"),
			[nameof(StockMovement)] = new(nameof(StockMovement), BusinessRecordRetentionCategory.AuditEvidence, "None after creation", "Immediately immutable", "Linked reversal movement; original movement is retained", "Database identity plus immutable reference", true)
		};

	public static IReadOnlyCollection<BusinessRecordClassification> All => Classifications.Values;

	public static BusinessRecordClassification? Find(string entityType) =>
		Classifications.GetValueOrDefault(entityType);

	public static BusinessRecordClassification Require(string entityType) =>
		Find(entityType) ?? throw new InvalidOperationException($"'{entityType}' is not classified as a retained business record.");

	private static BusinessRecordClassification Record(
		string entityType,
		BusinessRecordRetentionCategory category,
		string editableState,
		string finalState,
		string correction,
		string numbering) =>
		new(entityType, category, editableState, finalState, correction, numbering, true);
}
