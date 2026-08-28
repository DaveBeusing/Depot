// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;
using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class FinanceFinancialReportingInventoryRepository : DatabaseRepository
{
	public FinanceFinancialReportingInventoryRepository(DatabaseAccess database) : base(database) { }

	internal async Task<IReadOnlyList<FinanceInventoryValuationSourceRow>> GetValuationAsync(DatabaseTransactionContext transaction, Guid bookId, DateOnly asOfDate, CancellationToken cancellationToken)
	{
		var cutoff = asOfDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);
		const string sql = "SELECT l.Id,l.ItemId,l.OriginalQuantity-COALESCE(SUM(CASE WHEN c.CreatedAtUtc<=$Cutoff AND (c.ReversedAtUtc IS NULL OR c.ReversedAtUtc>$Cutoff) THEN c.Quantity ELSE 0 END),0),l.UnitCost,j.ExchangeRate FROM FinanceInventoryValuationLayers l INNER JOIN FinanceInventoryAccountingEvents e ON e.MovementId=l.SourceMovementId INNER JOIN FinanceJournalEntries j ON j.Id=e.JournalEntryId LEFT JOIN FinanceInventoryValuationConsumptions c ON c.LayerId=l.Id WHERE l.AccountingBookId=$Book AND l.AcquiredDate<=$Date AND (l.ReversedAtUtc IS NULL OR l.ReversedAtUtc>$Cutoff) GROUP BY l.Id,l.ItemId,l.OriginalQuantity,l.UnitCost,j.ExchangeRate ORDER BY l.ItemId,l.Id;";
		var layers = await transaction.Session.QueryAsync(sql, reader => new Layer(reader.GetInt64(0), reader.GetInt64(1), Convert.ToInt32(reader.GetValue(2), CultureInfo.InvariantCulture), ReadDecimal(reader, 3), ReadDecimal(reader, 4)), cancellationToken, Parameter("$Book", bookId.ToString("D")), Parameter("$Date", asOfDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), Parameter("$Cutoff", cutoff));
		const string landedSql = "SELECT a.LayerId,a.UnitCostIncrease,j.ExchangeRate FROM FinanceInventoryLandedCostAllocations a INNER JOIN FinanceInventoryLandedCostOperations o ON o.Id=a.LandedCostOperationId INNER JOIN FinanceJournalEntries j ON j.Id=o.JournalEntryId INNER JOIN FinanceInventoryValuationLayers l ON l.Id=a.LayerId WHERE l.AccountingBookId=$Book AND o.PostingDate<=$Date AND (o.ReversedAtUtc IS NULL OR o.ReversedAtUtc>$Cutoff);";
		var landed = await transaction.Session.QueryAsync(landedSql, reader => new Landed(reader.GetInt64(0), ReadDecimal(reader, 1), ReadDecimal(reader, 2)), cancellationToken, Parameter("$Book", bookId.ToString("D")), Parameter("$Date", asOfDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), Parameter("$Cutoff", cutoff));
		var landedByLayer = landed.GroupBy(value => value.LayerId).ToDictionary(group => group.Key, group => group.Sum(value => value.UnitCostIncrease * value.ExchangeRate));
		return layers.GroupBy(value => value.ItemId).Select(group => new FinanceInventoryValuationSourceRow(group.Key, group.Sum(value => value.Quantity), group.Sum(value => value.Quantity * ((value.UnitCost * value.ExchangeRate) + landedByLayer.GetValueOrDefault(value.LayerId))))).OrderBy(value => value.ItemId).ToArray();
	}

	private static decimal ReadDecimal(DbDataReader reader, int ordinal) => Convert.ToDecimal(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
	private sealed record Layer(long LayerId, long ItemId, int Quantity, decimal UnitCost, decimal ExchangeRate);
	private sealed record Landed(long LayerId, decimal UnitCostIncrease, decimal ExchangeRate);
}
