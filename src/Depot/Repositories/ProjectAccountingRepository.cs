// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;

using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class ProjectAccountingRepository : DatabaseRepository
{
	private const string ProjectColumns = "Id,Version,Code,Name,LegalEntityId,OwnerUserId,CustomerId,PlannedStartDate,PlannedEndDate,Status,Description,CreatedAtUtc,CreatedByUserId,UpdatedAtUtc,UpdatedByUserId,ClosedAtUtc,ClosedByUserId,CancelledAtUtc,CancelledByUserId";
	private const string PhaseColumns = "Id,Version,ProjectId,Code,Name,PlannedStartDate,PlannedEndDate,Status,Description";
	private const string AttributionColumns = "Id,Version,ProjectId,ProjectPhaseId,EntityKind,EntityId,SourceType,SourceId,JournalEntryId,IsImmutable,CreatedAtUtc,CreatedByUserId";
	private const string BudgetLinkColumns = "Id,ProjectId,ProjectPhaseId,FinanceBudgetLineId,CategoryCode,CreatedAtUtc,CreatedByUserId";

	public ProjectAccountingRepository(DatabaseAccess database) : base(database) { }

	public Task<PageResult<ProjectRecord>> SearchAsync(ProjectListFilter filter, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(filter);
		var predicates = new List<string>();
		var parameters = new List<DatabaseParameter>();
		if (filter.LegalEntityId is { } legalEntityId)
		{
			predicates.Add("LegalEntityId=$LegalEntityId");
			parameters.Add(Parameter("$LegalEntityId", legalEntityId.ToString("D")));
		}
		if (filter.OwnerUserId is { } ownerUserId)
		{
			predicates.Add("OwnerUserId=$OwnerUserId");
			parameters.Add(Parameter("$OwnerUserId", ownerUserId));
		}
		if (filter.Status is { } status)
		{
			predicates.Add("Status=$Status");
			parameters.Add(Parameter("$Status", (int)status));
		}
		if (!string.IsNullOrWhiteSpace(filter.SearchText))
		{
			predicates.Add("(Code LIKE $Search OR Name LIKE $Search OR Description LIKE $Search)");
			parameters.Add(Parameter("$Search", $"%{filter.SearchText.Trim()}%"));
		}
		var where = predicates.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", predicates);
		return Database.QueryPageAsync(
			$"SELECT {ProjectColumns} FROM Projects{where} ORDER BY Status,Code,Id",
			$"SELECT COUNT(*) FROM Projects{where};",
			ReadProject,
			Math.Max(1, pageNumber),
			Math.Clamp(pageSize, 1, 200),
			cancellationToken,
			parameters.ToArray());
	}

	public Task<ProjectRecord?> GetAsync(long id, CancellationToken cancellationToken = default) =>
		Database.QuerySingleOrDefaultAsync($"SELECT {ProjectColumns} FROM Projects WHERE Id=$Id;", ReadProject, cancellationToken, Parameter("$Id", id));

	internal Task<ProjectRecord?> GetAsync(DatabaseTransactionContext transaction, long id, CancellationToken cancellationToken) =>
		transaction.Session.QuerySingleOrDefaultAsync($"SELECT {ProjectColumns} FROM Projects WHERE Id=$Id;", ReadProject, cancellationToken, Parameter("$Id", id));

	public Task<IReadOnlyList<ProjectPhase>> ListPhasesAsync(long projectId, CancellationToken cancellationToken = default) =>
		Database.QueryAsync($"SELECT {PhaseColumns} FROM ProjectPhases WHERE ProjectId=$ProjectId ORDER BY Code,Id;", ReadPhase, cancellationToken, Parameter("$ProjectId", projectId));

	internal Task<ProjectPhase?> GetPhaseAsync(DatabaseTransactionContext transaction, long id, CancellationToken cancellationToken) =>
		transaction.Session.QuerySingleOrDefaultAsync($"SELECT {PhaseColumns} FROM ProjectPhases WHERE Id=$Id;", ReadPhase, cancellationToken, Parameter("$Id", id));

	public Task<IReadOnlyList<ProjectAttribution>> ListAttributionsAsync(long projectId, CancellationToken cancellationToken = default) =>
		Database.QueryAsync($"SELECT {AttributionColumns} FROM ProjectAttributions WHERE ProjectId=$ProjectId ORDER BY EntityKind,EntityId,Id;", ReadAttribution, cancellationToken, Parameter("$ProjectId", projectId));

	internal Task<ProjectAttribution?> GetAttributionAsync(DatabaseTransactionContext transaction, ProjectAttributionEntityKind kind, long entityId, CancellationToken cancellationToken) =>
		transaction.Session.QuerySingleOrDefaultAsync($"SELECT {AttributionColumns} FROM ProjectAttributions WHERE EntityKind=$Kind AND EntityId=$EntityId;", ReadAttribution, cancellationToken, Parameter("$Kind", (int)kind), Parameter("$EntityId", entityId));

	public Task<IReadOnlyList<ProjectBudgetLink>> ListBudgetLinksAsync(long projectId, CancellationToken cancellationToken = default) =>
		Database.QueryAsync($"SELECT {BudgetLinkColumns} FROM ProjectBudgetLineLinks WHERE ProjectId=$ProjectId ORDER BY ProjectPhaseId,FinanceBudgetLineId,Id;", ReadBudgetLink, cancellationToken, Parameter("$ProjectId", projectId));

	internal Task<ProjectBudgetLink?> GetBudgetLinkAsync(DatabaseTransactionContext transaction, long financeBudgetLineId, CancellationToken cancellationToken) =>
		transaction.Session.QuerySingleOrDefaultAsync($"SELECT {BudgetLinkColumns} FROM ProjectBudgetLineLinks WHERE FinanceBudgetLineId=$LineId;", ReadBudgetLink, cancellationToken, Parameter("$LineId", financeBudgetLineId));

	internal Task<bool> LegalEntityExistsAsync(DatabaseTransactionContext transaction, Guid id, CancellationToken cancellationToken) =>
		ExistsAsync(transaction, "SELECT COUNT(*) FROM FinanceLegalEntities WHERE Id=$Id AND IsActive=1;", cancellationToken, Parameter("$Id", id.ToString("D")));

	internal Task<bool> ActiveUserExistsAsync(DatabaseTransactionContext transaction, long id, CancellationToken cancellationToken) =>
		ExistsAsync(transaction, "SELECT COUNT(*) FROM Users WHERE Id=$Id AND IsActive=1;", cancellationToken, Parameter("$Id", id));

	internal Task<bool> CustomerExistsAsync(DatabaseTransactionContext transaction, long id, CancellationToken cancellationToken) =>
		ExistsAsync(transaction, "SELECT COUNT(*) FROM Customers WHERE Id=$Id AND IsActive=1;", cancellationToken, Parameter("$Id", id));

	internal async Task<ProjectRecord> CreateProjectAsync(DatabaseTransactionContext transaction, ProjectRecord value, CancellationToken cancellationToken)
	{
		var id = await transaction.Session.InsertAsync(
			"""
			INSERT INTO Projects
			(Version,Code,Name,LegalEntityId,OwnerUserId,CustomerId,PlannedStartDate,PlannedEndDate,Status,Description,CreatedAtUtc,CreatedByUserId,UpdatedAtUtc,UpdatedByUserId,ClosedAtUtc,ClosedByUserId,CancelledAtUtc,CancelledByUserId)
			VALUES
			(1,$Code,$Name,$LegalEntityId,$OwnerUserId,$CustomerId,$PlannedStartDate,$PlannedEndDate,$Status,$Description,$CreatedAtUtc,$CreatedByUserId,$UpdatedAtUtc,$UpdatedByUserId,$ClosedAtUtc,$ClosedByUserId,$CancelledAtUtc,$CancelledByUserId);
			""",
			cancellationToken,
			ProjectParameters(value));
		return value with { Id = id, Version = 1 };
	}

	internal Task<int> UpdateProjectAsync(DatabaseTransactionContext transaction, ProjectRecord value, long expectedVersion, CancellationToken cancellationToken) =>
		transaction.Session.ExecuteAsync(
			"""
			UPDATE Projects SET
			Version=Version+1,Code=$Code,Name=$Name,LegalEntityId=$LegalEntityId,OwnerUserId=$OwnerUserId,CustomerId=$CustomerId,
			PlannedStartDate=$PlannedStartDate,PlannedEndDate=$PlannedEndDate,Status=$Status,Description=$Description,
			UpdatedAtUtc=$UpdatedAtUtc,UpdatedByUserId=$UpdatedByUserId,ClosedAtUtc=$ClosedAtUtc,ClosedByUserId=$ClosedByUserId,
			CancelledAtUtc=$CancelledAtUtc,CancelledByUserId=$CancelledByUserId
			WHERE Id=$Id AND Version=$ExpectedVersion;
			""",
			cancellationToken,
			[.. ProjectParameters(value), Parameter("$Id", value.Id), Parameter("$ExpectedVersion", expectedVersion)]);

	internal async Task<ProjectPhase> CreatePhaseAsync(DatabaseTransactionContext transaction, ProjectPhase value, CancellationToken cancellationToken)
	{
		var id = await transaction.Session.InsertAsync(
			"INSERT INTO ProjectPhases (Version,ProjectId,Code,Name,PlannedStartDate,PlannedEndDate,Status,Description) VALUES (1,$ProjectId,$Code,$Name,$Start,$End,$Status,$Description);",
			cancellationToken,
			PhaseParameters(value));
		return value with { Id = id, Version = 1 };
	}

	internal Task<int> UpdatePhaseAsync(DatabaseTransactionContext transaction, ProjectPhase value, long expectedVersion, CancellationToken cancellationToken) =>
		transaction.Session.ExecuteAsync(
			"UPDATE ProjectPhases SET Version=Version+1,Code=$Code,Name=$Name,PlannedStartDate=$Start,PlannedEndDate=$End,Status=$Status,Description=$Description WHERE Id=$Id AND ProjectId=$ProjectId AND Version=$ExpectedVersion;",
			cancellationToken,
			[.. PhaseParameters(value), Parameter("$Id", value.Id), Parameter("$ExpectedVersion", expectedVersion)]);

	internal async Task<ProjectAttribution> CreateAttributionAsync(DatabaseTransactionContext transaction, ProjectAttribution value, CancellationToken cancellationToken)
	{
		var id = await transaction.Session.InsertAsync(
			"INSERT INTO ProjectAttributions (Version,ProjectId,ProjectPhaseId,EntityKind,EntityId,SourceType,SourceId,JournalEntryId,IsImmutable,CreatedAtUtc,CreatedByUserId) VALUES (1,$ProjectId,$ProjectPhaseId,$Kind,$EntityId,$SourceType,$SourceId,$JournalEntryId,$Immutable,$CreatedAtUtc,$CreatedByUserId);",
			cancellationToken,
			AttributionParameters(value));
		return value with { Id = id, Version = 1 };
	}

	internal Task<int> UpdateAttributionAsync(DatabaseTransactionContext transaction, ProjectAttribution value, long expectedVersion, CancellationToken cancellationToken) =>
		transaction.Session.ExecuteAsync(
			"UPDATE ProjectAttributions SET Version=Version+1,ProjectId=$ProjectId,ProjectPhaseId=$ProjectPhaseId,SourceType=$SourceType,SourceId=$SourceId,JournalEntryId=$JournalEntryId,IsImmutable=$Immutable WHERE Id=$Id AND Version=$ExpectedVersion;",
			cancellationToken,
			[.. AttributionParameters(value), Parameter("$Id", value.Id), Parameter("$ExpectedVersion", expectedVersion)]);

	internal Task<int> DeleteAttributionAsync(DatabaseTransactionContext transaction, long id, long expectedVersion, CancellationToken cancellationToken) =>
		transaction.Session.ExecuteAsync("DELETE FROM ProjectAttributions WHERE Id=$Id AND Version=$Version AND IsImmutable=0;", cancellationToken, Parameter("$Id", id), Parameter("$Version", expectedVersion));

	internal async Task<ProjectBudgetLink> CreateBudgetLinkAsync(DatabaseTransactionContext transaction, ProjectBudgetLink value, CancellationToken cancellationToken)
	{
		var id = await transaction.Session.InsertAsync(
			"INSERT INTO ProjectBudgetLineLinks (ProjectId,ProjectPhaseId,FinanceBudgetLineId,CategoryCode,CreatedAtUtc,CreatedByUserId) VALUES ($ProjectId,$ProjectPhaseId,$LineId,$Category,$CreatedAtUtc,$CreatedByUserId);",
			cancellationToken,
			Parameter("$ProjectId", value.ProjectId),
			Parameter("$ProjectPhaseId", value.ProjectPhaseId),
			Parameter("$LineId", value.FinanceBudgetLineId),
			Parameter("$Category", value.CategoryCode),
			Parameter("$CreatedAtUtc", Utc(value.CreatedAtUtc)),
			Parameter("$CreatedByUserId", value.CreatedByUserId));
		return value with { Id = id };
	}

	internal Task<int> DeleteBudgetLinkAsync(DatabaseTransactionContext transaction, long id, CancellationToken cancellationToken) =>
		transaction.Session.ExecuteAsync("DELETE FROM ProjectBudgetLineLinks WHERE Id=$Id;", cancellationToken, Parameter("$Id", id));

	public Task<IReadOnlyList<ProjectBudgetLineOption>> ListBudgetLineOptionsAsync(Guid legalEntityId, int limit = 200, CancellationToken cancellationToken = default) =>
		Database.QuerySliceAsync(
			"""
			SELECT l.Id,v.Id,v.BudgetName,v.FiscalYear,a.Number,a.Name,p.Code,l.Amount
			FROM FinanceBudgetLines l
			INNER JOIN FinanceBudgetVersions v ON v.Id=l.BudgetVersionId
			INNER JOIN FinanceAccounts a ON a.Id=l.AccountId
			INNER JOIN FinanceAccountingPeriods p ON p.Id=l.AccountingPeriodId
			LEFT JOIN ProjectBudgetLineLinks link ON link.FinanceBudgetLineId=l.Id
			WHERE v.LegalEntityId=$LegalEntityId AND link.Id IS NULL
			ORDER BY v.FiscalYear DESC,v.BudgetName,p.StartDate,a.Number,l.Id
			""",
			ReadBudgetOption,
			0,
			Math.Clamp(limit, 1, 500),
			cancellationToken,
			Parameter("$LegalEntityId", legalEntityId.ToString("D")));

	internal Task<ProjectBudgetLineContext?> GetBudgetLineContextAsync(DatabaseTransactionContext transaction, long lineId, CancellationToken cancellationToken) =>
		transaction.Session.QuerySingleOrDefaultAsync(
			"""
			SELECT l.Id,v.LegalEntityId,v.AccountingBookId,l.AccountId,l.AccountingPeriodId
			FROM FinanceBudgetLines l INNER JOIN FinanceBudgetVersions v ON v.Id=l.BudgetVersionId
			WHERE l.Id=$Id;
			""",
			reader => new ProjectBudgetLineContext(
				reader.GetInt64(0),
				Guid.Parse(reader.GetString(1)),
				Guid.Parse(reader.GetString(2)),
				Guid.Parse(reader.GetString(3)),
				Guid.Parse(reader.GetString(4))),
			cancellationToken,
			Parameter("$Id", lineId));

	internal async Task<ProjectAttributionSource?> ResolveSourceAsync(DatabaseTransactionContext transaction, ProjectAttributionEntityKind kind, long entityId, CancellationToken cancellationToken)
	{
		switch (kind)
		{
			case ProjectAttributionEntityKind.PurchaseOrder:
				return await transaction.Session.QuerySingleOrDefaultAsync(
					"SELECT Id,OrderNumber,Status FROM PurchaseOrders WHERE Id=$Id;",
					reader => new ProjectAttributionSource(
						kind, reader.GetInt64(0), reader.GetString(1),
						(PurchaseOrderStatus)Convert.ToInt32(reader.GetValue(2), CultureInfo.InvariantCulture) is PurchaseOrderStatus.Approved or PurchaseOrderStatus.Ordered or PurchaseOrderStatus.PartiallyReceived or PurchaseOrderStatus.Received or PurchaseOrderStatus.Closed,
						null, null, null, null),
					cancellationToken, Parameter("$Id", entityId));
			case ProjectAttributionEntityKind.SalesOrder:
				return await transaction.Session.QuerySingleOrDefaultAsync(
					"SELECT Id,OrderNumber,Status FROM SalesOrders WHERE Id=$Id;",
					reader => new ProjectAttributionSource(
						kind, reader.GetInt64(0), reader.GetString(1),
						(SalesOrderStatus)Convert.ToInt32(reader.GetValue(2), CultureInfo.InvariantCulture) is SalesOrderStatus.Released or SalesOrderStatus.PartiallyShipped or SalesOrderStatus.Shipped or SalesOrderStatus.Completed or SalesOrderStatus.Cancelled,
						null, null, null, null),
					cancellationToken, Parameter("$Id", entityId));
			case ProjectAttributionEntityKind.SalesInvoice:
			{
				var sourceType = FinanceReceivableSourceTypes.SalesInvoice;
				var sourceId = entityId.ToString(CultureInfo.InvariantCulture);
				return await transaction.Session.QuerySingleOrDefaultAsync(
					"""
					SELECT si.Id,si.InvoiceNumber,si.Status,oi.JournalEntryId,oi.LegalEntityId
					FROM SalesInvoices si
					LEFT JOIN FinanceReceivableOpenItems oi ON oi.SourceType=$SourceType AND oi.SourceId=$SourceId
					WHERE si.Id=$Id;
					""",
					reader => new ProjectAttributionSource(
						kind, reader.GetInt64(0), reader.GetString(1),
						(SalesInvoiceStatus)Convert.ToInt32(reader.GetValue(2), CultureInfo.InvariantCulture) != SalesInvoiceStatus.Draft,
						sourceType, sourceId,
						reader.IsDBNull(3) ? null : reader.GetInt64(3),
						reader.IsDBNull(4) ? null : Guid.Parse(reader.GetString(4))),
					cancellationToken, Parameter("$Id", entityId), Parameter("$SourceType", sourceType), Parameter("$SourceId", sourceId));
			}
			case ProjectAttributionEntityKind.SupplierDocument:
				return await transaction.Session.QuerySingleOrDefaultAsync(
					"""
					SELECT d.Id,d.SupplierDocumentNumber,d.Status,d.Kind,d.JournalEntryId,b.LegalEntityId
					FROM FinanceSupplierDocuments d
					LEFT JOIN FinanceJournalEntries e ON e.Id=d.JournalEntryId
					LEFT JOIN FinanceAccountingBooks b ON b.Id=e.AccountingBookId
					WHERE d.Id=$Id;
					""",
					reader =>
					{
						var documentKind = (FinancePayableDocumentKind)Convert.ToInt32(reader.GetValue(3), CultureInfo.InvariantCulture);
						var sourceType = documentKind == FinancePayableDocumentKind.Invoice ? FinancePayableSourceTypes.SupplierInvoice : FinancePayableSourceTypes.SupplierCreditNote;
						return new ProjectAttributionSource(
							kind, reader.GetInt64(0), reader.GetString(1),
							(FinancePayableDocumentStatus)Convert.ToInt32(reader.GetValue(2), CultureInfo.InvariantCulture) is FinancePayableDocumentStatus.Posted or FinancePayableDocumentStatus.Reversed,
							sourceType, entityId.ToString(CultureInfo.InvariantCulture),
							reader.IsDBNull(4) ? null : reader.GetInt64(4),
							reader.IsDBNull(5) ? null : Guid.Parse(reader.GetString(5)));
					},
					cancellationToken, Parameter("$Id", entityId));
			case ProjectAttributionEntityKind.JournalEntry:
				return await transaction.Session.QuerySingleOrDefaultAsync(
					"""
					SELECT e.Id,e.EntryNumber,e.EntryKind,e.SourceType,e.SourceId,b.LegalEntityId
					FROM FinanceJournalEntries e INNER JOIN FinanceAccountingBooks b ON b.Id=e.AccountingBookId
					WHERE e.Id=$Id;
					""",
					reader => new ProjectAttributionSource(
						kind, reader.GetInt64(0), reader.GetString(1), true,
						reader.GetString(3), reader.GetString(4), reader.GetInt64(0), Guid.Parse(reader.GetString(5)),
						(FinanceJournalEntryKind)Convert.ToInt32(reader.GetValue(2), CultureInfo.InvariantCulture)),
					cancellationToken, Parameter("$Id", entityId));
			default:
				throw new ArgumentOutOfRangeException(nameof(kind));
		}
	}

	public Task<IReadOnlyList<ProjectActualRow>> ListActualsAsync(long projectId, long? projectPhaseId = null, DateOnly? fromDate = null, DateOnly? toDate = null, CancellationToken cancellationToken = default)
	{
		var predicates = new List<string> { "pa.ProjectId=$ProjectId", "a.AccountType IN ($Revenue,$Expense)" };
		var parameters = new List<DatabaseParameter>
		{
			Parameter("$ProjectId", projectId),
			Parameter("$Revenue", (int)FinanceAccountType.Revenue),
			Parameter("$Expense", (int)FinanceAccountType.Expense)
		};
		if (projectPhaseId.HasValue)
		{
			predicates.Add("pa.ProjectPhaseId=$ProjectPhaseId");
			parameters.Add(Parameter("$ProjectPhaseId", projectPhaseId.Value));
		}
		if (fromDate.HasValue)
		{
			predicates.Add("e.PostingDate>=$FromDate");
			parameters.Add(Parameter("$FromDate", Date(fromDate.Value)));
		}
		if (toDate.HasValue)
		{
			predicates.Add("e.PostingDate<=$ToDate");
			parameters.Add(Parameter("$ToDate", Date(toDate.Value)));
		}
		return Database.QueryAsync(
			$"""
			SELECT e.AccountingBookId,e.Id,e.EntryNumber,e.PostingDate,e.AccountingPeriodId,a.Id,a.Number,a.Name,a.AccountType,e.ReportingCurrencyCode,
				CASE WHEN a.AccountType=$Revenue THEN SUM(l.ReportingCredit-l.ReportingDebit) ELSE SUM(l.ReportingDebit-l.ReportingCredit) END,
				pa.ProjectPhaseId,e.SourceType,e.SourceId
			FROM ProjectAttributions pa
			INNER JOIN FinanceJournalEntries e ON
				(pa.JournalEntryId IS NOT NULL AND e.Id=pa.JournalEntryId) OR
				(pa.JournalEntryId IS NULL AND pa.SourceType=e.SourceType AND pa.SourceId=e.SourceId)
			INNER JOIN FinanceAccountingBooks b ON b.Id=e.AccountingBookId
			INNER JOIN FinanceJournalEntryLines l ON l.JournalEntryId=e.Id
			INNER JOIN FinanceAccounts a ON a.Id=l.AccountId
			INNER JOIN Projects p ON p.Id=pa.ProjectId AND p.LegalEntityId=b.LegalEntityId
			WHERE {string.Join(" AND ", predicates)}
			GROUP BY e.AccountingBookId,e.Id,e.EntryNumber,e.PostingDate,e.AccountingPeriodId,a.Id,a.Number,a.Name,a.AccountType,e.ReportingCurrencyCode,pa.ProjectPhaseId,e.SourceType,e.SourceId
			ORDER BY e.PostingDate,e.Id,a.Number,a.Id;
			""",
			ReadActual,
			cancellationToken,
			parameters.ToArray());
	}

	public Task<IReadOnlyList<ProjectCommitmentRow>> ListCommitmentsAsync(long projectId, long? projectPhaseId = null, CancellationToken cancellationToken = default)
	{
		var phasePredicate = projectPhaseId.HasValue ? " AND pa.ProjectPhaseId=$ProjectPhaseId" : string.Empty;
		var parameters = new List<DatabaseParameter>
		{
			Parameter("$ProjectId", projectId),
			Parameter("$Kind", (int)ProjectAttributionEntityKind.PurchaseOrder),
			Parameter("$Approved", (int)PurchaseOrderStatus.Approved),
			Parameter("$Ordered", (int)PurchaseOrderStatus.Ordered),
			Parameter("$PartiallyReceived", (int)PurchaseOrderStatus.PartiallyReceived)
		};
		if (projectPhaseId.HasValue) parameters.Add(Parameter("$ProjectPhaseId", projectPhaseId.Value));
		return Database.QueryAsync(
			$"""
			SELECT po.Id,po.OrderNumber,s.Name,po.OrderDate,po.ExpectedDeliveryDate,pol.Id,i.PartNumber,i.Description,pol.Quantity,pol.ReceivedQuantity,pol.UnitPrice,
				CASE WHEN pol.Quantity>pol.ReceivedQuantity THEN (pol.Quantity-pol.ReceivedQuantity)*pol.UnitPrice ELSE 0 END,
				pa.ProjectPhaseId
			FROM ProjectAttributions pa
			INNER JOIN PurchaseOrders po ON po.Id=pa.EntityId
			INNER JOIN PurchaseOrderLines pol ON pol.PurchaseOrderId=po.Id
			INNER JOIN Suppliers s ON s.Id=po.SupplierId
			INNER JOIN Items i ON i.Id=pol.ItemId
			WHERE pa.ProjectId=$ProjectId AND pa.EntityKind=$Kind{phasePredicate}
			  AND po.Status IN ($Approved,$Ordered,$PartiallyReceived) AND pol.Quantity>pol.ReceivedQuantity
			ORDER BY po.ExpectedDeliveryDate,po.OrderDate,po.Id,pol.LineNumber;
			""",
			ReadCommitment,
			cancellationToken,
			parameters.ToArray());
	}

	internal Task<IReadOnlyList<ProjectBudgetAggregate>> ListBudgetAggregatesAsync(long projectId, long? projectPhaseId = null, CancellationToken cancellationToken = default)
	{
		var phasePredicate = projectPhaseId.HasValue ? " AND link.ProjectPhaseId=$ProjectPhaseId" : string.Empty;
		var parameters = new List<DatabaseParameter> { Parameter("$ProjectId", projectId) };
		if (projectPhaseId.HasValue) parameters.Add(Parameter("$ProjectPhaseId", projectPhaseId.Value));
		return Database.QueryAsync(
			$"""
			SELECT v.AccountingBookId,l.AccountingPeriodId,p.Code,l.AccountId,a.Number,a.Name,link.ProjectPhaseId,link.CategoryCode,SUM(l.Amount)
			FROM ProjectBudgetLineLinks link
			INNER JOIN FinanceBudgetLines l ON l.Id=link.FinanceBudgetLineId
			INNER JOIN FinanceBudgetVersions v ON v.Id=l.BudgetVersionId
			INNER JOIN FinanceAccountingPeriods p ON p.Id=l.AccountingPeriodId
			INNER JOIN FinanceAccounts a ON a.Id=l.AccountId
			WHERE link.ProjectId=$ProjectId{phasePredicate}
			GROUP BY v.AccountingBookId,l.AccountingPeriodId,p.Code,l.AccountId,a.Number,a.Name,link.ProjectPhaseId,link.CategoryCode
			ORDER BY p.Code,a.Number,link.ProjectPhaseId,link.CategoryCode;
			""",
			reader => new ProjectBudgetAggregate(
				Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)), reader.GetString(2), Guid.Parse(reader.GetString(3)),
				reader.GetString(4), reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetInt64(6),
				reader.IsDBNull(7) ? null : reader.GetString(7), ReadDecimal(reader, 8)),
			cancellationToken,
			parameters.ToArray());
	}

	private static async Task<bool> ExistsAsync(DatabaseTransactionContext transaction, string sql, CancellationToken cancellationToken, params DatabaseParameter[] parameters)
	{
		var value = await transaction.Session.ExecuteScalarAsync(sql, cancellationToken, parameters);
		return Convert.ToInt32(value, CultureInfo.InvariantCulture) > 0;
	}

	private static DatabaseParameter[] ProjectParameters(ProjectRecord value) =>
	[
		Parameter("$Code", value.Code), Parameter("$Name", value.Name), Parameter("$LegalEntityId", value.LegalEntityId.ToString("D")),
		Parameter("$OwnerUserId", value.OwnerUserId), Parameter("$CustomerId", value.CustomerId),
		Parameter("$PlannedStartDate", value.PlannedStartDate.HasValue ? Date(value.PlannedStartDate.Value) : null),
		Parameter("$PlannedEndDate", value.PlannedEndDate.HasValue ? Date(value.PlannedEndDate.Value) : null),
		Parameter("$Status", (int)value.Status), Parameter("$Description", value.Description),
		Parameter("$CreatedAtUtc", Utc(value.CreatedAtUtc)), Parameter("$CreatedByUserId", value.CreatedByUserId),
		Parameter("$UpdatedAtUtc", Utc(value.UpdatedAtUtc)), Parameter("$UpdatedByUserId", value.UpdatedByUserId),
		Parameter("$ClosedAtUtc", value.ClosedAtUtc.HasValue ? Utc(value.ClosedAtUtc.Value) : null), Parameter("$ClosedByUserId", value.ClosedByUserId),
		Parameter("$CancelledAtUtc", value.CancelledAtUtc.HasValue ? Utc(value.CancelledAtUtc.Value) : null), Parameter("$CancelledByUserId", value.CancelledByUserId)
	];

	private static DatabaseParameter[] PhaseParameters(ProjectPhase value) =>
	[
		Parameter("$ProjectId", value.ProjectId), Parameter("$Code", value.Code), Parameter("$Name", value.Name),
		Parameter("$Start", value.PlannedStartDate.HasValue ? Date(value.PlannedStartDate.Value) : null),
		Parameter("$End", value.PlannedEndDate.HasValue ? Date(value.PlannedEndDate.Value) : null),
		Parameter("$Status", (int)value.Status), Parameter("$Description", value.Description)
	];

	private static DatabaseParameter[] AttributionParameters(ProjectAttribution value) =>
	[
		Parameter("$ProjectId", value.ProjectId), Parameter("$ProjectPhaseId", value.ProjectPhaseId), Parameter("$Kind", (int)value.EntityKind),
		Parameter("$EntityId", value.EntityId), Parameter("$SourceType", value.SourceType), Parameter("$SourceId", value.SourceId),
		Parameter("$JournalEntryId", value.JournalEntryId), Parameter("$Immutable", value.IsImmutable ? 1 : 0),
		Parameter("$CreatedAtUtc", Utc(value.CreatedAtUtc)), Parameter("$CreatedByUserId", value.CreatedByUserId)
	];

	private static ProjectRecord ReadProject(DbDataReader reader) => new()
	{
		Id = reader.GetInt64(0), Version = reader.GetInt64(1), Code = reader.GetString(2), Name = reader.GetString(3),
		LegalEntityId = Guid.Parse(reader.GetString(4)), OwnerUserId = reader.GetInt64(5), CustomerId = reader.IsDBNull(6) ? null : reader.GetInt64(6),
		PlannedStartDate = ReadNullableDate(reader, 7), PlannedEndDate = ReadNullableDate(reader, 8),
		Status = (ProjectStatus)Convert.ToInt32(reader.GetValue(9), CultureInfo.InvariantCulture), Description = reader.IsDBNull(10) ? null : reader.GetString(10),
		CreatedAtUtc = ReadUtc(reader, 11), CreatedByUserId = reader.GetInt64(12), UpdatedAtUtc = ReadUtc(reader, 13), UpdatedByUserId = reader.GetInt64(14),
		ClosedAtUtc = reader.IsDBNull(15) ? null : ReadUtc(reader, 15), ClosedByUserId = reader.IsDBNull(16) ? null : reader.GetInt64(16),
		CancelledAtUtc = reader.IsDBNull(17) ? null : ReadUtc(reader, 17), CancelledByUserId = reader.IsDBNull(18) ? null : reader.GetInt64(18)
	};

	private static ProjectPhase ReadPhase(DbDataReader reader) => new()
	{
		Id = reader.GetInt64(0), Version = reader.GetInt64(1), ProjectId = reader.GetInt64(2), Code = reader.GetString(3), Name = reader.GetString(4),
		PlannedStartDate = ReadNullableDate(reader, 5), PlannedEndDate = ReadNullableDate(reader, 6),
		Status = (ProjectPhaseStatus)Convert.ToInt32(reader.GetValue(7), CultureInfo.InvariantCulture), Description = reader.IsDBNull(8) ? null : reader.GetString(8)
	};

	private static ProjectAttribution ReadAttribution(DbDataReader reader) => new()
	{
		Id = reader.GetInt64(0), Version = reader.GetInt64(1), ProjectId = reader.GetInt64(2), ProjectPhaseId = reader.IsDBNull(3) ? null : reader.GetInt64(3),
		EntityKind = (ProjectAttributionEntityKind)Convert.ToInt32(reader.GetValue(4), CultureInfo.InvariantCulture), EntityId = reader.GetInt64(5),
		SourceType = reader.IsDBNull(6) ? null : reader.GetString(6), SourceId = reader.IsDBNull(7) ? null : reader.GetString(7),
		JournalEntryId = reader.IsDBNull(8) ? null : reader.GetInt64(8), IsImmutable = ReadBool(reader, 9), CreatedAtUtc = ReadUtc(reader, 10), CreatedByUserId = reader.GetInt64(11)
	};

	private static ProjectBudgetLink ReadBudgetLink(DbDataReader reader) => new()
	{
		Id = reader.GetInt64(0), ProjectId = reader.GetInt64(1), ProjectPhaseId = reader.IsDBNull(2) ? null : reader.GetInt64(2),
		FinanceBudgetLineId = reader.GetInt64(3), CategoryCode = reader.IsDBNull(4) ? null : reader.GetString(4),
		CreatedAtUtc = ReadUtc(reader, 5), CreatedByUserId = reader.GetInt64(6)
	};

	private static ProjectBudgetLineOption ReadBudgetOption(DbDataReader reader) =>
		new(reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), Convert.ToInt32(reader.GetValue(3), CultureInfo.InvariantCulture), reader.GetString(4), reader.GetString(5), reader.GetString(6), ReadDecimal(reader, 7));

	private static ProjectActualRow ReadActual(DbDataReader reader) =>
		new(Guid.Parse(reader.GetString(0)), reader.GetInt64(1), reader.GetString(2), ReadDate(reader, 3), Guid.Parse(reader.GetString(4)),
			Guid.Parse(reader.GetString(5)), reader.GetString(6), reader.GetString(7), (FinanceAccountType)Convert.ToInt32(reader.GetValue(8), CultureInfo.InvariantCulture),
			new CurrencyCode(reader.GetString(9)), ReadDecimal(reader, 10), reader.IsDBNull(11) ? null : reader.GetInt64(11), reader.GetString(12), reader.GetString(13));

	private static ProjectCommitmentRow ReadCommitment(DbDataReader reader) =>
		new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), ReadDate(reader, 3).ToDateTime(TimeOnly.MinValue),
			reader.IsDBNull(4) ? null : ReadDate(reader, 4).ToDateTime(TimeOnly.MinValue), reader.GetInt64(5), reader.GetString(6), reader.GetString(7),
			Convert.ToInt32(reader.GetValue(8), CultureInfo.InvariantCulture), Convert.ToInt32(reader.GetValue(9), CultureInfo.InvariantCulture),
			ReadDecimal(reader, 10), ReadDecimal(reader, 11), reader.IsDBNull(12) ? null : reader.GetInt64(12));

	private static DateOnly ReadDate(DbDataReader reader, int ordinal) =>
		reader.GetValue(ordinal) is DateTime value ? DateOnly.FromDateTime(value) : DateOnly.Parse(Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture);
	private static DateOnly? ReadNullableDate(DbDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : ReadDate(reader, ordinal);
	private static DateTime ReadUtc(DbDataReader reader, int ordinal) =>
		reader.GetValue(ordinal) is DateTime value ? DateTime.SpecifyKind(value, DateTimeKind.Utc) :
			DateTime.Parse(Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
	private static decimal ReadDecimal(DbDataReader reader, int ordinal) => Convert.ToDecimal(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
	private static bool ReadBool(DbDataReader reader, int ordinal) => Convert.ToBoolean(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
	private static string Date(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
	private static string Utc(DateTime value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}

internal sealed record ProjectBudgetLineContext(long LineId, Guid LegalEntityId, Guid AccountingBookId, Guid AccountId, Guid AccountingPeriodId);
internal sealed record ProjectBudgetAggregate(Guid AccountingBookId, Guid AccountingPeriodId, string PeriodCode, Guid AccountId, string AccountNumber, string AccountName, long? ProjectPhaseId, string? CategoryCode, decimal Budget);
internal sealed record ProjectAttributionSource(
	ProjectAttributionEntityKind Kind,
	long EntityId,
	string Reference,
	bool IsImmutable,
	string? SourceType,
	string? SourceId,
	long? JournalEntryId,
	Guid? LegalEntityId,
	FinanceJournalEntryKind? JournalEntryKind = null);
