// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public enum CommercialRoleCenterKind
{
	SalesWorkspace = 1,
	SalesControlCenter = 2,
	BuyerWorkbench = 3,
	ApprovalInbox = 4,
	ReceivingWorkspace = 5,
	FulfillmentWorkspace = 6,
	InventoryControlWorkspace = 7,
	ReceivablesWorkspace = 8,
	PayablesWorkspace = 9,
	TreasuryWorkspace = 10,
	AccountingControlWorkspace = 11
}

public enum CommercialRoleItemKind
{
	Customer = 1,
	SalesQuote = 2,
	SalesOrder = 3,
	SalesOrderApproval = 4,
	PurchaseOrder = 5,
	PurchaseOrderApproval = 6,
	SupplierReturn = 7,
	SupplierInvoice = 8,
	PaymentProposal = 9,
	Shipment = 10,
	GoodsReceipt = 11,
	CustomerReturn = 12,
	InventoryCount = 13,
	StockTransfer = 14,
	MaterialIssue = 15,
	MaterialReturn = 16,
	ReceivableOpenItem = 17,
	PayableOpenItem = 18,
	BankStatement = 19,
	BankStatementLine = 20,
	CashPosition = 21,
	GeneralLedgerEntry = 22,
	InventoryReconciliation = 23,
	InventoryValuation = 24,
	ReportSnapshot = 25,
	FinanceStatus = 26
}

public sealed record CommercialRoleItem(
	CommercialRoleItemKind Kind,
	long EntityId,
	long Version,
	string DisplayNumber,
	string Title,
	string? Context,
	string Status,
	decimal? Amount,
	DateTime? SubmittedAtUtc,
	DateTime? DueAt,
	int? AgeDays,
	string RouteId,
	long? CreatedByUserId = null,
	bool CanApprove = false,
	bool CanReject = false,
	string? Requester = null,
	string? Currency = null,
	string? StateDetail = null,
	string? NextAction = null)
{
	public string SubmittedDisplay => SubmittedAtUtc?.ToLocalTime().ToString("g") ?? string.Empty;
	public string DueDisplay => DueAt?.ToString("d") ?? string.Empty;
	public string AgeDisplay => AgeDays is not null ? $"{AgeDays.Value:N0} d" : string.Empty;
	public string DueOrAgeDisplay => DueAt is not null ? DueAt.Value.ToString("d") : AgeDays is not null ? $"{AgeDays.Value:N0} d" : string.Empty;
	public bool HasDecisionActions => CanApprove || CanReject;
}

public sealed record CommercialRoleSection(string Title, string EmptyText, IReadOnlyList<CommercialRoleItem> Items)
{
	public bool IsEmpty => Items.Count == 0;
}

public sealed record CommercialRoleKpi(string Label, string Value, string? SupportingText = null, string? RouteId = null);

public sealed record CommercialRoleQuickAction(string Label, string ActionId, string RouteId);

public sealed record CommercialRoleCenterSnapshot(
	CommercialRoleCenterKind Kind,
	string Title,
	string Subtitle,
	IReadOnlyList<CommercialRoleSection> Sections,
	IReadOnlyList<CommercialRoleKpi> Kpis,
	IReadOnlyList<CommercialRoleQuickAction> QuickActions,
	IReadOnlyList<MyWorkProviderFailure> Failures);
