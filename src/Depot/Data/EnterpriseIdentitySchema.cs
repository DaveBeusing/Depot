// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Data;

internal static class EnterpriseIdentitySchema
{
	public static void Ensure(IDatabaseConnectionFactory connectionFactory)
	{
		ArgumentNullException.ThrowIfNull(connectionFactory);
		using var connection = connectionFactory.CreateConnection();
		connection.Open();
		using var transaction = connectionFactory.BeginWriteTransaction(connection);
		using var command = connection.CreateCommand();
		command.Transaction = transaction;
		var statements = connectionFactory.Provider switch
		{
			DatabaseProvider.Local => Sqlite,
			DatabaseProvider.SqlServer => SqlServer,
			DatabaseProvider.MySql => MySql,
			_ => throw new NotSupportedException($"Enterprise identity schema is not supported for provider '{connectionFactory.Provider}'.")
		};
		foreach (var statement in statements)
		{
			command.CommandText = statement;
			command.ExecuteNonQuery();
		}
		transaction.Commit();
	}

	private static readonly string[] Sqlite =
	[
		"CREATE TABLE IF NOT EXISTS EnterpriseIdentityProviders (Id INTEGER PRIMARY KEY AUTOINCREMENT, Code TEXT NOT NULL, Kind INTEGER NOT NULL, DisplayName TEXT NOT NULL, Authority TEXT NOT NULL, ClientId TEXT NOT NULL, TenantId TEXT NULL, IsEnabled INTEGER NOT NULL, CreatedUtc TEXT NOT NULL, UpdatedUtc TEXT NOT NULL, Version INTEGER NOT NULL DEFAULT 1, UNIQUE(Code));",
		"CREATE TABLE IF NOT EXISTS ExternalIdentityLinks (Id INTEGER PRIMARY KEY AUTOINCREMENT, ProviderId INTEGER NOT NULL, UserId INTEGER NOT NULL, Issuer TEXT NOT NULL, Subject TEXT NOT NULL, IdentityKeySha256 TEXT NOT NULL, TenantId TEXT NULL, Email TEXT NULL, DisplayName TEXT NULL, LinkedUtc TEXT NOT NULL, LastSeenUtc TEXT NULL, Version INTEGER NOT NULL DEFAULT 1, FOREIGN KEY(ProviderId) REFERENCES EnterpriseIdentityProviders(Id) ON DELETE RESTRICT, FOREIGN KEY(UserId) REFERENCES Users(Id) ON DELETE CASCADE, UNIQUE(IdentityKeySha256));",
		"CREATE INDEX IF NOT EXISTS IX_ExternalIdentityLinks_User ON ExternalIdentityLinks(UserId);",
		"CREATE INDEX IF NOT EXISTS IX_ExternalIdentityLinks_Provider_User ON ExternalIdentityLinks(ProviderId, UserId);"
	];

	private static readonly string[] SqlServer =
	[
		"IF OBJECT_ID(N'EnterpriseIdentityProviders', N'U') IS NULL CREATE TABLE EnterpriseIdentityProviders (Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_EnterpriseIdentityProviders PRIMARY KEY, Code nvarchar(64) NOT NULL, Kind int NOT NULL, DisplayName nvarchar(120) NOT NULL, Authority nvarchar(512) NOT NULL, ClientId nvarchar(200) NOT NULL, TenantId nvarchar(128) NULL, IsEnabled bit NOT NULL, CreatedUtc nvarchar(40) NOT NULL, UpdatedUtc nvarchar(40) NOT NULL, Version bigint NOT NULL CONSTRAINT DF_EnterpriseIdentityProviders_Version DEFAULT 1, CONSTRAINT UQ_EnterpriseIdentityProviders_Code UNIQUE(Code));",
		"IF OBJECT_ID(N'ExternalIdentityLinks', N'U') IS NULL CREATE TABLE ExternalIdentityLinks (Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ExternalIdentityLinks PRIMARY KEY, ProviderId bigint NOT NULL, UserId bigint NOT NULL, Issuer nvarchar(512) NOT NULL, Subject nvarchar(512) NOT NULL, IdentityKeySha256 varchar(64) NOT NULL, TenantId nvarchar(128) NULL, Email nvarchar(320) NULL, DisplayName nvarchar(200) NULL, LinkedUtc nvarchar(40) NOT NULL, LastSeenUtc nvarchar(40) NULL, Version bigint NOT NULL CONSTRAINT DF_ExternalIdentityLinks_Version DEFAULT 1, CONSTRAINT FK_ExternalIdentityLinks_Provider FOREIGN KEY(ProviderId) REFERENCES EnterpriseIdentityProviders(Id), CONSTRAINT FK_ExternalIdentityLinks_Users FOREIGN KEY(UserId) REFERENCES Users(Id) ON DELETE CASCADE, CONSTRAINT UQ_ExternalIdentityLinks_IdentityKey UNIQUE(IdentityKeySha256));",
		"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_ExternalIdentityLinks_User' AND object_id=OBJECT_ID(N'ExternalIdentityLinks')) CREATE INDEX IX_ExternalIdentityLinks_User ON ExternalIdentityLinks(UserId);",
		"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_ExternalIdentityLinks_Provider_User' AND object_id=OBJECT_ID(N'ExternalIdentityLinks')) CREATE INDEX IX_ExternalIdentityLinks_Provider_User ON ExternalIdentityLinks(ProviderId, UserId);"
	];

	private static readonly string[] MySql =
	[
		"CREATE TABLE IF NOT EXISTS EnterpriseIdentityProviders (Id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY, Code VARCHAR(64) NOT NULL, Kind INT NOT NULL, DisplayName VARCHAR(120) NOT NULL, Authority VARCHAR(512) NOT NULL, ClientId VARCHAR(200) NOT NULL, TenantId VARCHAR(128) NULL, IsEnabled TINYINT(1) NOT NULL, CreatedUtc VARCHAR(40) NOT NULL, UpdatedUtc VARCHAR(40) NOT NULL, Version BIGINT NOT NULL DEFAULT 1, UNIQUE KEY UQ_EnterpriseIdentityProviders_Code(Code)) ENGINE=InnoDB;",
		"CREATE TABLE IF NOT EXISTS ExternalIdentityLinks (Id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY, ProviderId BIGINT NOT NULL, UserId BIGINT NOT NULL, Issuer VARCHAR(512) NOT NULL, Subject VARCHAR(512) NOT NULL, IdentityKeySha256 CHAR(64) NOT NULL, TenantId VARCHAR(128) NULL, Email VARCHAR(320) NULL, DisplayName VARCHAR(200) NULL, LinkedUtc VARCHAR(40) NOT NULL, LastSeenUtc VARCHAR(40) NULL, Version BIGINT NOT NULL DEFAULT 1, CONSTRAINT FK_ExternalIdentityLinks_Provider FOREIGN KEY(ProviderId) REFERENCES EnterpriseIdentityProviders(Id) ON DELETE RESTRICT, CONSTRAINT FK_ExternalIdentityLinks_Users FOREIGN KEY(UserId) REFERENCES Users(Id) ON DELETE CASCADE, UNIQUE KEY UQ_ExternalIdentityLinks_IdentityKey(IdentityKeySha256), INDEX IX_ExternalIdentityLinks_User(UserId), INDEX IX_ExternalIdentityLinks_Provider_User(ProviderId, UserId)) ENGINE=InnoDB;"
	];
}
