// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;
using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class FinanceFinancialReportingRepository : DatabaseRepository
{
	public FinanceFinancialReportingRepository(DatabaseAccess database) : base(database) { }

	public Task<IReadOnlyList<FinanceReportingAccountMapping>> GetMappingsAsync(Guid accountingBookId, CancellationToken cancellationToken = default) =>
		Database.QueryAsync("SELECT Id,Version,AccountingBookId,AccountId,StatementSection,CashFlowCategory,TaxCategory,IsCashAccount,IsCostOfGoodsSold,SortOrder,IsActive FROM FinanceReportingAccountMappings WHERE AccountingBookId=$Book ORDER BY SortOrder,Id;", ReadMapping, cancellationToken, Parameter("$Book", accountingBookId.ToString("D")));

	public Task<IReadOnlyList<FinanceReportingAccountRecord>> GetAccountsAsync(Guid accountingBookId, CancellationToken cancellationToken = default) =>
		Database.QueryAsync("SELECT a.Id,a.Number,a.Name,a.AccountType,a.IsActive FROM FinanceAccounts a INNER JOIN FinanceAccountingBooks b ON b.ChartOfAccountsId=a.ChartOfAccountsId WHERE b.Id=$Book ORDER BY a.Number,a.Id;", reader => new FinanceReportingAccountRecord(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), (FinanceAccountType)Convert.ToInt32(reader.GetValue(3), CultureInfo.InvariantCulture), ReadBool(reader, 4)), cancellationToken, Parameter("$Book", accountingBookId.ToString("D")));

	public Task<IReadOnlyList<FinanceReportSnapshot>> GetRecentSnapshotsAsync(Guid? accountingBookId = null, int count = 50, CancellationToken cancellationToken = default)
	{
		var sql = "SELECT Id,OperationId,Kind,AccountingBookId,FromDate,ToDate,AsOfDate,DimensionId,DimensionValueId,ParameterHash,ContentHash,ContentCsv,CreatedAtUtc,CreatedByUserId FROM FinanceReportSnapshots";
		var parameters = new List<DatabaseParameter>();
		if (accountingBookId.HasValue)
		{
			sql += " WHERE AccountingBookId=$Book";
			parameters.Add(Parameter("$Book", accountingBookId.Value.ToString("D")));
		}
		sql += " ORDER BY CreatedAtUtc DESC,Id DESC;";
		return LoadLimitedAsync(sql, ReadSnapshot, Math.Clamp(count, 1, 200), cancellationToken, parameters.ToArray());
	}

	internal Task<FinanceReportingBookRecord?> GetBookAsync(DatabaseTransactionContext transaction, Guid id, CancellationToken cancellationToken) =>
		transaction.Session.QuerySingleOrDefaultAsync("SELECT Id,LegalEntityId,ChartOfAccountsId,ReportingCurrencyCode,IsActive FROM FinanceAccountingBooks WHERE Id=$Id;", reader => new FinanceReportingBookRecord(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)), Guid.Parse(reader.GetString(2)), new CurrencyCode(reader.GetString(3)), ReadBool(reader, 4)), cancellationToken, Parameter("$Id", id.ToString("D")));

	internal Task<FinanceReportingAccountRecord?> GetAccountAsync(DatabaseTransactionContext transaction, Guid bookId, Guid accountId, CancellationToken cancellationToken) =>
		transaction.Session.QuerySingleOrDefaultAsync("SELECT a.Id,a.Number,a.Name,a.AccountType,a.IsActive FROM FinanceAccounts a INNER JOIN FinanceAccountingBooks b ON b.ChartOfAccountsId=a.ChartOfAccountsId WHERE b.Id=$Book AND a.Id=$Account;", reader => new FinanceReportingAccountRecord(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), (FinanceAccountType)Convert.ToInt32(reader.GetValue(3), CultureInfo.InvariantCulture), ReadBool(reader, 4)), cancellationToken, Parameter("$Book", bookId.ToString("D")), Parameter("$Account", accountId.ToString("D")));

	internal Task<FinanceReportingAccountMapping?> GetMappingAsync(DatabaseTransactionContext transaction, long id, CancellationToken cancellationToken) =>
		transaction.Session.QuerySingleOrDefaultAsync("SELECT Id,Version,AccountingBookId,AccountId,StatementSection,CashFlowCategory,TaxCategory,IsCashAccount,IsCostOfGoodsSold,SortOrder,IsActive FROM FinanceReportingAccountMappings WHERE Id=$Id;", ReadMapping, cancellationToken, Parameter("$Id", id));

	internal Task<FinanceReportingAccountMapping?> FindMappingAsync(DatabaseTransactionContext transaction, Guid bookId, Guid accountId, CancellationToken cancellationToken) =>
		transaction.Session.QuerySingleOrDefaultAsync("SELECT Id,Version,AccountingBookId,AccountId,StatementSection,CashFlowCategory,TaxCategory,IsCashAccount,IsCostOfGoodsSold,SortOrder,IsActive FROM FinanceReportingAccountMappings WHERE AccountingBookId=$Book AND AccountId=$Account;", ReadMapping, cancellationToken, Parameter("$Book", bookId.ToString("D")), Parameter("$Account", accountId.ToString("D")));

	internal Task<long> CreateMappingAsync(DatabaseTransactionContext transaction, FinanceReportingAccountMapping value, CancellationToken cancellationToken) =>
		transaction.Session.InsertAsync("INSERT INTO FinanceReportingAccountMappings (Version,AccountingBookId,AccountId,StatementSection,CashFlowCategory,TaxCategory,IsCashAccount,IsCostOfGoodsSold,SortOrder,IsActive) VALUES (1,$Book,$Account,$Section,$Cash,$Tax,$IsCash,$Cogs,$Sort,$Active);", cancellationToken, MappingParameters(value));

	internal Task<int> UpdateMappingAsync(DatabaseTransactionContext transaction, FinanceReportingAccountMapping value, long expectedVersion, CancellationToken cancellationToken) =>
		transaction.Session.ExecuteAsync("UPDATE FinanceReportingAccountMappings SET Version=Version+1,StatementSection=$Section,CashFlowCategory=$Cash,TaxCategory=$Tax,IsCashAccount=$IsCash,IsCostOfGoodsSold=$Cogs,SortOrder=$Sort,IsActive=$Active WHERE Id=$Id AND Version=$Version;", cancellationToken, MappingParameters(value).Concat([Parameter("$Id", value.Id),Parameter("$Version", expectedVersion)]).ToArray());

	internal Task<IReadOnlyList<FinanceReportingAccountMapping>> GetMappingsAsync(DatabaseTransactionContext transaction, Guid bookId, CancellationToken cancellationToken) =>
		transaction.Session.QueryAsync("SELECT Id,Version,AccountingBookId,AccountId,StatementSection,CashFlowCategory,TaxCategory,IsCashAccount,IsCostOfGoodsSold,SortOrder,IsActive FROM FinanceReportingAccountMappings WHERE AccountingBookId=$Book AND IsActive=1 ORDER BY SortOrder,Id;", ReadMapping, cancellationToken, Parameter("$Book", bookId.ToString("D")));

	internal Task<IReadOnlyList<FinanceTrialBalanceSourceRow>> GetTrialBalanceAsync(DatabaseTransactionContext transaction, FinanceReportParameters parameters, CancellationToken cancellationToken)
	{
		var from = parameters.FromDate;
		var to = parameters.ToDate ?? parameters.AsOfDate ?? throw new InvalidOperationException("A report end/as-of date is required.");
		var filters = new List<string> { "e.AccountingBookId=$Book", "e.PostingDate<=$ToDate" };
		var dbParameters = new List<DatabaseParameter> { Parameter("$Book", parameters.AccountingBookId.ToString("D")), Parameter("$ToDate", to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) };
		AppendDimensionFilter(filters, dbParameters, parameters, "l");
		var openingExpression = "0";
		var debitExpression = "l.ReportingDebit";
		var creditExpression = "l.ReportingCredit";
		if (from.HasValue)
		{
			dbParameters.Add(Parameter("$FromDate", from.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
			openingExpression = "CASE WHEN e.PostingDate<$FromDate THEN l.ReportingDebit-l.ReportingCredit ELSE 0 END";
			debitExpression = "CASE WHEN e.PostingDate>=$FromDate THEN l.ReportingDebit ELSE 0 END";
			creditExpression = "CASE WHEN e.PostingDate>=$FromDate THEN l.ReportingCredit ELSE 0 END";
		}
		var sql = $"SELECT a.Id,a.Number,a.Name,a.AccountType,COALESCE(SUM({openingExpression}),0),COALESCE(SUM({debitExpression}),0),COALESCE(SUM({creditExpression}),0) FROM FinanceAccounts a INNER JOIN FinanceAccountingBooks b ON b.ChartOfAccountsId=a.ChartOfAccountsId LEFT JOIN FinanceJournalEntryLines l ON l.AccountId=a.Id LEFT JOIN FinanceJournalEntries e ON e.Id=l.JournalEntryId AND {string.Join(" AND ", filters)} WHERE b.Id=$Book GROUP BY a.Id,a.Number,a.Name,a.AccountType ORDER BY a.Number,a.Id;";
		return transaction.Session.QueryAsync(sql, reader => new FinanceTrialBalanceSourceRow(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), (FinanceAccountType)Convert.ToInt32(reader.GetValue(3), CultureInfo.InvariantCulture), ReadDecimal(reader, 4), ReadDecimal(reader, 5), ReadDecimal(reader, 6)), cancellationToken, dbParameters.ToArray());
	}

	internal Task<IReadOnlyList<FinanceGeneralLedgerSourceRow>> GetGeneralLedgerAsync(DatabaseTransactionContext transaction, FinanceReportParameters parameters, CancellationToken cancellationToken)
	{
		var from = parameters.FromDate ?? throw new InvalidOperationException("General Ledger report requires a start date.");
		var to = parameters.ToDate ?? throw new InvalidOperationException("General Ledger report requires an end date.");
		var filters = new List<string> { "e.AccountingBookId=$Book", "e.PostingDate>=$FromDate", "e.PostingDate<=$ToDate" };
		var dbParameters = new List<DatabaseParameter> { Parameter("$Book", parameters.AccountingBookId.ToString("D")), Parameter("$FromDate", from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), Parameter("$ToDate", to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) };
		AppendDimensionFilter(filters, dbParameters, parameters, "l");
		var sql = $"SELECT e.Id,e.PostingDate,e.EntryNumber,a.Id,a.Number,a.Name,COALESCE(l.Description,e.Description),e.SourceType,e.SourceId,e.SourceEvent,e.SourceReference,l.ReportingDebit,l.ReportingCredit FROM FinanceJournalEntryLines l INNER JOIN FinanceJournalEntries e ON e.Id=l.JournalEntryId INNER JOIN FinanceAccounts a ON a.Id=l.AccountId WHERE {string.Join(" AND ", filters)} ORDER BY e.PostingDate,e.Id,l.LineNumber;";
		return transaction.Session.QueryAsync(sql, reader => new FinanceGeneralLedgerSourceRow(reader.GetInt64(0), ReadDate(reader, 1), reader.GetString(2), Guid.Parse(reader.GetString(3)), reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetString(8), reader.GetString(9), reader.IsDBNull(10) ? null : reader.GetString(10), ReadDecimal(reader, 11), ReadDecimal(reader, 12)), cancellationToken, dbParameters.ToArray());
	}

	internal Task<IReadOnlyList<FinanceCashFlowSourceRow>> GetCashFlowSourceAsync(DatabaseTransactionContext transaction, FinanceReportParameters parameters, CancellationToken cancellationToken)
	{
		var from = parameters.FromDate ?? throw new InvalidOperationException("Cash-flow report requires a start date.");
		var to = parameters.ToDate ?? throw new InvalidOperationException("Cash-flow report requires an end date.");
		var filters = new List<string> { "e.AccountingBookId=$Book", "e.PostingDate>=$FromDate", "e.PostingDate<=$ToDate" };
		var dbParameters = new List<DatabaseParameter> { Parameter("$Book", parameters.AccountingBookId.ToString("D")), Parameter("$FromDate", from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), Parameter("$ToDate", to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) };
		AppendDimensionFilter(filters, dbParameters, parameters, "l");
		var sql = $"SELECT e.Id,a.Id,a.Number,a.Name,COALESCE(m.IsCashAccount,0),COALESCE(m.CashFlowCategory,0),l.ReportingDebit,l.ReportingCredit FROM FinanceJournalEntryLines l INNER JOIN FinanceJournalEntries e ON e.Id=l.JournalEntryId INNER JOIN FinanceAccounts a ON a.Id=l.AccountId LEFT JOIN FinanceReportingAccountMappings m ON m.AccountingBookId=e.AccountingBookId AND m.AccountId=l.AccountId AND m.IsActive=1 WHERE {string.Join(" AND ", filters)} ORDER BY e.Id,l.LineNumber;";
		return transaction.Session.QueryAsync(sql, reader => new FinanceCashFlowSourceRow(reader.GetInt64(0), Guid.Parse(reader.GetString(1)), reader.GetString(2), reader.GetString(3), ReadBool(reader, 4), (FinanceCashFlowCategory)Convert.ToInt32(reader.GetValue(5), CultureInfo.InvariantCulture), ReadDecimal(reader, 6), ReadDecimal(reader, 7)), cancellationToken, dbParameters.ToArray());
	}

	internal Task<IReadOnlyList<(long ItemId, string ItemNumber, string ItemName)>> GetItemsAsync(DatabaseTransactionContext transaction, IReadOnlyCollection<long> itemIds, CancellationToken cancellationToken)
	{
		if (itemIds.Count == 0) return Task.FromResult<IReadOnlyList<(long, string, string)>>([]);
		var ids = itemIds.Distinct().OrderBy(value => value).ToArray();
		var names = new List<string>(ids.Length);
		var parameters = new List<DatabaseParameter>(ids.Length);
		for (var index = 0; index < ids.Length; index++) { var name = $"$Item{index}"; names.Add(name); parameters.Add(Parameter(name, ids[index])); }
		return transaction.Session.QueryAsync($"SELECT Id,ItemNumber,Description FROM Items WHERE Id IN ({string.Join(',', names)}) ORDER BY ItemNumber,Id;", reader => (reader.GetInt64(0), reader.GetString(1), reader.GetString(2)), cancellationToken, parameters.ToArray());
	}

	internal Task<FinanceReportSnapshot?> FindSnapshotByOperationAsync(DatabaseTransactionContext transaction, Guid operationId, CancellationToken cancellationToken) =>
		transaction.Session.QuerySingleOrDefaultAsync("SELECT Id,OperationId,Kind,AccountingBookId,FromDate,ToDate,AsOfDate,DimensionId,DimensionValueId,ParameterHash,ContentHash,ContentCsv,CreatedAtUtc,CreatedByUserId FROM FinanceReportSnapshots WHERE OperationId=$Operation;", ReadSnapshot, cancellationToken, Parameter("$Operation", operationId.ToString("D")));

	internal Task<long> CreateSnapshotAsync(DatabaseTransactionContext transaction, FinanceReportSnapshot value, CancellationToken cancellationToken) =>
		transaction.Session.InsertAsync("INSERT INTO FinanceReportSnapshots (OperationId,Kind,AccountingBookId,FromDate,ToDate,AsOfDate,DimensionId,DimensionValueId,ParameterHash,ContentHash,ContentCsv,CreatedAtUtc,CreatedByUserId) VALUES ($Operation,$Kind,$Book,$From,$To,$AsOf,$Dimension,$DimensionValue,$ParameterHash,$ContentHash,$Content,$At,$User);", cancellationToken,
			Parameter("$Operation", value.OperationId.ToString("D")), Parameter("$Kind", (int)value.Kind), Parameter("$Book", value.AccountingBookId.ToString("D")), Parameter("$From", Date(value.FromDate)), Parameter("$To", Date(value.ToDate)), Parameter("$AsOf", Date(value.AsOfDate)), Parameter("$Dimension", value.DimensionId?.ToString("D")), Parameter("$DimensionValue", value.DimensionValueId?.ToString("D")), Parameter("$ParameterHash", value.ParameterHash), Parameter("$ContentHash", value.ContentHash), Parameter("$Content", value.ContentCsv), Parameter("$At", value.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture)), Parameter("$User", value.CreatedByUserId));

	private async Task<IReadOnlyList<T>> LoadLimitedAsync<T>(string sql, Func<DbDataReader,T> map, int count, CancellationToken cancellationToken, params DatabaseParameter[] parameters)
	{
		var values = await Database.QueryAsync(sql, map, cancellationToken, parameters);
		return values.Take(count).ToArray();
	}

	private static void AppendDimensionFilter(List<string> filters, List<DatabaseParameter> parameters, FinanceReportParameters value, string lineAlias)
	{
		if (!value.DimensionId.HasValue && !value.DimensionValueId.HasValue) return;
		if (!value.DimensionId.HasValue || !value.DimensionValueId.HasValue) throw new ArgumentException("Dimension and dimension value must be supplied together.");
		filters.Add($"EXISTS (SELECT 1 FROM FinanceJournalLineDimensions d WHERE d.JournalEntryLineId={lineAlias}.Id AND d.DimensionId=$DimensionId AND d.DimensionValueId=$DimensionValueId)");
		parameters.Add(Parameter("$DimensionId", value.DimensionId.Value.ToString("D")));
		parameters.Add(Parameter("$DimensionValueId", value.DimensionValueId.Value.ToString("D")));
	}

	private static DatabaseParameter[] MappingParameters(FinanceReportingAccountMapping value) =>
	[
		Parameter("$Book", value.AccountingBookId.ToString("D")), Parameter("$Account", value.AccountId.ToString("D")), Parameter("$Section", (int)value.StatementSection), Parameter("$Cash", (int)value.CashFlowCategory), Parameter("$Tax", (int)value.TaxCategory), Parameter("$IsCash", value.IsCashAccount), Parameter("$Cogs", value.IsCostOfGoodsSold), Parameter("$Sort", value.SortOrder), Parameter("$Active", value.IsActive)
	];

	private static FinanceReportingAccountMapping ReadMapping(DbDataReader reader) => new()
	{
		Id = reader.GetInt64(0), Version = Convert.ToInt64(reader.GetValue(1), CultureInfo.InvariantCulture), AccountingBookId = Guid.Parse(reader.GetString(2)), AccountId = Guid.Parse(reader.GetString(3)), StatementSection = (FinanceStatementSection)Convert.ToInt32(reader.GetValue(4), CultureInfo.InvariantCulture), CashFlowCategory = (FinanceCashFlowCategory)Convert.ToInt32(reader.GetValue(5), CultureInfo.InvariantCulture), TaxCategory = (FinanceTaxReportCategory)Convert.ToInt32(reader.GetValue(6), CultureInfo.InvariantCulture), IsCashAccount = ReadBool(reader, 7), IsCostOfGoodsSold = ReadBool(reader, 8), SortOrder = Convert.ToInt32(reader.GetValue(9), CultureInfo.InvariantCulture), IsActive = ReadBool(reader, 10)
	};

	private static FinanceReportSnapshot ReadSnapshot(DbDataReader reader) => new()
	{
		Id = reader.GetInt64(0), OperationId = Guid.Parse(reader.GetString(1)), Kind = (FinanceReportKind)Convert.ToInt32(reader.GetValue(2), CultureInfo.InvariantCulture), AccountingBookId = Guid.Parse(reader.GetString(3)), FromDate = ReadNullableDate(reader, 4), ToDate = ReadNullableDate(reader, 5), AsOfDate = ReadNullableDate(reader, 6), DimensionId = ReadNullableGuid(reader, 7), DimensionValueId = ReadNullableGuid(reader, 8), ParameterHash = reader.GetString(9), ContentHash = reader.GetString(10), ContentCsv = reader.GetString(11), CreatedAtUtc = ReadDateTime(reader, 12), CreatedByUserId = reader.GetInt64(13)
	};

	private static bool ReadBool(DbDataReader reader, int ordinal) => Convert.ToBoolean(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
	private static decimal ReadDecimal(DbDataReader reader, int ordinal) => Convert.ToDecimal(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
	private static DateOnly ReadDate(DbDataReader reader, int ordinal) => reader.GetValue(ordinal) is DateTime dateTime ? DateOnly.FromDateTime(dateTime) : DateOnly.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture);
	private static DateOnly? ReadNullableDate(DbDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : ReadDate(reader, ordinal);
	private static Guid? ReadNullableGuid(DbDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : Guid.Parse(reader.GetString(ordinal));
	private static DateTime ReadDateTime(DbDataReader reader, int ordinal) => reader.GetValue(ordinal) is DateTime dateTime ? DateTime.SpecifyKind(dateTime, DateTimeKind.Utc) : DateTime.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
	private static string? Date(DateOnly? value) => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
