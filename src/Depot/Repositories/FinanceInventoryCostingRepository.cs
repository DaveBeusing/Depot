// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;
using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class FinanceInventoryCostingRepository : DatabaseRepository
{
	public FinanceInventoryCostingRepository(DatabaseAccess database) : base(database) { }

	public Task<FinanceInventoryAccountingPolicy?> GetPolicyAsync(CancellationToken cancellationToken = default) =>
		Database.QuerySingleOrDefaultAsync("SELECT Id,Version,InventoryControlAccountId,InventoryAdjustmentPostingProfileId,PurchaseVariancePostingProfileId,LandedCostPostingProfileId,IsActive FROM FinanceInventoryAccountingPolicies ORDER BY Id;", ReadPolicy, cancellationToken);

	public Task<IReadOnlyList<FinanceInventoryValuationSummary>> GetValuationSummaryAsync(CancellationToken cancellationToken = default) =>
		Database.QueryAsync("SELECT ItemId,CurrencyCode,SUM(RemainingQuantity),SUM(RemainingQuantity*UnitCost) FROM FinanceInventoryValuationLayers WHERE ReversedAtUtc IS NULL AND RemainingQuantity>0 GROUP BY ItemId,CurrencyCode ORDER BY ItemId,CurrencyCode;", reader => new FinanceInventoryValuationSummary(reader.GetInt64(0), Convert.ToInt32(reader.GetValue(2), CultureInfo.InvariantCulture), new CurrencyCode(reader.GetString(1)), ReadDecimal(reader, 3)), cancellationToken);

	public async Task<IReadOnlyList<FinanceInventoryReconciliationRun>> GetRecentReconciliationsAsync(int count = 20, CancellationToken cancellationToken = default)
	{
		var rows = await Database.QueryAsync("SELECT Id,OperationId,AccountingBookId,InventoryControlAccountId,AsOfDate,ReportingCurrencyCode,ValuationAmount,GeneralLedgerAmount,Difference,CreatedAtUtc,CreatedByUserId FROM FinanceInventoryReconciliationRuns ORDER BY AsOfDate DESC,Id DESC;", ReadRun, cancellationToken);
		return rows.Take(Math.Clamp(count, 1, 100)).ToArray();
	}

	internal Task<FinanceInventoryAccountingPolicy?> GetPolicyAsync(DatabaseTransactionContext transaction, CancellationToken cancellationToken) =>
		transaction.Session.QuerySingleOrDefaultAsync("SELECT Id,Version,InventoryControlAccountId,InventoryAdjustmentPostingProfileId,PurchaseVariancePostingProfileId,LandedCostPostingProfileId,IsActive FROM FinanceInventoryAccountingPolicies ORDER BY Id;", ReadPolicy, cancellationToken);

	internal Task<long> CreatePolicyAsync(DatabaseTransactionContext transaction, FinanceInventoryAccountingPolicy value, CancellationToken cancellationToken) =>
		transaction.Session.InsertAsync("INSERT INTO FinanceInventoryAccountingPolicies (Version,InventoryControlAccountId,InventoryAdjustmentPostingProfileId,PurchaseVariancePostingProfileId,LandedCostPostingProfileId,IsActive) VALUES (1,$Account,$Adjustment,$Variance,$Landed,$Active);", cancellationToken, PolicyParameters(value));

	internal Task<int> UpdatePolicyAsync(DatabaseTransactionContext transaction, FinanceInventoryAccountingPolicy value, long expectedVersion, CancellationToken cancellationToken) =>
		transaction.Session.ExecuteAsync("UPDATE FinanceInventoryAccountingPolicies SET Version=Version+1,InventoryControlAccountId=$Account,InventoryAdjustmentPostingProfileId=$Adjustment,PurchaseVariancePostingProfileId=$Variance,LandedCostPostingProfileId=$Landed,IsActive=$Active WHERE Id=$Id AND Version=$Version;", cancellationToken, PolicyParameters(value).Append(Parameter("$Id", value.Id)).Append(Parameter("$Version", expectedVersion)).ToArray());

	internal async Task<IReadOnlyList<FinanceInventoryValuationLayer>> LockLayersAsync(DatabaseTransactionContext transaction, IReadOnlyList<long> ids, CancellationToken cancellationToken)
	{
		if (ids.Count == 0) return [];
		var result = new List<FinanceInventoryValuationLayer>(ids.Count);
		foreach (var id in ids.Distinct().OrderBy(value => value))
		{
			await transaction.Session.ExecuteAsync("UPDATE FinanceInventoryValuationLayers SET RemainingQuantity=RemainingQuantity WHERE Id=$Id;", cancellationToken, Parameter("$Id", id));
			var layer = await transaction.Session.QuerySingleOrDefaultAsync("SELECT Id,AccountingBookId,ItemId,SourceMovementId,AcquiredDate,CurrencyCode,OriginalQuantity,RemainingQuantity,UnitCost,CreatedAtUtc,CreatedByUserId,ReversedAtUtc,ReversedByUserId FROM FinanceInventoryValuationLayers WHERE Id=$Id;", ReadLayer, cancellationToken, Parameter("$Id", id));
			if (layer is null) throw new InvalidOperationException($"Inventory valuation layer '{id}' was not found.");
			result.Add(layer);
		}
		return result;
	}

	internal Task<int> UpdateLayerUnitCostAsync(DatabaseTransactionContext transaction, long layerId, decimal oldCost, decimal newCost, CancellationToken cancellationToken) =>
		transaction.Session.ExecuteAsync("UPDATE FinanceInventoryValuationLayers SET UnitCost=$NewCost WHERE Id=$Id AND UnitCost=$OldCost AND ReversedAtUtc IS NULL;", cancellationToken, Parameter("$NewCost", newCost), Parameter("$Id", layerId), Parameter("$OldCost", oldCost));

	internal Task<FinanceInventoryLandedCostOperation?> FindLandedCostByOperationAsync(DatabaseTransactionContext transaction, Guid operationId, CancellationToken cancellationToken) =>
		transaction.Session.QuerySingleOrDefaultAsync("SELECT Id,OperationId,RequestHash,PostingDate,CurrencyCode,Amount,AllocationMethod,Reference,JournalEntryId,CreatedAtUtc,CreatedByUserId,ReversalOperationId,ReversalJournalEntryId,ReversedAtUtc,ReversedByUserId FROM FinanceInventoryLandedCostOperations WHERE OperationId=$OperationId;", ReadLandedCost, cancellationToken, Parameter("$OperationId", operationId.ToString("D")));

	internal Task<FinanceInventoryLandedCostOperation?> GetLandedCostAsync(DatabaseTransactionContext transaction, long id, CancellationToken cancellationToken) =>
		transaction.Session.QuerySingleOrDefaultAsync("SELECT Id,OperationId,RequestHash,PostingDate,CurrencyCode,Amount,AllocationMethod,Reference,JournalEntryId,CreatedAtUtc,CreatedByUserId,ReversalOperationId,ReversalJournalEntryId,ReversedAtUtc,ReversedByUserId FROM FinanceInventoryLandedCostOperations WHERE Id=$Id;", ReadLandedCost, cancellationToken, Parameter("$Id", id));

	internal Task<long> CreateLandedCostAsync(DatabaseTransactionContext transaction, FinanceInventoryLandedCostOperation value, CancellationToken cancellationToken) =>
		transaction.Session.InsertAsync("INSERT INTO FinanceInventoryLandedCostOperations (OperationId,RequestHash,PostingDate,CurrencyCode,Amount,AllocationMethod,Reference,JournalEntryId,CreatedAtUtc,CreatedByUserId) VALUES ($OperationId,$Hash,$Date,$Currency,$Amount,$Method,$Reference,$Journal,$At,$User);", cancellationToken, Parameter("$OperationId", value.OperationId.ToString("D")), Parameter("$Hash", value.RequestHash), Parameter("$Date", value.PostingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), Parameter("$Currency", value.Currency.Value), Parameter("$Amount", value.Amount), Parameter("$Method", (int)value.AllocationMethod), Parameter("$Reference", value.Reference), Parameter("$Journal", value.JournalEntryId), Parameter("$At", value.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture)), Parameter("$User", value.CreatedByUserId));

	internal Task<int> CreateLandedCostAllocationAsync(DatabaseTransactionContext transaction, FinanceInventoryLandedCostAllocation value, CancellationToken cancellationToken) =>
		transaction.Session.ExecuteAsync("INSERT INTO FinanceInventoryLandedCostAllocations (LandedCostOperationId,LayerId,Amount,UnitCostIncrease) VALUES ($Operation,$Layer,$Amount,$Increase);", cancellationToken, Parameter("$Operation", value.OperationId), Parameter("$Layer", value.LayerId), Parameter("$Amount", value.Amount), Parameter("$Increase", value.UnitCostIncrease));

	internal Task<IReadOnlyList<FinanceInventoryLandedCostAllocation>> GetLandedCostAllocationsAsync(DatabaseTransactionContext transaction, long operationId, CancellationToken cancellationToken) =>
		transaction.Session.QueryAsync("SELECT Id,LandedCostOperationId,LayerId,Amount,UnitCostIncrease FROM FinanceInventoryLandedCostAllocations WHERE LandedCostOperationId=$Id ORDER BY Id;", reader => new FinanceInventoryLandedCostAllocation { Id = reader.GetInt64(0), OperationId = reader.GetInt64(1), LayerId = reader.GetInt64(2), Amount = ReadDecimal(reader, 3), UnitCostIncrease = ReadDecimal(reader, 4) }, cancellationToken, Parameter("$Id", operationId));

	internal Task<int> MarkLandedCostReversedAsync(DatabaseTransactionContext transaction, long id, Guid operationId, long journalId, DateTime reversedAtUtc, long userId, CancellationToken cancellationToken) =>
		transaction.Session.ExecuteAsync("UPDATE FinanceInventoryLandedCostOperations SET ReversalOperationId=$Operation,ReversalJournalEntryId=$Journal,ReversedAtUtc=$At,ReversedByUserId=$User WHERE Id=$Id AND ReversedAtUtc IS NULL;", cancellationToken, Parameter("$Operation", operationId.ToString("D")), Parameter("$Journal", journalId), Parameter("$At", reversedAtUtc.ToString("O", CultureInfo.InvariantCulture)), Parameter("$User", userId), Parameter("$Id", id));

	internal Task<FinanceInventoryPurchaseVariance?> GetPurchaseVarianceAsync(DatabaseTransactionContext transaction, long supplierDocumentId, CancellationToken cancellationToken) =>
		transaction.Session.QuerySingleOrDefaultAsync("SELECT Id,SupplierDocumentId,OperationId,CurrencyCode,ExpectedNetAmount,ActualNetAmount,SignedVarianceAmount,JournalEntryId,CreatedAtUtc,CreatedByUserId,ReversalOperationId,ReversalJournalEntryId,ReversedAtUtc,ReversedByUserId FROM FinanceInventoryPurchaseVariances WHERE SupplierDocumentId=$Id;", ReadVariance, cancellationToken, Parameter("$Id", supplierDocumentId));

	internal Task<long> CreatePurchaseVarianceAsync(DatabaseTransactionContext transaction, FinanceInventoryPurchaseVariance value, CancellationToken cancellationToken) =>
		transaction.Session.InsertAsync("INSERT INTO FinanceInventoryPurchaseVariances (SupplierDocumentId,OperationId,CurrencyCode,ExpectedNetAmount,ActualNetAmount,SignedVarianceAmount,JournalEntryId,CreatedAtUtc,CreatedByUserId) VALUES ($Document,$Operation,$Currency,$Expected,$Actual,$Variance,$Journal,$At,$User);", cancellationToken, Parameter("$Document", value.SupplierDocumentId), Parameter("$Operation", value.OperationId.ToString("D")), Parameter("$Currency", value.Currency.Value), Parameter("$Expected", value.ExpectedNetAmount), Parameter("$Actual", value.ActualNetAmount), Parameter("$Variance", value.SignedVarianceAmount), Parameter("$Journal", value.JournalEntryId), Parameter("$At", value.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture)), Parameter("$User", value.CreatedByUserId));

	internal Task<int> MarkPurchaseVarianceReversedAsync(DatabaseTransactionContext transaction, long id, Guid operationId, long journalId, DateTime reversedAtUtc, long userId, CancellationToken cancellationToken) =>
		transaction.Session.ExecuteAsync("UPDATE FinanceInventoryPurchaseVariances SET ReversalOperationId=$Operation,ReversalJournalEntryId=$Journal,ReversedAtUtc=$At,ReversedByUserId=$User WHERE Id=$Id AND ReversedAtUtc IS NULL;", cancellationToken, Parameter("$Operation", operationId.ToString("D")), Parameter("$Journal", journalId), Parameter("$At", reversedAtUtc.ToString("O", CultureInfo.InvariantCulture)), Parameter("$User", userId), Parameter("$Id", id));

	internal Task<IReadOnlyList<FinanceInventoryValuationReportingRow>> GetValuationReportingRowsAsync(DatabaseTransactionContext transaction, Guid bookId, DateOnly asOfDate, CancellationToken cancellationToken)
	{
		var cutoff = asOfDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);
		const string sql = "SELECT l.Id,l.ItemId,l.OriginalQuantity-COALESCE(SUM(CASE WHEN c.CreatedAtUtc<=$Cutoff AND (c.ReversedAtUtc IS NULL OR c.ReversedAtUtc>$Cutoff) THEN c.Quantity ELSE 0 END),0),l.UnitCost,l.CurrencyCode,j.ExchangeRate FROM FinanceInventoryValuationLayers l INNER JOIN FinanceInventoryAccountingEvents e ON e.MovementId=l.SourceMovementId INNER JOIN FinanceJournalEntries j ON j.Id=e.JournalEntryId LEFT JOIN FinanceInventoryValuationConsumptions c ON c.LayerId=l.Id WHERE l.AccountingBookId=$Book AND l.AcquiredDate<=$Date AND (l.ReversedAtUtc IS NULL OR l.ReversedAtUtc>$Cutoff) GROUP BY l.Id,l.ItemId,l.OriginalQuantity,l.UnitCost,l.CurrencyCode,j.ExchangeRate ORDER BY l.ItemId,l.Id;";
		return transaction.Session.QueryAsync(sql, reader => new FinanceInventoryValuationReportingRow(reader.GetInt64(0), reader.GetInt64(1), Convert.ToInt32(reader.GetValue(2), CultureInfo.InvariantCulture), ReadDecimal(reader, 3), new CurrencyCode(reader.GetString(4)), ReadDecimal(reader, 5)), cancellationToken, Parameter("$Book", bookId.ToString("D")), Parameter("$Date", asOfDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), Parameter("$Cutoff", cutoff));
	}

	internal Task<IReadOnlyList<FinanceInventoryLandedReportingRow>> GetCurrentActiveLandedRowsAsync(DatabaseTransactionContext transaction, Guid bookId, CancellationToken cancellationToken) =>
		transaction.Session.QueryAsync("SELECT a.LayerId,a.UnitCostIncrease,j.ExchangeRate FROM FinanceInventoryLandedCostAllocations a INNER JOIN FinanceInventoryLandedCostOperations o ON o.Id=a.LandedCostOperationId INNER JOIN FinanceJournalEntries j ON j.Id=o.JournalEntryId INNER JOIN FinanceInventoryValuationLayers l ON l.Id=a.LayerId WHERE l.AccountingBookId=$Book AND o.ReversedAtUtc IS NULL;", reader => new FinanceInventoryLandedReportingRow(reader.GetInt64(0), ReadDecimal(reader, 1), ReadDecimal(reader, 2)), cancellationToken, Parameter("$Book", bookId.ToString("D")));

	internal Task<IReadOnlyList<FinanceInventoryLandedReportingRow>> GetLandedReportingRowsAsync(DatabaseTransactionContext transaction, Guid bookId, DateOnly asOfDate, CancellationToken cancellationToken)
	{
		var cutoff = asOfDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);
		return transaction.Session.QueryAsync("SELECT a.LayerId,a.UnitCostIncrease,j.ExchangeRate FROM FinanceInventoryLandedCostAllocations a INNER JOIN FinanceInventoryLandedCostOperations o ON o.Id=a.LandedCostOperationId INNER JOIN FinanceJournalEntries j ON j.Id=o.JournalEntryId INNER JOIN FinanceInventoryValuationLayers l ON l.Id=a.LayerId WHERE l.AccountingBookId=$Book AND o.PostingDate<=$Date AND (o.ReversedAtUtc IS NULL OR o.ReversedAtUtc>$Cutoff);", reader => new FinanceInventoryLandedReportingRow(reader.GetInt64(0), ReadDecimal(reader, 1), ReadDecimal(reader, 2)), cancellationToken, Parameter("$Book", bookId.ToString("D")), Parameter("$Date", asOfDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), Parameter("$Cutoff", cutoff));
	}

	internal async Task<decimal> GetGlBalanceAsync(DatabaseTransactionContext transaction, Guid bookId, Guid accountId, DateOnly asOfDate, CancellationToken cancellationToken)
	{
		var value = await transaction.Session.ExecuteScalarAsync("SELECT COALESCE(SUM(l.ReportingDebit-l.ReportingCredit),0) FROM FinanceJournalEntryLines l INNER JOIN FinanceJournalEntries e ON e.Id=l.JournalEntryId WHERE e.AccountingBookId=$Book AND l.AccountId=$Account AND e.PostingDate<=$Date;", cancellationToken, Parameter("$Book", bookId.ToString("D")), Parameter("$Account", accountId.ToString("D")), Parameter("$Date", asOfDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
		return Convert.ToDecimal(value ?? 0m, CultureInfo.InvariantCulture);
	}

	internal async Task<CurrencyCode> GetBookReportingCurrencyAsync(DatabaseTransactionContext transaction, Guid bookId, CancellationToken cancellationToken)
	{
		var value = await transaction.Session.ExecuteScalarAsync("SELECT ReportingCurrencyCode FROM FinanceAccountingBooks WHERE Id=$Id AND IsActive=1;", cancellationToken, Parameter("$Id", bookId.ToString("D"))) ?? throw new InvalidOperationException("Accounting book was not found or inactive.");
		return new CurrencyCode(Convert.ToString(value, CultureInfo.InvariantCulture)!);
	}

	internal async Task<bool> AccountBelongsToBookAsync(DatabaseTransactionContext transaction, Guid bookId, Guid accountId, CancellationToken cancellationToken)
	{
		var value = await transaction.Session.ExecuteScalarAsync("SELECT COUNT(*) FROM FinanceAccounts a INNER JOIN FinanceAccountingBooks b ON b.ChartOfAccountsId=a.ChartOfAccountsId WHERE b.Id=$Book AND a.Id=$Account AND a.IsActive=1;", cancellationToken, Parameter("$Book", bookId.ToString("D")), Parameter("$Account", accountId.ToString("D")));
		return Convert.ToInt64(value ?? 0, CultureInfo.InvariantCulture) == 1;
	}

	internal Task<FinanceInventoryReconciliationRun?> FindRunAsync(DatabaseTransactionContext transaction, Guid operationId, CancellationToken cancellationToken) =>
		transaction.Session.QuerySingleOrDefaultAsync("SELECT Id,OperationId,AccountingBookId,InventoryControlAccountId,AsOfDate,ReportingCurrencyCode,ValuationAmount,GeneralLedgerAmount,Difference,CreatedAtUtc,CreatedByUserId FROM FinanceInventoryReconciliationRuns WHERE OperationId=$Operation;", ReadRun, cancellationToken, Parameter("$Operation", operationId.ToString("D")));

	internal Task<long> CreateRunAsync(DatabaseTransactionContext transaction, FinanceInventoryReconciliationRun value, CancellationToken cancellationToken) =>
		transaction.Session.InsertAsync("INSERT INTO FinanceInventoryReconciliationRuns (OperationId,AccountingBookId,InventoryControlAccountId,AsOfDate,ReportingCurrencyCode,ValuationAmount,GeneralLedgerAmount,Difference,CreatedAtUtc,CreatedByUserId) VALUES ($Operation,$Book,$Account,$Date,$Currency,$Valuation,$Gl,$Difference,$At,$User);", cancellationToken, Parameter("$Operation", value.OperationId.ToString("D")), Parameter("$Book", value.AccountingBookId.ToString("D")), Parameter("$Account", value.InventoryControlAccountId.ToString("D")), Parameter("$Date", value.AsOfDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), Parameter("$Currency", value.ReportingCurrency.Value), Parameter("$Valuation", value.ValuationAmount), Parameter("$Gl", value.GeneralLedgerAmount), Parameter("$Difference", value.Difference), Parameter("$At", value.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture)), Parameter("$User", value.CreatedByUserId));

	internal Task<int> CreateRunLineAsync(DatabaseTransactionContext transaction, long runId, FinanceInventoryReconciliationLine line, CancellationToken cancellationToken) =>
		transaction.Session.ExecuteAsync("INSERT INTO FinanceInventoryReconciliationLines (RunId,ItemId,Quantity,ReportingValue) VALUES ($Run,$Item,$Quantity,$Value);", cancellationToken, Parameter("$Run", runId), Parameter("$Item", line.ItemId), Parameter("$Quantity", line.Quantity), Parameter("$Value", line.ReportingValue));

	private static DatabaseParameter[] PolicyParameters(FinanceInventoryAccountingPolicy value) =>
	[
		Parameter("$Account", value.InventoryControlAccountId.ToString("D")), Parameter("$Adjustment", value.InventoryAdjustmentPostingProfileId), Parameter("$Variance", value.PurchaseVariancePostingProfileId), Parameter("$Landed", value.LandedCostPostingProfileId), Parameter("$Active", value.IsActive)
	];

	private static FinanceInventoryAccountingPolicy ReadPolicy(DbDataReader reader) => new() { Id = reader.GetInt64(0), Version = reader.GetInt64(1), InventoryControlAccountId = Guid.Parse(reader.GetString(2)), InventoryAdjustmentPostingProfileId = reader.GetInt64(3), PurchaseVariancePostingProfileId = reader.GetInt64(4), LandedCostPostingProfileId = reader.GetInt64(5), IsActive = ReadBool(reader, 6) };
	private static FinanceInventoryValuationLayer ReadLayer(DbDataReader reader) => new() { Id = reader.GetInt64(0), AccountingBookId = Guid.Parse(reader.GetString(1)), ItemId = reader.GetInt64(2), SourceMovementId = reader.GetInt64(3), AcquiredDate = DateOnly.FromDateTime(Convert.ToDateTime(reader.GetValue(4), CultureInfo.InvariantCulture)), Currency = new CurrencyCode(reader.GetString(5)), OriginalQuantity = Convert.ToInt32(reader.GetValue(6), CultureInfo.InvariantCulture), RemainingQuantity = Convert.ToInt32(reader.GetValue(7), CultureInfo.InvariantCulture), UnitCost = ReadDecimal(reader, 8), CreatedAtUtc = Convert.ToDateTime(reader.GetValue(9), CultureInfo.InvariantCulture), CreatedByUserId = reader.IsDBNull(10) ? null : reader.GetInt64(10), ReversedAtUtc = reader.IsDBNull(11) ? null : Convert.ToDateTime(reader.GetValue(11), CultureInfo.InvariantCulture), ReversedByUserId = reader.IsDBNull(12) ? null : reader.GetInt64(12) };
	private static FinanceInventoryLandedCostOperation ReadLandedCost(DbDataReader reader) => new() { Id = reader.GetInt64(0), OperationId = Guid.Parse(reader.GetString(1)), RequestHash = reader.GetString(2), PostingDate = DateOnly.FromDateTime(Convert.ToDateTime(reader.GetValue(3), CultureInfo.InvariantCulture)), Currency = new CurrencyCode(reader.GetString(4)), Amount = ReadDecimal(reader, 5), AllocationMethod = (FinanceLandedCostAllocationMethod)Convert.ToInt32(reader.GetValue(6), CultureInfo.InvariantCulture), Reference = reader.IsDBNull(7) ? null : reader.GetString(7), JournalEntryId = reader.GetInt64(8), CreatedAtUtc = Convert.ToDateTime(reader.GetValue(9), CultureInfo.InvariantCulture), CreatedByUserId = reader.GetInt64(10), ReversalOperationId = reader.IsDBNull(11) ? null : Guid.Parse(reader.GetString(11)), ReversalJournalEntryId = reader.IsDBNull(12) ? null : reader.GetInt64(12), ReversedAtUtc = reader.IsDBNull(13) ? null : Convert.ToDateTime(reader.GetValue(13), CultureInfo.InvariantCulture), ReversedByUserId = reader.IsDBNull(14) ? null : reader.GetInt64(14) };
	private static FinanceInventoryPurchaseVariance ReadVariance(DbDataReader reader) => new() { Id = reader.GetInt64(0), SupplierDocumentId = reader.GetInt64(1), OperationId = Guid.Parse(reader.GetString(2)), Currency = new CurrencyCode(reader.GetString(3)), ExpectedNetAmount = ReadDecimal(reader, 4), ActualNetAmount = ReadDecimal(reader, 5), SignedVarianceAmount = ReadDecimal(reader, 6), JournalEntryId = reader.GetInt64(7), CreatedAtUtc = Convert.ToDateTime(reader.GetValue(8), CultureInfo.InvariantCulture), CreatedByUserId = reader.GetInt64(9), ReversalOperationId = reader.IsDBNull(10) ? null : Guid.Parse(reader.GetString(10)), ReversalJournalEntryId = reader.IsDBNull(11) ? null : reader.GetInt64(11), ReversedAtUtc = reader.IsDBNull(12) ? null : Convert.ToDateTime(reader.GetValue(12), CultureInfo.InvariantCulture), ReversedByUserId = reader.IsDBNull(13) ? null : reader.GetInt64(13) };
	private static FinanceInventoryReconciliationRun ReadRun(DbDataReader reader) => new() { Id = reader.GetInt64(0), OperationId = Guid.Parse(reader.GetString(1)), AccountingBookId = Guid.Parse(reader.GetString(2)), InventoryControlAccountId = Guid.Parse(reader.GetString(3)), AsOfDate = DateOnly.FromDateTime(Convert.ToDateTime(reader.GetValue(4), CultureInfo.InvariantCulture)), ReportingCurrency = new CurrencyCode(reader.GetString(5)), ValuationAmount = ReadDecimal(reader, 6), GeneralLedgerAmount = ReadDecimal(reader, 7), Difference = ReadDecimal(reader, 8), CreatedAtUtc = Convert.ToDateTime(reader.GetValue(9), CultureInfo.InvariantCulture), CreatedByUserId = reader.GetInt64(10) };
	private static bool ReadBool(DbDataReader reader, int ordinal) => Convert.ToBoolean(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
	private static decimal ReadDecimal(DbDataReader reader, int ordinal) => Convert.ToDecimal(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
}

internal sealed record FinanceInventoryValuationReportingRow(long LayerId, long ItemId, int Quantity, decimal CurrentUnitCost, CurrencyCode Currency, decimal ReceiptExchangeRate);
internal sealed record FinanceInventoryLandedReportingRow(long LayerId, decimal UnitCostIncrease, decimal ExchangeRate);
