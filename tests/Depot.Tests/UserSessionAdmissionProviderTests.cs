// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;

using Xunit;

namespace Depot.Tests;

[Collection("Provider database")]
[Trait("Acceptance", "DatabaseProvider")]
[Trait("AcceptanceLevel", "Smoke")]
public sealed class UserSessionAdmissionProviderTests
{
	[SqlServerProcurementFact]
	[Trait("Provider", "SqlServer")]
	public async Task SqlServerSerializesAdmissionWithAccountChanges()
	{
		var factory = new SqlServerConnectionFactory(ProcurementProviderConfiguration.GetSqlServerSettings());
		await VerifyAsync(factory, new SqlServerDatabase(factory));
	}

	[MariaDbProcurementFact]
	[Trait("Provider", "MariaDB")]
	public async Task MariaDbSerializesAdmissionWithAccountChanges()
	{
		var factory = new MySqlConnectionFactory(ProcurementProviderConfiguration.GetMariaDbSettings());
		await VerifyAsync(factory, new MySqlDatabase(factory));
	}

	[MySqlProcurementFact]
	[Trait("Provider", "MySQL")]
	public async Task MySqlSerializesAdmissionWithAccountChanges()
	{
		var factory = new MySqlConnectionFactory(ProcurementProviderConfiguration.GetMySqlSettings());
		await VerifyAsync(factory, new MySqlDatabase(factory));
	}

	private static async Task VerifyAsync(IDatabaseConnectionFactory factory, IDatabaseInitializer initializer)
	{
		await using var context = await UserSessionAdmissionTestContext.CreateAsync(factory, initializer);
		await UserSessionAdmissionScenarios.RejectChangedAccountAsync(context, 0);
		await UserSessionAdmissionScenarios.RejectChangedAccountAsync(context, 1);
		await UserSessionAdmissionScenarios.RejectChangedAccountAsync(context, 2);
		await UserSessionAdmissionScenarios.RejectChangedAccountAsync(context, 0, EnterpriseIdentityProviderKind.OpenIdConnect);
		await UserSessionAdmissionScenarios.RejectChangedAccountAsync(context, 1, EnterpriseIdentityProviderKind.MicrosoftEntraId);
		await UserSessionAdmissionScenarios.RevokeAlreadyAdmittedSessionAsync(context, 0);
		await UserSessionAdmissionScenarios.RevokeAlreadyAdmittedSessionAsync(context, 1);
		await UserSessionAdmissionScenarios.VerifyConcurrentLoginsAsync(context, ConcurrentSessionLimitAction.RejectNewSession);
		await UserSessionAdmissionScenarios.VerifyConcurrentLoginsAsync(context, ConcurrentSessionLimitAction.SupersedeOldestSession);
	}
}
