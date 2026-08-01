// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Services;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class MaterialIssueTests
{
	[Fact]
	public async Task DraftCanBeSavedEditedAndCancelled()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var issue = await context.MaterialIssues.SaveDraftAsync(await NewIssueAsync(context, 2));
		Assert.Matches("^MI-[0-9]{6}$", issue.IssueNumber);
		Assert.Equal(MaterialIssueStatus.Draft, issue.Status);
		issue.Recipient = "Material issue test updated recipient";
		issue = await context.MaterialIssues.SaveDraftAsync(issue);
		var cancelled = await context.MaterialIssues.CancelAsync(issue.Id, issue.Version);
		Assert.Equal(MaterialIssueStatus.Cancelled, cancelled.Status);
		await Assert.ThrowsAsync<InvalidOperationException>(() => context.MaterialIssues.PostMaterialIssueAsync(cancelled.Id, cancelled.Version));
	}

	[Fact]
	public async Task PostingCreatesOneWithdrawalPerLineAndUpdatesStatusAtomically()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		await AddStockAsync(context, context.InventoryId, 10);
		await AddStockAsync(context, context.SecondInventoryId, 8);
		var issue = await NewIssueAsync(context, 4);
		issue.Lines = [.. issue.Lines, new MaterialIssueLine { InventoryId = context.SecondInventoryId, Quantity = 3, ReasonCodeId = issue.Lines[0].ReasonCodeId, Notes = "Second line" }];
		issue = await context.MaterialIssues.SaveDraftAsync(issue);

		var posted = await context.MaterialIssues.PostMaterialIssueAsync(issue.Id, issue.Version);

		Assert.Equal(MaterialIssueStatus.Posted, posted.Status);
		Assert.Equal(context.Authorization.CurrentUser?.Id, posted.PostedByUserId);
		Assert.NotNull(posted.PostedAtUtc);
		Assert.Equal(2, await context.ScalarAsync("SELECT COUNT(*) FROM StockMovements WHERE Reference = $Reference AND MovementType = $Type;", new DatabaseParameter("$Reference", $"Material Issue {issue.IssueNumber}"), new DatabaseParameter("$Type", (int)StockMovementType.Withdrawal)));
		Assert.Equal(6, await CurrentStockAsync(context, context.InventoryId));
		Assert.Equal(5, await CurrentStockAsync(context, context.SecondInventoryId));
		Assert.Equal(1, await context.ScalarAsync("SELECT COUNT(*) FROM AuditEntries WHERE EntityType = 'MaterialIssue' AND EntityId = $Id AND Action = 'Updated' AND AfterJson LIKE '%postedAtUtc%';", new DatabaseParameter("$Id", issue.Id)));
	}

	[Fact]
	public async Task InsufficientStockRollsBackMovementsStatusAndAudit()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		await AddStockAsync(context, context.InventoryId, 2);
		var issue = await context.MaterialIssues.SaveDraftAsync(await NewIssueAsync(context, 3));
		var auditBefore = await context.ScalarAsync("SELECT COUNT(*) FROM AuditEntries WHERE EntityType = 'MaterialIssue' AND EntityId = $Id;", new DatabaseParameter("$Id", issue.Id));

		await Assert.ThrowsAsync<InsufficientStockException>(() => context.MaterialIssues.PostMaterialIssueAsync(issue.Id, issue.Version));

		Assert.Equal(MaterialIssueStatus.Draft, (await context.MaterialIssues.GetByIdAsync(issue.Id))?.Status);
		Assert.Equal(0, await context.ScalarAsync("SELECT COUNT(*) FROM StockMovements WHERE Reference = $Reference;", new DatabaseParameter("$Reference", $"Material Issue {issue.IssueNumber}")));
		Assert.Equal(auditBefore, await context.ScalarAsync("SELECT COUNT(*) FROM AuditEntries WHERE EntityType = 'MaterialIssue' AND EntityId = $Id;", new DatabaseParameter("$Id", issue.Id)));
	}

	[Fact]
	public async Task AuditFailureRollsBackPosting()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		await AddStockAsync(context, context.InventoryId, 10);
		var issue = await context.MaterialIssues.SaveDraftAsync(await NewIssueAsync(context, 2));
		await context.Data.ExecuteAsync("CREATE TRIGGER FailMaterialIssuePostAudit BEFORE INSERT ON AuditEntries WHEN NEW.EntityType = 'MaterialIssue' BEGIN SELECT RAISE(ABORT, 'forced material issue audit failure'); END;", CancellationToken.None);

		await Assert.ThrowsAsync<SqliteException>(() => context.MaterialIssues.PostMaterialIssueAsync(issue.Id, issue.Version));

		Assert.Equal(MaterialIssueStatus.Draft, (await context.MaterialIssues.GetByIdAsync(issue.Id))?.Status);
		Assert.Equal(0, await context.ScalarAsync("SELECT COUNT(*) FROM StockMovements WHERE Reference = $Reference;", new DatabaseParameter("$Reference", $"Material Issue {issue.IssueNumber}")));
	}

	[Fact]
	public async Task PostedIssueCanBeReversedExactlyOnce()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		await AddStockAsync(context, context.InventoryId, 10);
		var issue = await context.MaterialIssues.SaveDraftAsync(await NewIssueAsync(context, 4));
		var posted = await context.MaterialIssues.PostMaterialIssueAsync(issue.Id, issue.Version);
		var reasonId = await ReasonIdAsync(context, ReasonCodeSystemCodes.Returned);

		var reversed = await context.MaterialIssues.ReverseAsync(posted.Id, posted.Version, reasonId, "Issued to the wrong recipient");

		Assert.Equal(MaterialIssueStatus.Reversed, reversed.Status);
		Assert.Equal(10, await CurrentStockAsync(context, context.InventoryId));
		Assert.Equal(1, await context.ScalarAsync("SELECT COUNT(*) FROM StockMovements WHERE ReversalOfMovementId IS NOT NULL AND Reference = $Reference;", new DatabaseParameter("$Reference", $"Material Issue {issue.IssueNumber}")));
		await Assert.ThrowsAnyAsync<InvalidOperationException>(() => context.MaterialIssues.ReverseAsync(reversed.Id, reversed.Version, reasonId, "Duplicate"));
	}

	[Fact]
	public async Task PostingHonorsOptimisticConcurrencyAndLineValidation()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		await AddStockAsync(context, context.InventoryId, 10);
		var invalid = await NewIssueAsync(context, 1); invalid.Lines[0].ReasonCodeId = 0;
		await Assert.ThrowsAsync<InvalidOperationException>(() => context.MaterialIssues.SaveDraftAsync(invalid));
		var issue = await context.MaterialIssues.SaveDraftAsync(await NewIssueAsync(context, 2));
		await context.MaterialIssues.PostMaterialIssueAsync(issue.Id, issue.Version);
		await Assert.ThrowsAsync<ConcurrencyConflictException>(() => context.MaterialIssues.PostMaterialIssueAsync(issue.Id, issue.Version));
		Assert.Equal(1, await context.ScalarAsync("SELECT COUNT(*) FROM StockMovements WHERE Reference = $Reference AND ReversalOfMovementId IS NULL;", new DatabaseParameter("$Reference", $"Material Issue {issue.IssueNumber}")));
	}

	private static async Task<MaterialIssue> NewIssueAsync(ProcurementTestContext context, int quantity) => new()
	{
		IssueDate = DateTime.Today,
		Recipient = $"Material issue test {Guid.NewGuid():N}",
		Reference = "SERVICE-42",
		Notes = "Automated material issue test",
		Lines = [new MaterialIssueLine { InventoryId = context.InventoryId, Quantity = quantity, ReasonCodeId = await ReasonIdAsync(context, ReasonCodeSystemCodes.Consumed), Notes = "Consumed by test" }]
	};

	private static Task<long> ReasonIdAsync(ProcurementTestContext context, string code) => context.ScalarAsync("SELECT Id FROM ReasonCodes WHERE Code = $Code;", new DatabaseParameter("$Code", code));
	private static async Task AddStockAsync(ProcurementTestContext context, long inventoryId, int quantity) => await context.Data.InsertAsync("INSERT INTO StockMovements (InventoryId, MovementType, TimestampUtc, Quantity, Reference) VALUES ($InventoryId, $Type, $TimestampUtc, $Quantity, 'MATERIAL-ISSUE-TEST-STOCK');", CancellationToken.None, new DatabaseParameter("$InventoryId", inventoryId), new DatabaseParameter("$Type", (int)StockMovementType.OpeningBalance), new DatabaseParameter("$TimestampUtc", DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture)), new DatabaseParameter("$Quantity", quantity));
	private static Task<long> CurrentStockAsync(ProcurementTestContext context, long inventoryId) => context.ScalarAsync("SELECT COALESCE(SUM(Quantity), 0) FROM StockMovements WHERE InventoryId = $InventoryId;", new DatabaseParameter("$InventoryId", inventoryId));
}
