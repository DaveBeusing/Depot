// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public enum CommercialRoleCenterKind
{
	SalesWorkspace = 1,
	SalesControlCenter = 2,
	BuyerWorkbench = 3,
	ApprovalInbox = 4
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
	Shipment = 10
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
	string? Requester = null)
{
	public string SubmittedDisplay => SubmittedAtUtc?.ToLocalTime().ToString("g") ?? string.Empty;
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
