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
