// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Data;

internal static class FinanceInventoryReportingSchemaInitializer
{
	public static void Ensure(IDatabaseConnectionFactory connectionFactory)
	{
		using var connection = connectionFactory.CreateConnection();
		connection.Open();
		using var command = connection.CreateCommand();
		command.CommandText = connectionFactory.Provider switch
		{
			DatabaseProvider.Local => "CREATE TABLE IF NOT EXISTS FinanceInventoryReportingConsumptions (ConsumptionId INTEGER NOT NULL PRIMARY KEY, ReportingCurrencyCode TEXT NOT NULL, ReportingAmount NUMERIC NOT NULL, FOREIGN KEY(ConsumptionId) REFERENCES FinanceInventoryValuationConsumptions(Id));",
			DatabaseProvider.SqlServer => "IF OBJECT_ID(N'FinanceInventoryReportingConsumptions',N'U') IS NULL CREATE TABLE FinanceInventoryReportingConsumptions (ConsumptionId bigint NOT NULL PRIMARY KEY, ReportingCurrencyCode nvarchar(3) NOT NULL, ReportingAmount decimal(28,9) NOT NULL, CONSTRAINT FK_FinanceInventoryReportingConsumptions_Consumption FOREIGN KEY(ConsumptionId) REFERENCES FinanceInventoryValuationConsumptions(Id));",
			DatabaseProvider.MySql => "CREATE TABLE IF NOT EXISTS FinanceInventoryReportingConsumptions (ConsumptionId BIGINT NOT NULL PRIMARY KEY, ReportingCurrencyCode VARCHAR(3) NOT NULL, ReportingAmount DECIMAL(28,9) NOT NULL, CONSTRAINT FK_FinanceInventoryReportingConsumptions_Consumption FOREIGN KEY(ConsumptionId) REFERENCES FinanceInventoryValuationConsumptions(Id));",
			_ => throw new NotSupportedException($"Inventory reporting valuation is not supported for provider '{connectionFactory.Provider}'.")
		};
		command.ExecuteNonQuery();
	}
}
