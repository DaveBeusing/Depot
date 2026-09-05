// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

namespace Depot.Data;

public static class DatabaseProvisioningService
{
	public static IDatabaseConnectionFactory Initialize(DatabaseConnectionSettings settings)
	{
		ArgumentNullException.ThrowIfNull(settings);
		var connectionFactory = DatabaseProviderFactory.CreateConnectionFactory(settings);
		Initialize(connectionFactory);
		return connectionFactory;
	}

	public static void Initialize(IDatabaseConnectionFactory connectionFactory)
	{
		ArgumentNullException.ThrowIfNull(connectionFactory);
		DatabaseProviderFactory.CreateInitializer(connectionFactory).Initialize();
		SalesSchemaMigration.Migrate(connectionFactory);
		FinanceInventoryAccountingSchemaMigration.Migrate(connectionFactory);
		UserSessionSchemaMigration.Migrate(connectionFactory);
		SecurityEventSchemaMigration.Migrate(connectionFactory);
	}
}
