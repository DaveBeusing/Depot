// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;

using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class DashboardRepository : DatabaseRepository
{
	public DashboardRepository(DatabaseAccess database) : base(database) { }

	public Task<DashboardRoleMetrics?> GetRoleMetricsAsync(
		bool includeApprovals,
		bool includePurchasing,
		bool includeWarehouse,
		bool includeSales,
		bool includeAdministration,
		DateTime presenceCutoffUtc,
		CancellationToken cancellationToken)
	{
		var columns = new List<string>();
		var parameters = new List<DatabaseParameter>();
		if (includeApprovals)
		{
			columns.Add("(SELECT COUNT(*) FROM PurchaseOrders WHERE Status = $PendingApproval)");
			columns.Add("(SELECT MIN(SubmittedAtUtc) FROM PurchaseOrders WHERE Status = $PendingApproval)");
			columns.Add("(SELECT COALESCE(SUM(pol.Quantity * pol.UnitPrice), 0) FROM PurchaseOrderLines pol INNER JOIN PurchaseOrders po ON po.Id = pol.PurchaseOrderId WHERE po.Status = $PendingApproval)");
			parameters.Add(Parameter("$PendingApproval", (int)PurchaseOrderStatus.PendingApproval));
		}
		if (includePurchasing)
		{
			columns.Add("(SELECT COUNT(*) FROM PurchaseOrders WHERE Status IN ($PendingApprovalForPurchasing, $Approved))");
			columns.Add("(SELECT COUNT(*) FROM PurchaseOrders WHERE Status = $PartiallyReceived)");
			columns.Add("(SELECT COUNT(*) FROM PurchaseOrders WHERE Status IN ($Ordered, $PartiallyReceived) AND ExpectedDeliveryDate < $Today)");
			columns.Add("(SELECT COUNT(*) FROM SupplierReturns WHERE Status = $SupplierReturnDraft)");
			parameters.AddRange([
				Parameter("$PendingApprovalForPurchasing", (int)PurchaseOrderStatus.PendingApproval),
				Parameter("$Approved", (int)PurchaseOrderStatus.Approved),
				Parameter("$PartiallyReceived", (int)PurchaseOrderStatus.PartiallyReceived),
				Parameter("$Ordered", (int)PurchaseOrderStatus.Ordered),
				Parameter("$Today", DateTime.Today),
				Parameter("$SupplierReturnDraft", (int)SupplierReturnStatus.Draft)
			]);
		}
		if (includeWarehouse)
		{
			columns.Add("(SELECT COUNT(*) FROM InventoryCounts WHERE Status = $InventoryCountReview)");
			columns.Add("(SELECT COUNT(*) FROM StockTransfers WHERE Status = $TransferDraft)");
			parameters.Add(Parameter("$InventoryCountReview", (int)InventoryCountStatus.Review));
			parameters.Add(Parameter("$TransferDraft", (int)StockTransferStatus.Draft));
		}
		if (includeSales)
		{
			columns.Add("(SELECT COUNT(*) FROM SalesOrders WHERE Status = $SalesPendingApproval)");
			columns.Add("(SELECT COUNT(*) FROM SalesOrders so WHERE so.Status = $SalesApproved AND EXISTS (SELECT 1 FROM SalesOrderLines sol WHERE sol.SalesOrderId=so.Id AND sol.ReservedQuantity + sol.ShippedQuantity < sol.Quantity))");
			columns.Add("(SELECT COUNT(*) FROM SalesOrders so WHERE so.Status IN ($SalesReleased,$SalesPartiallyShipped) AND EXISTS (SELECT 1 FROM SalesOrderLines sol WHERE sol.SalesOrderId=so.Id AND sol.ReservedQuantity + sol.ShippedQuantity < sol.Quantity))");
			columns.Add("(SELECT COUNT(*) FROM SalesOrders WHERE Status IN ($SalesReleased, $SalesPartiallyShipped))");
			columns.Add("(SELECT COUNT(*) FROM Shipments WHERE Status = $ShipmentDraft)");
			columns.Add("(SELECT COUNT(*) FROM SalesInvoices WHERE Status = $InvoiceDraft)");
			columns.Add("(SELECT COUNT(*) FROM CustomerReturns WHERE ReturnDate >= $MonthStart)");
			columns.Add("(SELECT COUNT(*) FROM SalesCreditNotes WHERE CreditDate >= $MonthStart)");
			columns.Add("((SELECT COALESCE(SUM(sil.Quantity * sil.UnitPrice * (1 - sil.DiscountPercent / 100.0)),0) FROM SalesInvoiceLines sil INNER JOIN SalesInvoices si ON si.Id=sil.SalesInvoiceId WHERE si.Status=$InvoicePosted AND si.InvoiceDate >= $MonthStart) - (SELECT COALESCE(SUM(cnl.Quantity * cnl.UnitPrice * (1 - cnl.DiscountPercent / 100.0)),0) FROM SalesCreditNoteLines cnl INNER JOIN SalesCreditNotes cn ON cn.Id=cnl.SalesCreditNoteId WHERE cn.Status=$CreditPosted AND cn.CreditDate >= $MonthStart))");
			parameters.AddRange([
				Parameter("$SalesPendingApproval", (int)SalesOrderStatus.PendingApproval),
				Parameter("$SalesApproved", (int)SalesOrderStatus.Approved),
				Parameter("$SalesReleased", (int)SalesOrderStatus.Released),
				Parameter("$SalesPartiallyShipped", (int)SalesOrderStatus.PartiallyShipped),
				Parameter("$ShipmentDraft", (int)ShipmentStatus.Draft),
				Parameter("$InvoiceDraft", (int)SalesInvoiceStatus.Draft),
				Parameter("$InvoicePosted", (int)SalesInvoiceStatus.Posted),
				Parameter("$CreditPosted", (int)SalesCreditNoteStatus.Posted),
				Parameter("$MonthStart", new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1))
			]);
		}
		if (includeAdministration)
		{
			columns.Add("(SELECT COUNT(DISTINCT UserId) FROM UserSessions WHERE EndedUtc IS NULL AND LastSeenUtc >= $PresenceCutoff)");
			columns.Add("(SELECT COUNT(*) FROM UserSessions WHERE EndedUtc IS NULL AND LastSeenUtc >= $PresenceCutoff)");
			parameters.Add(Parameter("$PresenceCutoff", presenceCutoffUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)));
		}
		if (columns.Count == 0) return Task.FromResult<DashboardRoleMetrics?>(new DashboardRoleMetrics(null, null, null, null, null));

		return Database.QuerySingleOrDefaultAsync(
			$"SELECT {string.Join(", ", columns)};",
			reader => ReadMetrics(reader, includeApprovals, includePurchasing, includeWarehouse, includeSales, includeAdministration),
			cancellationToken,
			parameters.ToArray());
	}

	private static DashboardRoleMetrics ReadMetrics(DbDataReader reader, bool includeApprovals, bool includePurchasing, bool includeWarehouse, bool includeSales, bool includeAdministration)
	{
		var ordinal = 0;
		PurchaseOrderApprovalSummary? approvals = null;
		DashboardPurchasingMetrics? purchasing = null;
		DashboardWarehouseMetrics? warehouse = null;
		DashboardSalesMetrics? sales = null;
		DashboardAdministrationMetrics? administration = null;
		if (includeApprovals) approvals = new PurchaseOrderApprovalSummary(Long(reader, ordinal++), Utc(reader, ordinal++), Decimal(reader, ordinal++));
		if (includePurchasing) purchasing = new DashboardPurchasingMetrics(Long(reader, ordinal++), Long(reader, ordinal++), Long(reader, ordinal++), Long(reader, ordinal++));
		if (includeWarehouse) warehouse = new DashboardWarehouseMetrics(Long(reader, ordinal++), Long(reader, ordinal++));
		if (includeSales) sales = new DashboardSalesMetrics(Long(reader, ordinal++), Long(reader, ordinal++), Long(reader, ordinal++), Long(reader, ordinal++), Long(reader, ordinal++), Long(reader, ordinal++), Long(reader, ordinal++), Long(reader, ordinal++), Decimal(reader, ordinal++));
		if (includeAdministration) administration = new DashboardAdministrationMetrics(Long(reader, ordinal++), Long(reader, ordinal));
		return new DashboardRoleMetrics(approvals, purchasing, warehouse, sales, administration);
	}

	private static long Long(DbDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? 0 : Convert.ToInt64(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
	private static decimal Decimal(DbDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? 0 : Convert.ToDecimal(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
	private static DateTime? Utc(DbDataReader reader, int ordinal)
	{
		if (reader.IsDBNull(ordinal)) return null;
		var value = reader.GetValue(ordinal);
		var parsed = value is DateTime dateTime ? dateTime : DateTime.Parse(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
		return parsed.Kind == DateTimeKind.Utc ? parsed : DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
	}
}
