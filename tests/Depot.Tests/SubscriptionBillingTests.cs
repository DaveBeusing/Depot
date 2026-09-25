// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Depot.Tests;

public sealed class SubscriptionBillingTests : IDisposable
{
	private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"depot-subscription-{Guid.NewGuid():N}.db");

	[Fact]
	public void MonthlySchedulePreservesEndOfMonth()
	{
		Assert.Equal(new DateOnly(2028, 2, 29), SubscriptionBillingSchedule.Advance(new DateOnly(2028, 1, 31), new DateOnly(2028, 1, 31), SubscriptionBillingCadence.Monthly));
		Assert.Equal(new DateOnly(2028, 3, 31), SubscriptionBillingSchedule.Advance(new DateOnly(2028, 1, 31), new DateOnly(2028, 2, 29), SubscriptionBillingCadence.Monthly));
	}

	[Fact]
	public void NonEndOfMonthScheduleReturnsToAnchorDayAfterShortMonth()
	{
		Assert.Equal(new DateOnly(2027, 2, 28), SubscriptionBillingSchedule.Advance(new DateOnly(2027, 1, 30), new DateOnly(2027, 1, 30), SubscriptionBillingCadence.Monthly));
		Assert.Equal(new DateOnly(2027, 3, 30), SubscriptionBillingSchedule.Advance(new DateOnly(2027, 1, 30), new DateOnly(2027, 2, 28), SubscriptionBillingCadence.Monthly));
	}

	[Theory]
	[InlineData(SubscriptionBillingCadence.Monthly, 2027, 2, 28)]
	[InlineData(SubscriptionBillingCadence.Quarterly, 2027, 4, 30)]
	[InlineData(SubscriptionBillingCadence.Annual, 2028, 1, 31)]
	public void CadenceAdvanceIsDeterministic(SubscriptionBillingCadence cadence, int year, int month, int day)
	{
		var anchor = new DateOnly(2027, 1, 31);
		Assert.Equal(new DateOnly(year, month, day), SubscriptionBillingSchedule.Advance(anchor, anchor, cadence));
	}

	[Fact]
	public void FiniteContractClampsFinalBillingPeriod()
	{
		var contract = new SubscriptionContract
		{
			StartDate = new DateOnly(2027, 1, 31),
			EndDate = new DateOnly(2027, 2, 15),
			Cadence = SubscriptionBillingCadence.Monthly
		};
		var period = SubscriptionBillingSchedule.Period(contract, contract.StartDate);
		Assert.Equal(new DateOnly(2027, 1, 31), period.PeriodStart);
		Assert.Equal(new DateOnly(2027, 2, 15), period.PeriodEnd);
		Assert.Equal(new DateOnly(2027, 2, 28), period.NextBillingDate);
	}

	[Fact]
	public async Task SalesMigrationCreatesRecurringBillingPersistenceAndCurrentVersion()
	{
		var factory = new SqliteConnectionFactory(_databasePath);
		new DepotDatabase(factory).Initialize();
		SalesSchemaMigration.Migrate(factory);
		var data = new DatabaseAccess(factory);

		Assert.Equal(SalesSchemaMigration.CurrentVersion, Convert.ToInt32(await data.ExecuteScalarAsync("SELECT Version FROM DepotFeatureVersions WHERE Name='Sales';", CancellationToken.None)));
		foreach (var table in new[] { "SubscriptionContracts", "SubscriptionContractLines", "SubscriptionContractRevisions", "SubscriptionLifecycleEvents", "SubscriptionBillingInstances", "SubscriptionBillingPriceEvidence" })
		{
			var count = Convert.ToInt32(await data.ExecuteScalarAsync("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$Name;", CancellationToken.None, new DatabaseParameter("$Name", table)));
			Assert.Equal(1, count);
		}
	}

	public void Dispose()
	{
		SqliteConnection.ClearAllPools();
		if (File.Exists(_databasePath)) File.Delete(_databasePath);
	}
}
