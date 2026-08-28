// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;
using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class FinanceLocalizationRepository : DatabaseRepository
{
	public FinanceLocalizationRepository(DatabaseAccess database) : base(database) { }

	public Task<IReadOnlyList<LegalEntity>> GetLegalEntitiesAsync(CancellationToken cancellationToken = default) =>
		Database.QueryAsync("SELECT Id,Code,Name,CountryCode,FunctionalCurrencyCode,IsActive FROM FinanceLegalEntities ORDER BY Code,Id;", ReadLegalEntity, cancellationToken);

	public Task<IReadOnlyList<FinanceLocalizationPack>> GetPacksAsync(CancellationToken cancellationToken = default) =>
		Database.QueryAsync(PackSelect + " ORDER BY Layer,Code,Id;", ReadPack, cancellationToken);

	public Task<IReadOnlyList<FinanceLocalizationAssignment>> GetAssignmentsAsync(Guid legalEntityId, CancellationToken cancellationToken = default) =>
		Database.QueryAsync(AssignmentSelect + " WHERE LegalEntityId=$Entity ORDER BY EffectiveFrom DESC,Id DESC;", ReadAssignment, cancellationToken, Parameter("$Entity", legalEntityId.ToString("D")));

	public Task<IReadOnlyList<FinanceLocalizationRegistryEntry>> GetRegistryAsync(string? packCode = null, CancellationToken cancellationToken = default)
	{
		var sql = RegistrySelect;
		if (!string.IsNullOrWhiteSpace(packCode))
			return Database.QueryAsync(sql + " WHERE PackCode=$Pack ORDER BY EffectiveFrom DESC,RequirementCode,Id;", ReadRegistry, cancellationToken, Parameter("$Pack", packCode.Trim().ToUpperInvariant()));
		return Database.QueryAsync(sql + " ORDER BY PackCode,EffectiveFrom DESC,RequirementCode,Id;", ReadRegistry, cancellationToken);
	}

	internal Task<LegalEntity?> GetLegalEntityAsync(DatabaseTransactionContext transaction, Guid id, CancellationToken cancellationToken) =>
		transaction.Session.QuerySingleOrDefaultAsync("SELECT Id,Code,Name,CountryCode,FunctionalCurrencyCode,IsActive FROM FinanceLegalEntities WHERE Id=$Id;", ReadLegalEntity, cancellationToken, Parameter("$Id", id.ToString("D")));

	internal Task<FinanceLocalizationPack?> GetPackAsync(DatabaseTransactionContext transaction, string code, CancellationToken cancellationToken) =>
		transaction.Session.QuerySingleOrDefaultAsync(PackSelect + " WHERE Code=$Code;", ReadPack, cancellationToken, Parameter("$Code", code));

	internal Task<FinanceLocalizationPack?> GetPackByIdAsync(DatabaseTransactionContext transaction, long id, CancellationToken cancellationToken) =>
		transaction.Session.QuerySingleOrDefaultAsync(PackSelect + " WHERE Id=$Id;", ReadPack, cancellationToken, Parameter("$Id", id));

	internal Task<long> CreatePackAsync(DatabaseTransactionContext transaction, FinanceLocalizationPack value, CancellationToken cancellationToken) =>
		transaction.Session.InsertAsync("INSERT INTO FinanceLocalizationPacks (Version,Code,Name,Layer,CountryCode,ParentPackCode,Description,IsBuiltIn,IsActive) VALUES (1,$Code,$Name,$Layer,$Country,$Parent,$Description,0,$Active);", cancellationToken, PackParameters(value));

	internal Task<int> UpdatePackAsync(DatabaseTransactionContext transaction, FinanceLocalizationPack value, long expectedVersion, CancellationToken cancellationToken) =>
		transaction.Session.ExecuteAsync("UPDATE FinanceLocalizationPacks SET Version=Version+1,Name=$Name,Description=$Description,IsActive=$Active WHERE Id=$Id AND Version=$Version AND IsBuiltIn=0;", cancellationToken,
			Parameter("$Name", value.Name), Parameter("$Description", value.Description), Parameter("$Active", value.IsActive), Parameter("$Id", value.Id), Parameter("$Version", expectedVersion));

	internal Task<FinanceLocalizationAssignment?> GetAssignmentAsync(DatabaseTransactionContext transaction, long id, CancellationToken cancellationToken) =>
		transaction.Session.QuerySingleOrDefaultAsync(AssignmentSelect + " WHERE Id=$Id;", ReadAssignment, cancellationToken, Parameter("$Id", id));

	internal Task<FinanceLocalizationAssignment?> GetEffectiveAssignmentAsync(DatabaseTransactionContext transaction, Guid legalEntityId, DateOnly asOfDate, CancellationToken cancellationToken) =>
		transaction.Session.QuerySingleOrDefaultAsync(AssignmentSelect + " WHERE LegalEntityId=$Entity AND IsActive=1 AND EffectiveFrom<=$Date AND (EffectiveTo IS NULL OR EffectiveTo>=$Date) ORDER BY EffectiveFrom DESC,Id DESC;", ReadAssignment, cancellationToken,
			Parameter("$Entity", legalEntityId.ToString("D")), Parameter("$Date", Date(asOfDate)));

	internal async Task<bool> HasOverlappingAssignmentAsync(DatabaseTransactionContext transaction, Guid legalEntityId, DateOnly effectiveFrom, DateOnly? effectiveTo, long excludeId, CancellationToken cancellationToken)
	{
		var values = await transaction.Session.QueryAsync("SELECT COUNT(*) FROM FinanceLocalizationAssignments WHERE LegalEntityId=$Entity AND IsActive=1 AND Id<>$Exclude AND ($End IS NULL OR EffectiveFrom<=$End) AND (EffectiveTo IS NULL OR EffectiveTo>=$From);", reader => Convert.ToInt64(reader.GetValue(0), CultureInfo.InvariantCulture), cancellationToken,
			Parameter("$Entity", legalEntityId.ToString("D")), Parameter("$Exclude", excludeId), Parameter("$From", Date(effectiveFrom)), Parameter("$End", effectiveTo.HasValue ? Date(effectiveTo.Value) : null));
		return values.FirstOrDefault() > 0;
	}

	internal Task<long> CreateAssignmentAsync(DatabaseTransactionContext transaction, FinanceLocalizationAssignment value, CancellationToken cancellationToken) =>
		transaction.Session.InsertAsync("INSERT INTO FinanceLocalizationAssignments (Version,LegalEntityId,PackCode,EffectiveFrom,EffectiveTo,IsActive,CreatedAtUtc,CreatedByUserId) VALUES (1,$Entity,$Pack,$From,$To,$Active,$Created,$User);", cancellationToken,
			Parameter("$Entity", value.LegalEntityId.ToString("D")), Parameter("$Pack", value.PackCode), Parameter("$From", Date(value.EffectiveFrom)), Parameter("$To", value.EffectiveTo.HasValue ? Date(value.EffectiveTo.Value) : null), Parameter("$Active", value.IsActive), Parameter("$Created", value.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture)), Parameter("$User", value.CreatedByUserId));

	internal Task<int> UpdateAssignmentAsync(DatabaseTransactionContext transaction, FinanceLocalizationAssignment value, long expectedVersion, CancellationToken cancellationToken) =>
		transaction.Session.ExecuteAsync("UPDATE FinanceLocalizationAssignments SET Version=Version+1,EffectiveTo=$To,IsActive=$Active WHERE Id=$Id AND Version=$Version;", cancellationToken,
			Parameter("$To", value.EffectiveTo.HasValue ? Date(value.EffectiveTo.Value) : null), Parameter("$Active", value.IsActive), Parameter("$Id", value.Id), Parameter("$Version", expectedVersion));

	internal Task<FinanceLocalizationRegistryEntry?> GetRegistryEntryAsync(DatabaseTransactionContext transaction, long id, CancellationToken cancellationToken) =>
		transaction.Session.QuerySingleOrDefaultAsync(RegistrySelect + " WHERE Id=$Id;", ReadRegistry, cancellationToken, Parameter("$Id", id));

	internal Task<IReadOnlyList<FinanceLocalizationRegistryEntry>> GetEffectiveRegistryAsync(DatabaseTransactionContext transaction, string packCode, DateOnly asOfDate, CancellationToken cancellationToken) =>
		transaction.Session.QueryAsync(RegistrySelect + " WHERE PackCode=$Pack AND IsActive=1 AND EffectiveFrom<=$Date AND (EffectiveTo IS NULL OR EffectiveTo>=$Date) ORDER BY RequirementCode,EffectiveFrom DESC,Id DESC;", ReadRegistry, cancellationToken,
			Parameter("$Pack", packCode), Parameter("$Date", Date(asOfDate)));

	internal async Task<bool> HasOverlappingRegistryEntryAsync(DatabaseTransactionContext transaction, string packCode, string requirementCode, DateOnly effectiveFrom, DateOnly? effectiveTo, long excludeId, CancellationToken cancellationToken)
	{
		var values = await transaction.Session.QueryAsync("SELECT COUNT(*) FROM FinanceLocalizationRegistryEntries WHERE PackCode=$Pack AND RequirementCode=$Requirement AND IsActive=1 AND Id<>$Exclude AND ($End IS NULL OR EffectiveFrom<=$End) AND (EffectiveTo IS NULL OR EffectiveTo>=$From);", reader => Convert.ToInt64(reader.GetValue(0), CultureInfo.InvariantCulture), cancellationToken,
			Parameter("$Pack", packCode), Parameter("$Requirement", requirementCode), Parameter("$Exclude", excludeId), Parameter("$From", Date(effectiveFrom)), Parameter("$End", effectiveTo.HasValue ? Date(effectiveTo.Value) : null));
		return values.FirstOrDefault() > 0;
	}

	internal Task<long> CreateRegistryEntryAsync(DatabaseTransactionContext transaction, FinanceLocalizationRegistryEntry value, CancellationToken cancellationToken) =>
		transaction.Session.InsertAsync("INSERT INTO FinanceLocalizationRegistryEntries (Version,PackCode,RequirementCode,Category,SupportLevel,EffectiveFrom,EffectiveTo,Title,Description,Reference,IsBuiltIn,IsActive) VALUES (1,$Pack,$Requirement,$Category,$Support,$From,$To,$Title,$Description,$Reference,0,$Active);", cancellationToken, RegistryParameters(value));

	internal Task<int> UpdateRegistryEntryAsync(DatabaseTransactionContext transaction, FinanceLocalizationRegistryEntry value, long expectedVersion, CancellationToken cancellationToken) =>
		transaction.Session.ExecuteAsync("UPDATE FinanceLocalizationRegistryEntries SET Version=Version+1,Category=$Category,SupportLevel=$Support,EffectiveTo=$To,Title=$Title,Description=$Description,Reference=$Reference,IsActive=$Active WHERE Id=$Id AND Version=$Version AND IsBuiltIn=0;", cancellationToken,
			RegistryParameters(value).Concat([Parameter("$Id", value.Id), Parameter("$Version", expectedVersion)]).ToArray());

	private const string PackSelect = "SELECT Id,Version,Code,Name,Layer,CountryCode,ParentPackCode,Description,IsBuiltIn,IsActive FROM FinanceLocalizationPacks";
	private const string AssignmentSelect = "SELECT Id,Version,LegalEntityId,PackCode,EffectiveFrom,EffectiveTo,IsActive,CreatedAtUtc,CreatedByUserId FROM FinanceLocalizationAssignments";
	private const string RegistrySelect = "SELECT Id,Version,PackCode,RequirementCode,Category,SupportLevel,EffectiveFrom,EffectiveTo,Title,Description,Reference,IsBuiltIn,IsActive FROM FinanceLocalizationRegistryEntries";

	private static DatabaseParameter[] PackParameters(FinanceLocalizationPack value) =>
	[
		Parameter("$Code", value.Code), Parameter("$Name", value.Name), Parameter("$Layer", (int)value.Layer), Parameter("$Country", value.CountryCode), Parameter("$Parent", value.ParentPackCode), Parameter("$Description", value.Description), Parameter("$Active", value.IsActive)
	];

	private static DatabaseParameter[] RegistryParameters(FinanceLocalizationRegistryEntry value) =>
	[
		Parameter("$Pack", value.PackCode), Parameter("$Requirement", value.RequirementCode), Parameter("$Category", (int)value.Category), Parameter("$Support", (int)value.SupportLevel), Parameter("$From", Date(value.EffectiveFrom)), Parameter("$To", value.EffectiveTo.HasValue ? Date(value.EffectiveTo.Value) : null), Parameter("$Title", value.Title), Parameter("$Description", value.Description), Parameter("$Reference", value.Reference), Parameter("$Active", value.IsActive)
	];

	private static LegalEntity ReadLegalEntity(DbDataReader reader) => new(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetString(3), new CurrencyCode(reader.GetString(4)), ReadBool(reader, 5));
	private static FinanceLocalizationPack ReadPack(DbDataReader reader) => new() { Id=reader.GetInt64(0),Version=Convert.ToInt64(reader.GetValue(1),CultureInfo.InvariantCulture),Code=reader.GetString(2),Name=reader.GetString(3),Layer=(FinanceLocalizationLayer)Convert.ToInt32(reader.GetValue(4),CultureInfo.InvariantCulture),CountryCode=reader.IsDBNull(5)?null:reader.GetString(5),ParentPackCode=reader.IsDBNull(6)?null:reader.GetString(6),Description=reader.GetString(7),IsBuiltIn=ReadBool(reader,8),IsActive=ReadBool(reader,9) };
	private static FinanceLocalizationAssignment ReadAssignment(DbDataReader reader) => new() { Id=reader.GetInt64(0),Version=Convert.ToInt64(reader.GetValue(1),CultureInfo.InvariantCulture),LegalEntityId=Guid.Parse(reader.GetString(2)),PackCode=reader.GetString(3),EffectiveFrom=ReadDate(reader,4),EffectiveTo=reader.IsDBNull(5)?null:ReadDate(reader,5),IsActive=ReadBool(reader,6),CreatedAtUtc=ReadDateTime(reader,7),CreatedByUserId=reader.GetInt64(8) };
	private static FinanceLocalizationRegistryEntry ReadRegistry(DbDataReader reader) => new() { Id=reader.GetInt64(0),Version=Convert.ToInt64(reader.GetValue(1),CultureInfo.InvariantCulture),PackCode=reader.GetString(2),RequirementCode=reader.GetString(3),Category=(FinanceLocalizationRequirementCategory)Convert.ToInt32(reader.GetValue(4),CultureInfo.InvariantCulture),SupportLevel=(FinanceLocalizationSupportLevel)Convert.ToInt32(reader.GetValue(5),CultureInfo.InvariantCulture),EffectiveFrom=ReadDate(reader,6),EffectiveTo=reader.IsDBNull(7)?null:ReadDate(reader,7),Title=reader.GetString(8),Description=reader.GetString(9),Reference=reader.GetString(10),IsBuiltIn=ReadBool(reader,11),IsActive=ReadBool(reader,12) };
	private static bool ReadBool(DbDataReader reader,int ordinal)=>Convert.ToInt32(reader.GetValue(ordinal),CultureInfo.InvariantCulture)!=0;
	private static DateOnly ReadDate(DbDataReader reader,int ordinal)=>reader.GetValue(ordinal) is DateTime date?DateOnly.FromDateTime(date):DateOnly.Parse(reader.GetString(ordinal),CultureInfo.InvariantCulture);
	private static DateTime ReadDateTime(DbDataReader reader,int ordinal)=>reader.GetValue(ordinal) is DateTime date?DateTime.SpecifyKind(date,DateTimeKind.Utc):DateTime.Parse(reader.GetString(ordinal),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind).ToUniversalTime();
	private static string Date(DateOnly value)=>value.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
}
