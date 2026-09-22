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
	AccountingControlWorkspace = 11,
	ManagementCockpit = 12,
	AuditComplianceCenter = 13,
	MasterDataWorkspace = 14,
	ApplicationAdministrationCenter = 15
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
	FinanceStatus = 26,
	SalesLead = 27,
	SalesOpportunity = 28
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
	public bool OpenIsPrimary => !CanApprove;
}

public sealed record CommercialRoleSection(string Title, string EmptyText, IReadOnlyList<CommercialRoleItem> Items, bool IsFiltered = false)
{
	public bool IsEmpty => Items.Count == 0;
	public string DisplayEmptyText => IsFiltered ? "No work items match the current filter." : EmptyText;
}

public sealed record CommercialRoleKpi(
	string Label,
	string Value,
	string? SupportingText = null,
	string? RouteId = null,
	bool HasChanged = false)
{
	public string PresentationKey => $"{RouteId ?? string.Empty}|{Label}";
}

public enum CommercialRoleQuickActionPresentation
{
	Primary = 1,
	Secondary = 2,
	Overflow = 3
}

public sealed record CommercialRoleQuickAction(
	string Label,
	string ActionId,
	string RouteId,
	CommercialRoleQuickActionPresentation Presentation = CommercialRoleQuickActionPresentation.Secondary)
{
	public bool IsPrimary => Presentation == CommercialRoleQuickActionPresentation.Primary;
	public bool IsSecondary => Presentation == CommercialRoleQuickActionPresentation.Secondary;
	public bool IsOverflow => Presentation == CommercialRoleQuickActionPresentation.Overflow;
}

public sealed record CommercialRoleColumnProfile(
	string WorkspaceId,
	IReadOnlySet<string> VisibleColumnIds,
	IReadOnlyDictionary<string, string> HeaderOverrides)
{
	public bool IsVisible(string columnId) => VisibleColumnIds.Contains(columnId);
	public string GetHeader(string columnId, string fallback) => HeaderOverrides.GetValueOrDefault(columnId, fallback);
}

public static class CommercialRoleColumnProfiles
{
	public const string Type = "role.type";
	public const string Reference = "role.reference";
	public const string Requester = "role.requester";
	public const string Context = "role.context";
	public const string Amount = "role.amount";
	public const string Currency = "role.currency";
	public const string Submitted = "role.submitted";
	public const string Due = "role.due";
	public const string Age = "role.age";
	public const string StateDetail = "role.state-detail";
	public const string Status = "role.status";
	public const string NextAction = "role.next-action";
	public const string Actions = "role.actions";

	private static readonly IReadOnlyDictionary<CommercialRoleCenterKind, CommercialRoleColumnProfile> Profiles =
		new Dictionary<CommercialRoleCenterKind, CommercialRoleColumnProfile>
		{
			[CommercialRoleCenterKind.SalesWorkspace] = Create("role-center.sales", [Type, Reference, Context, Amount, Submitted, Status, NextAction, Actions], (Reference, "Document"), (Context, "Customer / Context")),
			[CommercialRoleCenterKind.SalesControlCenter] = Create("role-center.sales-control", [Type, Reference, Context, Amount, Due, Status, NextAction, Actions], (Reference, "Order / Work"), (Context, "Customer / Context")),
			[CommercialRoleCenterKind.BuyerWorkbench] = Create("role-center.buyer", [Type, Reference, Context, Amount, Due, Status, NextAction, Actions], (Reference, "PO / Return"), (Context, "Supplier")),
			[CommercialRoleCenterKind.ApprovalInbox] = Create("role-center.approvals", [Type, Reference, Requester, Context, Amount, Age, Status, Actions], (Type, "Type"), (Requester, "Requester"), (Context, "Counterparty")),
			[CommercialRoleCenterKind.ReceivingWorkspace] = Create("role-center.receiving", [Reference, Context, Due, StateDetail, Status, NextAction, Actions], (Reference, "PO / Receipt"), (Context, "Supplier"), (Due, "Expected date"), (StateDetail, "Receipt state")),
			[CommercialRoleCenterKind.FulfillmentWorkspace] = Create("role-center.fulfillment", [Reference, Context, Due, StateDetail, Status, NextAction, Actions], (Reference, "Order / Shipment"), (Context, "Customer"), (Due, "Requested date"), (StateDetail, "Picking / Packing")),
			[CommercialRoleCenterKind.InventoryControlWorkspace] = Create("role-center.inventory-control", [Type, Reference, Context, StateDetail, Status, NextAction, Actions], (Context, "Warehouse / Context")),
			[CommercialRoleCenterKind.ReceivablesWorkspace] = Create("role-center.receivables", [Context, Reference, Amount, Due, Age, StateDetail, NextAction, Actions], (Context, "Customer"), (Reference, "Document"), (StateDetail, "Allocation")),
			[CommercialRoleCenterKind.PayablesWorkspace] = Create("role-center.payables", [Context, Reference, Amount, Due, StateDetail, Status, NextAction, Actions], (Context, "Supplier"), (Reference, "Invoice"), (StateDetail, "Match")),
			[CommercialRoleCenterKind.TreasuryWorkspace] = Create("role-center.treasury", [Type, Reference, Context, Amount, Currency, Due, Status, NextAction], (Context, "Bank / Counterparty")),
			[CommercialRoleCenterKind.AccountingControlWorkspace] = Create("role-center.accounting-control", [Type, Reference, Context, Amount, Status, NextAction, Actions], (Context, "Accounting context")),
			[CommercialRoleCenterKind.ManagementCockpit] = Create("role-center.management", [Type, Reference, Context, Amount, Status, NextAction], (Context, "Business context")),
			[CommercialRoleCenterKind.AuditComplianceCenter] = Create("role-center.audit-compliance", [Type, Reference, Context, Submitted, Status, NextAction, Actions], (Context, "Audit context")),
			[CommercialRoleCenterKind.MasterDataWorkspace] = Create("role-center.master-data", [Type, Reference, Context, Status, NextAction, Actions], (Context, "Master-data context")),
			[CommercialRoleCenterKind.ApplicationAdministrationCenter] = Create("role-center.application-admin", [Type, Reference, Context, Status, NextAction, Actions], (Context, "Administration context"))
		};

	public static CommercialRoleColumnProfile Get(CommercialRoleCenterKind kind) =>
		Profiles.TryGetValue(kind, out var profile) ? profile : throw new ArgumentOutOfRangeException(nameof(kind));

	private static CommercialRoleColumnProfile Create(string workspaceId, string[] visibleColumns, params (string Id, string Header)[] headers) =>
		new(
			workspaceId,
			new HashSet<string>(visibleColumns, StringComparer.Ordinal),
			headers.ToDictionary(value => value.Id, value => value.Header, StringComparer.Ordinal));
}

public sealed record CommercialRoleCenterSnapshot(
	CommercialRoleCenterKind Kind,
	string Title,
	string Subtitle,
	IReadOnlyList<CommercialRoleSection> Sections,
	IReadOnlyList<CommercialRoleKpi> Kpis,
	IReadOnlyList<CommercialRoleQuickAction> QuickActions,
	IReadOnlyList<MyWorkProviderFailure> Failures);
