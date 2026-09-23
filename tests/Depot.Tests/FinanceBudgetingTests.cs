// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Depot.Tests;

public sealed class FinanceBudgetingTests
{
	[Fact]
	public void CurrentFinanceMigrationRetainsBudgetingSchema()
	{
		var path = Path.Combine(Path.GetTempPath(), $"depot-finance-budgeting-{Guid.NewGuid():N}.db");
		try
		{
			var factory = new SqliteConnectionFactory(path);
			new DepotDatabase(factory).Initialize();
			FinanceInventoryAccountingSchemaMigration.Migrate(factory);
			using var connection = new SqliteConnection($"Data Source={path}");
			connection.Open();

			Assert.Equal(FinanceInventoryAccountingSchemaMigration.CurrentVersion, Scalar(connection, "SELECT Version FROM DepotFeatureVersions WHERE Name='Finance';"));
			Assert.Equal(1L, Scalar(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='FinanceBudgetVersions';"));
			Assert.Equal(1L, Scalar(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='FinanceBudgetLines';"));
			Assert.Equal(1L, Scalar(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='IX_FinanceBudgetVersions_List';"));
			Assert.Equal(1L, Scalar(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='IX_FinanceBudgetLines_Aggregate';"));
		}
		finally
		{
			SqliteConnection.ClearAllPools();
			try { File.Delete(path); } catch (IOException) { }
		}
	}

	[Theory]
	[InlineData(100.0, 3, 33.333333333, 33.333333333, 33.333333334)]
	[InlineData(-100.0, 3, -33.333333333, -33.333333333, -33.333333334)]
	[InlineData(1200.0, 12, 100.0, 100.0, 100.0)]
	public void EqualPeriodAllocationIsDeterministicAndPreservesTotal(
		double total,
		int periodCount,
		double first,
		double second,
		double last)
	{
		var allocations = FinanceBudgetingService.AllocateEqualPeriods((decimal)total, periodCount);

		Assert.Equal(periodCount, allocations.Count);
		Assert.Equal((decimal)first, allocations[0]);
		Assert.Equal((decimal)second, allocations[Math.Min(1, periodCount - 1)]);
		Assert.Equal((decimal)last, allocations[^1]);
		Assert.Equal((decimal)total, allocations.Sum());
	}

	[Fact]
	public void EqualPeriodAllocationRejectsInvalidPeriodCount()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => FinanceBudgetingService.AllocateEqualPeriods(100m, 0));
	}

	[Fact]
	public void BudgetPermissionsPreserveSegregationOfDuties()
	{
		var finance = SystemRoleCatalog.Definitions.Single(value => value.Code == SystemRoleCatalog.FinanceCode);
		var approver = SystemRoleCatalog.Definitions.Single(value => value.Code == SystemRoleCatalog.ApproverCode);

		Assert.Contains(ApplicationPermission.FinanceBudgetingView, finance.Permissions);
		Assert.Contains(ApplicationPermission.FinanceBudgetingManage, finance.Permissions);
		Assert.Contains(ApplicationPermission.FinanceBudgetingLock, finance.Permissions);
		Assert.DoesNotContain(ApplicationPermission.FinanceBudgetingApprove, finance.Permissions);
		Assert.Contains(ApplicationPermission.FinanceBudgetingView, approver.Permissions);
		Assert.Contains(ApplicationPermission.FinanceBudgetingApprove, approver.Permissions);
		Assert.DoesNotContain(ApplicationPermission.FinanceBudgetingManage, approver.Permissions);
		Assert.Equal("FinanceBudgeting.View", PermissionCatalog.Code(ApplicationPermission.FinanceBudgetingView));
		Assert.Equal("FinanceBudgeting.Manage", PermissionCatalog.Code(ApplicationPermission.FinanceBudgetingManage));
		Assert.Equal("FinanceBudgeting.Approve", PermissionCatalog.Code(ApplicationPermission.FinanceBudgetingApprove));
		Assert.Equal("FinanceBudgeting.Lock", PermissionCatalog.Code(ApplicationPermission.FinanceBudgetingLock));
	}

	[Fact]
	public void BudgetWorkspaceUsesServiceBoundaryAndMyWorkIntegration()
	{
		var root = FindRepositoryRoot();
		var viewModel = File.ReadAllText(Path.Combine(root, "src", "Depot", "ViewModels", "FinanceBudgetingViewModel.cs"));
		var view = File.ReadAllText(Path.Combine(root, "src", "Depot", "Views", "FinanceBudgetingView.xaml"));
		var providers = File.ReadAllText(Path.Combine(root, "src", "Depot", "Services", "MyWorkProviders.cs"));
		var composition = File.ReadAllText(Path.Combine(root, "src", "Depot", "Composition", "ServiceComposition.cs"));
		var main = File.ReadAllText(Path.Combine(root, "src", "Depot", "ViewModels", "MainViewModel.cs"));

		Assert.DoesNotContain("DatabaseAccess", viewModel, StringComparison.Ordinal);
		Assert.DoesNotContain("FinanceBudgetingRepository", viewModel, StringComparison.Ordinal);
		Assert.Contains("FinanceBudgetingService", viewModel, StringComparison.Ordinal);
		Assert.Contains("AutomationProperties.Name", view, StringComparison.Ordinal);
		Assert.Contains("BudgetingMyWorkProvider", providers, StringComparison.Ordinal);
		Assert.Contains("ApprovalSubjectKind.FinanceBudget", providers, StringComparison.Ordinal);
		Assert.Contains("new BudgetingMyWorkProvider(Budgeting, ApprovalPolicies)", composition, StringComparison.Ordinal);
		Assert.Contains("case MyWorkItemKind.FinanceBudget", main, StringComparison.Ordinal);
		Assert.Contains("OpenBudgetAsync", main, StringComparison.Ordinal);
	}

	[Fact]
	public void ApprovedAndLockedBudgetsAreDeclaredImmutable()
	{
		Assert.True(new FinanceBudgetVersion { Status = FinanceBudgetStatus.Approved }.IsImmutable);
		Assert.True(new FinanceBudgetVersion { Status = FinanceBudgetStatus.Locked }.IsImmutable);
		Assert.False(new FinanceBudgetVersion { Status = FinanceBudgetStatus.Draft }.IsImmutable);
	}

	private static long Scalar(SqliteConnection connection, string sql)
	{
		using var command = connection.CreateCommand();
		command.CommandText = sql;
		return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
	}

	private static string FindRepositoryRoot()
	{
		for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
			if (File.Exists(Path.Combine(directory.FullName, "Depot.slnx"))) return directory.FullName;
		throw new DirectoryNotFoundException("Could not locate the Depot repository root.");
	}
}
