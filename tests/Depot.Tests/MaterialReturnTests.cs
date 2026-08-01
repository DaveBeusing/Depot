// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Services;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class MaterialReturnTests
{
	[Fact]
	public async Task FreeReturnRequiresBusinessExplanationAndCanBeCancelledAsDraft()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var invalid = await NewReturnAsync(context, 2); invalid.Reference = null; invalid.Notes = null;
		await Assert.ThrowsAsync<ArgumentException>(() => context.MaterialReturns.SaveDraftAsync(invalid));

		var saved = await context.MaterialReturns.SaveDraftAsync(await NewReturnAsync(context, 2));
		Assert.Matches("^MR-[0-9]{6}$", saved.ReturnNumber);
		Assert.Equal(MaterialReturnStatus.Draft, saved.Status);
		var cancelled = await context.MaterialReturns.CancelAsync(saved.Id, saved.Version);
		Assert.Equal(MaterialReturnStatus.Cancelled, cancelled.Status);
	}

	[Fact]
	public async Task ReturnCanReferencePostedMaterialIssueButNotDraftIssue()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var issueReason = await ReasonIdAsync(context, ReasonCodeSystemCodes.Consumed);
		var issue = await context.MaterialIssues.SaveDraftAsync(new MaterialIssue { Recipient = $"Material issue test {Guid.NewGuid():N}", Reference = "ORIGINAL", Lines = [new MaterialIssueLine { InventoryId = context.InventoryId, Quantity = 1, ReasonCodeId = issueReason }] });
		var value = await NewReturnAsync(context, 1); value.OriginalMaterialIssueId = issue.Id;
		await Assert.ThrowsAsync<InvalidOperationException>(() => context.MaterialReturns.SaveDraftAsync(value));

		await AddStockAsync(context, 2);
		issue = await context.MaterialIssues.PostAsync(issue.Id, issue.Version);
		value = await NewReturnAsync(context, 1); value.OriginalMaterialIssueId = issue.Id; value.Reference = null; value.Notes = null;
		var saved = await context.MaterialReturns.SaveDraftAsync(value);
		Assert.Equal(issue.Id, saved.OriginalMaterialIssueId);
		Assert.Equal(issue.IssueNumber, saved.OriginalMaterialIssueNumber);
	}

	[Fact]
	public async Task PostingCreatesPositiveIndependentReturnMovementAndAtomicAudit()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var value = await context.MaterialReturns.SaveDraftAsync(await NewReturnAsync(context, 4));

		var posted = await context.MaterialReturns.PostAsync(value.Id, value.Version);

		Assert.Equal(MaterialReturnStatus.Posted, posted.Status);
		Assert.NotNull(posted.PostedAtUtc);
		Assert.Equal(4, await CurrentStockAsync(context));
		Assert.Equal(1, await context.ScalarAsync("SELECT COUNT(*) FROM StockMovements WHERE Reference = $Reference AND MovementType = $Type AND Quantity = 4 AND ReversalOfMovementId IS NULL;", new DatabaseParameter("$Reference", $"Material Return {value.ReturnNumber}"), new DatabaseParameter("$Type", (int)StockMovementType.MaterialReturn)));
		Assert.Equal(1, await context.ScalarAsync("SELECT COUNT(*) FROM AuditEntries WHERE EntityType = 'MaterialReturn' AND EntityId = $Id AND Action = 'Updated';", new DatabaseParameter("$Id", value.Id)));
	}

	[Fact]
	public async Task AuditFailureRollsBackReturnPosting()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var value = await context.MaterialReturns.SaveDraftAsync(await NewReturnAsync(context, 3));
		await context.Data.ExecuteAsync("CREATE TRIGGER FailMaterialReturnAudit BEFORE INSERT ON AuditEntries WHEN NEW.EntityType = 'MaterialReturn' BEGIN SELECT RAISE(ABORT, 'forced material return audit failure'); END;", CancellationToken.None);

		await Assert.ThrowsAsync<SqliteException>(() => context.MaterialReturns.PostAsync(value.Id, value.Version));
		Assert.Equal(MaterialReturnStatus.Draft, (await context.MaterialReturns.GetByIdAsync(value.Id))?.Status);
		Assert.Equal(0, await context.ScalarAsync("SELECT COUNT(*) FROM StockMovements WHERE Reference = $Reference;", new DatabaseParameter("$Reference", $"Material Return {value.ReturnNumber}")));
	}

	[Fact]
	public async Task PostedReturnIsImmutableAndHonorsConcurrency()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var value = await context.MaterialReturns.SaveDraftAsync(await NewReturnAsync(context, 2));
		var posted = await context.MaterialReturns.PostAsync(value.Id, value.Version);
		await Assert.ThrowsAsync<ConcurrencyConflictException>(() => context.MaterialReturns.PostAsync(value.Id, value.Version));
		await Assert.ThrowsAsync<InvalidOperationException>(() => context.MaterialReturns.SaveDraftAsync(posted));
		await Assert.ThrowsAsync<InvalidOperationException>(() => context.MaterialReturns.CancelAsync(posted.Id, posted.Version));
	}

	[Fact]
	public async Task PostedReturnCorrectionCreatesCounterMovementWithoutChangingDocumentOrUsingReturnAsReversal()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var value = await context.MaterialReturns.SaveDraftAsync(await NewReturnAsync(context, 5));
		var posted = await context.MaterialReturns.PostAsync(value.Id, value.Version);
		var correctionReason = await ReasonIdAsync(context, ReasonCodeSystemCodes.InventoryCorrection);

		var corrections = await context.MaterialReturns.CorrectAsync(posted.Id, posted.Version, correctionReason, "Wrong inventory selected");

		Assert.Single(corrections);
		Assert.Equal(StockMovementType.Reversal, corrections[0].MovementType);
		Assert.Equal(-5, corrections[0].Quantity);
		Assert.NotNull(corrections[0].ReversalOfMovementId);
		Assert.Equal(0, await CurrentStockAsync(context));
		Assert.Equal(MaterialReturnStatus.Posted, (await context.MaterialReturns.GetByIdAsync(posted.Id))?.Status);
		await Assert.ThrowsAnyAsync<InvalidOperationException>(() => context.MaterialReturns.CorrectAsync(posted.Id, posted.Version, correctionReason, "Duplicate"));
	}

	private static async Task<MaterialReturn> NewReturnAsync(ProcurementTestContext context, int quantity) => new() { ReturnDate = DateTime.Today, RecipientOrSource = $"Material return test {Guid.NewGuid():N}", Reference = "RETURN-CASE-42", Notes = "Independent return", Lines = [new MaterialReturnLine { InventoryId = context.InventoryId, Quantity = quantity, ReasonCodeId = await ReasonIdAsync(context, ReasonCodeSystemCodes.Returned), Notes = "Returned to stock" }] };
	private static Task<long> ReasonIdAsync(ProcurementTestContext context, string code) => context.ScalarAsync("SELECT Id FROM ReasonCodes WHERE Code = $Code;", new DatabaseParameter("$Code", code));
	private static async Task AddStockAsync(ProcurementTestContext context, int quantity) => await context.Data.InsertAsync("INSERT INTO StockMovements (InventoryId, MovementType, TimestampUtc, Quantity, Reference) VALUES ($InventoryId, $Type, $TimestampUtc, $Quantity, 'MATERIAL-RETURN-TEST-STOCK');", CancellationToken.None, new DatabaseParameter("$InventoryId", context.InventoryId), new DatabaseParameter("$Type", (int)StockMovementType.OpeningBalance), new DatabaseParameter("$TimestampUtc", DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture)), new DatabaseParameter("$Quantity", quantity));
	private static Task<long> CurrentStockAsync(ProcurementTestContext context) => context.ScalarAsync("SELECT COALESCE(SUM(Quantity), 0) FROM StockMovements WHERE InventoryId = $InventoryId;", new DatabaseParameter("$InventoryId", context.InventoryId));
}
