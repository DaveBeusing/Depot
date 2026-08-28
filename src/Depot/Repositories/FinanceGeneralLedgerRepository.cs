// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;

using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class FinanceGeneralLedgerRepository : DatabaseRepository
{
	private const string HeaderColumns = "Id, EntryNumber, OperationId, RequestHash, AccountingBookId, JournalId, AccountingPeriodId, PostingDate, PostedAtUtc, PostedByUserId, Description, SourceType, SourceId, SourceEvent, SourceReference, TransactionCurrencyCode, ReportingCurrencyCode, ExchangeRateId, ExchangeRate, EntryKind, ReversalOfEntryId";

	public FinanceGeneralLedgerRepository(DatabaseAccess database) : base(database) { }

	public async Task<FinanceJournalEntry?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
	{
		var header = await Database.QuerySingleOrDefaultAsync($"SELECT {HeaderColumns} FROM FinanceJournalEntries WHERE Id = $Id;", ReadEntryHeader, cancellationToken, Parameter("$Id", id));
		return header is null ? null : await LoadLinesAsync(header, cancellationToken);
	}

	public Task<PageResult<FinanceJournalEntrySummary>> SearchAsync(
		Guid? accountingBookId,
		DateOnly? fromDate,
		DateOnly? toDate,
		string? sourceType,
		int pageNumber,
		int pageSize,
		CancellationToken cancellationToken)
	{
		var predicates = new List<string>();
		var parameters = new List<DatabaseParameter>();
		if (accountingBookId.HasValue)
		{
			predicates.Add("AccountingBookId = $AccountingBookId");
			parameters.Add(Parameter("$AccountingBookId", accountingBookId.Value.ToString("D")));
		}
		if (fromDate.HasValue)
		{
			predicates.Add("PostingDate >= $FromDate");
			parameters.Add(Parameter("$FromDate", fromDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
		}
		if (toDate.HasValue)
		{
			predicates.Add("PostingDate <= $ToDate");
			parameters.Add(Parameter("$ToDate", toDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
		}
		if (!string.IsNullOrWhiteSpace(sourceType))
		{
			predicates.Add("SourceType = $SourceType");
			parameters.Add(Parameter("$SourceType", sourceType.Trim()));
		}
		var where = predicates.Count == 0 ? string.Empty : $"WHERE {string.Join(" AND ", predicates)}";
		return Database.QueryPageAsync(
			$"SELECT Id, EntryNumber, PostingDate, PostedAtUtc, Description, SourceType, SourceId, SourceEvent, SourceReference, ReportingCurrencyCode, EntryKind, ReversalOfEntryId FROM FinanceJournalEntries {where} ORDER BY PostingDate DESC, Id DESC",
			$"SELECT COUNT(*) FROM FinanceJournalEntries {where};",
			ReadSummary,
			pageNumber,
			pageSize,
			cancellationToken,
			parameters.ToArray());
	}

	internal Task<FinanceExistingPosting?> FindByOperationAsync(DatabaseTransactionContext transaction, Guid operationId, CancellationToken cancellationToken) =>
		transaction.Session.QuerySingleOrDefaultAsync(
			"SELECT Id, RequestHash FROM FinanceJournalEntries WHERE OperationId = $OperationId;",
			reader => new FinanceExistingPosting(reader.GetInt64(0), reader.GetString(1)),
			cancellationToken,
			Parameter("$OperationId", operationId.ToString("D")));

	internal Task<FinanceExistingPosting?> FindBySourceAsync(
		DatabaseTransactionContext transaction,
		Guid accountingBookId,
		string sourceType,
		string sourceId,
		string sourceEvent,
		CancellationToken cancellationToken) =>
		transaction.Session.QuerySingleOrDefaultAsync(
			"SELECT Id, RequestHash FROM FinanceJournalEntries WHERE AccountingBookId = $AccountingBookId AND SourceType = $SourceType AND SourceId = $SourceId AND SourceEvent = $SourceEvent;",
			reader => new FinanceExistingPosting(reader.GetInt64(0), reader.GetString(1)),
			cancellationToken,
			Parameter("$AccountingBookId", accountingBookId.ToString("D")),
			Parameter("$SourceType", sourceType),
			Parameter("$SourceId", sourceId),
			Parameter("$SourceEvent", sourceEvent));

	internal Task<FinanceBookRecord?> GetBookAsync(DatabaseTransactionContext transaction, Guid id, CancellationToken cancellationToken) =>
		transaction.Session.QuerySingleOrDefaultAsync(
			"SELECT Id, LegalEntityId, ChartOfAccountsId, ReportingCurrencyCode, IsActive FROM FinanceAccountingBooks WHERE Id = $Id;",
			reader => new FinanceBookRecord(ReadGuid(reader, 0), ReadGuid(reader, 1), ReadGuid(reader, 2), new CurrencyCode(reader.GetString(3)), ReadBool(reader, 4)),
			cancellationToken,
			Parameter("$Id", id.ToString("D")));

	internal Task<FinanceJournalRecord?> GetJournalAsync(DatabaseTransactionContext transaction, Guid id, CancellationToken cancellationToken) =>
		transaction.Session.QuerySingleOrDefaultAsync(
			"SELECT Id, AccountingBookId, IsActive FROM FinanceJournals WHERE Id = $Id;",
			reader => new FinanceJournalRecord(ReadGuid(reader, 0), ReadGuid(reader, 1), ReadBool(reader, 2)),
			cancellationToken,
			Parameter("$Id", id.ToString("D")));

	internal async Task<FinancePeriodRecord?> LockPeriodAsync(DatabaseTransactionContext transaction, Guid id, CancellationToken cancellationToken)
	{
		await transaction.Session.ExecuteAsync("UPDATE FinanceAccountingPeriods SET Status = Status WHERE Id = $Id;", cancellationToken, Parameter("$Id", id.ToString("D")));
		return await transaction.Session.QuerySingleOrDefaultAsync(
			"SELECT p.Id, p.FiscalCalendarId, p.Code, p.StartDate, p.EndDate, p.Status, c.LegalEntityId FROM FinanceAccountingPeriods p INNER JOIN FinanceFiscalCalendars c ON c.Id = p.FiscalCalendarId WHERE p.Id = $Id;",
			reader => new FinancePeriodRecord(ReadGuid(reader, 0), ReadGuid(reader, 1), reader.GetString(2), ReadDateOnly(reader, 3), ReadDateOnly(reader, 4), (AccountingPeriodStatus)Convert.ToInt32(reader.GetValue(5), CultureInfo.InvariantCulture), ReadGuid(reader, 6)),
			cancellationToken,
			Parameter("$Id", id.ToString("D")));
	}

	internal Task<FinanceCurrency?> GetCurrencyAsync(DatabaseTransactionContext transaction, CurrencyCode code, CancellationToken cancellationToken) =>
		transaction.Session.QuerySingleOrDefaultAsync(
			"SELECT Code, Name, MinorUnits, IsActive FROM FinanceCurrencies WHERE Code = $Code;",
			reader => new FinanceCurrency(new CurrencyCode(reader.GetString(0)), reader.GetString(1), Convert.ToInt32(reader.GetValue(2), CultureInfo.InvariantCulture), ReadBool(reader, 3)),
			cancellationToken,
			Parameter("$Code", code.Value));

	internal Task<ExchangeRate?> GetExchangeRateAsync(DatabaseTransactionContext transaction, Guid id, CancellationToken cancellationToken) =>
		transaction.Session.QuerySingleOrDefaultAsync(
			"SELECT Id, BaseCurrencyCode, QuoteCurrencyCode, Rate, EffectiveAtUtc, SourceCode FROM FinanceExchangeRates WHERE Id = $Id;",
			reader => new ExchangeRate(ReadGuid(reader, 0), new CurrencyCode(reader.GetString(1)), new CurrencyCode(reader.GetString(2)), ReadDecimal(reader, 3), ReadDateTimeOffset(reader, 4), reader.GetString(5)),
			cancellationToken,
			Parameter("$Id", id.ToString("D")));

	internal Task<FinanceAccount?> GetAccountAsync(DatabaseTransactionContext transaction, Guid id, CancellationToken cancellationToken) =>
		transaction.Session.QuerySingleOrDefaultAsync(
			"SELECT Id, ChartOfAccountsId, Number, Name, AccountType, AllowDirectPosting, IsActive FROM FinanceAccounts WHERE Id = $Id;",
			reader => new FinanceAccount(ReadGuid(reader, 0), ReadGuid(reader, 1), reader.GetString(2), reader.GetString(3), (FinanceAccountType)Convert.ToInt32(reader.GetValue(4), CultureInfo.InvariantCulture), ReadBool(reader, 5), ReadBool(reader, 6)),
			cancellationToken,
			Parameter("$Id", id.ToString("D")));

	internal Task<IReadOnlyList<AccountingDimension>> GetRequiredDimensionsAsync(DatabaseTransactionContext transaction, CancellationToken cancellationToken) =>
		transaction.Session.QueryAsync(
			"SELECT Id, Code, Name, IsRequired, IsActive FROM FinanceDimensions WHERE IsActive = 1 AND IsRequired = 1 ORDER BY Code;",
			reader => new AccountingDimension(ReadGuid(reader, 0), reader.GetString(1), reader.GetString(2), ReadBool(reader, 3), ReadBool(reader, 4)),
			cancellationToken);

	internal Task<FinanceDimensionValueRecord?> GetDimensionValueAsync(DatabaseTransactionContext transaction, Guid dimensionId, Guid valueId, CancellationToken cancellationToken) =>
		transaction.Session.QuerySingleOrDefaultAsync(
			"SELECT v.Id, v.DimensionId, v.IsActive, d.IsActive FROM FinanceDimensionValues v INNER JOIN FinanceDimensions d ON d.Id = v.DimensionId WHERE v.Id = $ValueId AND v.DimensionId = $DimensionId;",
			reader => new FinanceDimensionValueRecord(ReadGuid(reader, 0), ReadGuid(reader, 1), ReadBool(reader, 2), ReadBool(reader, 3)),
			cancellationToken,
			Parameter("$ValueId", valueId.ToString("D")),
			Parameter("$DimensionId", dimensionId.ToString("D")));

	internal async Task<FinanceNumberSequenceRecord?> LockNumberSequenceAsync(DatabaseTransactionContext transaction, Guid legalEntityId, string code, CancellationToken cancellationToken)
	{
		await transaction.Session.ExecuteAsync(
			"UPDATE FinanceNumberSequences SET NextNumber = NextNumber WHERE LegalEntityId = $LegalEntityId AND Code = $Code;",
			cancellationToken,
			Parameter("$LegalEntityId", legalEntityId.ToString("D")),
			Parameter("$Code", code));
		return await transaction.Session.QuerySingleOrDefaultAsync(
			"SELECT Id, LegalEntityId, Code, DocumentType, Prefix, NumericLength, NextNumber, IsActive FROM FinanceNumberSequences WHERE LegalEntityId = $LegalEntityId AND Code = $Code;",
			reader => new FinanceNumberSequenceRecord(ReadGuid(reader, 0), ReadGuid(reader, 1), reader.GetString(2), reader.GetString(3), reader.GetString(4), Convert.ToInt32(reader.GetValue(5), CultureInfo.InvariantCulture), Convert.ToInt64(reader.GetValue(6), CultureInfo.InvariantCulture), ReadBool(reader, 7)),
			cancellationToken,
			Parameter("$LegalEntityId", legalEntityId.ToString("D")),
			Parameter("$Code", code));
	}

	internal Task<int> AdvanceNumberSequenceAsync(DatabaseTransactionContext transaction, Guid id, long expectedNextNumber, CancellationToken cancellationToken) =>
		transaction.Session.ExecuteAsync(
			"UPDATE FinanceNumberSequences SET NextNumber = $NextNumber WHERE Id = $Id AND NextNumber = $ExpectedNextNumber;",
			cancellationToken,
			Parameter("$NextNumber", checked(expectedNextNumber + 1)),
			Parameter("$Id", id.ToString("D")),
			Parameter("$ExpectedNextNumber", expectedNextNumber));

	internal async Task<long> CreateEntryAsync(DatabaseTransactionContext transaction, FinanceJournalEntry entry, CancellationToken cancellationToken)
	{
		var id = await transaction.Session.InsertAsync(
			"INSERT INTO FinanceJournalEntries (EntryNumber, OperationId, RequestHash, AccountingBookId, JournalId, AccountingPeriodId, PostingDate, PostedAtUtc, PostedByUserId, Description, SourceType, SourceId, SourceEvent, SourceReference, TransactionCurrencyCode, ReportingCurrencyCode, ExchangeRateId, ExchangeRate, EntryKind, ReversalOfEntryId) VALUES ($EntryNumber, $OperationId, $RequestHash, $AccountingBookId, $JournalId, $AccountingPeriodId, $PostingDate, $PostedAtUtc, $PostedByUserId, $Description, $SourceType, $SourceId, $SourceEvent, $SourceReference, $TransactionCurrencyCode, $ReportingCurrencyCode, $ExchangeRateId, $ExchangeRate, $EntryKind, $ReversalOfEntryId);",
			cancellationToken,
			Parameter("$EntryNumber", entry.EntryNumber),
			Parameter("$OperationId", entry.OperationId.ToString("D")),
			Parameter("$RequestHash", entry.RequestHash),
			Parameter("$AccountingBookId", entry.AccountingBookId.ToString("D")),
			Parameter("$JournalId", entry.JournalId.ToString("D")),
			Parameter("$AccountingPeriodId", entry.AccountingPeriodId.ToString("D")),
			Parameter("$PostingDate", entry.PostingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
			Parameter("$PostedAtUtc", entry.PostedAtUtc.ToString("O", CultureInfo.InvariantCulture)),
			Parameter("$PostedByUserId", entry.PostedByUserId),
			Parameter("$Description", entry.Description),
			Parameter("$SourceType", entry.SourceType),
			Parameter("$SourceId", entry.SourceId),
			Parameter("$SourceEvent", entry.SourceEvent),
			Parameter("$SourceReference", entry.SourceReference),
			Parameter("$TransactionCurrencyCode", entry.TransactionCurrency.Value),
			Parameter("$ReportingCurrencyCode", entry.ReportingCurrency.Value),
			Parameter("$ExchangeRateId", entry.ExchangeRateId?.ToString("D")),
			Parameter("$ExchangeRate", entry.ExchangeRate),
			Parameter("$EntryKind", (int)entry.EntryKind),
			Parameter("$ReversalOfEntryId", entry.ReversalOfEntryId));

		foreach (var line in entry.Lines.OrderBy(value => value.LineNumber))
		{
			var lineId = await transaction.Session.InsertAsync(
				"INSERT INTO FinanceJournalEntryLines (JournalEntryId, LineNumber, AccountId, Description, TransactionDebit, TransactionCredit, ReportingDebit, ReportingCredit) VALUES ($JournalEntryId, $LineNumber, $AccountId, $Description, $TransactionDebit, $TransactionCredit, $ReportingDebit, $ReportingCredit);",
				cancellationToken,
				Parameter("$JournalEntryId", id),
				Parameter("$LineNumber", line.LineNumber),
				Parameter("$AccountId", line.AccountId.ToString("D")),
				Parameter("$Description", line.Description),
				Parameter("$TransactionDebit", line.TransactionDebit),
				Parameter("$TransactionCredit", line.TransactionCredit),
				Parameter("$ReportingDebit", line.ReportingDebit),
				Parameter("$ReportingCredit", line.ReportingCredit));
			foreach (var dimension in line.Dimensions)
			{
				await transaction.Session.ExecuteAsync(
					"INSERT INTO FinanceJournalLineDimensions (JournalEntryLineId, DimensionId, DimensionValueId) VALUES ($JournalEntryLineId, $DimensionId, $DimensionValueId);",
					cancellationToken,
					Parameter("$JournalEntryLineId", lineId),
					Parameter("$DimensionId", dimension.DimensionId.ToString("D")),
					Parameter("$DimensionValueId", dimension.DimensionValueId.ToString("D")));
			}
		}
		return id;
	}

	internal async Task<FinanceJournalEntry?> GetByIdAsync(DatabaseTransactionContext transaction, long id, CancellationToken cancellationToken)
	{
		var header = await transaction.Session.QuerySingleOrDefaultAsync($"SELECT {HeaderColumns} FROM FinanceJournalEntries WHERE Id = $Id;", ReadEntryHeader, cancellationToken, Parameter("$Id", id));
		return header is null ? null : await LoadLinesAsync(transaction, header, cancellationToken);
	}

	internal async Task<FinanceJournalEntry?> LockEntryAsync(DatabaseTransactionContext transaction, long id, CancellationToken cancellationToken)
	{
		await transaction.Session.ExecuteAsync("UPDATE FinanceJournalEntries SET EntryNumber = EntryNumber WHERE Id = $Id;", cancellationToken, Parameter("$Id", id));
		return await GetByIdAsync(transaction, id, cancellationToken);
	}

	internal Task<long?> GetReversalEntryIdAsync(DatabaseTransactionContext transaction, long originalEntryId, CancellationToken cancellationToken) =>
		GetNullableLongAsync(transaction, "SELECT ReversalEntryId FROM FinanceJournalReversals WHERE OriginalEntryId = $OriginalEntryId;", cancellationToken, Parameter("$OriginalEntryId", originalEntryId));

	internal Task<int> CreateReversalLinkAsync(DatabaseTransactionContext transaction, long originalEntryId, long reversalEntryId, CancellationToken cancellationToken) =>
		transaction.Session.ExecuteAsync(
			"INSERT INTO FinanceJournalReversals (OriginalEntryId, ReversalEntryId) VALUES ($OriginalEntryId, $ReversalEntryId);",
			cancellationToken,
			Parameter("$OriginalEntryId", originalEntryId),
			Parameter("$ReversalEntryId", reversalEntryId));

	private static async Task<long?> GetNullableLongAsync(DatabaseTransactionContext transaction, string sql, CancellationToken cancellationToken, params DatabaseParameter[] parameters)
	{
		var value = await transaction.Session.ExecuteScalarAsync(sql, cancellationToken, parameters);
		return value is null or DBNull ? null : Convert.ToInt64(value, CultureInfo.InvariantCulture);
	}

	private async Task<FinanceJournalEntry> LoadLinesAsync(FinanceJournalEntry header, CancellationToken cancellationToken)
	{
		var lines = await Database.QueryAsync(
			"SELECT Id, JournalEntryId, LineNumber, AccountId, Description, TransactionDebit, TransactionCredit, ReportingDebit, ReportingCredit FROM FinanceJournalEntryLines WHERE JournalEntryId = $Id ORDER BY LineNumber;",
			ReadLine,
			cancellationToken,
			Parameter("$Id", header.Id));
		var completed = new List<FinanceJournalEntryLine>(lines.Count);
		foreach (var line in lines)
		{
			var dimensions = await Database.QueryAsync(
				"SELECT DimensionId, DimensionValueId FROM FinanceJournalLineDimensions WHERE JournalEntryLineId = $LineId ORDER BY DimensionId;",
				reader => new FinanceJournalLineDimension(ReadGuid(reader, 0), ReadGuid(reader, 1)),
				cancellationToken,
				Parameter("$LineId", line.Id));
			completed.Add(line with { Dimensions = dimensions });
		}
		return header with { Lines = completed };
	}

	private static async Task<FinanceJournalEntry> LoadLinesAsync(DatabaseTransactionContext transaction, FinanceJournalEntry header, CancellationToken cancellationToken)
	{
		var lines = await transaction.Session.QueryAsync(
			"SELECT Id, JournalEntryId, LineNumber, AccountId, Description, TransactionDebit, TransactionCredit, ReportingDebit, ReportingCredit FROM FinanceJournalEntryLines WHERE JournalEntryId = $Id ORDER BY LineNumber;",
			ReadLine,
			cancellationToken,
			Parameter("$Id", header.Id));
		var completed = new List<FinanceJournalEntryLine>(lines.Count);
		foreach (var line in lines)
		{
			var dimensions = await transaction.Session.QueryAsync(
				"SELECT DimensionId, DimensionValueId FROM FinanceJournalLineDimensions WHERE JournalEntryLineId = $LineId ORDER BY DimensionId;",
				reader => new FinanceJournalLineDimension(ReadGuid(reader, 0), ReadGuid(reader, 1)),
				cancellationToken,
				Parameter("$LineId", line.Id));
			completed.Add(line with { Dimensions = dimensions });
		}
		return header with { Lines = completed };
	}

	private static FinanceJournalEntry ReadEntryHeader(DbDataReader reader) => new()
	{
		Id = reader.GetInt64(0),
		EntryNumber = reader.GetString(1),
		OperationId = Guid.Parse(reader.GetString(2)),
		RequestHash = reader.GetString(3),
		AccountingBookId = ReadGuid(reader, 4),
		JournalId = ReadGuid(reader, 5),
		AccountingPeriodId = ReadGuid(reader, 6),
		PostingDate = ReadDateOnly(reader, 7),
		PostedAtUtc = ReadDateTimeUtc(reader, 8),
		PostedByUserId = reader.IsDBNull(9) ? null : Convert.ToInt64(reader.GetValue(9), CultureInfo.InvariantCulture),
		Description = reader.GetString(10),
		SourceType = reader.GetString(11),
		SourceId = reader.GetString(12),
		SourceEvent = reader.GetString(13),
		SourceReference = reader.IsDBNull(14) ? null : reader.GetString(14),
		TransactionCurrency = new CurrencyCode(reader.GetString(15)),
		ReportingCurrency = new CurrencyCode(reader.GetString(16)),
		ExchangeRateId = reader.IsDBNull(17) ? null : ReadGuid(reader, 17),
		ExchangeRate = ReadDecimal(reader, 18),
		EntryKind = (FinanceJournalEntryKind)Convert.ToInt32(reader.GetValue(19), CultureInfo.InvariantCulture),
		ReversalOfEntryId = reader.IsDBNull(20) ? null : Convert.ToInt64(reader.GetValue(20), CultureInfo.InvariantCulture)
	};

	private static FinanceJournalEntryLine ReadLine(DbDataReader reader) => new()
	{
		Id = reader.GetInt64(0),
		JournalEntryId = reader.GetInt64(1),
		LineNumber = Convert.ToInt32(reader.GetValue(2), CultureInfo.InvariantCulture),
		AccountId = ReadGuid(reader, 3),
		Description = reader.IsDBNull(4) ? null : reader.GetString(4),
		TransactionDebit = ReadDecimal(reader, 5),
		TransactionCredit = ReadDecimal(reader, 6),
		ReportingDebit = ReadDecimal(reader, 7),
		ReportingCredit = ReadDecimal(reader, 8)
	};

	private static FinanceJournalEntrySummary ReadSummary(DbDataReader reader) =>
		new(
			reader.GetInt64(0),
			reader.GetString(1),
			ReadDateOnly(reader, 2),
			ReadDateTimeUtc(reader, 3),
			reader.GetString(4),
			reader.GetString(5),
			reader.GetString(6),
			reader.GetString(7),
			reader.IsDBNull(8) ? null : reader.GetString(8),
			new CurrencyCode(reader.GetString(9)),
			(FinanceJournalEntryKind)Convert.ToInt32(reader.GetValue(10), CultureInfo.InvariantCulture),
			reader.IsDBNull(11) ? null : Convert.ToInt64(reader.GetValue(11), CultureInfo.InvariantCulture));

	private static Guid ReadGuid(DbDataReader reader, int ordinal) => Guid.Parse(Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture) ?? string.Empty);
	private static bool ReadBool(DbDataReader reader, int ordinal) => Convert.ToBoolean(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
	private static decimal ReadDecimal(DbDataReader reader, int ordinal) => Convert.ToDecimal(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
	private static DateOnly ReadDateOnly(DbDataReader reader, int ordinal) => reader.GetValue(ordinal) is DateTime dateTime ? DateOnly.FromDateTime(dateTime) : DateOnly.Parse(Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture) ?? string.Empty, CultureInfo.InvariantCulture);
	private static DateTime ReadDateTimeUtc(DbDataReader reader, int ordinal) => reader.GetValue(ordinal) is DateTime dateTime ? DateTime.SpecifyKind(dateTime, DateTimeKind.Utc) : DateTime.Parse(Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture) ?? string.Empty, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
	private static DateTimeOffset ReadDateTimeOffset(DbDataReader reader, int ordinal)
	{
		var value = reader.GetValue(ordinal);
		if (value is DateTimeOffset offset) return offset.ToUniversalTime();
		if (value is DateTime dateTime) return new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc));
		return DateTimeOffset.Parse(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
	}
}

internal sealed record FinanceExistingPosting(long Id, string RequestHash);
internal sealed record FinanceBookRecord(Guid Id, Guid LegalEntityId, Guid ChartOfAccountsId, CurrencyCode ReportingCurrency, bool IsActive);
internal sealed record FinanceJournalRecord(Guid Id, Guid AccountingBookId, bool IsActive);
internal sealed record FinancePeriodRecord(Guid Id, Guid FiscalCalendarId, string Code, DateOnly StartDate, DateOnly EndDate, AccountingPeriodStatus Status, Guid LegalEntityId);
internal sealed record FinanceDimensionValueRecord(Guid Id, Guid DimensionId, bool IsValueActive, bool IsDimensionActive);
internal sealed record FinanceNumberSequenceRecord(Guid Id, Guid LegalEntityId, string Code, string DocumentType, string Prefix, int NumericLength, long NextNumber, bool IsActive);
