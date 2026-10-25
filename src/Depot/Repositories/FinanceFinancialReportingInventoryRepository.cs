// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class FinanceFinancialReportingInventoryRepository : DatabaseRepository
{
	public FinanceFinancialReportingInventoryRepository(DatabaseAccess database) : base(database) { }

	internal async Task<IReadOnlyList<FinanceInventoryValuationSourceRow>> GetValuationAsync(DatabaseTransactionContext transaction, Guid bookId, DateOnly asOfDate, CancellationToken cancellationToken)
	{
		var balances = await FinanceInventoryReportingRepository.GetBalancesAsync(transaction, bookId, asOfDate, cancellationToken);
		return balances.Values.GroupBy(value => value.ItemId)
			.Select(group => new FinanceInventoryValuationSourceRow(group.Key, group.Sum(value => value.Quantity), group.Sum(value => value.ReportingAmount)))
			.OrderBy(value => value.ItemId).ToArray();
	}
}
