// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Data;

internal static class UserPreferenceSchema
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
			_ => throw new NotSupportedException($"User preference schema is not supported for provider '{connectionFactory.Provider}'.")
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
		"CREATE TABLE IF NOT EXISTS UserWorkspaceViews (Id INTEGER PRIMARY KEY AUTOINCREMENT, UserId INTEGER NOT NULL, WorkspaceId TEXT NOT NULL, ViewId TEXT NOT NULL, Name TEXT NOT NULL, DefinitionJson TEXT NOT NULL, UpdatedUtc TEXT NOT NULL, Version INTEGER NOT NULL DEFAULT 1, FOREIGN KEY(UserId) REFERENCES Users(Id) ON DELETE CASCADE, UNIQUE(UserId, WorkspaceId, ViewId), UNIQUE(UserId, WorkspaceId, Name));",
		"CREATE INDEX IF NOT EXISTS IX_UserWorkspaceViews_User_Workspace ON UserWorkspaceViews(UserId, WorkspaceId);",
		"CREATE TABLE IF NOT EXISTS UserWorkspaceDefaultViews (UserId INTEGER NOT NULL, WorkspaceId TEXT NOT NULL, ViewId TEXT NOT NULL, UpdatedUtc TEXT NOT NULL, Version INTEGER NOT NULL DEFAULT 1, PRIMARY KEY(UserId, WorkspaceId), FOREIGN KEY(UserId) REFERENCES Users(Id) ON DELETE CASCADE);",
		"CREATE TABLE IF NOT EXISTS UserWorkspaceFavorites (UserId INTEGER NOT NULL, RouteId TEXT NOT NULL, PinnedUtc TEXT NOT NULL, PRIMARY KEY(UserId, RouteId), FOREIGN KEY(UserId) REFERENCES Users(Id) ON DELETE CASCADE);",
		"CREATE INDEX IF NOT EXISTS IX_UserWorkspaceFavorites_User_Pinned ON UserWorkspaceFavorites(UserId, PinnedUtc DESC);",
		"CREATE TABLE IF NOT EXISTS UserWorkspaceRecents (UserId INTEGER NOT NULL, RouteId TEXT NOT NULL, LastUsedUtc TEXT NOT NULL, VisitCount INTEGER NOT NULL DEFAULT 1, PRIMARY KEY(UserId, RouteId), FOREIGN KEY(UserId) REFERENCES Users(Id) ON DELETE CASCADE);",
		"CREATE INDEX IF NOT EXISTS IX_UserWorkspaceRecents_User_LastUsed ON UserWorkspaceRecents(UserId, LastUsedUtc DESC);",
		"CREATE TABLE IF NOT EXISTS UserWorkspacePreferences (UserId INTEGER PRIMARY KEY, DefaultLandingRouteId TEXT NULL, UpdatedUtc TEXT NOT NULL, Version INTEGER NOT NULL DEFAULT 1, FOREIGN KEY(UserId) REFERENCES Users(Id) ON DELETE CASCADE);"
	];

	private static readonly string[] SqlServer =
	[
		"IF OBJECT_ID(N'UserWorkspaceViews', N'U') IS NULL CREATE TABLE UserWorkspaceViews (Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_UserWorkspaceViews PRIMARY KEY, UserId bigint NOT NULL, WorkspaceId nvarchar(160) NOT NULL, ViewId nvarchar(36) NOT NULL, Name nvarchar(120) NOT NULL, DefinitionJson nvarchar(max) NOT NULL, UpdatedUtc nvarchar(40) NOT NULL, Version bigint NOT NULL CONSTRAINT DF_UserWorkspaceViews_Version DEFAULT 1, CONSTRAINT FK_UserWorkspaceViews_Users FOREIGN KEY(UserId) REFERENCES Users(Id) ON DELETE CASCADE, CONSTRAINT UQ_UserWorkspaceViews_View UNIQUE(UserId, WorkspaceId, ViewId), CONSTRAINT UQ_UserWorkspaceViews_Name UNIQUE(UserId, WorkspaceId, Name));",
		"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_UserWorkspaceViews_User_Workspace' AND object_id=OBJECT_ID(N'UserWorkspaceViews')) CREATE INDEX IX_UserWorkspaceViews_User_Workspace ON UserWorkspaceViews(UserId, WorkspaceId);",
		"IF OBJECT_ID(N'UserWorkspaceDefaultViews', N'U') IS NULL CREATE TABLE UserWorkspaceDefaultViews (UserId bigint NOT NULL, WorkspaceId nvarchar(160) NOT NULL, ViewId nvarchar(36) NOT NULL, UpdatedUtc nvarchar(40) NOT NULL, Version bigint NOT NULL CONSTRAINT DF_UserWorkspaceDefaultViews_Version DEFAULT 1, CONSTRAINT PK_UserWorkspaceDefaultViews PRIMARY KEY(UserId, WorkspaceId), CONSTRAINT FK_UserWorkspaceDefaultViews_Users FOREIGN KEY(UserId) REFERENCES Users(Id) ON DELETE CASCADE);",
		"IF OBJECT_ID(N'UserWorkspaceFavorites', N'U') IS NULL CREATE TABLE UserWorkspaceFavorites (UserId bigint NOT NULL, RouteId nvarchar(160) NOT NULL, PinnedUtc nvarchar(40) NOT NULL, CONSTRAINT PK_UserWorkspaceFavorites PRIMARY KEY(UserId, RouteId), CONSTRAINT FK_UserWorkspaceFavorites_Users FOREIGN KEY(UserId) REFERENCES Users(Id) ON DELETE CASCADE);",
		"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_UserWorkspaceFavorites_User_Pinned' AND object_id=OBJECT_ID(N'UserWorkspaceFavorites')) CREATE INDEX IX_UserWorkspaceFavorites_User_Pinned ON UserWorkspaceFavorites(UserId, PinnedUtc DESC);",
		"IF OBJECT_ID(N'UserWorkspaceRecents', N'U') IS NULL CREATE TABLE UserWorkspaceRecents (UserId bigint NOT NULL, RouteId nvarchar(160) NOT NULL, LastUsedUtc nvarchar(40) NOT NULL, VisitCount bigint NOT NULL CONSTRAINT DF_UserWorkspaceRecents_VisitCount DEFAULT 1, CONSTRAINT PK_UserWorkspaceRecents PRIMARY KEY(UserId, RouteId), CONSTRAINT FK_UserWorkspaceRecents_Users FOREIGN KEY(UserId) REFERENCES Users(Id) ON DELETE CASCADE);",
		"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_UserWorkspaceRecents_User_LastUsed' AND object_id=OBJECT_ID(N'UserWorkspaceRecents')) CREATE INDEX IX_UserWorkspaceRecents_User_LastUsed ON UserWorkspaceRecents(UserId, LastUsedUtc DESC);",
		"IF OBJECT_ID(N'UserWorkspacePreferences', N'U') IS NULL CREATE TABLE UserWorkspacePreferences (UserId bigint NOT NULL CONSTRAINT PK_UserWorkspacePreferences PRIMARY KEY, DefaultLandingRouteId nvarchar(160) NULL, UpdatedUtc nvarchar(40) NOT NULL, Version bigint NOT NULL CONSTRAINT DF_UserWorkspacePreferences_Version DEFAULT 1, CONSTRAINT FK_UserWorkspacePreferences_Users FOREIGN KEY(UserId) REFERENCES Users(Id) ON DELETE CASCADE);"
	];

	private static readonly string[] MySql =
	[
		"CREATE TABLE IF NOT EXISTS UserWorkspaceViews (Id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY, UserId BIGINT NOT NULL, WorkspaceId VARCHAR(160) NOT NULL, ViewId VARCHAR(36) NOT NULL, Name VARCHAR(120) NOT NULL, DefinitionJson LONGTEXT NOT NULL, UpdatedUtc VARCHAR(40) NOT NULL, Version BIGINT NOT NULL DEFAULT 1, CONSTRAINT FK_UserWorkspaceViews_Users FOREIGN KEY(UserId) REFERENCES Users(Id) ON DELETE CASCADE, UNIQUE KEY UQ_UserWorkspaceViews_View(UserId, WorkspaceId, ViewId), UNIQUE KEY UQ_UserWorkspaceViews_Name(UserId, WorkspaceId, Name), INDEX IX_UserWorkspaceViews_User_Workspace(UserId, WorkspaceId)) ENGINE=InnoDB;",
		"CREATE TABLE IF NOT EXISTS UserWorkspaceDefaultViews (UserId BIGINT NOT NULL, WorkspaceId VARCHAR(160) NOT NULL, ViewId VARCHAR(36) NOT NULL, UpdatedUtc VARCHAR(40) NOT NULL, Version BIGINT NOT NULL DEFAULT 1, PRIMARY KEY(UserId, WorkspaceId), CONSTRAINT FK_UserWorkspaceDefaultViews_Users FOREIGN KEY(UserId) REFERENCES Users(Id) ON DELETE CASCADE) ENGINE=InnoDB;",
		"CREATE TABLE IF NOT EXISTS UserWorkspaceFavorites (UserId BIGINT NOT NULL, RouteId VARCHAR(160) NOT NULL, PinnedUtc VARCHAR(40) NOT NULL, PRIMARY KEY(UserId, RouteId), CONSTRAINT FK_UserWorkspaceFavorites_Users FOREIGN KEY(UserId) REFERENCES Users(Id) ON DELETE CASCADE, INDEX IX_UserWorkspaceFavorites_User_Pinned(UserId, PinnedUtc)) ENGINE=InnoDB;",
		"CREATE TABLE IF NOT EXISTS UserWorkspaceRecents (UserId BIGINT NOT NULL, RouteId VARCHAR(160) NOT NULL, LastUsedUtc VARCHAR(40) NOT NULL, VisitCount BIGINT NOT NULL DEFAULT 1, PRIMARY KEY(UserId, RouteId), CONSTRAINT FK_UserWorkspaceRecents_Users FOREIGN KEY(UserId) REFERENCES Users(Id) ON DELETE CASCADE, INDEX IX_UserWorkspaceRecents_User_LastUsed(UserId, LastUsedUtc)) ENGINE=InnoDB;",
		"CREATE TABLE IF NOT EXISTS UserWorkspacePreferences (UserId BIGINT NOT NULL PRIMARY KEY, DefaultLandingRouteId VARCHAR(160) NULL, UpdatedUtc VARCHAR(40) NOT NULL, Version BIGINT NOT NULL DEFAULT 1, CONSTRAINT FK_UserWorkspacePreferences_Users FOREIGN KEY(UserId) REFERENCES Users(Id) ON DELETE CASCADE) ENGINE=InnoDB;"
	];
}
