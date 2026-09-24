// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;

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

	private static string FindRepositoryRoot()
	{
		for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
			if (File.Exists(Path.Combine(directory.FullName, "Depot.slnx"))) return directory.FullName;
		throw new DirectoryNotFoundException("Could not locate the Depot repository root.");
	}
}
