// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;

using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class AuthenticationSecurityRepository : DatabaseRepository
{
	private const string PolicyColumns = "Id, FailureWindowMinutes, LockoutThreshold, LockoutDurationMinutes, SecurityEventRetentionDays, UpdatedUtc, Version";
	private const string ThrottleColumns = "AccountKey, FirstFailureUtc, FailureCount, BlockedUntilUtc, UpdatedUtc, Version";

	public AuthenticationSecurityRepository(DatabaseAccess database) : base(database) { }

	public async Task<AuthenticationSecurityPolicy> GetPolicyAsync(CancellationToken cancellationToken) =>
		await Database.QuerySingleOrDefaultAsync($"SELECT {PolicyColumns} FROM AuthenticationSecurityPolicy WHERE Id=1;", ReadPolicy, cancellationToken)
		?? DefaultPolicy();

	public static async Task<AuthenticationSecurityPolicy> GetPolicyAsync(DatabaseTransactionContext transaction, CancellationToken cancellationToken) =>
		await transaction.Session.QuerySingleOrDefaultAsync($"SELECT {PolicyColumns} FROM AuthenticationSecurityPolicy WHERE Id=1;", ReadPolicy, cancellationToken)
		?? DefaultPolicy();

	public static Task<int> AcquirePolicyLockAsync(DatabaseTransactionContext transaction, CancellationToken cancellationToken) =>
		transaction.Session.ExecuteAsync("UPDATE AuthenticationSecurityPolicy SET Version=Version WHERE Id=1;", cancellationToken);

	public static async Task<bool> UpdatePolicyAsync(DatabaseTransactionContext transaction, AuthenticationSecurityPolicy policy, long expectedVersion, CancellationToken cancellationToken) =>
		await transaction.Session.ExecuteAsync(
			"UPDATE AuthenticationSecurityPolicy SET FailureWindowMinutes=$FailureWindowMinutes, LockoutThreshold=$LockoutThreshold, LockoutDurationMinutes=$LockoutDurationMinutes, SecurityEventRetentionDays=$SecurityEventRetentionDays, UpdatedUtc=$UpdatedUtc, Version=Version+1 WHERE Id=1 AND Version=$ExpectedVersion;",
			cancellationToken,
			Parameter("$FailureWindowMinutes", policy.FailureWindowMinutes),
			Parameter("$LockoutThreshold", policy.LockoutThreshold),
			Parameter("$LockoutDurationMinutes", policy.LockoutDurationMinutes),
			Parameter("$SecurityEventRetentionDays", policy.SecurityEventRetentionDays),
			Parameter("$UpdatedUtc", Format(policy.UpdatedUtc)),
			Parameter("$ExpectedVersion", expectedVersion)) == 1;

	public static Task<AuthenticationThrottleState?> GetThrottleAsync(DatabaseTransactionContext transaction, string accountKey, CancellationToken cancellationToken) =>
		transaction.Session.QuerySingleOrDefaultAsync(
			$"SELECT {ThrottleColumns} FROM AuthenticationThrottle WHERE AccountKey=$AccountKey;",
			ReadThrottle,
			cancellationToken,
			Parameter("$AccountKey", accountKey));

	public static async Task UpsertThrottleAsync(DatabaseTransactionContext transaction, AuthenticationThrottleState state, CancellationToken cancellationToken)
	{
		var updated = await transaction.Session.ExecuteAsync(
			"UPDATE AuthenticationThrottle SET FirstFailureUtc=$FirstFailureUtc, FailureCount=$FailureCount, BlockedUntilUtc=$BlockedUntilUtc, UpdatedUtc=$UpdatedUtc, Version=Version+1 WHERE AccountKey=$AccountKey;",
			cancellationToken,
			ThrottleParameters(state));
		if (updated == 1) return;
		await transaction.Session.ExecuteAsync(
			"INSERT INTO AuthenticationThrottle (AccountKey, FirstFailureUtc, FailureCount, BlockedUntilUtc, UpdatedUtc, Version) VALUES ($AccountKey,$FirstFailureUtc,$FailureCount,$BlockedUntilUtc,$UpdatedUtc,1);",
			cancellationToken,
			ThrottleParameters(state));
	}

	public static Task<int> DeleteThrottleAsync(DatabaseTransactionContext transaction, string accountKey, CancellationToken cancellationToken) =>
		transaction.Session.ExecuteAsync(
			"DELETE FROM AuthenticationThrottle WHERE AccountKey=$AccountKey;",
			cancellationToken,
			Parameter("$AccountKey", accountKey));

	public Task<IReadOnlyList<string>> GetThrottleKeysBeforeAsync(DateTime cutoffUtc, int count, CancellationToken cancellationToken)
	{
		if (count is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(count));
		return Database.QuerySliceAsync(
			"SELECT AccountKey FROM AuthenticationThrottle WHERE UpdatedUtc < $CutoffUtc ORDER BY UpdatedUtc, AccountKey",
			reader => reader.GetString(0),
			0,
			count,
			cancellationToken,
			Parameter("$CutoffUtc", Format(cutoffUtc)));
	}

	public static async Task<int> DeleteThrottleKeysBeforeAsync(DatabaseTransactionContext transaction, IReadOnlyCollection<string> accountKeys, DateTime cutoffUtc, CancellationToken cancellationToken)
	{
		var deleted = 0;
		foreach (var accountKey in accountKeys)
		{
			deleted += await transaction.Session.ExecuteAsync(
				"DELETE FROM AuthenticationThrottle WHERE AccountKey=$AccountKey AND UpdatedUtc < $CutoffUtc;",
				cancellationToken,
				Parameter("$AccountKey", accountKey),
				Parameter("$CutoffUtc", Format(cutoffUtc)));
		}
		return deleted;
	}

	private static DatabaseParameter[] ThrottleParameters(AuthenticationThrottleState state) =>
	[
		Parameter("$AccountKey", state.AccountKey),
		Parameter("$FirstFailureUtc", Format(state.FirstFailureUtc)),
		Parameter("$FailureCount", state.FailureCount),
		Parameter("$BlockedUntilUtc", Format(state.BlockedUntilUtc)),
		Parameter("$UpdatedUtc", Format(state.UpdatedUtc))
	];

	private static AuthenticationSecurityPolicy ReadPolicy(DbDataReader reader) => new()
	{
		Id = reader.GetInt64(0),
		FailureWindowMinutes = Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture),
		LockoutThreshold = Convert.ToInt32(reader.GetValue(2), CultureInfo.InvariantCulture),
		LockoutDurationMinutes = Convert.ToInt32(reader.GetValue(3), CultureInfo.InvariantCulture),
		SecurityEventRetentionDays = Convert.ToInt32(reader.GetValue(4), CultureInfo.InvariantCulture),
		UpdatedUtc = ReadUtc(reader, 5),
		Version = reader.GetInt64(6)
	};

	private static AuthenticationThrottleState ReadThrottle(DbDataReader reader) => new()
	{
		AccountKey = reader.GetString(0),
		FirstFailureUtc = ReadUtc(reader, 1),
		FailureCount = Convert.ToInt32(reader.GetValue(2), CultureInfo.InvariantCulture),
		BlockedUntilUtc = ReadNullableUtc(reader, 3),
		UpdatedUtc = ReadUtc(reader, 4),
		Version = reader.GetInt64(5)
	};

	private static AuthenticationSecurityPolicy DefaultPolicy() => new() { UpdatedUtc = DateTime.UnixEpoch, Version = 1 };
	private static string Format(DateTime value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
	private static string? Format(DateTime? value) => value?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
	private static DateTime ReadUtc(DbDataReader reader, int ordinal) => DateTime.Parse(Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture) ?? string.Empty, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
	private static DateTime? ReadNullableUtc(DbDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : ReadUtc(reader, ordinal);
}
