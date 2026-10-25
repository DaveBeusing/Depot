// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Xunit;

namespace Depot.Tests;

[Collection("Provider database")]
[Trait("Acceptance", "DatabaseProvider")]
[Trait("AcceptanceLevel", "Smoke")]
public sealed class FinancePaymentIntegrityProviderAcceptanceTests
{
	[Fact]
	[Trait("Provider", "SQLite")]
	public async Task SQLitePaymentIntegrityContract()
	{
		using var fixture = FinancePaymentIntegrityFixture.CreateSqlite();
		await VerifyAsync(fixture);
	}

	[SqlServerProcurementFact]
	[Trait("Provider", "SqlServer")]
	public Task SqlServerPaymentIntegrityContract() => VerifyProviderAsync(new SqlServerConnectionFactory(ProcurementProviderConfiguration.GetSqlServerSettings()));

	[MariaDbProcurementFact]
	[Trait("Provider", "MariaDB")]
	public Task MariaDbPaymentIntegrityContract() => VerifyProviderAsync(new MySqlConnectionFactory(ProcurementProviderConfiguration.GetMariaDbSettings()));

	[MySqlProcurementFact]
	[Trait("Provider", "MySQL")]
	public Task MySqlPaymentIntegrityContract() => VerifyProviderAsync(new MySqlConnectionFactory(ProcurementProviderConfiguration.GetMySqlSettings()));

	private static async Task VerifyProviderAsync(IDatabaseConnectionFactory factory)
	{
		using var fixture = FinancePaymentIntegrityFixture.CreateProvider(factory);
		await VerifyAsync(fixture);
	}

	private static async Task VerifyAsync(FinancePaymentIntegrityFixture fixture)
	{
		await FinancePaymentIntegrityTests.VerifyMatchingAsync(fixture);
		await FinancePaymentIntegrityTests.VerifyConcurrentMatchingAsync(fixture);
		await FinancePaymentIntegrityTests.VerifySelectedBankAsync(fixture);
		await FinancePaymentIntegrityTests.VerifyReconciliationAsync(fixture);
		await FinancePaymentIntegrityTests.VerifyTransferAsync(fixture);
		await FinancePaymentIntegrityTests.VerifySepaLifecycleAsync(fixture);
		await FinancePaymentIntegrityTests.VerifyConcurrentSepaAsync(fixture);
	}
}
