// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;
using Depot.ViewModels;

using Xunit;

namespace Depot.Tests;

public sealed class PurchaseOrderApprovalServiceTests
{
	[Fact]
	public async Task WorkQueueReturnsOnlyPendingOrdersWithFiltersSummaryAndHistory()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var first = await context.Orders.SaveDraftAsync(context.NewOrder(quantity: 2, unitPrice: 10m));
		var ignoredDraft = await context.Orders.SaveDraftAsync(context.NewOrder(quantity: 5, unitPrice: 7m));
		first = await context.Orders.SubmitForApprovalAsync(first.Id, first.Version);

		var result = await context.Approvals.SearchAsync(
			new PurchaseOrderApprovalFilter(
				first.OrderNumber,
				"Test Supplier",
				context.Authorization.CurrentUser?.DisplayName,
				DateTime.UtcNow.AddDays(-1),
				DateTime.UtcNow.AddDays(1)),
			1,
			10);

		var item = Assert.Single(result.Page.Items);
		Assert.Equal(first.Id, item.Id);
		Assert.Equal(20m, item.TotalAmount);
		Assert.Equal(1, result.Summary.OpenCount);
		Assert.Equal(20m, result.Summary.TotalAmount);
		Assert.NotNull(result.Summary.OldestSubmittedAtUtc);
		Assert.DoesNotContain(result.Page.Items, order => order.Id == ignoredDraft.Id);

		var details = await context.Approvals.GetDetailsAsync(first.Id);
		Assert.NotNull(details);
		Assert.Single(details.Order.Lines);
		Assert.Equal(20m, details.TotalAmount);
		Assert.Contains(details.History, entry => entry.StatusChange.Contains("Pending Approval", StringComparison.Ordinal));
	}

	[Fact]
	public async Task WorkQueueIsSortedBySubmissionAndPagedOnTheServer()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var first = await context.Orders.SaveDraftAsync(context.NewOrder());
		first = await context.Orders.SubmitForApprovalAsync(first.Id, first.Version);
		await Task.Delay(20);
		var second = await context.Orders.SaveDraftAsync(context.NewOrder(itemId: context.SecondItemId));
		second = await context.Orders.SubmitForApprovalAsync(second.Id, second.Version);
		var filter = new PurchaseOrderApprovalFilter(null, null, null, null, null);

		var firstPage = await context.Approvals.SearchAsync(filter, 1, 1);
		var secondPage = await context.Approvals.SearchAsync(filter, 2, 1);

		Assert.Equal(2, firstPage.Page.TotalCount);
		Assert.Equal(first.Id, Assert.Single(firstPage.Page.Items).Id);
		Assert.Equal(second.Id, Assert.Single(secondPage.Page.Items).Id);
	}

	[Fact]
	public async Task ApprovalViewModelRemovesOnlyTheDecidedWorkItem()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var first = await context.Orders.SaveDraftAsync(context.NewOrder());
		await context.Orders.SubmitForApprovalAsync(first.Id, first.Version);
		var second = await context.Orders.SaveDraftAsync(context.NewOrder(itemId: context.SecondItemId));
		await context.Orders.SubmitForApprovalAsync(second.Id, second.Version);
		context.SignInApprover();
		using var viewModel = new PurchaseOrderApprovalsViewModel(context.Approvals);
		await viewModel.LoadAsync();
		var selected = viewModel.Approvals.First();
		viewModel.SelectedApproval = selected;

		await viewModel.ApproveCommand.ExecuteAsync();

		Assert.DoesNotContain(viewModel.Approvals, item => item.Id == selected.Id);
		Assert.Single(viewModel.Approvals);
		Assert.Equal(1, viewModel.Summary.OpenCount);
	}

	[Fact]
	public async Task WorkQueueRejectsUsersWithoutApprovalPermission()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		context.Authorization.SignIn(new User
		{
			Id = context.ApproverUserId,
			Email = "ordinary-user@depot.test",
			DisplayName = "Ordinary User",
			IsActive = true
		});

		await Assert.ThrowsAsync<UnauthorizedAccessException>(() => context.Approvals.SearchAsync(
			new PurchaseOrderApprovalFilter(null, null, null, null, null), 1, 50));
	}
}
