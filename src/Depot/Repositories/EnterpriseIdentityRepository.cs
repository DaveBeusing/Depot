// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;

using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class EnterpriseIdentityRepository : DatabaseRepository
{
	private const string ProviderColumns =
		"Id, Code, Kind, DisplayName, Authority, ClientId, TenantId, RequiredAmr, RequiredAcr, MaximumAuthenticationAgeMinutes, IsEnabled, CreatedUtc, UpdatedUtc, Version";
	private const string LinkColumns =
		"l.Id, l.ProviderId, p.Code, l.UserId, l.Issuer, l.Subject, l.IdentityKeySha256, l.TenantId, l.Email, l.DisplayName, l.LinkedUtc, l.LastSeenUtc, l.Version";

	public EnterpriseIdentityRepository(DatabaseAccess database) : base(database)
	{
	}

	public Task<IReadOnlyList<EnterpriseIdentityProvider>> ListProvidersAsync(CancellationToken cancellationToken) =>
		Database.QueryAsync(
			$"SELECT {ProviderColumns} FROM EnterpriseIdentityProviders ORDER BY Code, Id;",
			ReadProvider,
			cancellationToken);

	public Task<EnterpriseIdentityProvider?> GetProviderByIdAsync(long id, CancellationToken cancellationToken) =>
		Database.QuerySingleOrDefaultAsync(
			$"SELECT {ProviderColumns} FROM EnterpriseIdentityProviders WHERE Id = $Id;",
			ReadProvider,
			cancellationToken,
			Parameter("$Id", id));

	public Task<EnterpriseIdentityProvider?> GetProviderByCodeAsync(string code, CancellationToken cancellationToken) =>
		Database.QuerySingleOrDefaultAsync(
			$"SELECT {ProviderColumns} FROM EnterpriseIdentityProviders WHERE Code = $Code;",
			ReadProvider,
			cancellationToken,
			Parameter("$Code", code));

	public Task<IReadOnlyList<ExternalIdentityLink>> ListLinksForUserAsync(long userId, CancellationToken cancellationToken) =>
		Database.QueryAsync(
			$"SELECT {LinkColumns} FROM ExternalIdentityLinks l INNER JOIN EnterpriseIdentityProviders p ON p.Id = l.ProviderId WHERE l.UserId = $UserId ORDER BY p.Code, l.Issuer, l.Subject, l.Id;",
			ReadLink,
			cancellationToken,
			Parameter("$UserId", userId));

	public Task<ExternalIdentityLink?> GetLinkByIdAsync(long id, CancellationToken cancellationToken) =>
		Database.QuerySingleOrDefaultAsync(
			$"SELECT {LinkColumns} FROM ExternalIdentityLinks l INNER JOIN EnterpriseIdentityProviders p ON p.Id = l.ProviderId WHERE l.Id = $Id;",
			ReadLink,
			cancellationToken,
			Parameter("$Id", id));

	public Task<ExternalIdentityLink?> GetLinkByIdentityKeyAsync(string identityKeySha256, CancellationToken cancellationToken) =>
		Database.QuerySingleOrDefaultAsync(
			$"SELECT {LinkColumns} FROM ExternalIdentityLinks l INNER JOIN EnterpriseIdentityProviders p ON p.Id = l.ProviderId WHERE l.IdentityKeySha256 = $IdentityKey;",
			ReadLink,
			cancellationToken,
			Parameter("$IdentityKey", identityKeySha256));

	public Task<long> CreateProviderAsync(
		DatabaseTransactionContext transaction,
		EnterpriseIdentityProvider provider,
		CancellationToken cancellationToken) =>
		transaction.Session.InsertAsync(
			"""
			INSERT INTO EnterpriseIdentityProviders
			(Code, Kind, DisplayName, Authority, ClientId, TenantId, RequiredAmr, RequiredAcr, MaximumAuthenticationAgeMinutes, IsEnabled, CreatedUtc, UpdatedUtc, Version)
			VALUES ($Code, $Kind, $DisplayName, $Authority, $ClientId, $TenantId, $RequiredAmr, $RequiredAcr, $MaximumAuthenticationAgeMinutes, $IsEnabled, $CreatedUtc, $UpdatedUtc, 1);
			""",
			cancellationToken,
			ProviderParameters(provider));

	public async Task<bool> UpdateProviderAsync(
		DatabaseTransactionContext transaction,
		EnterpriseIdentityProvider provider,
		long expectedVersion,
		CancellationToken cancellationToken) =>
		await transaction.Session.ExecuteAsync(
			"""
			UPDATE EnterpriseIdentityProviders
			SET DisplayName = $DisplayName, Authority = $Authority, ClientId = $ClientId, TenantId = $TenantId,
			    RequiredAmr = $RequiredAmr, RequiredAcr = $RequiredAcr, MaximumAuthenticationAgeMinutes = $MaximumAuthenticationAgeMinutes,
			    IsEnabled = $IsEnabled, UpdatedUtc = $UpdatedUtc, Version = Version + 1
			WHERE Id = $Id AND Version = $ExpectedVersion;
			""",
			cancellationToken,
			Parameter("$Id", provider.Id),
			Parameter("$DisplayName", provider.DisplayName),
			Parameter("$Authority", provider.Authority),
			Parameter("$ClientId", provider.ClientId),
			Parameter("$TenantId", provider.TenantId),
			Parameter("$RequiredAmr", provider.RequiredAmr),
			Parameter("$RequiredAcr", provider.RequiredAcr),
			Parameter("$MaximumAuthenticationAgeMinutes", provider.MaximumAuthenticationAgeMinutes),
			Parameter("$IsEnabled", provider.IsEnabled),
			Parameter("$UpdatedUtc", Format(provider.UpdatedUtc)),
			Parameter("$ExpectedVersion", expectedVersion)) == 1;

	public Task<long> CreateLinkAsync(
		DatabaseTransactionContext transaction,
		ExternalIdentityLink link,
		CancellationToken cancellationToken) =>
		transaction.Session.InsertAsync(
			"""
			INSERT INTO ExternalIdentityLinks
			(ProviderId, UserId, Issuer, Subject, IdentityKeySha256, TenantId, Email, DisplayName, LinkedUtc, LastSeenUtc, Version)
			VALUES ($ProviderId, $UserId, $Issuer, $Subject, $IdentityKey, $TenantId, $Email, $DisplayName, $LinkedUtc, $LastSeenUtc, 1);
			""",
			cancellationToken,
			LinkParameters(link));

	public async Task<bool> UpdateObservationAsync(
		DatabaseTransactionContext transaction,
		ExternalIdentityLink link,
		long expectedVersion,
		CancellationToken cancellationToken) =>
		await transaction.Session.ExecuteAsync(
			"""
			UPDATE ExternalIdentityLinks
			SET TenantId = $TenantId, Email = $Email, DisplayName = $DisplayName, LastSeenUtc = $LastSeenUtc, Version = Version + 1
			WHERE Id = $Id AND Version = $ExpectedVersion;
			""",
			cancellationToken,
			Parameter("$Id", link.Id),
			Parameter("$TenantId", link.TenantId),
			Parameter("$Email", link.Email),
			Parameter("$DisplayName", link.DisplayName),
			Parameter("$LastSeenUtc", link.LastSeenUtc is null ? null : Format(link.LastSeenUtc.Value)),
			Parameter("$ExpectedVersion", expectedVersion)) == 1;

	public async Task<bool> DeleteLinkAsync(
		DatabaseTransactionContext transaction,
		long id,
		long expectedVersion,
		CancellationToken cancellationToken) =>
		await transaction.Session.ExecuteAsync(
			"DELETE FROM ExternalIdentityLinks WHERE Id = $Id AND Version = $ExpectedVersion;",
			cancellationToken,
			Parameter("$Id", id),
			Parameter("$ExpectedVersion", expectedVersion)) == 1;

	private static DatabaseParameter[] ProviderParameters(EnterpriseIdentityProvider provider) =>
	[
		Parameter("$Code", provider.Code),
		Parameter("$Kind", (int)provider.Kind),
		Parameter("$DisplayName", provider.DisplayName),
		Parameter("$Authority", provider.Authority),
		Parameter("$ClientId", provider.ClientId),
		Parameter("$TenantId", provider.TenantId),
		Parameter("$RequiredAmr", provider.RequiredAmr),
		Parameter("$RequiredAcr", provider.RequiredAcr),
		Parameter("$MaximumAuthenticationAgeMinutes", provider.MaximumAuthenticationAgeMinutes),
		Parameter("$IsEnabled", provider.IsEnabled),
		Parameter("$CreatedUtc", Format(provider.CreatedUtc)),
		Parameter("$UpdatedUtc", Format(provider.UpdatedUtc))
	];

	private static DatabaseParameter[] LinkParameters(ExternalIdentityLink link) =>
	[
		Parameter("$ProviderId", link.ProviderId),
		Parameter("$UserId", link.UserId),
		Parameter("$Issuer", link.Issuer),
		Parameter("$Subject", link.Subject),
		Parameter("$IdentityKey", link.IdentityKeySha256),
		Parameter("$TenantId", link.TenantId),
		Parameter("$Email", link.Email),
		Parameter("$DisplayName", link.DisplayName),
		Parameter("$LinkedUtc", Format(link.LinkedUtc)),
		Parameter("$LastSeenUtc", link.LastSeenUtc is null ? null : Format(link.LastSeenUtc.Value))
	];

	private static EnterpriseIdentityProvider ReadProvider(DbDataReader reader) => new()
	{
		Id = reader.GetInt64(0),
		Code = reader.GetString(1),
		Kind = (EnterpriseIdentityProviderKind)reader.GetInt32(2),
		DisplayName = reader.GetString(3),
		Authority = reader.GetString(4),
		ClientId = reader.GetString(5),
		TenantId = reader.IsDBNull(6) ? null : reader.GetString(6),
		RequiredAmr = reader.IsDBNull(7) ? null : reader.GetString(7),
		RequiredAcr = reader.IsDBNull(8) ? null : reader.GetString(8),
		MaximumAuthenticationAgeMinutes = reader.IsDBNull(9) ? null : reader.GetInt32(9),
		IsEnabled = reader.GetBoolean(10),
		CreatedUtc = ReadDateTime(reader, 11),
		UpdatedUtc = ReadDateTime(reader, 12),
		Version = reader.GetInt64(13)
	};

	private static ExternalIdentityLink ReadLink(DbDataReader reader) => new()
	{
		Id = reader.GetInt64(0),
		ProviderId = reader.GetInt64(1),
		ProviderCode = reader.GetString(2),
		UserId = reader.GetInt64(3),
		Issuer = reader.GetString(4),
		Subject = reader.GetString(5),
		IdentityKeySha256 = reader.GetString(6),
		TenantId = reader.IsDBNull(7) ? null : reader.GetString(7),
		Email = reader.IsDBNull(8) ? null : reader.GetString(8),
		DisplayName = reader.IsDBNull(9) ? null : reader.GetString(9),
		LinkedUtc = ReadDateTime(reader, 10),
		LastSeenUtc = reader.IsDBNull(11) ? null : ReadDateTime(reader, 11),
		Version = reader.GetInt64(12)
	};

	private static DateTime ReadDateTime(DbDataReader reader, int ordinal)
	{
		var value = reader.GetValue(ordinal);
		return value is DateTime dateTime
			? DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)
			: DateTime.Parse(
				Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
				CultureInfo.InvariantCulture,
				DateTimeStyles.RoundtripKind).ToUniversalTime();
	}

	private static string Format(DateTime value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
