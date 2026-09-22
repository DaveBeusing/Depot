// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Data;

internal static class SalesCrmSchema
{
	public static void Ensure(IDatabaseConnectionFactory connectionFactory)
	{
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
			_ => throw new NotSupportedException($"Sales CRM schema is not supported for provider '{connectionFactory.Provider}'.")
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
		"CREATE TABLE SalesOpportunityStages (Id INTEGER PRIMARY KEY AUTOINCREMENT, Code TEXT NOT NULL UNIQUE, Name TEXT NOT NULL, SortOrder INTEGER NOT NULL UNIQUE, IsActive INTEGER NOT NULL DEFAULT 1, Version INTEGER NOT NULL DEFAULT 1);",
		"INSERT INTO SalesOpportunityStages (Code,Name,SortOrder,IsActive) VALUES ('PROSPECTING','Prospecting',10,1),('QUALIFICATION','Qualification',20,1),('PROPOSAL','Proposal',30,1),('NEGOTIATION','Negotiation',40,1),('DECISION','Decision',50,1);",
		"CREATE TABLE SalesLeads (Id INTEGER PRIMARY KEY AUTOINCREMENT, LeadNumber TEXT NOT NULL UNIQUE, CompanyName TEXT NULL, PersonName TEXT NULL, Email TEXT NULL, Phone TEXT NULL, Source TEXT NULL, OwnerUserId INTEGER NOT NULL, Status INTEGER NOT NULL, NotesSummary TEXT NULL, CreatedAtUtc TEXT NOT NULL, UpdatedAtUtc TEXT NOT NULL, ConvertedCustomerId INTEGER NULL, ConvertedOpportunityId INTEGER NULL, ConvertedAtUtc TEXT NULL, Version INTEGER NOT NULL DEFAULT 1, CHECK(Status IN (1,2,3,4,5)), CHECK(CompanyName IS NOT NULL OR PersonName IS NOT NULL), FOREIGN KEY(OwnerUserId) REFERENCES Users(Id), FOREIGN KEY(ConvertedCustomerId) REFERENCES Customers(Id));",
		"CREATE TABLE SalesOpportunities (Id INTEGER PRIMARY KEY AUTOINCREMENT, OpportunityNumber TEXT NOT NULL UNIQUE, CustomerId INTEGER NOT NULL, LeadId INTEGER NULL, OwnerUserId INTEGER NOT NULL, StageId INTEGER NOT NULL, ExpectedCloseDate TEXT NULL, Currency TEXT NOT NULL, ExpectedAmount NUMERIC NOT NULL DEFAULT 0, ProbabilityPercent INTEGER NOT NULL DEFAULT 0, NextActivityDate TEXT NULL, Outcome INTEGER NOT NULL DEFAULT 0, CloseReason TEXT NULL, ClosedAtUtc TEXT NULL, LinkedSalesQuoteId INTEGER NULL, CreatedAtUtc TEXT NOT NULL, UpdatedAtUtc TEXT NOT NULL, Version INTEGER NOT NULL DEFAULT 1, CHECK(ExpectedAmount>=0), CHECK(ProbabilityPercent BETWEEN 0 AND 100), CHECK(Outcome IN (0,1,2)), FOREIGN KEY(CustomerId) REFERENCES Customers(Id), FOREIGN KEY(LeadId) REFERENCES SalesLeads(Id), FOREIGN KEY(OwnerUserId) REFERENCES Users(Id), FOREIGN KEY(StageId) REFERENCES SalesOpportunityStages(Id), FOREIGN KEY(LinkedSalesQuoteId) REFERENCES SalesQuotes(Id));",
		"CREATE UNIQUE INDEX UX_SalesOpportunities_LeadId ON SalesOpportunities(LeadId) WHERE LeadId IS NOT NULL;",
		"CREATE TABLE SalesActivities (Id INTEGER PRIMARY KEY AUTOINCREMENT, LeadId INTEGER NULL, OpportunityId INTEGER NULL, Type INTEGER NOT NULL, DueAtUtc TEXT NOT NULL, OwnerUserId INTEGER NOT NULL, Status INTEGER NOT NULL DEFAULT 1, Subject TEXT NOT NULL, Notes TEXT NULL, CompletedAtUtc TEXT NULL, CompletedByUserId INTEGER NULL, CancelledAtUtc TEXT NULL, CancelledByUserId INTEGER NULL, Version INTEGER NOT NULL DEFAULT 1, CHECK(Type IN (1,2,3,4)), CHECK(Status IN (1,2,3)), CHECK(LeadId IS NOT NULL OR OpportunityId IS NOT NULL), FOREIGN KEY(LeadId) REFERENCES SalesLeads(Id), FOREIGN KEY(OpportunityId) REFERENCES SalesOpportunities(Id), FOREIGN KEY(OwnerUserId) REFERENCES Users(Id), FOREIGN KEY(CompletedByUserId) REFERENCES Users(Id), FOREIGN KEY(CancelledByUserId) REFERENCES Users(Id));",
		"CREATE INDEX IX_SalesLeads_Owner_Status ON SalesLeads(OwnerUserId,Status,UpdatedAtUtc DESC);",
		"CREATE INDEX IX_SalesLeads_Status_Source ON SalesLeads(Status,Source);",
		"CREATE INDEX IX_SalesOpportunities_Owner_Stage ON SalesOpportunities(OwnerUserId,StageId,Outcome,UpdatedAtUtc DESC);",
		"CREATE INDEX IX_SalesOpportunities_Stage_Close ON SalesOpportunities(StageId,Outcome,ExpectedCloseDate);",
		"CREATE INDEX IX_SalesActivities_Owner_Status_Due ON SalesActivities(OwnerUserId,Status,DueAtUtc);",
		"CREATE INDEX IX_SalesActivities_Opportunity ON SalesActivities(OpportunityId,Status,DueAtUtc);",
		"CREATE INDEX IX_SalesActivities_Lead ON SalesActivities(LeadId,Status,DueAtUtc);",
		"CREATE TRIGGER TR_SalesLeads_ConvertedOpportunity_Insert AFTER INSERT ON SalesOpportunities WHEN NEW.LeadId IS NOT NULL BEGIN UPDATE SalesLeads SET ConvertedOpportunityId=NEW.Id WHERE Id=NEW.LeadId AND ConvertedOpportunityId IS NULL; END;"
	];

	private static readonly string[] SqlServer =
	[
		"CREATE TABLE SalesOpportunityStages (Id bigint IDENTITY(1,1) PRIMARY KEY, Code nvarchar(50) NOT NULL UNIQUE, Name nvarchar(120) NOT NULL, SortOrder int NOT NULL UNIQUE, IsActive bit NOT NULL CONSTRAINT DF_SalesOpportunityStages_IsActive DEFAULT 1, Version bigint NOT NULL CONSTRAINT DF_SalesOpportunityStages_Version DEFAULT 1);",
		"INSERT INTO SalesOpportunityStages (Code,Name,SortOrder,IsActive) VALUES (N'PROSPECTING',N'Prospecting',10,1),(N'QUALIFICATION',N'Qualification',20,1),(N'PROPOSAL',N'Proposal',30,1),(N'NEGOTIATION',N'Negotiation',40,1),(N'DECISION',N'Decision',50,1);",
		"CREATE TABLE SalesLeads (Id bigint IDENTITY(1,1) PRIMARY KEY, LeadNumber nvarchar(40) NOT NULL UNIQUE, CompanyName nvarchar(250) NULL, PersonName nvarchar(250) NULL, Email nvarchar(250) NULL, Phone nvarchar(100) NULL, Source nvarchar(120) NULL, OwnerUserId bigint NOT NULL, Status int NOT NULL, NotesSummary nvarchar(2000) NULL, CreatedAtUtc datetime2 NOT NULL, UpdatedAtUtc datetime2 NOT NULL, ConvertedCustomerId bigint NULL, ConvertedOpportunityId bigint NULL, ConvertedAtUtc datetime2 NULL, Version bigint NOT NULL CONSTRAINT DF_SalesLeads_Version DEFAULT 1, CONSTRAINT CK_SalesLeads_Status CHECK(Status IN (1,2,3,4,5)), CONSTRAINT CK_SalesLeads_Identity CHECK(CompanyName IS NOT NULL OR PersonName IS NOT NULL), CONSTRAINT FK_SalesLeads_Users FOREIGN KEY(OwnerUserId) REFERENCES Users(Id), CONSTRAINT FK_SalesLeads_Customers FOREIGN KEY(ConvertedCustomerId) REFERENCES Customers(Id));",
		"CREATE TABLE SalesOpportunities (Id bigint IDENTITY(1,1) PRIMARY KEY, OpportunityNumber nvarchar(40) NOT NULL UNIQUE, CustomerId bigint NOT NULL, LeadId bigint NULL, OwnerUserId bigint NOT NULL, StageId bigint NOT NULL, ExpectedCloseDate date NULL, Currency nvarchar(3) NOT NULL, ExpectedAmount decimal(18,2) NOT NULL CONSTRAINT DF_SalesOpportunities_Amount DEFAULT 0, ProbabilityPercent int NOT NULL CONSTRAINT DF_SalesOpportunities_Probability DEFAULT 0, NextActivityDate datetime2 NULL, Outcome int NOT NULL CONSTRAINT DF_SalesOpportunities_Outcome DEFAULT 0, CloseReason nvarchar(500) NULL, ClosedAtUtc datetime2 NULL, LinkedSalesQuoteId bigint NULL, CreatedAtUtc datetime2 NOT NULL, UpdatedAtUtc datetime2 NOT NULL, Version bigint NOT NULL CONSTRAINT DF_SalesOpportunities_Version DEFAULT 1, CONSTRAINT CK_SalesOpportunities_Amount CHECK(ExpectedAmount>=0), CONSTRAINT CK_SalesOpportunities_Probability CHECK(ProbabilityPercent BETWEEN 0 AND 100), CONSTRAINT CK_SalesOpportunities_Outcome CHECK(Outcome IN (0,1,2)), CONSTRAINT FK_SalesOpportunities_Customers FOREIGN KEY(CustomerId) REFERENCES Customers(Id), CONSTRAINT FK_SalesOpportunities_Leads FOREIGN KEY(LeadId) REFERENCES SalesLeads(Id), CONSTRAINT FK_SalesOpportunities_Users FOREIGN KEY(OwnerUserId) REFERENCES Users(Id), CONSTRAINT FK_SalesOpportunities_Stages FOREIGN KEY(StageId) REFERENCES SalesOpportunityStages(Id), CONSTRAINT FK_SalesOpportunities_Quotes FOREIGN KEY(LinkedSalesQuoteId) REFERENCES SalesQuotes(Id));",
		"CREATE UNIQUE INDEX UX_SalesOpportunities_LeadId ON SalesOpportunities(LeadId) WHERE LeadId IS NOT NULL;",
		"CREATE TABLE SalesActivities (Id bigint IDENTITY(1,1) PRIMARY KEY, LeadId bigint NULL, OpportunityId bigint NULL, Type int NOT NULL, DueAtUtc datetime2 NOT NULL, OwnerUserId bigint NOT NULL, Status int NOT NULL CONSTRAINT DF_SalesActivities_Status DEFAULT 1, Subject nvarchar(250) NOT NULL, Notes nvarchar(4000) NULL, CompletedAtUtc datetime2 NULL, CompletedByUserId bigint NULL, CancelledAtUtc datetime2 NULL, CancelledByUserId bigint NULL, Version bigint NOT NULL CONSTRAINT DF_SalesActivities_Version DEFAULT 1, CONSTRAINT CK_SalesActivities_Type CHECK(Type IN (1,2,3,4)), CONSTRAINT CK_SalesActivities_Status CHECK(Status IN (1,2,3)), CONSTRAINT CK_SalesActivities_Target CHECK(LeadId IS NOT NULL OR OpportunityId IS NOT NULL), CONSTRAINT FK_SalesActivities_Leads FOREIGN KEY(LeadId) REFERENCES SalesLeads(Id), CONSTRAINT FK_SalesActivities_Opportunities FOREIGN KEY(OpportunityId) REFERENCES SalesOpportunities(Id), CONSTRAINT FK_SalesActivities_Owner FOREIGN KEY(OwnerUserId) REFERENCES Users(Id), CONSTRAINT FK_SalesActivities_CompletedBy FOREIGN KEY(CompletedByUserId) REFERENCES Users(Id), CONSTRAINT FK_SalesActivities_CancelledBy FOREIGN KEY(CancelledByUserId) REFERENCES Users(Id));",
		"CREATE INDEX IX_SalesLeads_Owner_Status ON SalesLeads(OwnerUserId,Status,UpdatedAtUtc DESC);",
		"CREATE INDEX IX_SalesLeads_Status_Source ON SalesLeads(Status,Source);",
		"CREATE INDEX IX_SalesOpportunities_Owner_Stage ON SalesOpportunities(OwnerUserId,StageId,Outcome,UpdatedAtUtc DESC);",
		"CREATE INDEX IX_SalesOpportunities_Stage_Close ON SalesOpportunities(StageId,Outcome,ExpectedCloseDate);",
		"CREATE INDEX IX_SalesActivities_Owner_Status_Due ON SalesActivities(OwnerUserId,Status,DueAtUtc);",
		"CREATE INDEX IX_SalesActivities_Opportunity ON SalesActivities(OpportunityId,Status,DueAtUtc);",
		"CREATE INDEX IX_SalesActivities_Lead ON SalesActivities(LeadId,Status,DueAtUtc);"
	];

	private static readonly string[] MySql =
	[
		"CREATE TABLE SalesOpportunityStages (Id BIGINT AUTO_INCREMENT PRIMARY KEY, Code VARCHAR(50) NOT NULL UNIQUE, Name VARCHAR(120) NOT NULL, SortOrder INT NOT NULL UNIQUE, IsActive TINYINT(1) NOT NULL DEFAULT 1, Version BIGINT NOT NULL DEFAULT 1) ENGINE=InnoDB;",
		"INSERT INTO SalesOpportunityStages (Code,Name,SortOrder,IsActive) VALUES ('PROSPECTING','Prospecting',10,1),('QUALIFICATION','Qualification',20,1),('PROPOSAL','Proposal',30,1),('NEGOTIATION','Negotiation',40,1),('DECISION','Decision',50,1);",
		"CREATE TABLE SalesLeads (Id BIGINT AUTO_INCREMENT PRIMARY KEY, LeadNumber VARCHAR(40) NOT NULL UNIQUE, CompanyName VARCHAR(250) NULL, PersonName VARCHAR(250) NULL, Email VARCHAR(250) NULL, Phone VARCHAR(100) NULL, Source VARCHAR(120) NULL, OwnerUserId BIGINT NOT NULL, Status INT NOT NULL, NotesSummary VARCHAR(2000) NULL, CreatedAtUtc DATETIME(6) NOT NULL, UpdatedAtUtc DATETIME(6) NOT NULL, ConvertedCustomerId BIGINT NULL, ConvertedOpportunityId BIGINT NULL, ConvertedAtUtc DATETIME(6) NULL, Version BIGINT NOT NULL DEFAULT 1, CHECK(Status IN (1,2,3,4,5)), CHECK(CompanyName IS NOT NULL OR PersonName IS NOT NULL), CONSTRAINT FK_SalesLeads_Users FOREIGN KEY(OwnerUserId) REFERENCES Users(Id), CONSTRAINT FK_SalesLeads_Customers FOREIGN KEY(ConvertedCustomerId) REFERENCES Customers(Id)) ENGINE=InnoDB;",
		"CREATE TABLE SalesOpportunities (Id BIGINT AUTO_INCREMENT PRIMARY KEY, OpportunityNumber VARCHAR(40) NOT NULL UNIQUE, CustomerId BIGINT NOT NULL, LeadId BIGINT NULL, OwnerUserId BIGINT NOT NULL, StageId BIGINT NOT NULL, ExpectedCloseDate DATE NULL, Currency VARCHAR(3) NOT NULL, ExpectedAmount DECIMAL(18,2) NOT NULL DEFAULT 0, ProbabilityPercent INT NOT NULL DEFAULT 0, NextActivityDate DATETIME(6) NULL, Outcome INT NOT NULL DEFAULT 0, CloseReason VARCHAR(500) NULL, ClosedAtUtc DATETIME(6) NULL, LinkedSalesQuoteId BIGINT NULL, CreatedAtUtc DATETIME(6) NOT NULL, UpdatedAtUtc DATETIME(6) NOT NULL, Version BIGINT NOT NULL DEFAULT 1, CHECK(ExpectedAmount>=0), CHECK(ProbabilityPercent BETWEEN 0 AND 100), CHECK(Outcome IN (0,1,2)), CONSTRAINT FK_SalesOpportunities_Customers FOREIGN KEY(CustomerId) REFERENCES Customers(Id), CONSTRAINT FK_SalesOpportunities_Leads FOREIGN KEY(LeadId) REFERENCES SalesLeads(Id), CONSTRAINT FK_SalesOpportunities_Users FOREIGN KEY(OwnerUserId) REFERENCES Users(Id), CONSTRAINT FK_SalesOpportunities_Stages FOREIGN KEY(StageId) REFERENCES SalesOpportunityStages(Id), CONSTRAINT FK_SalesOpportunities_Quotes FOREIGN KEY(LinkedSalesQuoteId) REFERENCES SalesQuotes(Id), UNIQUE KEY UX_SalesOpportunities_LeadId(LeadId)) ENGINE=InnoDB;",
		"CREATE TABLE SalesActivities (Id BIGINT AUTO_INCREMENT PRIMARY KEY, LeadId BIGINT NULL, OpportunityId BIGINT NULL, Type INT NOT NULL, DueAtUtc DATETIME(6) NOT NULL, OwnerUserId BIGINT NOT NULL, Status INT NOT NULL DEFAULT 1, Subject VARCHAR(250) NOT NULL, Notes VARCHAR(4000) NULL, CompletedAtUtc DATETIME(6) NULL, CompletedByUserId BIGINT NULL, CancelledAtUtc DATETIME(6) NULL, CancelledByUserId BIGINT NULL, Version BIGINT NOT NULL DEFAULT 1, CHECK(Type IN (1,2,3,4)), CHECK(Status IN (1,2,3)), CHECK(LeadId IS NOT NULL OR OpportunityId IS NOT NULL), CONSTRAINT FK_SalesActivities_Leads FOREIGN KEY(LeadId) REFERENCES SalesLeads(Id), CONSTRAINT FK_SalesActivities_Opportunities FOREIGN KEY(OpportunityId) REFERENCES SalesOpportunities(Id), CONSTRAINT FK_SalesActivities_Owner FOREIGN KEY(OwnerUserId) REFERENCES Users(Id), CONSTRAINT FK_SalesActivities_CompletedBy FOREIGN KEY(CompletedByUserId) REFERENCES Users(Id), CONSTRAINT FK_SalesActivities_CancelledBy FOREIGN KEY(CancelledByUserId) REFERENCES Users(Id), INDEX IX_SalesActivities_Owner_Status_Due(OwnerUserId,Status,DueAtUtc), INDEX IX_SalesActivities_Opportunity(OpportunityId,Status,DueAtUtc), INDEX IX_SalesActivities_Lead(LeadId,Status,DueAtUtc)) ENGINE=InnoDB;",
		"CREATE INDEX IX_SalesLeads_Owner_Status ON SalesLeads(OwnerUserId,Status,UpdatedAtUtc);",
		"CREATE INDEX IX_SalesLeads_Status_Source ON SalesLeads(Status,Source);",
		"CREATE INDEX IX_SalesOpportunities_Owner_Stage ON SalesOpportunities(OwnerUserId,StageId,Outcome,UpdatedAtUtc);",
		"CREATE INDEX IX_SalesOpportunities_Stage_Close ON SalesOpportunities(StageId,Outcome,ExpectedCloseDate);"
	];
}
