// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

namespace Depot.Data;

public static class SalesInvoiceFinalizationSchema
{
	public static void Ensure(IDatabaseConnectionFactory connectionFactory)
	{
		using var connection = connectionFactory.CreateConnection();
		connection.Open();
		using var command = connection.CreateCommand();
		var statements = connectionFactory.Provider switch
		{
			DatabaseProvider.Local => Sqlite,
			DatabaseProvider.SqlServer => SqlServer,
			DatabaseProvider.MySql => MySql,
			_ => throw new NotSupportedException($"Invoice finalization schema is not supported for provider '{connectionFactory.Provider}'.")
		};
		foreach (var statement in statements)
		{
			command.CommandText = statement;
			command.ExecuteNonQuery();
		}
	}

	private static readonly string[] Sqlite =
	[
		"ALTER TABLE Customers ADD COLUMN VatId TEXT NULL;",
		"ALTER TABLE Customers ADD COLUMN BuyerReference TEXT NULL;",
		"ALTER TABLE Customers ADD COLUMN EInvoiceEndpoint TEXT NULL;",
		"ALTER TABLE Customers ADD COLUMN EInvoiceEndpointScheme TEXT NULL;",
		"ALTER TABLE Customers ADD COLUMN BillingStreet TEXT NULL;",
		"ALTER TABLE Customers ADD COLUMN BillingAddressLine2 TEXT NULL;",
		"ALTER TABLE Customers ADD COLUMN BillingPostalCode TEXT NULL;",
		"ALTER TABLE Customers ADD COLUMN BillingCity TEXT NULL;",
		"ALTER TABLE Customers ADD COLUMN BillingCountryCode TEXT NULL;",
		"CREATE TABLE IF NOT EXISTS SalesInvoiceFinalizations (SalesInvoiceId INTEGER NOT NULL PRIMARY KEY, BuyerPayload TEXT NOT NULL, XRechnungXml TEXT NOT NULL, XRechnungSha256 TEXT NOT NULL, FinalizedAtUtc TEXT NOT NULL, FOREIGN KEY(SalesInvoiceId) REFERENCES SalesInvoices(Id));"
	];

	private static readonly string[] SqlServer =
	[
		"IF COL_LENGTH('Customers','VatId') IS NULL ALTER TABLE Customers ADD VatId nvarchar(100) NULL;",
		"IF COL_LENGTH('Customers','BuyerReference') IS NULL ALTER TABLE Customers ADD BuyerReference nvarchar(250) NULL;",
		"IF COL_LENGTH('Customers','EInvoiceEndpoint') IS NULL ALTER TABLE Customers ADD EInvoiceEndpoint nvarchar(250) NULL;",
		"IF COL_LENGTH('Customers','EInvoiceEndpointScheme') IS NULL ALTER TABLE Customers ADD EInvoiceEndpointScheme nvarchar(50) NULL;",
		"IF COL_LENGTH('Customers','BillingStreet') IS NULL ALTER TABLE Customers ADD BillingStreet nvarchar(250) NULL;",
		"IF COL_LENGTH('Customers','BillingAddressLine2') IS NULL ALTER TABLE Customers ADD BillingAddressLine2 nvarchar(250) NULL;",
		"IF COL_LENGTH('Customers','BillingPostalCode') IS NULL ALTER TABLE Customers ADD BillingPostalCode nvarchar(50) NULL;",
		"IF COL_LENGTH('Customers','BillingCity') IS NULL ALTER TABLE Customers ADD BillingCity nvarchar(250) NULL;",
		"IF COL_LENGTH('Customers','BillingCountryCode') IS NULL ALTER TABLE Customers ADD BillingCountryCode nvarchar(2) NULL;",
		"IF OBJECT_ID(N'SalesInvoiceFinalizations',N'U') IS NULL CREATE TABLE SalesInvoiceFinalizations (SalesInvoiceId bigint NOT NULL CONSTRAINT PK_SalesInvoiceFinalizations PRIMARY KEY, BuyerPayload nvarchar(max) NOT NULL, XRechnungXml nvarchar(max) NOT NULL, XRechnungSha256 char(64) NOT NULL, FinalizedAtUtc nvarchar(40) NOT NULL, CONSTRAINT FK_SalesInvoiceFinalizations_Invoice FOREIGN KEY(SalesInvoiceId) REFERENCES SalesInvoices(Id));"
	];

	private static readonly string[] MySql =
	[
		"ALTER TABLE Customers ADD COLUMN IF NOT EXISTS VatId VARCHAR(100) NULL, ADD COLUMN IF NOT EXISTS BuyerReference VARCHAR(250) NULL, ADD COLUMN IF NOT EXISTS EInvoiceEndpoint VARCHAR(250) NULL, ADD COLUMN IF NOT EXISTS EInvoiceEndpointScheme VARCHAR(50) NULL, ADD COLUMN IF NOT EXISTS BillingStreet VARCHAR(250) NULL, ADD COLUMN IF NOT EXISTS BillingAddressLine2 VARCHAR(250) NULL, ADD COLUMN IF NOT EXISTS BillingPostalCode VARCHAR(50) NULL, ADD COLUMN IF NOT EXISTS BillingCity VARCHAR(250) NULL, ADD COLUMN IF NOT EXISTS BillingCountryCode VARCHAR(2) NULL;",
		"CREATE TABLE IF NOT EXISTS SalesInvoiceFinalizations (SalesInvoiceId BIGINT NOT NULL PRIMARY KEY, BuyerPayload LONGTEXT NOT NULL, XRechnungXml LONGTEXT NOT NULL, XRechnungSha256 CHAR(64) NOT NULL, FinalizedAtUtc VARCHAR(40) NOT NULL, FOREIGN KEY(SalesInvoiceId) REFERENCES SalesInvoices(Id)) ENGINE=InnoDB;"
	];
}
