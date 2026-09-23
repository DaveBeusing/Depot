// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;
using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class FinanceBudgetingRepository : DatabaseRepository
{
	private const string VersionColumns = "Id,Version,LegalEntityId,AccountingBookId,FiscalCalendarId,FiscalYear,BudgetName,BudgetVersionNumber,CurrencyCode,Status,OwnerUserId,Description,SourceKind,SourceBudgetVersionId,ApprovalInstanceId,CreatedAtUtc,CreatedByUserId,UpdatedAtUtc,UpdatedByUserId";
	private const string LineColumns = "Id,Version,BudgetVersionId,AccountId,AccountingPeriodId,DimensionId,DimensionValueId,Amount,SourceEvidence";

	public FinanceBudgetingRepository(DatabaseAccess database) : base(database) { }

	public Task<PageResult<FinanceBudgetVersion>> SearchVersionsAsync(
		FinanceBudgetListFilter filter,
		int pageNumber,
		int pageSize,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(filter);
		var clauses = new List<string>();
		var parameters = new List<DatabaseParameter>();
		if (filter.LegalEntityId.HasValue)
		{
			clauses.Add("LegalEntityId=$LegalEntityId");
			parameters.Add(Parameter("$LegalEntityId", filter.LegalEntityId.Value.ToString("D")));
		}
		if (filter.AccountingBookId.HasValue)
		{
			clauses.Add("AccountingBookId=$AccountingBookId");
			parameters.Add(Parameter("$AccountingBookId", filter.AccountingBookId.Value.ToString("D")));
		}
		if (filter.FiscalYear.HasValue)
		{
			clauses.Add("FiscalYear=$FiscalYear");
			parameters.Add(Parameter("$FiscalYear", filter.FiscalYear.Value));
		}
		if (filter.Status.HasValue)
		{
			clauses.Add("Status=$Status");
			parameters.Add(Parameter("$Status", (int)filter.Status.Value));
		}
		if (!string.IsNullOrWhiteSpace(filter.SearchText))
		{
			clauses.Add("(BudgetName LIKE $Search OR Description LIKE $Search)");
			parameters.Add(Parameter("$Search", $"%{filter.SearchText.Trim()}%"));
		}
		var where = clauses.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", clauses);
		return Database.QueryPageAsync(
			$"SELECT {VersionColumns} FROM FinanceBudgetVersions{where} ORDER BY FiscalYear DESC,BudgetName,BudgetVersionNumber DESC,Id",
			$"SELECT COUNT(*) FROM FinanceBudgetVersions{where};",
			ReadVersion,
			Math.Max(1, pageNumber),
			Math.Clamp(pageSize, 1, 200),
			cancellationToken,
			parameters.ToArray());
	}

	public Task<FinanceBudgetVersion?> GetVersionAsync(long id, CancellationToken cancellationToken = default) =>
		Database.QuerySingleOrDefaultAsync(
			$"SELECT {VersionColumns} FROM FinanceBudgetVersions WHERE Id=$Id;",
			ReadVersion,
			cancellationToken,
			Parameter("$Id", id));

	public Task<PageResult<FinanceBudgetLine>> GetLinesAsync(long budgetVersionId, int pageNumber, int pageSize, CancellationToken cancellationToken = default) =>
		Database.QueryPageAsync(
			$"SELECT {LineColumns} FROM FinanceBudgetLines WHERE BudgetVersionId=$BudgetVersionId ORDER BY AccountingPeriodId,AccountId,DimensionId,DimensionValueId,Id",
			"SELECT COUNT(*) FROM FinanceBudgetLines WHERE BudgetVersionId=$BudgetVersionId;",
			ReadLine,
			Math.Max(1, pageNumber),
			Math.Clamp(pageSize, 1, 500),
			cancellationToken,
			Parameter("$BudgetVersionId", budgetVersionId));

	public async Task<FinanceBudgetSummary> GetSummaryAsync(long budgetVersionId, CancellationToken cancellationToken = default)
	{
		var total = Database.Query(
			"SELECT COALESCE(SUM(Amount),0),COUNT(*) FROM FinanceBudgetLines WHERE BudgetVersionId=$BudgetVersionId;",
			reader => (Amount: ReadDecimal(reader, 0), Count: Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture)),
			Parameter("$BudgetVersionId", budgetVersionId)).Single();
		var periods = await Database.QueryAsync(
			"""
			SELECT l.AccountingPeriodId,p.Code,COALESCE(SUM(l.Amount),0),COUNT(*)
			FROM FinanceBudgetLines l
			INNER JOIN FinanceAccountingPeriods p ON p.Id=l.AccountingPeriodId
			WHERE l.BudgetVersionId=$BudgetVersionId
			GROUP BY l.AccountingPeriodId,p.Code,p.StartDate
			ORDER BY p.StartDate,p.Code;
			""",
			reader => new FinanceBudgetSummaryPeriod(
				Guid.Parse(reader.GetString(0)),
				reader.GetString(1),
				ReadDecimal(reader, 2),
				Convert.ToInt32(reader.GetValue(3), CultureInfo.InvariantCulture)),
			cancellationToken,
			Parameter("$BudgetVersionId", budgetVersionId));
		return new FinanceBudgetSummary(budgetVersionId, total.Amount, total.Count, periods);
	}

	public Task<IReadOnlyList<FinanceBudgetOption>> GetLegalEntitiesAsync(CancellationToken cancellationToken = default) =>
		Database.QueryAsync(
			"SELECT Id,Code,Name FROM FinanceLegalEntities WHERE IsActive=1 ORDER BY Code;",
			reader => new FinanceBudgetOption(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2)),
			cancellationToken);

	public Task<IReadOnlyList<FinanceBudgetOption>> GetFiscalCalendarsAsync(Guid legalEntityId, CancellationToken cancellationToken = default) =>
		Database.QueryAsync(
			"SELECT Id,Code,Name FROM FinanceFiscalCalendars WHERE LegalEntityId=$LegalEntityId AND IsActive=1 ORDER BY Code;",
			reader => new FinanceBudgetOption(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2)),
			cancellationToken,
			Parameter("$LegalEntityId", legalEntityId.ToString("D")));

	public Task<IReadOnlyList<FinanceBudgetOption>> GetAccountingBooksAsync(Guid? legalEntityId = null, CancellationToken cancellationToken = default)
	{
		var where = legalEntityId.HasValue ? " WHERE IsActive=1 AND LegalEntityId=$LegalEntityId" : " WHERE IsActive=1";
		var parameters = legalEntityId.HasValue ? new[] { Parameter("$LegalEntityId", legalEntityId.Value.ToString("D")) } : [];
		return Database.QueryAsync(
			$"SELECT Id,Code,Name FROM FinanceAccountingBooks{where} ORDER BY Code;",
			reader => new FinanceBudgetOption(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2)),
			cancellationToken,
			parameters);
	}

	public Task<IReadOnlyList<FinanceBudgetPeriodOption>> GetPeriodsAsync(Guid fiscalCalendarId, CancellationToken cancellationToken = default) =>
		Database.QueryAsync(
			"SELECT Id,Code,StartDate,EndDate FROM FinanceAccountingPeriods WHERE FiscalCalendarId=$CalendarId ORDER BY StartDate,Code;",
			reader => new FinanceBudgetPeriodOption(
				Guid.Parse(reader.GetString(0)),
				reader.GetString(1),
				ReadDate(reader, 2),
				ReadDate(reader, 3)),
			cancellationToken,
			Parameter("$CalendarId", fiscalCalendarId.ToString("D")));

	public Task<IReadOnlyList<FinanceBudgetAccountOption>> GetAccountsAsync(Guid accountingBookId, CancellationToken cancellationToken = default) =>
		Database.QueryAsync(
			"""
			SELECT a.Id,a.Number,a.Name,a.AccountType
			FROM FinanceAccounts a
			INNER JOIN FinanceAccountingBooks b ON b.ChartOfAccountsId=a.ChartOfAccountsId
			WHERE b.Id=$BookId AND b.IsActive=1 AND a.IsActive=1
			ORDER BY a.Number,a.Name;
			""",
			reader => new FinanceBudgetAccountOption(
				Guid.Parse(reader.GetString(0)),
				reader.GetString(1),
				reader.GetString(2),
				(FinanceAccountType)Convert.ToInt32(reader.GetValue(3), CultureInfo.InvariantCulture)),
			cancellationToken,
			Parameter("$BookId", accountingBookId.ToString("D")));

	public Task<IReadOnlyList<FinanceBudgetDimensionOption>> GetDimensionsAsync(CancellationToken cancellationToken = default) =>
		Database.QueryAsync(
			"SELECT Id,Code,Name FROM FinanceAccountingDimensions WHERE IsActive=1 ORDER BY Code;",
			reader => new FinanceBudgetDimensionOption(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2)),
			cancellationToken);

	public Task<IReadOnlyList<FinanceBudgetDimensionValueOption>> GetDimensionValuesAsync(Guid dimensionId, CancellationToken cancellationToken = default) =>
		Database.QueryAsync(
			"SELECT Id,DimensionId,Code,Name FROM FinanceAccountingDimensionValues WHERE DimensionId=$DimensionId AND IsActive=1 ORDER BY Code;",
			reader => new FinanceBudgetDimensionValueOption(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)), reader.GetString(2), reader.GetString(3)),
			cancellationToken,
			Parameter("$DimensionId", dimensionId.ToString("D")));

	internal Task<FinanceBudgetVersion?> GetVersionAsync(DatabaseTransactionContext transaction, long id, CancellationToken cancellationToken) =>
		transaction.Session.QuerySingleOrDefaultAsync(
			$"SELECT {VersionColumns} FROM FinanceBudgetVersions WHERE Id=$Id;",
			ReadVersion,
			cancellationToken,
			Parameter("$Id", id));

	internal Task<FinanceBudgetLine?> GetLineAsync(DatabaseTransactionContext transaction, long budgetVersionId, long lineId, CancellationToken cancellationToken) =>
		transaction.Session.QuerySingleOrDefaultAsync(
			$"SELECT {LineColumns} FROM FinanceBudgetLines WHERE BudgetVersionId=$BudgetVersionId AND Id=$Id;",
			ReadLine,
			cancellationToken,
			Parameter("$BudgetVersionId", budgetVersionId),
			Parameter("$Id", lineId));

	internal Task<IReadOnlyList<FinanceBudgetPeriodOption>> GetPeriodsAsync(DatabaseTransactionContext transaction, Guid fiscalCalendarId, CancellationToken cancellationToken) =>
		transaction.Session.QueryAsync(
			"SELECT Id,Code,StartDate,EndDate FROM FinanceAccountingPeriods WHERE FiscalCalendarId=$CalendarId ORDER BY StartDate,Code;",
			reader => new FinanceBudgetPeriodOption(Guid.Parse(reader.GetString(0)),reader.GetString(1),ReadDate(reader,2),ReadDate(reader,3)),
			cancellationToken,
			Parameter("$CalendarId", fiscalCalendarId.ToString("D")));

	public async Task<decimal> GetApprovalAmountAsync(long budgetVersionId, CancellationToken cancellationToken = default) =>
		Convert.ToDecimal(
			await Database.ExecuteScalarAsync(
				"SELECT COALESCE(SUM(ABS(Amount)),0) FROM FinanceBudgetLines WHERE BudgetVersionId=$BudgetVersionId;",
				cancellationToken,
				Parameter("$BudgetVersionId", budgetVersionId)),
			CultureInfo.InvariantCulture);

	internal Task<IReadOnlyList<FinanceBudgetAggregateRow>> GetAggregatesAsync(
		DatabaseTransactionContext transaction,
		long budgetVersionId,
		Guid? dimensionId,
		Guid? dimensionValueId,
		CancellationToken cancellationToken)
	{
		var dimensionFilter = dimensionId.HasValue
			? " AND l.DimensionId=$DimensionId AND l.DimensionValueId=$DimensionValueId"
			: string.Empty;
		var parameters = new List<DatabaseParameter> { Parameter("$BudgetVersionId", budgetVersionId) };
		if (dimensionId.HasValue)
		{
			parameters.Add(Parameter("$DimensionId", dimensionId.Value.ToString("D")));
			parameters.Add(Parameter("$DimensionValueId", dimensionValueId!.Value.ToString("D")));
		}
		return transaction.Session.QueryAsync(
			$"""
			SELECT l.AccountId,a.Number,a.Name,a.AccountType,l.AccountingPeriodId,p.Code,p.StartDate,p.EndDate,COALESCE(SUM(l.Amount),0)
			FROM FinanceBudgetLines l
			INNER JOIN FinanceAccounts a ON a.Id=l.AccountId
			INNER JOIN FinanceAccountingPeriods p ON p.Id=l.AccountingPeriodId
			WHERE l.BudgetVersionId=$BudgetVersionId{dimensionFilter}
			GROUP BY l.AccountId,a.Number,a.Name,a.AccountType,l.AccountingPeriodId,p.Code,p.StartDate,p.EndDate
			ORDER BY p.StartDate,a.Number;
			""",
			reader => new FinanceBudgetAggregateRow(
				Guid.Parse(reader.GetString(0)),
				reader.GetString(1),
				reader.GetString(2),
				(FinanceAccountType)Convert.ToInt32(reader.GetValue(3),CultureInfo.InvariantCulture),
				Guid.Parse(reader.GetString(4)),
				reader.GetString(5),
				ReadDate(reader,6),
				ReadDate(reader,7),
				ReadDecimal(reader,8)),
			cancellationToken,
			parameters.ToArray());
	}

	internal async Task<int> GetNextVersionNumberAsync(
		DatabaseTransactionContext transaction,
		Guid legalEntityId,
		Guid accountingBookId,
		int fiscalYear,
		string name,
		CancellationToken cancellationToken)
	{
		var value = await transaction.Session.ExecuteScalarAsync(
			"""
			SELECT COALESCE(MAX(BudgetVersionNumber),0)
			FROM FinanceBudgetVersions
			WHERE LegalEntityId=$LegalEntityId AND AccountingBookId=$AccountingBookId AND FiscalYear=$FiscalYear AND BudgetName=$BudgetName;
			""",
			cancellationToken,
			Parameter("$LegalEntityId", legalEntityId.ToString("D")),
			Parameter("$AccountingBookId", accountingBookId.ToString("D")),
			Parameter("$FiscalYear", fiscalYear),
			Parameter("$BudgetName", name));
		return Convert.ToInt32(value, CultureInfo.InvariantCulture) + 1;
	}

	internal Task<long> CreateVersionAsync(DatabaseTransactionContext transaction, FinanceBudgetVersion value, CancellationToken cancellationToken) =>
		transaction.Session.InsertAsync(
			"""
			INSERT INTO FinanceBudgetVersions
			(Version,LegalEntityId,AccountingBookId,FiscalCalendarId,FiscalYear,BudgetName,BudgetVersionNumber,CurrencyCode,Status,OwnerUserId,Description,SourceKind,SourceBudgetVersionId,ApprovalInstanceId,CreatedAtUtc,CreatedByUserId,UpdatedAtUtc,UpdatedByUserId)
			VALUES
			(1,$LegalEntityId,$AccountingBookId,$FiscalCalendarId,$FiscalYear,$BudgetName,$BudgetVersionNumber,$CurrencyCode,$Status,$OwnerUserId,$Description,$SourceKind,$SourceBudgetVersionId,$ApprovalInstanceId,$CreatedAtUtc,$CreatedByUserId,$UpdatedAtUtc,$UpdatedByUserId);
			""",
			cancellationToken,
			VersionParameters(value));

	internal Task<int> UpdateVersionAsync(DatabaseTransactionContext transaction, FinanceBudgetVersion value, long expectedVersion, CancellationToken cancellationToken) =>
		transaction.Session.ExecuteAsync(
			"""
			UPDATE FinanceBudgetVersions
			SET Version=Version+1,BudgetName=$BudgetName,OwnerUserId=$OwnerUserId,Description=$Description,Status=$Status,SourceKind=$SourceKind,SourceBudgetVersionId=$SourceBudgetVersionId,ApprovalInstanceId=$ApprovalInstanceId,UpdatedAtUtc=$UpdatedAtUtc,UpdatedByUserId=$UpdatedByUserId
			WHERE Id=$Id AND Version=$ExpectedVersion;
			""",
			cancellationToken,
			VersionParameters(value)
				.Append(Parameter("$Id", value.Id))
				.Append(Parameter("$ExpectedVersion", expectedVersion))
				.ToArray());

	internal Task<long> CreateLineAsync(DatabaseTransactionContext transaction, FinanceBudgetLine value, CancellationToken cancellationToken) =>
		transaction.Session.InsertAsync(
			"""
			INSERT INTO FinanceBudgetLines
			(Version,BudgetVersionId,AccountId,AccountingPeriodId,DimensionId,DimensionValueId,Amount,SourceEvidence)
			VALUES
			(1,$BudgetVersionId,$AccountId,$AccountingPeriodId,$DimensionId,$DimensionValueId,$Amount,$SourceEvidence);
			""",
			cancellationToken,
			LineParameters(value));

	internal Task<int> UpdateLineAsync(DatabaseTransactionContext transaction, FinanceBudgetLine value, long expectedVersion, CancellationToken cancellationToken) =>
		transaction.Session.ExecuteAsync(
			"""
			UPDATE FinanceBudgetLines
			SET Version=Version+1,AccountId=$AccountId,AccountingPeriodId=$AccountingPeriodId,DimensionId=$DimensionId,DimensionValueId=$DimensionValueId,Amount=$Amount,SourceEvidence=$SourceEvidence
			WHERE Id=$Id AND BudgetVersionId=$BudgetVersionId AND Version=$ExpectedVersion;
			""",
			cancellationToken,
			LineParameters(value)
				.Append(Parameter("$Id", value.Id))
				.Append(Parameter("$ExpectedVersion", expectedVersion))
				.ToArray());

	internal Task<int> DeleteLineAsync(DatabaseTransactionContext transaction, long budgetVersionId, long lineId, long expectedVersion, CancellationToken cancellationToken) =>
		transaction.Session.ExecuteAsync(
			"DELETE FROM FinanceBudgetLines WHERE Id=$Id AND BudgetVersionId=$BudgetVersionId AND Version=$ExpectedVersion;",
			cancellationToken,
			Parameter("$Id", lineId),
			Parameter("$BudgetVersionId", budgetVersionId),
			Parameter("$ExpectedVersion", expectedVersion));

	internal Task<int> DeleteAllLinesAsync(DatabaseTransactionContext transaction, long budgetVersionId, CancellationToken cancellationToken) =>
		transaction.Session.ExecuteAsync(
			"DELETE FROM FinanceBudgetLines WHERE BudgetVersionId=$BudgetVersionId;",
			cancellationToken,
			Parameter("$BudgetVersionId", budgetVersionId));

	internal Task<long> CountLinesAsync(DatabaseTransactionContext transaction, long budgetVersionId, CancellationToken cancellationToken) =>
		CountAsync(transaction, "SELECT COUNT(*) FROM FinanceBudgetLines WHERE BudgetVersionId=$BudgetVersionId;", cancellationToken, Parameter("$BudgetVersionId", budgetVersionId));

	internal Task<IReadOnlyList<FinanceBudgetLine>> ListLinesAsync(DatabaseTransactionContext transaction, long budgetVersionId, CancellationToken cancellationToken) =>
		transaction.Session.QueryAsync(
			$"SELECT {LineColumns} FROM FinanceBudgetLines WHERE BudgetVersionId=$BudgetVersionId ORDER BY AccountingPeriodId,AccountId,DimensionId,DimensionValueId,Id;",
			ReadLine,
			cancellationToken,
			Parameter("$BudgetVersionId", budgetVersionId));

	internal Task<bool> LineKeyExistsAsync(DatabaseTransactionContext transaction, FinanceBudgetLine value, long? excludeId, CancellationToken cancellationToken) =>
		ExistsAsync(
			transaction,
			"""
			SELECT COUNT(*) FROM FinanceBudgetLines
			WHERE BudgetVersionId=$BudgetVersionId AND AccountId=$AccountId AND AccountingPeriodId=$AccountingPeriodId
			  AND DimensionId=$DimensionId AND DimensionValueId=$DimensionValueId
			  AND ($ExcludeId=0 OR Id<>$ExcludeId);
			""",
			cancellationToken,
			LineParameters(value).Append(Parameter("$ExcludeId", excludeId ?? 0L)).ToArray());

	internal Task<FinanceBudgetBookContext?> GetBookContextAsync(DatabaseTransactionContext transaction, Guid accountingBookId, CancellationToken cancellationToken) =>
		transaction.Session.QuerySingleOrDefaultAsync(
			"SELECT Id,LegalEntityId,ChartOfAccountsId,ReportingCurrencyCode,IsActive FROM FinanceAccountingBooks WHERE Id=$Id;",
			reader => new FinanceBudgetBookContext(
				Guid.Parse(reader.GetString(0)),
				Guid.Parse(reader.GetString(1)),
				Guid.Parse(reader.GetString(2)),
				new CurrencyCode(reader.GetString(3)),
				ReadBool(reader, 4)),
			cancellationToken,
			Parameter("$Id", accountingBookId.ToString("D")));

	internal Task<bool> FiscalCalendarMatchesEntityAsync(DatabaseTransactionContext transaction, Guid fiscalCalendarId, Guid legalEntityId, CancellationToken cancellationToken) =>
		ExistsAsync(
			transaction,
			"SELECT COUNT(*) FROM FinanceFiscalCalendars WHERE Id=$CalendarId AND LegalEntityId=$LegalEntityId AND IsActive=1;",
			cancellationToken,
			Parameter("$CalendarId", fiscalCalendarId.ToString("D")),
			Parameter("$LegalEntityId", legalEntityId.ToString("D")));

	internal Task<bool> AccountBelongsToBookAsync(DatabaseTransactionContext transaction, Guid accountingBookId, Guid accountId, CancellationToken cancellationToken) =>
		ExistsAsync(
			transaction,
			"""
			SELECT COUNT(*) FROM FinanceAccounts a
			INNER JOIN FinanceAccountingBooks b ON b.ChartOfAccountsId=a.ChartOfAccountsId
			WHERE b.Id=$BookId AND a.Id=$AccountId AND b.IsActive=1 AND a.IsActive=1;
			""",
			cancellationToken,
			Parameter("$BookId", accountingBookId.ToString("D")),
			Parameter("$AccountId", accountId.ToString("D")));

	internal Task<bool> PeriodBelongsToCalendarAsync(DatabaseTransactionContext transaction, Guid fiscalCalendarId, Guid accountingPeriodId, CancellationToken cancellationToken) =>
		ExistsAsync(
			transaction,
			"SELECT COUNT(*) FROM FinanceAccountingPeriods WHERE Id=$PeriodId AND FiscalCalendarId=$CalendarId;",
			cancellationToken,
			Parameter("$PeriodId", accountingPeriodId.ToString("D")),
			Parameter("$CalendarId", fiscalCalendarId.ToString("D")));

	internal Task<bool> DimensionValueMatchesAsync(DatabaseTransactionContext transaction, Guid dimensionId, Guid dimensionValueId, CancellationToken cancellationToken) =>
		ExistsAsync(
			transaction,
			"SELECT COUNT(*) FROM FinanceAccountingDimensionValues WHERE Id=$ValueId AND DimensionId=$DimensionId AND IsActive=1;",
			cancellationToken,
			Parameter("$ValueId", dimensionValueId.ToString("D")),
			Parameter("$DimensionId", dimensionId.ToString("D")));

	private static DatabaseParameter[] VersionParameters(FinanceBudgetVersion value) =>
	[
		Parameter("$LegalEntityId", value.LegalEntityId.ToString("D")),
		Parameter("$AccountingBookId", value.AccountingBookId.ToString("D")),
		Parameter("$FiscalCalendarId", value.FiscalCalendarId.ToString("D")),
		Parameter("$FiscalYear", value.FiscalYear),
		Parameter("$BudgetName", value.Name),
		Parameter("$BudgetVersionNumber", value.BudgetVersionNumber),
		Parameter("$CurrencyCode", value.Currency.Value),
		Parameter("$Status", (int)value.Status),
		Parameter("$OwnerUserId", value.OwnerUserId),
		Parameter("$Description", value.Description),
		Parameter("$SourceKind", (int)value.SourceKind),
		Parameter("$SourceBudgetVersionId", value.SourceBudgetVersionId),
		Parameter("$ApprovalInstanceId", value.ApprovalInstanceId?.ToString("D")),
		Parameter("$CreatedAtUtc", value.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture)),
		Parameter("$CreatedByUserId", value.CreatedByUserId),
		Parameter("$UpdatedAtUtc", value.UpdatedAtUtc.ToString("O", CultureInfo.InvariantCulture)),
		Parameter("$UpdatedByUserId", value.UpdatedByUserId)
	];

	private static DatabaseParameter[] LineParameters(FinanceBudgetLine value) =>
	[
		Parameter("$BudgetVersionId", value.BudgetVersionId),
		Parameter("$AccountId", value.AccountId.ToString("D")),
		Parameter("$AccountingPeriodId", value.AccountingPeriodId.ToString("D")),
		Parameter("$DimensionId", value.DimensionId?.ToString("D") ?? string.Empty),
		Parameter("$DimensionValueId", value.DimensionValueId?.ToString("D") ?? string.Empty),
		Parameter("$Amount", value.Amount),
		Parameter("$SourceEvidence", value.SourceEvidence)
	];

	private static FinanceBudgetVersion ReadVersion(DbDataReader reader) => new()
	{
		Id = Convert.ToInt64(reader.GetValue(0), CultureInfo.InvariantCulture),
		Version = Convert.ToInt64(reader.GetValue(1), CultureInfo.InvariantCulture),
		LegalEntityId = Guid.Parse(reader.GetString(2)),
		AccountingBookId = Guid.Parse(reader.GetString(3)),
		FiscalCalendarId = Guid.Parse(reader.GetString(4)),
		FiscalYear = Convert.ToInt32(reader.GetValue(5), CultureInfo.InvariantCulture),
		Name = reader.GetString(6),
		BudgetVersionNumber = Convert.ToInt32(reader.GetValue(7), CultureInfo.InvariantCulture),
		Currency = new CurrencyCode(reader.GetString(8)),
		Status = (FinanceBudgetStatus)Convert.ToInt32(reader.GetValue(9), CultureInfo.InvariantCulture),
		OwnerUserId = Convert.ToInt64(reader.GetValue(10), CultureInfo.InvariantCulture),
		Description = reader.IsDBNull(11) ? null : reader.GetString(11),
		SourceKind = (FinanceBudgetSourceKind)Convert.ToInt32(reader.GetValue(12), CultureInfo.InvariantCulture),
		SourceBudgetVersionId = reader.IsDBNull(13) ? null : Convert.ToInt64(reader.GetValue(13), CultureInfo.InvariantCulture),
		ApprovalInstanceId = ReadGuid(reader, 14),
		CreatedAtUtc = ReadDateTime(reader, 15),
		CreatedByUserId = Convert.ToInt64(reader.GetValue(16), CultureInfo.InvariantCulture),
		UpdatedAtUtc = ReadDateTime(reader, 17),
		UpdatedByUserId = Convert.ToInt64(reader.GetValue(18), CultureInfo.InvariantCulture)
	};

	private static FinanceBudgetLine ReadLine(DbDataReader reader) => new()
	{
		Id = Convert.ToInt64(reader.GetValue(0), CultureInfo.InvariantCulture),
		Version = Convert.ToInt64(reader.GetValue(1), CultureInfo.InvariantCulture),
		BudgetVersionId = Convert.ToInt64(reader.GetValue(2), CultureInfo.InvariantCulture),
		AccountId = Guid.Parse(reader.GetString(3)),
		AccountingPeriodId = Guid.Parse(reader.GetString(4)),
		DimensionId = ReadOptionalGuidString(reader, 5),
		DimensionValueId = ReadOptionalGuidString(reader, 6),
		Amount = ReadDecimal(reader, 7),
		SourceEvidence = reader.IsDBNull(8) ? null : reader.GetString(8)
	};

	private static Guid? ReadGuid(DbDataReader reader, int ordinal) =>
		reader.IsDBNull(ordinal) ? null : Guid.Parse(reader.GetString(ordinal));

	private static Guid? ReadOptionalGuidString(DbDataReader reader, int ordinal)
	{
		if (reader.IsDBNull(ordinal)) return null;
		var value = reader.GetString(ordinal);
		return string.IsNullOrWhiteSpace(value) ? null : Guid.Parse(value);
	}

	private static DateOnly ReadDate(DbDataReader reader, int ordinal) =>
		DateOnly.Parse(Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture);

	private static DateTime ReadDateTime(DbDataReader reader, int ordinal) =>
		DateTime.Parse(Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

	private static decimal ReadDecimal(DbDataReader reader, int ordinal) =>
		Convert.ToDecimal(reader.GetValue(ordinal), CultureInfo.InvariantCulture);

	private static bool ReadBool(DbDataReader reader, int ordinal) =>
		Convert.ToInt32(reader.GetValue(ordinal), CultureInfo.InvariantCulture) != 0;

	private static async Task<bool> ExistsAsync(DatabaseTransactionContext transaction, string sql, CancellationToken cancellationToken, params DatabaseParameter[] parameters) =>
		await CountAsync(transaction, sql, cancellationToken, parameters) > 0;

	private static async Task<long> CountAsync(DatabaseTransactionContext transaction, string sql, CancellationToken cancellationToken, params DatabaseParameter[] parameters) =>
		Convert.ToInt64(await transaction.Session.ExecuteScalarAsync(sql, cancellationToken, parameters), CultureInfo.InvariantCulture);
}

internal sealed record FinanceBudgetBookContext(Guid Id, Guid LegalEntityId, Guid ChartOfAccountsId, CurrencyCode ReportingCurrency, bool IsActive);


internal sealed record FinanceBudgetAggregateRow(
	Guid AccountId,
	string AccountNumber,
	string AccountName,
	FinanceAccountType AccountType,
	Guid AccountingPeriodId,
	string PeriodCode,
	DateOnly PeriodStart,
	DateOnly PeriodEnd,
	decimal RawBudget);
