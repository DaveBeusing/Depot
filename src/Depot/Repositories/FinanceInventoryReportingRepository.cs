// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;

using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

internal static class FinanceInventoryReportingRepository
{
	internal static async Task<IReadOnlyDictionary<long, FinanceInventoryCarryingBalance>> GetBalancesAsync(
		DatabaseTransactionContext transaction, Guid bookId, DateOnly? asOfDate, CancellationToken cancellationToken, long? itemId = null)
	{
		var parameters = new[] { new DatabaseParameter("$Book", bookId.ToString("D")), new DatabaseParameter("$ItemId", itemId) };
		var layers = await transaction.Session.QueryAsync(
			"""
			SELECT l.Id,l.ItemId,l.OriginalQuantity,l.RemainingQuantity,l.CurrencyCode,
			       e.Amount,j.ReportingCurrencyCode,tc.MinorUnits,rc.MinorUnits,j.PostingDate,rj.PostingDate,l.ReversedAtUtc,
			       (SELECT COALESCE(SUM(x.TransactionDebit),0) FROM FinanceJournalEntryLines x WHERE x.JournalEntryId=j.Id),
			       (SELECT COALESCE(SUM(x.ReportingDebit),0) FROM FinanceJournalEntryLines x WHERE x.JournalEntryId=j.Id),
			       CASE WHEN e.AccountingBookId=l.AccountingBookId AND j.AccountingBookId=l.AccountingBookId
			                 AND e.ItemId=l.ItemId AND e.Quantity=l.OriginalQuantity AND e.CurrencyCode=l.CurrencyCode
			                 AND j.TransactionCurrencyCode=l.CurrencyCode AND j.ReportingCurrencyCode=b.ReportingCurrencyCode
			                 AND j.OperationId=e.OperationId AND j.SourceType='InventoryAccounting'
			                 AND ((e.Kind=1 AND j.SourceEvent='GoodsReceipt') OR (e.Kind=5 AND j.SourceEvent='InventoryAdjustment')) THEN 1 ELSE 0 END
			FROM FinanceInventoryValuationLayers l
			LEFT JOIN FinanceAccountingBooks b ON b.Id=l.AccountingBookId
			LEFT JOIN FinanceInventoryAccountingEvents e ON e.MovementId=l.SourceMovementId
			LEFT JOIN FinanceJournalEntries j ON j.Id=e.JournalEntryId
			LEFT JOIN FinanceCurrencies tc ON tc.Code=l.CurrencyCode
			LEFT JOIN FinanceCurrencies rc ON rc.Code=j.ReportingCurrencyCode
			LEFT JOIN FinanceJournalReversals r ON r.OriginalEntryId=j.Id
			LEFT JOIN FinanceJournalEntries rj ON rj.Id=r.ReversalEntryId
			WHERE l.AccountingBookId=$Book AND ($ItemId IS NULL OR l.ItemId=$ItemId) ORDER BY l.Id;
			""", ReadLayer, cancellationToken, parameters);
		var balances = new Dictionary<long, FinanceInventoryCarryingBalance>();
		foreach (var layer in layers)
		{
			if (layer.TransactionAmount != layer.JournalTransactionAmount || layer.TransactionAmount < 0m || layer.ReportingAmount < 0m || layer.OriginalQuantity <= 0 ||
				(layer.Reversed && layer.ReversalDate is null))
				throw new InvalidOperationException("Inventory layer does not have complete, matching original journal evidence.");
			if (!IsActive(layer.PostingDate, layer.ReversalDate, asOfDate)) continue;
			balances.Add(layer.Id, new FinanceInventoryCarryingBalance(layer.Id, layer.ItemId, layer.OriginalQuantity,
				layer.TransactionAmount, layer.ReportingAmount, layer.Currency, layer.ReportingCurrency,
				layer.TransactionMinorUnits, layer.ReportingMinorUnits, layer.PostingDate));
		}

		var landed = await transaction.Session.QueryAsync(
			"""
			SELECT a.Id,a.LandedCostOperationId,a.LayerId,a.Amount,o.Amount,j.PostingDate,rj.PostingDate,
			       j.ReportingCurrencyCode,o.ReversedAtUtc,
			       (SELECT COALESCE(SUM(x.TransactionDebit),0) FROM FinanceJournalEntryLines x WHERE x.JournalEntryId=j.Id),
			       (SELECT COALESCE(SUM(x.ReportingDebit),0) FROM FinanceJournalEntryLines x WHERE x.JournalEntryId=j.Id),
			       CASE WHEN j.AccountingBookId=l.AccountingBookId AND j.TransactionCurrencyCode=o.CurrencyCode
			                 AND o.CurrencyCode=l.CurrencyCode AND j.OperationId=o.OperationId AND j.PostingDate=o.PostingDate
			                 AND j.SourceType='InventoryAccounting' AND j.SourceEvent='LandedCost' THEN 1 ELSE 0 END
			FROM FinanceInventoryLandedCostAllocations a
			INNER JOIN FinanceInventoryValuationLayers l ON l.Id=a.LayerId
			INNER JOIN FinanceInventoryLandedCostOperations o ON o.Id=a.LandedCostOperationId
			LEFT JOIN FinanceJournalEntries j ON j.Id=o.JournalEntryId
			LEFT JOIN FinanceJournalEntries rj ON rj.Id=o.ReversalJournalEntryId
			WHERE l.AccountingBookId=$Book AND ($ItemId IS NULL OR o.Id IN
			    (SELECT a2.LandedCostOperationId FROM FinanceInventoryLandedCostAllocations a2 INNER JOIN FinanceInventoryValuationLayers l2 ON l2.Id=a2.LayerId WHERE l2.ItemId=$ItemId AND l2.AccountingBookId=$Book))
			ORDER BY a.LandedCostOperationId,a.LayerId,a.Id;
			""", ReadLanded, cancellationToken, parameters);
		foreach (var operation in landed.GroupBy(value => value.OperationId))
		{
			var first = operation.First();
			if (first.Amount != first.JournalTransactionAmount || operation.Sum(value => value.AllocationAmount) != first.Amount ||
				first.Amount <= 0 || (first.Reversed && first.ReversalDate is null))
				throw new InvalidOperationException("Landed cost does not have complete, matching original allocation and journal evidence.");
			var source = layers.First(value => operation.Any(allocation => allocation.LayerId == value.Id));
			var weights = operation.Select(value => value.AllocationAmount).ToArray();
			var transactionShares = Allocate(first.Amount, weights, source.TransactionMinorUnits);
			var shares = Allocate(first.ReportingAmount, weights, source.ReportingMinorUnits);
			var index = 0;
			foreach (var allocation in operation)
			{
				var transactionAmount = transactionShares[index];
				var reportingAmount = shares[index++];
				if (!layers.Any(value => value.Id == allocation.LayerId)) continue;
				balances.TryGetValue(allocation.LayerId, out var balance);
				if (balance is not null)
				{
					balance = IncludeEvidenceDate(balance, allocation.PostingDate, allocation.ReversalDate);
					balances[allocation.LayerId] = balance;
				}
				if (!IsActive(allocation.PostingDate, allocation.ReversalDate, asOfDate)) continue;
				if (balance is null)
					throw new InvalidOperationException("An active landed cost references an unavailable valuation layer. Reverse landed cost before its receipt.");
				if (balance.ReportingCurrency != allocation.ReportingCurrency)
					throw new InvalidOperationException("Landed-cost reporting currency differs from its valuation layer.");
				balances[allocation.LayerId] = balance with
				{
					TransactionAmount = balance.TransactionAmount + transactionAmount,
					ReportingAmount = balance.ReportingAmount + reportingAmount
				};
			}
		}

		var consumptions = await transaction.Session.QueryAsync(
			"""
			SELECT c.Id,c.MovementId,c.LayerId,c.Quantity,c.Amount,v.ReportingAmount,v.ReportingCurrencyCode,
			       j.PostingDate,rj.PostingDate,c.ReversedAtUtc,e.Amount,j.ReportingCurrencyCode,
			       (SELECT COALESCE(SUM(x.TransactionDebit),0) FROM FinanceJournalEntryLines x WHERE x.JournalEntryId=j.Id),
			       (SELECT COALESCE(SUM(x.ReportingDebit),0) FROM FinanceJournalEntryLines x WHERE x.JournalEntryId=j.Id),e.Quantity,
			       CASE WHEN e.AccountingBookId=l.AccountingBookId AND j.AccountingBookId=l.AccountingBookId
			                 AND e.ItemId=l.ItemId AND e.CurrencyCode=l.CurrencyCode
			                 AND j.TransactionCurrencyCode=l.CurrencyCode AND j.OperationId=e.OperationId
			                 AND j.SourceType='InventoryAccounting'
			                 AND ((e.Kind=2 AND j.SourceEvent='SalesShipment') OR (e.Kind=5 AND j.SourceEvent='InventoryAdjustment')) THEN 1 ELSE 0 END
			FROM FinanceInventoryValuationConsumptions c
			INNER JOIN FinanceInventoryValuationLayers l ON l.Id=c.LayerId
			LEFT JOIN FinanceInventoryReportingConsumptions v ON v.ConsumptionId=c.Id
			LEFT JOIN FinanceInventoryAccountingEvents e ON e.MovementId=c.MovementId
			LEFT JOIN FinanceJournalEntries j ON j.Id=e.JournalEntryId
			LEFT JOIN FinanceJournalReversals r ON r.OriginalEntryId=j.Id
			LEFT JOIN FinanceJournalEntries rj ON rj.Id=r.ReversalEntryId
			WHERE l.AccountingBookId=$Book AND ($ItemId IS NULL OR l.ItemId=$ItemId) ORDER BY c.MovementId,c.LayerId,c.Id;
			""", ReadConsumption, cancellationToken, parameters);
		foreach (var movement in consumptions.GroupBy(value => value.MovementId))
		{
			var first = movement.First();
			if (movement.Sum(value => value.Amount) != first.EventAmount || first.EventAmount != first.JournalTransactionAmount ||
				movement.Sum(value => value.Quantity) != -first.EventQuantity || movement.Any(value => value.Quantity <= 0 || value.Amount < 0m) ||
				movement.Any(value => value.Reversed && value.ReversalDate is null))
				throw new InvalidOperationException("Inventory consumption does not have complete, matching original journal evidence.");
			var recorded = movement.All(value => value.ReportingAmount.HasValue);
			if (!recorded && movement.Any(value => value.ReportingAmount.HasValue))
				throw new InvalidOperationException("Inventory movement contains incomplete historical reporting evidence.");
			var source = layers.Single(value => value.Id == first.LayerId);
			var shares = recorded
				? movement.Select(value => value.ReportingAmount.GetValueOrDefault()).ToArray()
				: Allocate(first.JournalReportingAmount, movement.Select(value => value.Amount).ToArray(), source.ReportingMinorUnits);
			if (shares.Sum() != first.JournalReportingAmount || shares.Any(value => value < 0m || value != decimal.Round(value, source.ReportingMinorUnits, MidpointRounding.AwayFromZero)))
				throw new InvalidOperationException("Inventory consumption reporting values do not agree with the immutable journal.");
			var index = 0;
			foreach (var consumption in movement)
			{
				var reportingAmount = shares[index++];
				balances.TryGetValue(consumption.LayerId, out var balance);
				if (balance is not null)
				{
					balance = IncludeEvidenceDate(balance, consumption.PostingDate, consumption.ReversalDate);
					balances[consumption.LayerId] = balance;
				}
				if (!IsActive(consumption.PostingDate, consumption.ReversalDate, asOfDate)) continue;
				if (balance is null)
					throw new InvalidOperationException("An active inventory consumption references an unavailable valuation layer.");
				if (consumption.JournalCurrency != balance.ReportingCurrency ||
					(consumption.ReportingCurrency is not null && consumption.ReportingCurrency != balance.ReportingCurrency))
					throw new InvalidOperationException("Inventory consumption reporting currency differs from its valuation layer.");
				balances[consumption.LayerId] = balance with
				{
					Quantity = balance.Quantity - consumption.Quantity,
					TransactionAmount = balance.TransactionAmount - consumption.Amount,
					ReportingAmount = balance.ReportingAmount - reportingAmount
				};
			}
		}
		if (asOfDate is null)
			foreach (var layer in layers.Where(value => !value.Reversed))
				if (balances[layer.Id].Quantity != layer.RemainingQuantity)
					throw new InvalidOperationException("Inventory layer quantity differs from its immutable consumption evidence.");
		return balances;
	}

	internal static Task<int> RecordAsync(DatabaseTransactionContext transaction, long consumptionId,
		CurrencyCode currency, decimal amount, CancellationToken cancellationToken) =>
		transaction.Session.ExecuteAsync(
			"INSERT INTO FinanceInventoryReportingConsumptions (ConsumptionId,ReportingCurrencyCode,ReportingAmount) VALUES ($Id,$Currency,$Amount);",
			cancellationToken, new DatabaseParameter("$Id", consumptionId), new DatabaseParameter("$Currency", currency.Value), new DatabaseParameter("$Amount", amount));

	internal static decimal[] Allocate(decimal amount, IReadOnlyList<decimal> weights, int minorUnits)
	{
		if (amount < 0m || weights.Any(value => value < 0m)) throw new InvalidOperationException("Inventory carrying values and allocation weights cannot be negative.");
		var total = weights.Sum();
		if (total == 0m)
		{
			if (amount != 0m) throw new InvalidOperationException("A non-zero carrying value cannot be allocated without source amounts.");
			return new decimal[weights.Count];
		}
		var result = new decimal[weights.Count];
		decimal cumulativeWeight = 0m, allocated = 0m;
		for (var index = 0; index < weights.Count; index++)
		{
			cumulativeWeight += weights[index];
			var cumulative = index == weights.Count - 1 ? amount : decimal.Round(amount * cumulativeWeight / total, minorUnits, MidpointRounding.AwayFromZero);
			result[index] = cumulative - allocated;
			allocated = cumulative;
		}
		return result;
	}

	private static FinanceInventoryCarryingBalance IncludeEvidenceDate(FinanceInventoryCarryingBalance balance, DateOnly posted, DateOnly? reversed)
	{
		var latest = reversed is DateOnly reversalDate && reversalDate > posted ? reversalDate : posted;
		return latest > balance.LatestEvidenceDate ? balance with { LatestEvidenceDate = latest } : balance;
	}

	private static bool IsActive(DateOnly posted, DateOnly? reversed, DateOnly? date) =>
		date is null ? reversed is null : posted <= date.Value && (reversed is null || reversed.Value > date.Value);
	private static decimal Decimal(DbDataReader reader, int index) => Convert.ToDecimal(reader.GetValue(index), CultureInfo.InvariantCulture);
	private static DateOnly Date(DbDataReader reader, int index) => DateOnly.FromDateTime(Convert.ToDateTime(reader.GetValue(index), CultureInfo.InvariantCulture));
	private static DateOnly? NullableDate(DbDataReader reader, int index) => reader.IsDBNull(index) ? null : Date(reader, index);
	private static LayerRow ReadLayer(DbDataReader reader)
	{
		if (Enumerable.Range(5, 5).Any(reader.IsDBNull) || Convert.ToInt32(reader.GetValue(14), CultureInfo.InvariantCulture) != 1) throw new InvalidOperationException("Inventory receipt journal or currency evidence is missing.");
		return new LayerRow(reader.GetInt64(0), reader.GetInt64(1), Convert.ToInt32(reader.GetValue(2), CultureInfo.InvariantCulture),
			Convert.ToInt32(reader.GetValue(3), CultureInfo.InvariantCulture), new CurrencyCode(reader.GetString(4)), Decimal(reader, 5),
			new CurrencyCode(reader.GetString(6)), Convert.ToInt32(reader.GetValue(7), CultureInfo.InvariantCulture), Convert.ToInt32(reader.GetValue(8), CultureInfo.InvariantCulture),
			Date(reader, 9), NullableDate(reader, 10), !reader.IsDBNull(11), Decimal(reader, 12), Decimal(reader, 13));
	}
	private static ConsumptionRow ReadConsumption(DbDataReader reader)
	{
		if (reader.IsDBNull(7) || reader.IsDBNull(10) || reader.IsDBNull(11) || Convert.ToInt32(reader.GetValue(15), CultureInfo.InvariantCulture) != 1) throw new InvalidOperationException("Inventory consumption journal evidence is missing.");
		return new ConsumptionRow(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), Convert.ToInt32(reader.GetValue(3), CultureInfo.InvariantCulture),
			Decimal(reader, 4), reader.IsDBNull(5) ? null : Decimal(reader, 5), reader.IsDBNull(6) ? null : new CurrencyCode(reader.GetString(6)),
			Date(reader, 7), NullableDate(reader, 8), !reader.IsDBNull(9), Decimal(reader, 10), new CurrencyCode(reader.GetString(11)), Decimal(reader, 12), Decimal(reader, 13), Convert.ToInt32(reader.GetValue(14), CultureInfo.InvariantCulture));
	}
	private static LandedRow ReadLanded(DbDataReader reader)
	{
		if (reader.IsDBNull(5) || reader.IsDBNull(7) || Convert.ToInt32(reader.GetValue(11), CultureInfo.InvariantCulture) != 1) throw new InvalidOperationException("Landed-cost journal or currency evidence is missing or inconsistent.");
		return new LandedRow(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), Decimal(reader, 3),
			Decimal(reader, 4), Date(reader, 5), NullableDate(reader, 6), new CurrencyCode(reader.GetString(7)),
			!reader.IsDBNull(8), Decimal(reader, 9), Decimal(reader, 10));
	}
	private sealed record LayerRow(long Id, long ItemId, int OriginalQuantity, int RemainingQuantity, CurrencyCode Currency, decimal TransactionAmount,
		CurrencyCode ReportingCurrency, int TransactionMinorUnits, int ReportingMinorUnits, DateOnly PostingDate, DateOnly? ReversalDate, bool Reversed,
		decimal JournalTransactionAmount, decimal ReportingAmount);
	private sealed record LandedRow(long Id, long OperationId, long LayerId, decimal AllocationAmount, decimal Amount, DateOnly PostingDate,
		DateOnly? ReversalDate, CurrencyCode ReportingCurrency, bool Reversed, decimal JournalTransactionAmount, decimal ReportingAmount);
	private sealed record ConsumptionRow(long Id, long MovementId, long LayerId, int Quantity, decimal Amount, decimal? ReportingAmount,
		CurrencyCode? ReportingCurrency, DateOnly PostingDate, DateOnly? ReversalDate, bool Reversed, decimal EventAmount, CurrencyCode JournalCurrency,
		decimal JournalTransactionAmount, decimal JournalReportingAmount, int EventQuantity);
}

internal sealed record FinanceInventoryCarryingBalance(long LayerId, long ItemId, int Quantity, decimal TransactionAmount,
	decimal ReportingAmount, CurrencyCode Currency, CurrencyCode ReportingCurrency, int TransactionMinorUnits, int ReportingMinorUnits, DateOnly LatestEvidenceDate)
{
	internal (decimal Transaction, decimal Reporting) Consume(int quantity, DateOnly postingDate)
	{
		if (quantity <= 0 || quantity > Quantity || TransactionAmount < 0m || ReportingAmount < 0m || postingDate < LatestEvidenceDate)
			throw new InvalidOperationException("Inventory consumption cannot be allocated from the recorded historical carrying balance.");
		if (quantity == Quantity) return (TransactionAmount, ReportingAmount);
		return (decimal.Round(TransactionAmount * quantity / Quantity, TransactionMinorUnits, MidpointRounding.AwayFromZero),
			decimal.Round(ReportingAmount * quantity / Quantity, ReportingMinorUnits, MidpointRounding.AwayFromZero));
	}
}
