// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

namespace Depot.Data;

public static class ProcurementSourcingSchema
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
			_ => throw new NotSupportedException($"Procurement-sourcing persistence is not supported for provider '{connectionFactory.Provider}'.")
		};
		command.ExecuteNonQuery();
	}

	private const string Sqlite = """
		CREATE TABLE IF NOT EXISTS PurchaseRequisitions (
			Id INTEGER PRIMARY KEY AUTOINCREMENT, RequisitionNumber TEXT NOT NULL UNIQUE, Status INTEGER NOT NULL,
			RequestedByUserId INTEGER NOT NULL, CreatedByUserId INTEGER NOT NULL, CreatedAtUtc TEXT NOT NULL,
			SubmittedAtUtc TEXT NULL, ApprovalDecisionAtUtc TEXT NULL, ApprovalDecisionByUserId INTEGER NULL,
			ApprovalComment TEXT NULL, RequiredByDate TEXT NULL, PreferredSupplierId INTEGER NULL,
			BusinessJustification TEXT NULL, Version INTEGER NOT NULL DEFAULT 1,
			FOREIGN KEY(RequestedByUserId) REFERENCES Users(Id), FOREIGN KEY(CreatedByUserId) REFERENCES Users(Id),
			FOREIGN KEY(ApprovalDecisionByUserId) REFERENCES Users(Id), FOREIGN KEY(PreferredSupplierId) REFERENCES Suppliers(Id),
			CHECK(Status BETWEEN 1 AND 7));
		CREATE INDEX IF NOT EXISTS IX_PurchaseRequisitions_Status_Requester ON PurchaseRequisitions(Status, RequestedByUserId);
		CREATE INDEX IF NOT EXISTS IX_PurchaseRequisitions_RequiredByDate ON PurchaseRequisitions(RequiredByDate);
		CREATE TABLE IF NOT EXISTS PurchaseRequisitionLines (
			Id INTEGER PRIMARY KEY AUTOINCREMENT, PurchaseRequisitionId INTEGER NOT NULL, LineNumber INTEGER NOT NULL,
			ItemId INTEGER NOT NULL, Quantity INTEGER NOT NULL, Notes TEXT NULL, Version INTEGER NOT NULL DEFAULT 1,
			UNIQUE(PurchaseRequisitionId, LineNumber), UNIQUE(PurchaseRequisitionId, ItemId),
			FOREIGN KEY(PurchaseRequisitionId) REFERENCES PurchaseRequisitions(Id),
			FOREIGN KEY(ItemId) REFERENCES Items(Id), CHECK(Quantity > 0));
		CREATE INDEX IF NOT EXISTS IX_PurchaseRequisitionLines_ItemId ON PurchaseRequisitionLines(ItemId);
		CREATE TABLE IF NOT EXISTS RequestsForQuotation (
			Id INTEGER PRIMARY KEY AUTOINCREMENT, RfqNumber TEXT NOT NULL UNIQUE, PurchaseRequisitionId INTEGER NOT NULL,
			Status INTEGER NOT NULL, CreatedAtUtc TEXT NOT NULL, CreatedByUserId INTEGER NOT NULL, ResponseDueDate TEXT NULL,
			SelectedQuoteResponseId INTEGER NULL, SelectedByUserId INTEGER NULL, SelectedAtUtc TEXT NULL,
			ConvertedPurchaseOrderId INTEGER NULL UNIQUE, Version INTEGER NOT NULL DEFAULT 1,
			FOREIGN KEY(PurchaseRequisitionId) REFERENCES PurchaseRequisitions(Id), FOREIGN KEY(CreatedByUserId) REFERENCES Users(Id),
			FOREIGN KEY(SelectedByUserId) REFERENCES Users(Id), FOREIGN KEY(ConvertedPurchaseOrderId) REFERENCES PurchaseOrders(Id),
			CHECK(Status BETWEEN 1 AND 5));
		CREATE INDEX IF NOT EXISTS IX_RequestsForQuotation_Status_Due ON RequestsForQuotation(Status, ResponseDueDate);
		CREATE INDEX IF NOT EXISTS IX_RequestsForQuotation_Requisition ON RequestsForQuotation(PurchaseRequisitionId);
		CREATE TABLE IF NOT EXISTS RequestForQuotationSuppliers (
			RequestForQuotationId INTEGER NOT NULL, SupplierId INTEGER NOT NULL,
			PRIMARY KEY(RequestForQuotationId, SupplierId),
			FOREIGN KEY(RequestForQuotationId) REFERENCES RequestsForQuotation(Id),
			FOREIGN KEY(SupplierId) REFERENCES Suppliers(Id));
		CREATE INDEX IF NOT EXISTS IX_RequestForQuotationSuppliers_Supplier ON RequestForQuotationSuppliers(SupplierId, RequestForQuotationId);
		CREATE TABLE IF NOT EXISTS RequestForQuotationLines (
			Id INTEGER PRIMARY KEY AUTOINCREMENT, RequestForQuotationId INTEGER NOT NULL, PurchaseRequisitionLineId INTEGER NOT NULL,
			ItemId INTEGER NOT NULL, Quantity INTEGER NOT NULL, RequiredByDate TEXT NULL, Version INTEGER NOT NULL DEFAULT 1,
			UNIQUE(RequestForQuotationId, PurchaseRequisitionLineId),
			FOREIGN KEY(RequestForQuotationId) REFERENCES RequestsForQuotation(Id),
			FOREIGN KEY(PurchaseRequisitionLineId) REFERENCES PurchaseRequisitionLines(Id),
			FOREIGN KEY(ItemId) REFERENCES Items(Id), CHECK(Quantity > 0));
		CREATE TABLE IF NOT EXISTS SupplierQuoteResponses (
			Id INTEGER PRIMARY KEY AUTOINCREMENT, RequestForQuotationId INTEGER NOT NULL, SupplierId INTEGER NOT NULL,
			SupplierReference TEXT NULL, Currency TEXT NOT NULL, ValidUntil TEXT NULL, ReceivedAtUtc TEXT NOT NULL,
			CapturedByUserId INTEGER NOT NULL, Status INTEGER NOT NULL, Version INTEGER NOT NULL DEFAULT 1,
			FOREIGN KEY(RequestForQuotationId) REFERENCES RequestsForQuotation(Id), FOREIGN KEY(SupplierId) REFERENCES Suppliers(Id),
			FOREIGN KEY(CapturedByUserId) REFERENCES Users(Id), CHECK(Status BETWEEN 1 AND 3));
		CREATE INDEX IF NOT EXISTS IX_SupplierQuoteResponses_Rfq_Supplier ON SupplierQuoteResponses(RequestForQuotationId, SupplierId);
		CREATE INDEX IF NOT EXISTS IX_SupplierQuoteResponses_Validity ON SupplierQuoteResponses(Status, ValidUntil);
		CREATE TABLE IF NOT EXISTS SupplierQuoteResponseLines (
			Id INTEGER PRIMARY KEY AUTOINCREMENT, SupplierQuoteResponseId INTEGER NOT NULL, RequestForQuotationLineId INTEGER NOT NULL,
			ItemId INTEGER NOT NULL, Quantity INTEGER NOT NULL, UnitPrice NUMERIC NOT NULL, MinimumOrderQuantity INTEGER NULL,
			LeadTimeDays INTEGER NULL, Version INTEGER NOT NULL DEFAULT 1,
			UNIQUE(SupplierQuoteResponseId, RequestForQuotationLineId),
			FOREIGN KEY(SupplierQuoteResponseId) REFERENCES SupplierQuoteResponses(Id),
			FOREIGN KEY(RequestForQuotationLineId) REFERENCES RequestForQuotationLines(Id),
			FOREIGN KEY(ItemId) REFERENCES Items(Id),
			CHECK(Quantity > 0), CHECK(UnitPrice >= 0), CHECK(MinimumOrderQuantity IS NULL OR MinimumOrderQuantity > 0),
			CHECK(LeadTimeDays IS NULL OR LeadTimeDays >= 0));
		CREATE TABLE IF NOT EXISTS ProcurementSourcingEvidence (
			PurchaseOrderId INTEGER NOT NULL PRIMARY KEY, PurchaseRequisitionId INTEGER NOT NULL, RequestForQuotationId INTEGER NOT NULL UNIQUE,
			SupplierQuoteResponseId INTEGER NOT NULL UNIQUE, SelectedByUserId INTEGER NOT NULL, SelectedAtUtc TEXT NOT NULL,
			FOREIGN KEY(PurchaseOrderId) REFERENCES PurchaseOrders(Id), FOREIGN KEY(PurchaseRequisitionId) REFERENCES PurchaseRequisitions(Id),
			FOREIGN KEY(RequestForQuotationId) REFERENCES RequestsForQuotation(Id),
			FOREIGN KEY(SupplierQuoteResponseId) REFERENCES SupplierQuoteResponses(Id), FOREIGN KEY(SelectedByUserId) REFERENCES Users(Id));
		""";

	private const string SqlServer = """
		IF OBJECT_ID(N'PurchaseRequisitions', N'U') IS NULL BEGIN
			CREATE TABLE PurchaseRequisitions (Id bigint IDENTITY(1,1) PRIMARY KEY, RequisitionNumber nvarchar(50) NOT NULL UNIQUE,
			Status int NOT NULL, RequestedByUserId bigint NOT NULL, CreatedByUserId bigint NOT NULL, CreatedAtUtc nvarchar(40) NOT NULL,
			SubmittedAtUtc nvarchar(40) NULL, ApprovalDecisionAtUtc nvarchar(40) NULL, ApprovalDecisionByUserId bigint NULL,
			ApprovalComment nvarchar(2000) NULL, RequiredByDate nvarchar(10) NULL, PreferredSupplierId bigint NULL,
			BusinessJustification nvarchar(4000) NULL, Version bigint NOT NULL DEFAULT 1,
			CONSTRAINT FK_PurchaseRequisitions_Requester FOREIGN KEY(RequestedByUserId) REFERENCES Users(Id),
			CONSTRAINT FK_PurchaseRequisitions_Creator FOREIGN KEY(CreatedByUserId) REFERENCES Users(Id),
			CONSTRAINT FK_PurchaseRequisitions_Approver FOREIGN KEY(ApprovalDecisionByUserId) REFERENCES Users(Id),
			CONSTRAINT FK_PurchaseRequisitions_PreferredSupplier FOREIGN KEY(PreferredSupplierId) REFERENCES Suppliers(Id),
			CONSTRAINT CK_PurchaseRequisitions_Status CHECK(Status BETWEEN 1 AND 7));
		END;
		IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_PurchaseRequisitions_Status_Requester' AND object_id=OBJECT_ID(N'PurchaseRequisitions')) CREATE INDEX IX_PurchaseRequisitions_Status_Requester ON PurchaseRequisitions(Status, RequestedByUserId);
		IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_PurchaseRequisitions_RequiredByDate' AND object_id=OBJECT_ID(N'PurchaseRequisitions')) CREATE INDEX IX_PurchaseRequisitions_RequiredByDate ON PurchaseRequisitions(RequiredByDate);
		IF OBJECT_ID(N'PurchaseRequisitionLines', N'U') IS NULL BEGIN
			CREATE TABLE PurchaseRequisitionLines (Id bigint IDENTITY(1,1) PRIMARY KEY, PurchaseRequisitionId bigint NOT NULL,
			LineNumber int NOT NULL, ItemId bigint NOT NULL, Quantity int NOT NULL, Notes nvarchar(2000) NULL, Version bigint NOT NULL DEFAULT 1,
			CONSTRAINT UQ_PurchaseRequisitionLines_Number UNIQUE(PurchaseRequisitionId, LineNumber),
			CONSTRAINT UQ_PurchaseRequisitionLines_Item UNIQUE(PurchaseRequisitionId, ItemId),
			CONSTRAINT FK_PurchaseRequisitionLines_Header FOREIGN KEY(PurchaseRequisitionId) REFERENCES PurchaseRequisitions(Id),
			CONSTRAINT FK_PurchaseRequisitionLines_Item FOREIGN KEY(ItemId) REFERENCES Items(Id),
			CONSTRAINT CK_PurchaseRequisitionLines_Quantity CHECK(Quantity > 0)); END;
		IF OBJECT_ID(N'RequestsForQuotation', N'U') IS NULL BEGIN
			CREATE TABLE RequestsForQuotation (Id bigint IDENTITY(1,1) PRIMARY KEY, RfqNumber nvarchar(50) NOT NULL UNIQUE,
			PurchaseRequisitionId bigint NOT NULL, Status int NOT NULL, CreatedAtUtc nvarchar(40) NOT NULL, CreatedByUserId bigint NOT NULL,
			ResponseDueDate nvarchar(10) NULL, SelectedQuoteResponseId bigint NULL, SelectedByUserId bigint NULL, SelectedAtUtc nvarchar(40) NULL,
			ConvertedPurchaseOrderId bigint NULL UNIQUE, Version bigint NOT NULL DEFAULT 1,
			CONSTRAINT FK_RequestsForQuotation_Requisition FOREIGN KEY(PurchaseRequisitionId) REFERENCES PurchaseRequisitions(Id),
			CONSTRAINT FK_RequestsForQuotation_Creator FOREIGN KEY(CreatedByUserId) REFERENCES Users(Id),
			CONSTRAINT FK_RequestsForQuotation_SelectedBy FOREIGN KEY(SelectedByUserId) REFERENCES Users(Id),
			CONSTRAINT FK_RequestsForQuotation_PurchaseOrder FOREIGN KEY(ConvertedPurchaseOrderId) REFERENCES PurchaseOrders(Id),
			CONSTRAINT CK_RequestsForQuotation_Status CHECK(Status BETWEEN 1 AND 5)); END;
		IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_RequestsForQuotation_Status_Due' AND object_id=OBJECT_ID(N'RequestsForQuotation')) CREATE INDEX IX_RequestsForQuotation_Status_Due ON RequestsForQuotation(Status, ResponseDueDate);
		IF OBJECT_ID(N'RequestForQuotationSuppliers', N'U') IS NULL CREATE TABLE RequestForQuotationSuppliers (RequestForQuotationId bigint NOT NULL, SupplierId bigint NOT NULL, CONSTRAINT PK_RequestForQuotationSuppliers PRIMARY KEY(RequestForQuotationId,SupplierId), CONSTRAINT FK_RfqSuppliers_Rfq FOREIGN KEY(RequestForQuotationId) REFERENCES RequestsForQuotation(Id), CONSTRAINT FK_RfqSuppliers_Supplier FOREIGN KEY(SupplierId) REFERENCES Suppliers(Id));
		IF OBJECT_ID(N'RequestForQuotationLines', N'U') IS NULL CREATE TABLE RequestForQuotationLines (Id bigint IDENTITY(1,1) PRIMARY KEY, RequestForQuotationId bigint NOT NULL, PurchaseRequisitionLineId bigint NOT NULL, ItemId bigint NOT NULL, Quantity int NOT NULL, RequiredByDate nvarchar(10) NULL, Version bigint NOT NULL DEFAULT 1, CONSTRAINT UQ_RfqLines_RequisitionLine UNIQUE(RequestForQuotationId,PurchaseRequisitionLineId), CONSTRAINT FK_RfqLines_Rfq FOREIGN KEY(RequestForQuotationId) REFERENCES RequestsForQuotation(Id), CONSTRAINT FK_RfqLines_RequisitionLine FOREIGN KEY(PurchaseRequisitionLineId) REFERENCES PurchaseRequisitionLines(Id), CONSTRAINT FK_RfqLines_Item FOREIGN KEY(ItemId) REFERENCES Items(Id), CONSTRAINT CK_RfqLines_Quantity CHECK(Quantity > 0));
		IF OBJECT_ID(N'SupplierQuoteResponses', N'U') IS NULL CREATE TABLE SupplierQuoteResponses (Id bigint IDENTITY(1,1) PRIMARY KEY, RequestForQuotationId bigint NOT NULL, SupplierId bigint NOT NULL, SupplierReference nvarchar(250) NULL, Currency char(3) NOT NULL, ValidUntil nvarchar(10) NULL, ReceivedAtUtc nvarchar(40) NOT NULL, CapturedByUserId bigint NOT NULL, Status int NOT NULL, Version bigint NOT NULL DEFAULT 1, CONSTRAINT FK_QuoteResponses_Rfq FOREIGN KEY(RequestForQuotationId) REFERENCES RequestsForQuotation(Id), CONSTRAINT FK_QuoteResponses_Supplier FOREIGN KEY(SupplierId) REFERENCES Suppliers(Id), CONSTRAINT FK_QuoteResponses_User FOREIGN KEY(CapturedByUserId) REFERENCES Users(Id), CONSTRAINT CK_QuoteResponses_Status CHECK(Status BETWEEN 1 AND 3));
		IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_SupplierQuoteResponses_Rfq_Supplier' AND object_id=OBJECT_ID(N'SupplierQuoteResponses')) CREATE INDEX IX_SupplierQuoteResponses_Rfq_Supplier ON SupplierQuoteResponses(RequestForQuotationId,SupplierId);
		IF OBJECT_ID(N'SupplierQuoteResponseLines', N'U') IS NULL CREATE TABLE SupplierQuoteResponseLines (Id bigint IDENTITY(1,1) PRIMARY KEY, SupplierQuoteResponseId bigint NOT NULL, RequestForQuotationLineId bigint NOT NULL, ItemId bigint NOT NULL, Quantity int NOT NULL, UnitPrice decimal(18,4) NOT NULL, MinimumOrderQuantity int NULL, LeadTimeDays int NULL, Version bigint NOT NULL DEFAULT 1, CONSTRAINT UQ_QuoteResponseLines_RfqLine UNIQUE(SupplierQuoteResponseId,RequestForQuotationLineId), CONSTRAINT FK_QuoteResponseLines_Response FOREIGN KEY(SupplierQuoteResponseId) REFERENCES SupplierQuoteResponses(Id), CONSTRAINT FK_QuoteResponseLines_RfqLine FOREIGN KEY(RequestForQuotationLineId) REFERENCES RequestForQuotationLines(Id), CONSTRAINT FK_QuoteResponseLines_Item FOREIGN KEY(ItemId) REFERENCES Items(Id), CONSTRAINT CK_QuoteResponseLines_Values CHECK(Quantity > 0 AND UnitPrice >= 0 AND (MinimumOrderQuantity IS NULL OR MinimumOrderQuantity > 0) AND (LeadTimeDays IS NULL OR LeadTimeDays >= 0)));
		IF OBJECT_ID(N'ProcurementSourcingEvidence', N'U') IS NULL CREATE TABLE ProcurementSourcingEvidence (PurchaseOrderId bigint NOT NULL PRIMARY KEY, PurchaseRequisitionId bigint NOT NULL, RequestForQuotationId bigint NOT NULL UNIQUE, SupplierQuoteResponseId bigint NOT NULL UNIQUE, SelectedByUserId bigint NOT NULL, SelectedAtUtc nvarchar(40) NOT NULL, CONSTRAINT FK_SourcingEvidence_Order FOREIGN KEY(PurchaseOrderId) REFERENCES PurchaseOrders(Id), CONSTRAINT FK_SourcingEvidence_Requisition FOREIGN KEY(PurchaseRequisitionId) REFERENCES PurchaseRequisitions(Id), CONSTRAINT FK_SourcingEvidence_Rfq FOREIGN KEY(RequestForQuotationId) REFERENCES RequestsForQuotation(Id), CONSTRAINT FK_SourcingEvidence_Response FOREIGN KEY(SupplierQuoteResponseId) REFERENCES SupplierQuoteResponses(Id), CONSTRAINT FK_SourcingEvidence_User FOREIGN KEY(SelectedByUserId) REFERENCES Users(Id));
		""";

	private const string MySql = """
		CREATE TABLE IF NOT EXISTS PurchaseRequisitions (Id bigint NOT NULL AUTO_INCREMENT PRIMARY KEY, RequisitionNumber varchar(50) NOT NULL UNIQUE, Status int NOT NULL, RequestedByUserId bigint NOT NULL, CreatedByUserId bigint NOT NULL, CreatedAtUtc varchar(40) NOT NULL, SubmittedAtUtc varchar(40) NULL, ApprovalDecisionAtUtc varchar(40) NULL, ApprovalDecisionByUserId bigint NULL, ApprovalComment varchar(2000) NULL, RequiredByDate varchar(10) NULL, PreferredSupplierId bigint NULL, BusinessJustification text NULL, Version bigint NOT NULL DEFAULT 1, INDEX IX_PurchaseRequisitions_Status_Requester(Status,RequestedByUserId), INDEX IX_PurchaseRequisitions_RequiredByDate(RequiredByDate), CONSTRAINT FK_PurchaseRequisitions_Requester FOREIGN KEY(RequestedByUserId) REFERENCES Users(Id), CONSTRAINT FK_PurchaseRequisitions_Creator FOREIGN KEY(CreatedByUserId) REFERENCES Users(Id), CONSTRAINT FK_PurchaseRequisitions_Approver FOREIGN KEY(ApprovalDecisionByUserId) REFERENCES Users(Id), CONSTRAINT FK_PurchaseRequisitions_PreferredSupplier FOREIGN KEY(PreferredSupplierId) REFERENCES Suppliers(Id), CONSTRAINT CK_PurchaseRequisitions_Status CHECK(Status BETWEEN 1 AND 7)) ENGINE=InnoDB;
		CREATE TABLE IF NOT EXISTS PurchaseRequisitionLines (Id bigint NOT NULL AUTO_INCREMENT PRIMARY KEY, PurchaseRequisitionId bigint NOT NULL, LineNumber int NOT NULL, ItemId bigint NOT NULL, Quantity int NOT NULL, Notes text NULL, Version bigint NOT NULL DEFAULT 1, UNIQUE KEY UQ_PurchaseRequisitionLines_Number(PurchaseRequisitionId,LineNumber), UNIQUE KEY UQ_PurchaseRequisitionLines_Item(PurchaseRequisitionId,ItemId), INDEX IX_PurchaseRequisitionLines_ItemId(ItemId), CONSTRAINT FK_PurchaseRequisitionLines_Header FOREIGN KEY(PurchaseRequisitionId) REFERENCES PurchaseRequisitions(Id), CONSTRAINT FK_PurchaseRequisitionLines_Item FOREIGN KEY(ItemId) REFERENCES Items(Id), CONSTRAINT CK_PurchaseRequisitionLines_Quantity CHECK(Quantity > 0)) ENGINE=InnoDB;
		CREATE TABLE IF NOT EXISTS RequestsForQuotation (Id bigint NOT NULL AUTO_INCREMENT PRIMARY KEY, RfqNumber varchar(50) NOT NULL UNIQUE, PurchaseRequisitionId bigint NOT NULL, Status int NOT NULL, CreatedAtUtc varchar(40) NOT NULL, CreatedByUserId bigint NOT NULL, ResponseDueDate varchar(10) NULL, SelectedQuoteResponseId bigint NULL, SelectedByUserId bigint NULL, SelectedAtUtc varchar(40) NULL, ConvertedPurchaseOrderId bigint NULL UNIQUE, Version bigint NOT NULL DEFAULT 1, INDEX IX_RequestsForQuotation_Status_Due(Status,ResponseDueDate), INDEX IX_RequestsForQuotation_Requisition(PurchaseRequisitionId), CONSTRAINT FK_RequestsForQuotation_Requisition FOREIGN KEY(PurchaseRequisitionId) REFERENCES PurchaseRequisitions(Id), CONSTRAINT FK_RequestsForQuotation_Creator FOREIGN KEY(CreatedByUserId) REFERENCES Users(Id), CONSTRAINT FK_RequestsForQuotation_SelectedBy FOREIGN KEY(SelectedByUserId) REFERENCES Users(Id), CONSTRAINT FK_RequestsForQuotation_PurchaseOrder FOREIGN KEY(ConvertedPurchaseOrderId) REFERENCES PurchaseOrders(Id), CONSTRAINT CK_RequestsForQuotation_Status CHECK(Status BETWEEN 1 AND 5)) ENGINE=InnoDB;
		CREATE TABLE IF NOT EXISTS RequestForQuotationSuppliers (RequestForQuotationId bigint NOT NULL, SupplierId bigint NOT NULL, PRIMARY KEY(RequestForQuotationId,SupplierId), INDEX IX_RequestForQuotationSuppliers_Supplier(SupplierId,RequestForQuotationId), CONSTRAINT FK_RfqSuppliers_Rfq FOREIGN KEY(RequestForQuotationId) REFERENCES RequestsForQuotation(Id), CONSTRAINT FK_RfqSuppliers_Supplier FOREIGN KEY(SupplierId) REFERENCES Suppliers(Id)) ENGINE=InnoDB;
		CREATE TABLE IF NOT EXISTS RequestForQuotationLines (Id bigint NOT NULL AUTO_INCREMENT PRIMARY KEY, RequestForQuotationId bigint NOT NULL, PurchaseRequisitionLineId bigint NOT NULL, ItemId bigint NOT NULL, Quantity int NOT NULL, RequiredByDate varchar(10) NULL, Version bigint NOT NULL DEFAULT 1, UNIQUE KEY UQ_RfqLines_RequisitionLine(RequestForQuotationId,PurchaseRequisitionLineId), CONSTRAINT FK_RfqLines_Rfq FOREIGN KEY(RequestForQuotationId) REFERENCES RequestsForQuotation(Id), CONSTRAINT FK_RfqLines_RequisitionLine FOREIGN KEY(PurchaseRequisitionLineId) REFERENCES PurchaseRequisitionLines(Id), CONSTRAINT FK_RfqLines_Item FOREIGN KEY(ItemId) REFERENCES Items(Id), CONSTRAINT CK_RfqLines_Quantity CHECK(Quantity > 0)) ENGINE=InnoDB;
		CREATE TABLE IF NOT EXISTS SupplierQuoteResponses (Id bigint NOT NULL AUTO_INCREMENT PRIMARY KEY, RequestForQuotationId bigint NOT NULL, SupplierId bigint NOT NULL, SupplierReference varchar(250) NULL, Currency char(3) NOT NULL, ValidUntil varchar(10) NULL, ReceivedAtUtc varchar(40) NOT NULL, CapturedByUserId bigint NOT NULL, Status int NOT NULL, Version bigint NOT NULL DEFAULT 1, INDEX IX_SupplierQuoteResponses_Rfq_Supplier(RequestForQuotationId,SupplierId), INDEX IX_SupplierQuoteResponses_Validity(Status,ValidUntil), CONSTRAINT FK_QuoteResponses_Rfq FOREIGN KEY(RequestForQuotationId) REFERENCES RequestsForQuotation(Id), CONSTRAINT FK_QuoteResponses_Supplier FOREIGN KEY(SupplierId) REFERENCES Suppliers(Id), CONSTRAINT FK_QuoteResponses_User FOREIGN KEY(CapturedByUserId) REFERENCES Users(Id), CONSTRAINT CK_QuoteResponses_Status CHECK(Status BETWEEN 1 AND 3)) ENGINE=InnoDB;
		CREATE TABLE IF NOT EXISTS SupplierQuoteResponseLines (Id bigint NOT NULL AUTO_INCREMENT PRIMARY KEY, SupplierQuoteResponseId bigint NOT NULL, RequestForQuotationLineId bigint NOT NULL, ItemId bigint NOT NULL, Quantity int NOT NULL, UnitPrice decimal(18,4) NOT NULL, MinimumOrderQuantity int NULL, LeadTimeDays int NULL, Version bigint NOT NULL DEFAULT 1, UNIQUE KEY UQ_QuoteResponseLines_RfqLine(SupplierQuoteResponseId,RequestForQuotationLineId), CONSTRAINT FK_QuoteResponseLines_Response FOREIGN KEY(SupplierQuoteResponseId) REFERENCES SupplierQuoteResponses(Id), CONSTRAINT FK_QuoteResponseLines_RfqLine FOREIGN KEY(RequestForQuotationLineId) REFERENCES RequestForQuotationLines(Id), CONSTRAINT FK_QuoteResponseLines_Item FOREIGN KEY(ItemId) REFERENCES Items(Id), CONSTRAINT CK_QuoteResponseLines_Values CHECK(Quantity > 0 AND UnitPrice >= 0 AND (MinimumOrderQuantity IS NULL OR MinimumOrderQuantity > 0) AND (LeadTimeDays IS NULL OR LeadTimeDays >= 0))) ENGINE=InnoDB;
		CREATE TABLE IF NOT EXISTS ProcurementSourcingEvidence (PurchaseOrderId bigint NOT NULL PRIMARY KEY, PurchaseRequisitionId bigint NOT NULL, RequestForQuotationId bigint NOT NULL UNIQUE, SupplierQuoteResponseId bigint NOT NULL UNIQUE, SelectedByUserId bigint NOT NULL, SelectedAtUtc varchar(40) NOT NULL, CONSTRAINT FK_SourcingEvidence_Order FOREIGN KEY(PurchaseOrderId) REFERENCES PurchaseOrders(Id), CONSTRAINT FK_SourcingEvidence_Requisition FOREIGN KEY(PurchaseRequisitionId) REFERENCES PurchaseRequisitions(Id), CONSTRAINT FK_SourcingEvidence_Rfq FOREIGN KEY(RequestForQuotationId) REFERENCES RequestsForQuotation(Id), CONSTRAINT FK_SourcingEvidence_Response FOREIGN KEY(SupplierQuoteResponseId) REFERENCES SupplierQuoteResponses(Id), CONSTRAINT FK_SourcingEvidence_User FOREIGN KEY(SelectedByUserId) REFERENCES Users(Id)) ENGINE=InnoDB;
		""";
}
