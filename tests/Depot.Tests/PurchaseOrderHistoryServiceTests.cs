// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;
using Depot.Repositories;
using Depot.Services;

using Xunit;

namespace Depot.Tests;

public sealed class PurchaseOrderHistoryServiceTests
{
	[Fact]
	public async Task HistoryReturnsChronologicalAuditStatusChangesForViewAuthorizedUsers()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var draft = await context.Orders.SaveDraftAsync(context.NewOrder());
		var pending = await context.Orders.SubmitForApprovalAsync(draft.Id, draft.Version);
		var service = new PurchaseOrderHistoryService(
			new AuditRepository(context.Data),
			context.Authorization,
			new AuditJsonSanitizer());

		var history = await service.GetHistoryAsync(pending.Id);

		Assert.Contains(history, item =>
			item.PreviousStatus == nameof(PurchaseOrderStatus.Draft) &&
			item.NewStatus == "Pending Approval");
		Assert.Contains(history, item =>
			item.PreviousStatus == "—" &&
			item.NewStatus == nameof(PurchaseOrderStatus.Draft));
		Assert.All(history, item => Assert.False(string.IsNullOrWhiteSpace(item.ChangedBy)));
	}
}
