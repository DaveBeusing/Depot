// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Data;

public static class ProjectAccountingSchema
{
	public static void Ensure(IDatabaseConnectionFactory connectionFactory)
	{
		ArgumentNullException.ThrowIfNull(connectionFactory);
		using var connection = connectionFactory.CreateConnection();
		connection.Open();
		using var command = connection.CreateCommand();
		command.CommandText = connectionFactory.Provider switch
		{
			DatabaseProvider.Local => Sqlite,
			DatabaseProvider.SqlServer => SqlServer,
			DatabaseProvider.MySql => MySql,
			_ => throw new NotSupportedException($"Project-accounting persistence is not supported for provider '{connectionFactory.Provider}'.")
		};
		command.ExecuteNonQuery();
	}

	private const string Sqlite = """
		CREATE TABLE IF NOT EXISTS Projects (
			Id INTEGER PRIMARY KEY AUTOINCREMENT, Version INTEGER NOT NULL DEFAULT 1, Code TEXT NOT NULL, Name TEXT NOT NULL,
			LegalEntityId TEXT NOT NULL, OwnerUserId INTEGER NOT NULL, CustomerId INTEGER NULL, PlannedStartDate TEXT NULL, PlannedEndDate TEXT NULL,
			Status INTEGER NOT NULL, Description TEXT NULL, CreatedAtUtc TEXT NOT NULL, CreatedByUserId INTEGER NOT NULL,
			UpdatedAtUtc TEXT NOT NULL, UpdatedByUserId INTEGER NOT NULL, ClosedAtUtc TEXT NULL, ClosedByUserId INTEGER NULL,
			CancelledAtUtc TEXT NULL, CancelledByUserId INTEGER NULL, UNIQUE(LegalEntityId,Code),
			FOREIGN KEY(OwnerUserId) REFERENCES Users(Id), FOREIGN KEY(CustomerId) REFERENCES Customers(Id),
			CHECK(Status BETWEEN 1 AND 5));
		CREATE INDEX IF NOT EXISTS IX_Projects_Owner_Status ON Projects(OwnerUserId,Status,PlannedEndDate,Id);
		CREATE INDEX IF NOT EXISTS IX_Projects_Entity_Status ON Projects(LegalEntityId,Status,Code,Id);
		CREATE TABLE IF NOT EXISTS ProjectPhases (
			Id INTEGER PRIMARY KEY AUTOINCREMENT, Version INTEGER NOT NULL DEFAULT 1, ProjectId INTEGER NOT NULL, Code TEXT NOT NULL, Name TEXT NOT NULL,
			PlannedStartDate TEXT NULL, PlannedEndDate TEXT NULL, Status INTEGER NOT NULL, Description TEXT NULL,
			UNIQUE(ProjectId,Code), FOREIGN KEY(ProjectId) REFERENCES Projects(Id), CHECK(Status BETWEEN 1 AND 4));
		CREATE INDEX IF NOT EXISTS IX_ProjectPhases_Project_Status ON ProjectPhases(ProjectId,Status,Code,Id);
		CREATE TABLE IF NOT EXISTS ProjectAttributions (
			Id INTEGER PRIMARY KEY AUTOINCREMENT, Version INTEGER NOT NULL DEFAULT 1, ProjectId INTEGER NOT NULL, ProjectPhaseId INTEGER NULL,
			EntityKind INTEGER NOT NULL, EntityId INTEGER NOT NULL, SourceType TEXT NULL, SourceId TEXT NULL, JournalEntryId INTEGER NULL,
			IsImmutable INTEGER NOT NULL DEFAULT 0, CreatedAtUtc TEXT NOT NULL, CreatedByUserId INTEGER NOT NULL,
			UNIQUE(EntityKind,EntityId), FOREIGN KEY(ProjectId) REFERENCES Projects(Id), FOREIGN KEY(ProjectPhaseId) REFERENCES ProjectPhases(Id),
			CHECK(EntityKind BETWEEN 1 AND 5));
		CREATE INDEX IF NOT EXISTS IX_ProjectAttributions_Project ON ProjectAttributions(ProjectId,ProjectPhaseId,EntityKind,EntityId);
		CREATE INDEX IF NOT EXISTS IX_ProjectAttributions_Source ON ProjectAttributions(SourceType,SourceId);
		CREATE INDEX IF NOT EXISTS IX_ProjectAttributions_Journal ON ProjectAttributions(JournalEntryId);
		CREATE TABLE IF NOT EXISTS ProjectBudgetLineLinks (
			Id INTEGER PRIMARY KEY AUTOINCREMENT, ProjectId INTEGER NOT NULL, ProjectPhaseId INTEGER NULL, FinanceBudgetLineId INTEGER NOT NULL,
			CategoryCode TEXT NULL, CreatedAtUtc TEXT NOT NULL, CreatedByUserId INTEGER NOT NULL, UNIQUE(FinanceBudgetLineId),
			FOREIGN KEY(ProjectId) REFERENCES Projects(Id), FOREIGN KEY(ProjectPhaseId) REFERENCES ProjectPhases(Id),
			FOREIGN KEY(FinanceBudgetLineId) REFERENCES FinanceBudgetLines(Id));
		CREATE INDEX IF NOT EXISTS IX_ProjectBudgetLineLinks_Project ON ProjectBudgetLineLinks(ProjectId,ProjectPhaseId,FinanceBudgetLineId);
		""";

	private const string SqlServer = """
		IF OBJECT_ID(N'Projects',N'U') IS NULL CREATE TABLE Projects (
			Id bigint IDENTITY(1,1) PRIMARY KEY, Version bigint NOT NULL DEFAULT 1, Code nvarchar(50) NOT NULL, Name nvarchar(200) NOT NULL,
			LegalEntityId nvarchar(36) NOT NULL, OwnerUserId bigint NOT NULL, CustomerId bigint NULL, PlannedStartDate date NULL, PlannedEndDate date NULL,
			Status int NOT NULL, Description nvarchar(2000) NULL, CreatedAtUtc datetime2 NOT NULL, CreatedByUserId bigint NOT NULL,
			UpdatedAtUtc datetime2 NOT NULL, UpdatedByUserId bigint NOT NULL, ClosedAtUtc datetime2 NULL, ClosedByUserId bigint NULL,
			CancelledAtUtc datetime2 NULL, CancelledByUserId bigint NULL,
			CONSTRAINT UQ_Projects_Entity_Code UNIQUE(LegalEntityId,Code), CONSTRAINT FK_Projects_Owner FOREIGN KEY(OwnerUserId) REFERENCES Users(Id),
			CONSTRAINT FK_Projects_Customer FOREIGN KEY(CustomerId) REFERENCES Customers(Id), CONSTRAINT CK_Projects_Status CHECK(Status BETWEEN 1 AND 5));
		IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_Projects_Owner_Status' AND object_id=OBJECT_ID(N'Projects')) CREATE INDEX IX_Projects_Owner_Status ON Projects(OwnerUserId,Status,PlannedEndDate,Id);
		IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_Projects_Entity_Status' AND object_id=OBJECT_ID(N'Projects')) CREATE INDEX IX_Projects_Entity_Status ON Projects(LegalEntityId,Status,Code,Id);
		IF OBJECT_ID(N'ProjectPhases',N'U') IS NULL CREATE TABLE ProjectPhases (
			Id bigint IDENTITY(1,1) PRIMARY KEY, Version bigint NOT NULL DEFAULT 1, ProjectId bigint NOT NULL, Code nvarchar(50) NOT NULL, Name nvarchar(200) NOT NULL,
			PlannedStartDate date NULL, PlannedEndDate date NULL, Status int NOT NULL, Description nvarchar(2000) NULL,
			CONSTRAINT UQ_ProjectPhases_Project_Code UNIQUE(ProjectId,Code), CONSTRAINT FK_ProjectPhases_Project FOREIGN KEY(ProjectId) REFERENCES Projects(Id),
			CONSTRAINT CK_ProjectPhases_Status CHECK(Status BETWEEN 1 AND 4));
		IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_ProjectPhases_Project_Status' AND object_id=OBJECT_ID(N'ProjectPhases')) CREATE INDEX IX_ProjectPhases_Project_Status ON ProjectPhases(ProjectId,Status,Code,Id);
		IF OBJECT_ID(N'ProjectAttributions',N'U') IS NULL CREATE TABLE ProjectAttributions (
			Id bigint IDENTITY(1,1) PRIMARY KEY, Version bigint NOT NULL DEFAULT 1, ProjectId bigint NOT NULL, ProjectPhaseId bigint NULL,
			EntityKind int NOT NULL, EntityId bigint NOT NULL, SourceType nvarchar(100) NULL, SourceId nvarchar(200) NULL, JournalEntryId bigint NULL,
			IsImmutable bit NOT NULL DEFAULT 0, CreatedAtUtc datetime2 NOT NULL, CreatedByUserId bigint NOT NULL,
			CONSTRAINT UQ_ProjectAttributions_Entity UNIQUE(EntityKind,EntityId), CONSTRAINT FK_ProjectAttributions_Project FOREIGN KEY(ProjectId) REFERENCES Projects(Id),
			CONSTRAINT FK_ProjectAttributions_Phase FOREIGN KEY(ProjectPhaseId) REFERENCES ProjectPhases(Id), CONSTRAINT CK_ProjectAttributions_Kind CHECK(EntityKind BETWEEN 1 AND 5));
		IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_ProjectAttributions_Project' AND object_id=OBJECT_ID(N'ProjectAttributions')) CREATE INDEX IX_ProjectAttributions_Project ON ProjectAttributions(ProjectId,ProjectPhaseId,EntityKind,EntityId);
		IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_ProjectAttributions_Source' AND object_id=OBJECT_ID(N'ProjectAttributions')) CREATE INDEX IX_ProjectAttributions_Source ON ProjectAttributions(SourceType,SourceId);
		IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_ProjectAttributions_Journal' AND object_id=OBJECT_ID(N'ProjectAttributions')) CREATE INDEX IX_ProjectAttributions_Journal ON ProjectAttributions(JournalEntryId);
		IF OBJECT_ID(N'ProjectBudgetLineLinks',N'U') IS NULL CREATE TABLE ProjectBudgetLineLinks (
			Id bigint IDENTITY(1,1) PRIMARY KEY, ProjectId bigint NOT NULL, ProjectPhaseId bigint NULL, FinanceBudgetLineId bigint NOT NULL,
			CategoryCode nvarchar(100) NULL, CreatedAtUtc datetime2 NOT NULL, CreatedByUserId bigint NOT NULL,
			CONSTRAINT UQ_ProjectBudgetLineLinks_Line UNIQUE(FinanceBudgetLineId), CONSTRAINT FK_ProjectBudgetLineLinks_Project FOREIGN KEY(ProjectId) REFERENCES Projects(Id),
			CONSTRAINT FK_ProjectBudgetLineLinks_Phase FOREIGN KEY(ProjectPhaseId) REFERENCES ProjectPhases(Id),
			CONSTRAINT FK_ProjectBudgetLineLinks_BudgetLine FOREIGN KEY(FinanceBudgetLineId) REFERENCES FinanceBudgetLines(Id));
		IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_ProjectBudgetLineLinks_Project' AND object_id=OBJECT_ID(N'ProjectBudgetLineLinks')) CREATE INDEX IX_ProjectBudgetLineLinks_Project ON ProjectBudgetLineLinks(ProjectId,ProjectPhaseId,FinanceBudgetLineId);
		""";

	private const string MySql = """
		CREATE TABLE IF NOT EXISTS Projects (
			Id bigint NOT NULL AUTO_INCREMENT PRIMARY KEY, Version bigint NOT NULL DEFAULT 1, Code varchar(50) NOT NULL, Name varchar(200) NOT NULL,
			LegalEntityId varchar(36) NOT NULL, OwnerUserId bigint NOT NULL, CustomerId bigint NULL, PlannedStartDate date NULL, PlannedEndDate date NULL,
			Status int NOT NULL, Description varchar(2000) NULL, CreatedAtUtc datetime(6) NOT NULL, CreatedByUserId bigint NOT NULL,
			UpdatedAtUtc datetime(6) NOT NULL, UpdatedByUserId bigint NOT NULL, ClosedAtUtc datetime(6) NULL, ClosedByUserId bigint NULL,
			CancelledAtUtc datetime(6) NULL, CancelledByUserId bigint NULL, UNIQUE KEY UQ_Projects_Entity_Code(LegalEntityId,Code),
			INDEX IX_Projects_Owner_Status(OwnerUserId,Status,PlannedEndDate,Id), INDEX IX_Projects_Entity_Status(LegalEntityId,Status,Code,Id),
			CONSTRAINT FK_Projects_Owner FOREIGN KEY(OwnerUserId) REFERENCES Users(Id), CONSTRAINT FK_Projects_Customer FOREIGN KEY(CustomerId) REFERENCES Customers(Id),
			CONSTRAINT CK_Projects_Status CHECK(Status BETWEEN 1 AND 5)) ENGINE=InnoDB;
		CREATE TABLE IF NOT EXISTS ProjectPhases (
			Id bigint NOT NULL AUTO_INCREMENT PRIMARY KEY, Version bigint NOT NULL DEFAULT 1, ProjectId bigint NOT NULL, Code varchar(50) NOT NULL, Name varchar(200) NOT NULL,
			PlannedStartDate date NULL, PlannedEndDate date NULL, Status int NOT NULL, Description varchar(2000) NULL,
			UNIQUE KEY UQ_ProjectPhases_Project_Code(ProjectId,Code), INDEX IX_ProjectPhases_Project_Status(ProjectId,Status,Code,Id),
			CONSTRAINT FK_ProjectPhases_Project FOREIGN KEY(ProjectId) REFERENCES Projects(Id), CONSTRAINT CK_ProjectPhases_Status CHECK(Status BETWEEN 1 AND 4)) ENGINE=InnoDB;
		CREATE TABLE IF NOT EXISTS ProjectAttributions (
			Id bigint NOT NULL AUTO_INCREMENT PRIMARY KEY, Version bigint NOT NULL DEFAULT 1, ProjectId bigint NOT NULL, ProjectPhaseId bigint NULL,
			EntityKind int NOT NULL, EntityId bigint NOT NULL, SourceType varchar(100) NULL, SourceId varchar(200) NULL, JournalEntryId bigint NULL,
			IsImmutable boolean NOT NULL DEFAULT false, CreatedAtUtc datetime(6) NOT NULL, CreatedByUserId bigint NOT NULL,
			UNIQUE KEY UQ_ProjectAttributions_Entity(EntityKind,EntityId), INDEX IX_ProjectAttributions_Project(ProjectId,ProjectPhaseId,EntityKind,EntityId),
			INDEX IX_ProjectAttributions_Source(SourceType,SourceId), INDEX IX_ProjectAttributions_Journal(JournalEntryId),
			CONSTRAINT FK_ProjectAttributions_Project FOREIGN KEY(ProjectId) REFERENCES Projects(Id), CONSTRAINT FK_ProjectAttributions_Phase FOREIGN KEY(ProjectPhaseId) REFERENCES ProjectPhases(Id),
			CONSTRAINT CK_ProjectAttributions_Kind CHECK(EntityKind BETWEEN 1 AND 5)) ENGINE=InnoDB;
		CREATE TABLE IF NOT EXISTS ProjectBudgetLineLinks (
			Id bigint NOT NULL AUTO_INCREMENT PRIMARY KEY, ProjectId bigint NOT NULL, ProjectPhaseId bigint NULL, FinanceBudgetLineId bigint NOT NULL,
			CategoryCode varchar(100) NULL, CreatedAtUtc datetime(6) NOT NULL, CreatedByUserId bigint NOT NULL,
			UNIQUE KEY UQ_ProjectBudgetLineLinks_Line(FinanceBudgetLineId), INDEX IX_ProjectBudgetLineLinks_Project(ProjectId,ProjectPhaseId,FinanceBudgetLineId),
			CONSTRAINT FK_ProjectBudgetLineLinks_Project FOREIGN KEY(ProjectId) REFERENCES Projects(Id), CONSTRAINT FK_ProjectBudgetLineLinks_Phase FOREIGN KEY(ProjectPhaseId) REFERENCES ProjectPhases(Id),
			CONSTRAINT FK_ProjectBudgetLineLinks_BudgetLine FOREIGN KEY(FinanceBudgetLineId) REFERENCES FinanceBudgetLines(Id)) ENGINE=InnoDB;
		""";
}
