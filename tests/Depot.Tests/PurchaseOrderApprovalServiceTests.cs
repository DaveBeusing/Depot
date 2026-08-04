// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;
using Depot.Services;
using Depot.ViewModels;

using Xunit;

namespace Depot.Tests;

public sealed class PurchaseOrderApprovalServiceTests
{
	[Fact]
	public async Task ApproverCanApproveAnOrderCreatedByAnotherUser()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var pending = await CreatePendingAsync(context);
		context.SignInApprover();

		var approved = await context.Approvals.ApproveAsync(pending.Id, pending.Version, "Reviewed");

		Assert.Equal(PurchaseOrderStatus.Approved, approved.Status);
		Assert.Equal(context.ApproverUserId, approved.ApprovalDecisionByUserId);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task ApproverCannotDecideAnOrderTheyCreated(bool approve)
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		context.SignInApproverWithPurchasingPermissions();
		var pending = await CreatePendingAsync(context);

		Assert.False(context.Approvals.CanDecide(pending.CreatedByUserId));
		var error = approve
			? await Assert.ThrowsAsync<InvalidOperationException>(() => context.Approvals.ApproveAsync(pending.Id, pending.Version, null))
			: await Assert.ThrowsAsync<InvalidOperationException>(() => context.Approvals.RejectAsync(pending.Id, pending.Version, "Not acceptable"));

		Assert.Contains("creator", error.Message, StringComparison.OrdinalIgnoreCase);
		Assert.Equal(PurchaseOrderStatus.PendingApproval, (await context.Orders.GetByIdAsync(pending.Id))?.Status);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task AdministratorCanDecideAnOrderTheyCreated(bool approve)
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var pending = await CreatePendingAsync(context);

		Assert.True(context.Approvals.CanDecide(pending.CreatedByUserId));
		var decided = approve
			? await context.Approvals.ApproveAsync(pending.Id, pending.Version, "Administrator approval")
			: await context.Approvals.RejectAsync(pending.Id, pending.Version, "Administrator rejection");

		Assert.Equal(approve ? PurchaseOrderStatus.Approved : PurchaseOrderStatus.Rejected, decided.Status);
		Assert.Equal(pending.CreatedByUserId, decided.ApprovalDecisionByUserId);
	}

	[Fact]
	public async Task ApprovalViewModelEnablesAdministratorDecisionForAnOwnOrder()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var pending = await CreatePendingAsync(context);
		using var viewModel = new PurchaseOrderApprovalsViewModel(context.Approvals);
		await viewModel.LoadAsync();
		viewModel.SelectedApproval = viewModel.Approvals.Single(order => order.Id == pending.Id);

		Assert.True(viewModel.CanDecideSelected);
		Assert.True(viewModel.ApproveCommand.CanExecute(null));
		Assert.True(viewModel.RejectCommand.CanExecute(null));
	}

	[Fact]
	public async Task UserWithoutApprovalPermissionCannotDecideAnOrder()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var pending = await CreatePendingAsync(context);
		context.Authorization.SignIn(new User { Id = 800001, IsActive = true }, [ApplicationPermission.PurchaseOrdersView]);

		await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
			context.Approvals.ApproveAsync(pending.Id, pending.Version, null));
		await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
			context.Approvals.RejectAsync(pending.Id, pending.Version, null));
	}

	[Fact]
	public async Task AdministratorSelfApprovalRemainsAtomicAndIdempotent()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var pending = await CreatePendingAsync(context);
		var operationId = Guid.NewGuid();

		var approved = await context.Approvals.ApproveAsync(pending.Id, pending.Version, "Approved once", operationId);
		var retry = await context.Approvals.ApproveAsync(pending.Id, pending.Version, "Approved once", operationId);

		Assert.Equal(approved.Version, retry.Version);
		Assert.Equal(1, await context.ScalarAsync(
			"SELECT COUNT(*) FROM AuditEntries WHERE EntityType = 'PurchaseOrder' AND EntityId = $Id AND Action = 'Updated' AND AfterJson LIKE '%\"status\":7%';",
			new Depot.Data.DatabaseParameter("$Id", pending.Id)));
		Assert.Equal(1, await context.ScalarAsync(
			"SELECT COUNT(*) FROM WorkflowOperations WHERE OperationId = $OperationId;",
			new Depot.Data.DatabaseParameter("$OperationId", operationId.ToString("D"))));
	}

	[Fact]
	public async Task ConcurrentApprovalDecisionsAllowOnlyOneResult()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var pending = await CreatePendingAsync(context);

		var results = await Task.WhenAll(
			TryDecideAsync(() => context.Approvals.ApproveAsync(pending.Id, pending.Version, null, Guid.NewGuid())),
			TryDecideAsync(() => context.Approvals.RejectAsync(pending.Id, pending.Version, null, Guid.NewGuid())));

		Assert.Single(results, result => result);
		var current = await context.Orders.GetByIdAsync(pending.Id) ?? throw new InvalidOperationException();
		Assert.Contains(current.Status, new[] { PurchaseOrderStatus.Approved, PurchaseOrderStatus.Rejected });
		Assert.Equal(1, await context.ScalarAsync(
			"SELECT COUNT(*) FROM AuditEntries WHERE EntityType = 'PurchaseOrder' AND EntityId = $Id AND Action = 'Updated' AND (AfterJson LIKE '%\"status\":7%' OR AfterJson LIKE '%\"status\":9%');",
			new Depot.Data.DatabaseParameter("$Id", pending.Id)));
	}

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

	private static async Task<PurchaseOrder> CreatePendingAsync(ProcurementTestContext context)
	{
		var draft = await context.Orders.SaveDraftAsync(context.NewOrder());
		return await context.Orders.SubmitForApprovalAsync(draft.Id, draft.Version);
	}

	private static async Task<bool> TryDecideAsync(Func<Task<PurchaseOrder>> decision)
	{
		try
		{
			await decision();
			return true;
		}
		catch (ConcurrencyConflictException)
		{
			return false;
		}
		catch (InvalidOperationException)
		{
			return false;
		}
	}
}
