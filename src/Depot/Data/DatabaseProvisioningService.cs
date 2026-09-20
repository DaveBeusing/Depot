// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

namespace Depot.Data;

internal enum DatabaseProvisioningPath
{
	FastPath,
	FullProvisioning
}

public static class DatabaseProvisioningService
{
	public static IDatabaseConnectionFactory Initialize(DatabaseConnectionSettings settings)
	{
		ArgumentNullException.ThrowIfNull(settings);
		var connectionFactory = DatabaseProviderFactory.CreateConnectionFactory(settings);
		Initialize(connectionFactory);
		return connectionFactory;
	}

	public static void Initialize(IDatabaseConnectionFactory connectionFactory) =>
		_ = InitializeCore(connectionFactory);

	internal static DatabaseProvisioningPath InitializeCore(IDatabaseConnectionFactory connectionFactory)
	{
		ArgumentNullException.ThrowIfNull(connectionFactory);
		if (DatabaseSchemaStateInspector.IsCurrent(connectionFactory))
			return DatabaseProvisioningPath.FastPath;

		using var provisioningLock = DatabaseProvisioningLock.Acquire(connectionFactory);
		if (DatabaseSchemaStateInspector.IsCurrent(connectionFactory))
			return DatabaseProvisioningPath.FastPath;

		DatabaseProviderFactory.CreateInitializer(connectionFactory).Initialize();
		SalesSchemaMigration.Migrate(connectionFactory);
		FinanceInventoryAccountingSchemaMigration.Migrate(connectionFactory);
		UserSessionSchemaMigration.Migrate(connectionFactory);
		SecurityEventSchemaMigration.Migrate(connectionFactory);
		UserPreferenceSchemaMigration.Migrate(connectionFactory);
		DocumentTemplateSchemaMigration.Migrate(connectionFactory);
		EnterpriseIdentitySchemaMigration.Migrate(connectionFactory);
		return DatabaseProvisioningPath.FullProvisioning;
	}
}
