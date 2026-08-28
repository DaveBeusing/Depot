// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;

using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class FinancePostingProfileRepository : DatabaseRepository
{
	private const string HeaderColumns = "Id, Version, LegalEntityId, AccountingBookId, JournalId, Code, Name, SourceType, SourceEvent, NumberSequenceCode, IsActive";

	public FinancePostingProfileRepository(DatabaseAccess database) : base(database) { }

	public async Task<FinancePostingProfile?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
	{
		var header = await Database.QuerySingleOrDefaultAsync($"SELECT {HeaderColumns} FROM FinancePostingProfiles WHERE Id = $Id;", ReadHeader, cancellationToken, Parameter("$Id", id));
		return header is null ? null : await LoadLinesAsync(header, cancellationToken);
	}

	public async Task<IReadOnlyList<FinancePostingProfile>> GetAllAsync(CancellationToken cancellationToken = default)
	{
		var headers = await Database.QueryAsync($"SELECT {HeaderColumns} FROM FinancePostingProfiles ORDER BY Code;", ReadHeader, cancellationToken);
		var result = new List<FinancePostingProfile>(headers.Count);
		foreach (var header in headers) result.Add(await LoadLinesAsync(header, cancellationToken));
		return result;
	}

	internal async Task<FinancePostingProfile?> GetByIdAsync(DatabaseTransactionContext transaction, long id, CancellationToken cancellationToken)
	{
		var header = await transaction.Session.QuerySingleOrDefaultAsync($"SELECT {HeaderColumns} FROM FinancePostingProfiles WHERE Id = $Id;", ReadHeader, cancellationToken, Parameter("$Id", id));
		return header is null ? null : await LoadLinesAsync(transaction, header, cancellationToken);
	}

	internal async Task<long> CreateAsync(DatabaseTransactionContext transaction, FinancePostingProfile profile, CancellationToken cancellationToken)
	{
		var id = await transaction.Session.InsertAsync(
			"INSERT INTO FinancePostingProfiles (Version, LegalEntityId, AccountingBookId, JournalId, Code, Name, SourceType, SourceEvent, NumberSequenceCode, IsActive) VALUES (1, $LegalEntityId, $AccountingBookId, $JournalId, $Code, $Name, $SourceType, $SourceEvent, $NumberSequenceCode, $IsActive);",
			cancellationToken,
			HeaderParameters(profile));
		await InsertLinesAsync(transaction, id, profile.Lines, cancellationToken);
		return id;
	}

	internal async Task<bool> UpdateAsync(DatabaseTransactionContext transaction, FinancePostingProfile profile, long expectedVersion, CancellationToken cancellationToken)
	{
		var updated = await transaction.Session.ExecuteAsync(
			"UPDATE FinancePostingProfiles SET Version = Version + 1, LegalEntityId = $LegalEntityId, AccountingBookId = $AccountingBookId, JournalId = $JournalId, Code = $Code, Name = $Name, SourceType = $SourceType, SourceEvent = $SourceEvent, NumberSequenceCode = $NumberSequenceCode, IsActive = $IsActive WHERE Id = $Id AND Version = $ExpectedVersion;",
			cancellationToken,
			HeaderParameters(profile)
				.Append(Parameter("$Id", profile.Id))
				.Append(Parameter("$ExpectedVersion", expectedVersion))
				.ToArray());
		if (updated == 0) return false;
		await transaction.Session.ExecuteAsync("DELETE FROM FinancePostingProfileLines WHERE PostingProfileId = $PostingProfileId;", cancellationToken, Parameter("$PostingProfileId", profile.Id));
		await InsertLinesAsync(transaction, profile.Id, profile.Lines, cancellationToken);
		return true;
	}

	private static async Task InsertLinesAsync(DatabaseTransactionContext transaction, long profileId, IReadOnlyList<FinancePostingProfileLine> lines, CancellationToken cancellationToken)
	{
		foreach (var line in lines.OrderBy(value => value.LineNumber))
		{
			await transaction.Session.ExecuteAsync(
				"INSERT INTO FinancePostingProfileLines (PostingProfileId, LineNumber, AccountId, Direction, AmountKey, Multiplier, Description) VALUES ($PostingProfileId, $LineNumber, $AccountId, $Direction, $AmountKey, $Multiplier, $Description);",
				cancellationToken,
				Parameter("$PostingProfileId", profileId),
				Parameter("$LineNumber", line.LineNumber),
				Parameter("$AccountId", line.AccountId.ToString("D")),
				Parameter("$Direction", (int)line.Direction),
				Parameter("$AmountKey", line.AmountKey),
				Parameter("$Multiplier", line.Multiplier),
				Parameter("$Description", line.Description));
		}
	}

	private static DatabaseParameter[] HeaderParameters(FinancePostingProfile profile) =>
	[
		Parameter("$LegalEntityId", profile.LegalEntityId.ToString("D")),
		Parameter("$AccountingBookId", profile.AccountingBookId.ToString("D")),
		Parameter("$JournalId", profile.JournalId.ToString("D")),
		Parameter("$Code", profile.Code),
		Parameter("$Name", profile.Name),
		Parameter("$SourceType", profile.SourceType),
		Parameter("$SourceEvent", profile.SourceEvent),
		Parameter("$NumberSequenceCode", profile.NumberSequenceCode),
		Parameter("$IsActive", profile.IsActive)
	];

	private async Task<FinancePostingProfile> LoadLinesAsync(FinancePostingProfile header, CancellationToken cancellationToken)
	{
		var lines = await Database.QueryAsync(
			"SELECT Id, PostingProfileId, LineNumber, AccountId, Direction, AmountKey, Multiplier, Description FROM FinancePostingProfileLines WHERE PostingProfileId = $Id ORDER BY LineNumber;",
			ReadLine,
			cancellationToken,
			Parameter("$Id", header.Id));
		return header with { Lines = lines };
	}

	private static async Task<FinancePostingProfile> LoadLinesAsync(DatabaseTransactionContext transaction, FinancePostingProfile header, CancellationToken cancellationToken)
	{
		var lines = await transaction.Session.QueryAsync(
			"SELECT Id, PostingProfileId, LineNumber, AccountId, Direction, AmountKey, Multiplier, Description FROM FinancePostingProfileLines WHERE PostingProfileId = $Id ORDER BY LineNumber;",
			ReadLine,
			cancellationToken,
			Parameter("$Id", header.Id));
		return header with { Lines = lines };
	}

	private static FinancePostingProfile ReadHeader(DbDataReader reader) => new()
	{
		Id = reader.GetInt64(0),
		Version = Convert.ToInt64(reader.GetValue(1), CultureInfo.InvariantCulture),
		LegalEntityId = Guid.Parse(reader.GetString(2)),
		AccountingBookId = Guid.Parse(reader.GetString(3)),
		JournalId = Guid.Parse(reader.GetString(4)),
		Code = reader.GetString(5),
		Name = reader.GetString(6),
		SourceType = reader.GetString(7),
		SourceEvent = reader.GetString(8),
		NumberSequenceCode = reader.GetString(9),
		IsActive = Convert.ToBoolean(reader.GetValue(10), CultureInfo.InvariantCulture)
	};

	private static FinancePostingProfileLine ReadLine(DbDataReader reader) => new()
	{
		Id = reader.GetInt64(0),
		PostingProfileId = reader.GetInt64(1),
		LineNumber = Convert.ToInt32(reader.GetValue(2), CultureInfo.InvariantCulture),
		AccountId = Guid.Parse(reader.GetString(3)),
		Direction = (FinancePostingDirection)Convert.ToInt32(reader.GetValue(4), CultureInfo.InvariantCulture),
		AmountKey = reader.GetString(5),
		Multiplier = Convert.ToDecimal(reader.GetValue(6), CultureInfo.InvariantCulture),
		Description = reader.IsDBNull(7) ? null : reader.GetString(7)
	};
}
