// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

namespace Depot.Data;

internal static class CustomerAddressSchema
{
	public static void Ensure(IDatabaseConnectionFactory connectionFactory)
	{
		using var connection = connectionFactory.CreateConnection();
		connection.Open();
		using var command = connection.CreateCommand();
		command.CommandText = connectionFactory.Provider switch
		{
			DatabaseProvider.Local =>
				"""
				CREATE TABLE IF NOT EXISTS CustomerAddresses
				(
					Id INTEGER PRIMARY KEY AUTOINCREMENT,
					CustomerId INTEGER NOT NULL REFERENCES Customers(Id),
					Type INTEGER NOT NULL,
					Name TEXT NULL,
					Address TEXT NOT NULL,
					IsDefault INTEGER NOT NULL DEFAULT 0,
					IsActive INTEGER NOT NULL DEFAULT 1,
					Version INTEGER NOT NULL DEFAULT 1
				);
				CREATE INDEX IF NOT EXISTS IX_CustomerAddresses_CustomerId_Type ON CustomerAddresses(CustomerId, Type, IsActive);
				""",
			DatabaseProvider.SqlServer =>
				"""
				IF OBJECT_ID(N'CustomerAddresses', N'U') IS NULL
				BEGIN
					CREATE TABLE CustomerAddresses
					(
						Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
						CustomerId bigint NOT NULL REFERENCES Customers(Id),
						Type int NOT NULL,
						Name nvarchar(250) NULL,
						Address nvarchar(2000) NOT NULL,
						IsDefault bit NOT NULL CONSTRAINT DF_CustomerAddresses_IsDefault DEFAULT 0,
						IsActive bit NOT NULL CONSTRAINT DF_CustomerAddresses_IsActive DEFAULT 1,
						Version bigint NOT NULL CONSTRAINT DF_CustomerAddresses_Version DEFAULT 1
					);
					CREATE INDEX IX_CustomerAddresses_CustomerId_Type ON CustomerAddresses(CustomerId, Type, IsActive);
				END;
				""",
			DatabaseProvider.MySql =>
				"""
				CREATE TABLE IF NOT EXISTS CustomerAddresses
				(
					Id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
					CustomerId BIGINT NOT NULL,
					Type INT NOT NULL,
					Name VARCHAR(250) NULL,
					Address VARCHAR(2000) NOT NULL,
					IsDefault BOOLEAN NOT NULL DEFAULT FALSE,
					IsActive BOOLEAN NOT NULL DEFAULT TRUE,
					Version BIGINT NOT NULL DEFAULT 1,
					CONSTRAINT FK_CustomerAddresses_Customers FOREIGN KEY (CustomerId) REFERENCES Customers(Id),
					INDEX IX_CustomerAddresses_CustomerId_Type (CustomerId, Type, IsActive)
				);
				""",
			_ => throw new NotSupportedException($"Customer address schema is not supported for provider '{connectionFactory.Provider}'.")
		};
		command.ExecuteNonQuery();
	}
}
