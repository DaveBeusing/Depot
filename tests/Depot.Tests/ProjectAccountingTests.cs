// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class ProjectAccountingTests
{
	[Fact]
	public void ProjectLifecycleStatesBoundOperationalActivity()
	{
		Assert.True(new ProjectRecord { Status = ProjectStatus.Draft }.AcceptsOperationalActivity);
		Assert.True(new ProjectRecord { Status = ProjectStatus.Active }.AcceptsOperationalActivity);
		Assert.True(new ProjectRecord { Status = ProjectStatus.OnHold }.AcceptsOperationalActivity);
		Assert.False(new ProjectRecord { Status = ProjectStatus.Closed }.AcceptsOperationalActivity);
		Assert.False(new ProjectRecord { Status = ProjectStatus.Cancelled }.AcceptsOperationalActivity);
	}

	[Fact]
	public async Task ProjectFeatureSchemaMigratesIndependently()
	{
		var path = Path.Combine(Path.GetTempPath(), $"depot-project-accounting-{Guid.NewGuid():N}.db");
		try
		{
			var factory = new SqliteConnectionFactory(path);
			new DepotDatabase(factory).Initialize();
			FinanceInventoryAccountingSchemaMigration.Migrate(factory);
			ProjectAccountingSchemaMigration.Migrate(factory);
			var database = new DatabaseAccess(factory);

			Assert.Equal(ProjectAccountingSchemaMigration.CurrentVersion,
				Convert.ToInt32(await database.ExecuteScalarAsync("SELECT Version FROM DepotFeatureVersions WHERE Name='ProjectAccounting';", CancellationToken.None)));
			Assert.Equal(1L, Convert.ToInt64(await database.ExecuteScalarAsync("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Projects';", CancellationToken.None)));
			Assert.Equal(1L, Convert.ToInt64(await database.ExecuteScalarAsync("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='ProjectAttributions';", CancellationToken.None)));
			Assert.Equal(1L, Convert.ToInt64(await database.ExecuteScalarAsync("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='ProjectBudgetLineLinks';", CancellationToken.None)));
		}
		finally
		{
			try { File.Delete(path); } catch { }
		}
	}

	[Fact]
	public async Task ProjectServiceEnforcesLifecycleConcurrencyAndImmutableCommitments()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		FinanceInventoryAccountingSchemaMigration.Migrate(context.ConnectionFactory);
		ProjectAccountingSchemaMigration.Migrate(context.ConnectionFactory);
		var legalEntityId = await SeedLegalEntityAsync(context.Data, "OPS");
		var service = CreateService(context);
		var user = context.Authorization.CurrentUser ?? throw new InvalidOperationException("The test user is not authenticated.");

		var project = await service.SaveAsync(new ProjectRecord
		{
			Code = "PRJ-OPS",
			Name = "Operations project",
			LegalEntityId = legalEntityId,
			OwnerUserId = user.Id,
			PlannedStartDate = new DateOnly(2026, 9, 1),
			PlannedEndDate = new DateOnly(2026, 12, 31)
		});
		Assert.Equal(ProjectStatus.Draft, project.Status);
		await Assert.ThrowsAsync<SqliteException>(() => service.SaveAsync(new ProjectRecord
		{
			Code = "prj-ops",
			Name = "Duplicate project",
			LegalEntityId = legalEntityId,
			OwnerUserId = user.Id
		}));

		var updated = await service.SaveAsync(project with { Name = "Operations project updated" });
		Assert.Equal(project.Version + 1, updated.Version);
		await Assert.ThrowsAsync<ConcurrencyConflictException>(() => service.SaveAsync(project with { Name = "Stale update" }));

		var alternateLegalEntityId = await SeedLegalEntityAsync(context.Data, "ALT");
		await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(updated with { LegalEntityId = alternateLegalEntityId }));

		var active = await service.ActivateAsync(updated.Id, updated.Version);
		var phase = await service.SavePhaseAsync(new ProjectPhase
		{
			ProjectId = active.Id,
			Code = "DELIVERY",
			Name = "Delivery",
			Status = ProjectPhaseStatus.Active
		});
		var order = await context.Orders.SaveDraftAsync(context.NewOrder(quantity: 5, unitPrice: 12.50m));
		order = await context.ApproveAndOrderAsync(order);
		var attribution = await service.AttributeAsync(new ProjectAttributionRequest(
			active.Id,
			phase.Id,
			ProjectAttributionEntityKind.PurchaseOrder,
			order.Id));
		Assert.True(attribution.IsImmutable);

		var commitment = Assert.Single(await service.ListCommitmentsAsync(active.Id, phase.Id));
		Assert.Equal(order.Id, commitment.PurchaseOrderId);
		Assert.Equal(5, commitment.OrderedQuantity);
		Assert.Equal(0, commitment.ReceivedQuantity);
		Assert.Equal(62.50m, commitment.RemainingAmount);
		await Assert.ThrowsAsync<InvalidOperationException>(() => service.RemoveAttributionAsync(ProjectAttributionEntityKind.PurchaseOrder, order.Id));

		var closed = await service.CloseAsync(active.Id, active.Version);
		await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(closed with { Name = "Closed mutation" }));
		await Assert.ThrowsAsync<InvalidOperationException>(() => service.SavePhaseAsync(phase with { Name = "Closed mutation" }));

		context.Authorization.SignIn(user, [ApplicationPermission.ProjectsView]);
		Assert.False(service.CanManage);
		await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SaveAsync(new ProjectRecord
		{
			Code = "PRJ-DENIED",
			Name = "Denied",
			LegalEntityId = legalEntityId,
			OwnerUserId = user.Id
		}));
	}

	[Fact]
	public async Task ProjectActualsAndBudgetVarianceReconcileToFinanceEvidence()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		FinanceInventoryAccountingSchemaMigration.Migrate(context.ConnectionFactory);
		ProjectAccountingSchemaMigration.Migrate(context.ConnectionFactory);
		var user = context.Authorization.CurrentUser ?? throw new InvalidOperationException("The test user is not authenticated.");
		var finance = await SeedFinanceProjectionAsync(context.Data, user.Id);
		var service = CreateService(context);

		var draft = await service.SaveAsync(new ProjectRecord
		{
			Code = "PRJ-FIN",
			Name = "Finance projection project",
			LegalEntityId = finance.LegalEntityId,
			OwnerUserId = user.Id
		});
		var project = await service.ActivateAsync(draft.Id, draft.Version);
		var attribution = await service.AttributeAsync(new ProjectAttributionRequest(
			project.Id,
			null,
			ProjectAttributionEntityKind.JournalEntry,
			finance.JournalEntryId));
		Assert.True(attribution.IsImmutable);

		var actuals = await service.ListActualsAsync(project.Id);
		Assert.Equal(2, actuals.Count);
		var revenue = Assert.Single(actuals, value => value.AccountId == finance.RevenueAccountId);
		var expense = Assert.Single(actuals, value => value.AccountId == finance.ExpenseAccountId);
		Assert.Equal(150m, revenue.Amount);
		Assert.Equal(100m, expense.Amount);
		Assert.All(actuals, value => Assert.Equal("EUR", value.ReportingCurrency.Value));

		var summary = await service.GetFinancialSummaryAsync(project.Id);
		var totals = Assert.Single(summary.CurrencyTotals);
		Assert.Equal(150m, totals.Revenue);
		Assert.Equal(100m, totals.Cost);
		Assert.Equal(50m, totals.Margin);
		Assert.Equal(1, summary.ActualEntryCount);

		await service.LinkBudgetLineAsync(project.Id, null, finance.BudgetLineId, "REVENUE");
		var variance = Assert.Single(
			await service.GetBudgetVarianceAsync(project.Id),
			value => value.AccountId == finance.RevenueAccountId);
		Assert.Equal(150m, variance.Actual);
		Assert.Equal(120m, variance.Budget);
		Assert.Equal(30m, variance.Variance);
		Assert.Equal(0.25m, variance.VariancePercent);

		await service.LinkBudgetLineAsync(project.Id, null, finance.SecondBudgetLineId, "REVENUE");
		var combined = Assert.Single(
			await service.GetBudgetVarianceAsync(project.Id),
			value => value.AccountId == finance.RevenueAccountId);
		Assert.Equal(150m, combined.Actual);
		Assert.Equal(150m, combined.Budget);
		Assert.Equal(0m, combined.Variance);
	}

	[Fact]
	public void ProjectAccountingKeepsFinanceEvidenceAuthoritative()
	{
		var root = FindRepositoryRoot();
		var repository = File.ReadAllText(Path.Combine(root, "src", "Depot", "Repositories", "ProjectAccountingRepository.cs"));
		var service = File.ReadAllText(Path.Combine(root, "src", "Depot", "Services", "ProjectAccountingService.cs"));

		Assert.Contains("FinanceJournalEntries", repository, StringComparison.Ordinal);
		Assert.Contains("FinanceJournalEntryLines", repository, StringComparison.Ordinal);
		Assert.Contains("PurchaseOrders", repository, StringComparison.Ordinal);
		Assert.Contains("FinanceBudgetLines", repository, StringComparison.Ordinal);
		Assert.DoesNotContain("INSERT INTO FinanceJournalEntries", repository, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("ListActualsAsync", service, StringComparison.Ordinal);
		Assert.Contains("ListCommitmentsAsync", service, StringComparison.Ordinal);
		Assert.Contains("GetBudgetVarianceAsync", service, StringComparison.Ordinal);
	}

	[Fact]
	public void ProjectWorkspaceUsesServiceBoundaryAndStandardControls()
	{
		var root = FindRepositoryRoot();
		var viewModel = File.ReadAllText(Path.Combine(root, "src", "Depot", "ViewModels", "ProjectAccountingViewModel.cs"));
		var view = File.ReadAllText(Path.Combine(root, "src", "Depot", "Views", "ProjectAccountingView.xaml"));
		var main = File.ReadAllText(Path.Combine(root, "src", "Depot", "ViewModels", "MainViewModel.cs"));
		var templates = File.ReadAllText(Path.Combine(root, "src", "Depot", "Resources", "ViewTemplates.xaml"));

		Assert.Contains("ProjectAccountingService", viewModel, StringComparison.Ordinal);
		Assert.DoesNotContain("ProjectAccountingRepository", viewModel, StringComparison.Ordinal);
		Assert.DoesNotContain("DatabaseAccess", viewModel, StringComparison.Ordinal);
		Assert.Contains("<controls:PageHeader", view, StringComparison.Ordinal);
		Assert.Contains("<controls:OperationPanel", view, StringComparison.Ordinal);
		Assert.Contains("AppDataGridCompactStyle", view, StringComparison.Ordinal);
		Assert.Contains("BusinessAttachmentPanelViewModel", viewModel, StringComparison.Ordinal);
		Assert.Contains("BusinessAttachmentEntityKind.Project", viewModel, StringComparison.Ordinal);
		Assert.Contains("<controls:BusinessAttachmentPanel", view, StringComparison.Ordinal);
		Assert.Contains("ApplicationPermission.ProjectsView", main, StringComparison.Ordinal);
		Assert.Contains("ProjectAccountingViewModel", templates, StringComparison.Ordinal);
	}

	[Fact]
	public void OwnedProjectsParticipateInMyWork()
	{
		var root = FindRepositoryRoot();
		var providers = File.ReadAllText(Path.Combine(root, "src", "Depot", "Services", "MyWorkProviders.cs"));
		var composition = File.ReadAllText(Path.Combine(root, "src", "Depot", "Composition", "ServiceComposition.cs"));
		var main = File.ReadAllText(Path.Combine(root, "src", "Depot", "ViewModels", "MainViewModel.cs"));

		Assert.Contains("ProjectAccountingMyWorkProvider", providers, StringComparison.Ordinal);
		Assert.Contains("OwnerUserId: query.UserId", providers, StringComparison.Ordinal);
		Assert.Contains("MyWorkSectionKind.Exceptions", providers, StringComparison.Ordinal);
		Assert.Contains("new ProjectAccountingMyWorkProvider(ProjectAccounting)", composition, StringComparison.Ordinal);
		Assert.Contains("case MyWorkItemKind.Project", main, StringComparison.Ordinal);
		Assert.Contains("OpenProjectAsync", main, StringComparison.Ordinal);
	}

	private static ProjectAccountingService CreateService(ProcurementTestContext context)
	{
		var auditRepository = new AuditRepository(context.Data);
		return new ProjectAccountingService(
			new DatabaseTransactionRunner(context.Data),
			new ProjectAccountingRepository(context.Data),
			auditRepository,
			new AuditService(auditRepository, context.Authorization),
			context.Authorization);
	}

	private static async Task<Guid> SeedLegalEntityAsync(DatabaseAccess data, string prefix)
	{
		var id = Guid.NewGuid();
		var code = $"{prefix}-{id:N}"[..Math.Min(20, prefix.Length + 9)];
		await data.ExecuteAsync(
			"INSERT OR IGNORE INTO FinanceCurrencies (Code,Name,MinorUnits,IsActive) VALUES ('EUR','Euro',2,1);",
			CancellationToken.None);
		await data.ExecuteAsync(
			"INSERT INTO FinanceLegalEntities (Id,Code,Name,CountryCode,FunctionalCurrencyCode,IsActive) VALUES ($Id,$Code,$Name,'DE','EUR',1);",
			CancellationToken.None,
			new DatabaseParameter("$Id", id.ToString("D")),
			new DatabaseParameter("$Code", code),
			new DatabaseParameter("$Name", $"Project Test {code}"));
		return id;
	}

	private static async Task<FinanceProjectionSeed> SeedFinanceProjectionAsync(DatabaseAccess data, long userId)
	{
		var legalEntityId = await SeedLegalEntityAsync(data, "FIN");
		var suffix = legalEntityId.ToString("N")[..8];
		var calendarId = Guid.NewGuid();
		var periodId = Guid.NewGuid();
		var chartId = Guid.NewGuid();
		var revenueAccountId = Guid.NewGuid();
		var expenseAccountId = Guid.NewGuid();
		var assetAccountId = Guid.NewGuid();
		var bookId = Guid.NewGuid();
		var journalId = Guid.NewGuid();

		await data.ExecuteAsync(
			"INSERT INTO FinanceFiscalCalendars (Id,LegalEntityId,Code,Name,IsActive) VALUES ($Id,$Entity,$Code,$Name,1);",
			CancellationToken.None,
			new DatabaseParameter("$Id", calendarId.ToString("D")),
			new DatabaseParameter("$Entity", legalEntityId.ToString("D")),
			new DatabaseParameter("$Code", $"CAL-{suffix}"),
			new DatabaseParameter("$Name", $"Project Calendar {suffix}"));
		await data.ExecuteAsync(
			"INSERT INTO FinanceAccountingPeriods (Id,FiscalCalendarId,Code,StartDate,EndDate,Status) VALUES ($Id,$Calendar,$Code,'2026-09-01','2026-09-30',0);",
			CancellationToken.None,
			new DatabaseParameter("$Id", periodId.ToString("D")),
			new DatabaseParameter("$Calendar", calendarId.ToString("D")),
			new DatabaseParameter("$Code", $"2026-09-{suffix}"));
		await data.ExecuteAsync(
			"INSERT INTO FinanceChartsOfAccounts (Id,Code,Name,IsActive) VALUES ($Id,$Code,$Name,1);",
			CancellationToken.None,
			new DatabaseParameter("$Id", chartId.ToString("D")),
			new DatabaseParameter("$Code", $"COA-{suffix}"),
			new DatabaseParameter("$Name", $"Project Chart {suffix}"));

		await InsertAccountAsync(data, revenueAccountId, chartId, "4000", "Project revenue", FinanceAccountType.Revenue);
		await InsertAccountAsync(data, expenseAccountId, chartId, "6000", "Project expense", FinanceAccountType.Expense);
		await InsertAccountAsync(data, assetAccountId, chartId, "1000", "Project cash", FinanceAccountType.Asset);
		await data.ExecuteAsync(
			"INSERT INTO FinanceAccountingBooks (Id,LegalEntityId,ChartOfAccountsId,Code,Name,ReportingCurrencyCode,AccountingStandardCode,IsPrimary,IsActive) VALUES ($Id,$Entity,$Chart,$Code,$Name,'EUR','TEST',1,1);",
			CancellationToken.None,
			new DatabaseParameter("$Id", bookId.ToString("D")),
			new DatabaseParameter("$Entity", legalEntityId.ToString("D")),
			new DatabaseParameter("$Chart", chartId.ToString("D")),
			new DatabaseParameter("$Code", $"BOOK-{suffix}"),
			new DatabaseParameter("$Name", $"Project Book {suffix}"));
		await data.ExecuteAsync(
			"INSERT INTO FinanceJournals (Id,AccountingBookId,Code,Name,IsActive) VALUES ($Id,$Book,$Code,$Name,1);",
			CancellationToken.None,
			new DatabaseParameter("$Id", journalId.ToString("D")),
			new DatabaseParameter("$Book", bookId.ToString("D")),
			new DatabaseParameter("$Code", $"GJ-{suffix}"),
			new DatabaseParameter("$Name", $"Project Journal {suffix}"));

		var journalEntryId = await data.InsertAsync(
			"""
			INSERT INTO FinanceJournalEntries
			(EntryNumber,OperationId,RequestHash,AccountingBookId,JournalId,AccountingPeriodId,PostingDate,PostedAtUtc,PostedByUserId,Description,SourceType,SourceId,SourceEvent,SourceReference,TransactionCurrencyCode,ReportingCurrencyCode,ExchangeRateId,ExchangeRate,EntryKind,ReversalOfEntryId)
			VALUES
			($EntryNumber,$OperationId,$Hash,$Book,$Journal,$Period,'2026-09-24','2026-09-24T12:00:00.0000000Z',$User,'Project actuals','ProjectTest',$SourceId,'Posted',NULL,'EUR','EUR',NULL,1,$Kind,NULL);
			""",
			CancellationToken.None,
			new DatabaseParameter("$EntryNumber", $"GL-{suffix}"),
			new DatabaseParameter("$OperationId", Guid.NewGuid().ToString("D")),
			new DatabaseParameter("$Hash", new string('A', 64)),
			new DatabaseParameter("$Book", bookId.ToString("D")),
			new DatabaseParameter("$Journal", journalId.ToString("D")),
			new DatabaseParameter("$Period", periodId.ToString("D")),
			new DatabaseParameter("$User", userId),
			new DatabaseParameter("$SourceId", $"PROJECT-{suffix}"),
			new DatabaseParameter("$Kind", (int)FinanceJournalEntryKind.Manual));
		await InsertJournalLineAsync(data, journalEntryId, 1, revenueAccountId, 0m, 150m);
		await InsertJournalLineAsync(data, journalEntryId, 2, expenseAccountId, 100m, 0m);
		await InsertJournalLineAsync(data, journalEntryId, 3, assetAccountId, 50m, 0m);

		var now = "2026-09-24T12:00:00.0000000Z";
		var budgetVersionId = await data.InsertAsync(
			"""
			INSERT INTO FinanceBudgetVersions
			(LegalEntityId,AccountingBookId,FiscalCalendarId,FiscalYear,BudgetName,BudgetVersionNumber,CurrencyCode,Status,OwnerUserId,Description,SourceKind,SourceBudgetVersionId,ApprovalInstanceId,CreatedAtUtc,CreatedByUserId,UpdatedAtUtc,UpdatedByUserId)
			VALUES
			($Entity,$Book,$Calendar,2026,$Name,1,'EUR',$Status,$User,'Project test budget',$SourceKind,NULL,NULL,$Now,$User,$Now,$User);
			""",
			CancellationToken.None,
			new DatabaseParameter("$Entity", legalEntityId.ToString("D")),
			new DatabaseParameter("$Book", bookId.ToString("D")),
			new DatabaseParameter("$Calendar", calendarId.ToString("D")),
			new DatabaseParameter("$Name", $"Project Budget {suffix}"),
			new DatabaseParameter("$Status", (int)FinanceBudgetStatus.Approved),
			new DatabaseParameter("$User", userId),
			new DatabaseParameter("$SourceKind", (int)FinanceBudgetSourceKind.Manual),
			new DatabaseParameter("$Now", now));
		var budgetLineId = await data.InsertAsync(
			"INSERT INTO FinanceBudgetLines (BudgetVersionId,AccountId,AccountingPeriodId,DimensionId,DimensionValueId,Amount,SourceEvidence) VALUES ($Budget,$Account,$Period,'','',120,'Project test');",
			CancellationToken.None,
			new DatabaseParameter("$Budget", budgetVersionId),
			new DatabaseParameter("$Account", revenueAccountId.ToString("D")),
			new DatabaseParameter("$Period", periodId.ToString("D")));
		var secondBudgetVersionId = await data.InsertAsync(
			"""
			INSERT INTO FinanceBudgetVersions
			(LegalEntityId,AccountingBookId,FiscalCalendarId,FiscalYear,BudgetName,BudgetVersionNumber,CurrencyCode,Status,OwnerUserId,Description,SourceKind,SourceBudgetVersionId,ApprovalInstanceId,CreatedAtUtc,CreatedByUserId,UpdatedAtUtc,UpdatedByUserId)
			VALUES
			($Entity,$Book,$Calendar,2026,$Name,2,'EUR',$Status,$User,'Project test amendment',$SourceKind,$SourceBudget,NULL,$Now,$User,$Now,$User);
			""",
			CancellationToken.None,
			new DatabaseParameter("$Entity", legalEntityId.ToString("D")),
			new DatabaseParameter("$Book", bookId.ToString("D")),
			new DatabaseParameter("$Calendar", calendarId.ToString("D")),
			new DatabaseParameter("$Name", $"Project Budget {suffix}"),
			new DatabaseParameter("$Status", (int)FinanceBudgetStatus.Approved),
			new DatabaseParameter("$User", userId),
			new DatabaseParameter("$SourceKind", (int)FinanceBudgetSourceKind.Manual),
			new DatabaseParameter("$SourceBudget", budgetVersionId),
			new DatabaseParameter("$Now", now));
		var secondBudgetLineId = await data.InsertAsync(
			"INSERT INTO FinanceBudgetLines (BudgetVersionId,AccountId,AccountingPeriodId,DimensionId,DimensionValueId,Amount,SourceEvidence) VALUES ($Budget,$Account,$Period,'','',30,'Project amendment test');",
			CancellationToken.None,
			new DatabaseParameter("$Budget", secondBudgetVersionId),
			new DatabaseParameter("$Account", revenueAccountId.ToString("D")),
			new DatabaseParameter("$Period", periodId.ToString("D")));

		return new FinanceProjectionSeed(legalEntityId, revenueAccountId, expenseAccountId, journalEntryId, budgetLineId, secondBudgetLineId);
	}

	private static Task InsertAccountAsync(DatabaseAccess data, Guid id, Guid chartId, string number, string name, FinanceAccountType type) =>
		data.ExecuteAsync(
			"INSERT INTO FinanceAccounts (Id,ChartOfAccountsId,Number,Name,AccountType,AllowDirectPosting,IsActive) VALUES ($Id,$Chart,$Number,$Name,$Type,1,1);",
			CancellationToken.None,
			new DatabaseParameter("$Id", id.ToString("D")),
			new DatabaseParameter("$Chart", chartId.ToString("D")),
			new DatabaseParameter("$Number", number),
			new DatabaseParameter("$Name", name),
			new DatabaseParameter("$Type", (int)type));

	private static Task InsertJournalLineAsync(DatabaseAccess data, long journalEntryId, int lineNumber, Guid accountId, decimal debit, decimal credit) =>
		data.ExecuteAsync(
			"INSERT INTO FinanceJournalEntryLines (JournalEntryId,LineNumber,AccountId,Description,TransactionDebit,TransactionCredit,ReportingDebit,ReportingCredit) VALUES ($Entry,$Line,$Account,NULL,$Debit,$Credit,$Debit,$Credit);",
			CancellationToken.None,
			new DatabaseParameter("$Entry", journalEntryId),
			new DatabaseParameter("$Line", lineNumber),
			new DatabaseParameter("$Account", accountId.ToString("D")),
			new DatabaseParameter("$Debit", debit),
			new DatabaseParameter("$Credit", credit));

	private sealed record FinanceProjectionSeed(
		Guid LegalEntityId,
		Guid RevenueAccountId,
		Guid ExpenseAccountId,
		long JournalEntryId,
		long BudgetLineId,
		long SecondBudgetLineId);

	private static string FindRepositoryRoot()
	{
		for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
			if (File.Exists(Path.Combine(directory.FullName, "Depot.slnx"))) return directory.FullName;
		throw new DirectoryNotFoundException("Could not locate the Depot repository root.");
	}
}
