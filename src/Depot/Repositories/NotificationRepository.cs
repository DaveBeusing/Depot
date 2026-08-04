// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;

using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class NotificationRepository : DatabaseRepository
{
	private const string SelectColumns =
		"nr.Id, n.Id, n.Type, n.Severity, n.Title, n.Message, n.SourceType, n.SourceId, n.SourceNumber, n.CreatedAtUtc, n.ExpiresAtUtc, nr.ReadAtUtc, nr.ArchivedAtUtc, nr.Version";

	public NotificationRepository(DatabaseAccess database) : base(database)
	{
	}

	public Task<long> GetUnreadCountAsync(long userId, DateTime nowUtc, CancellationToken cancellationToken) =>
		CountAsync(null, userId, nowUtc, cancellationToken);

	public async Task<PageResult<NotificationListItem>> GetPageAsync(
		long userId,
		NotificationFilter filter,
		int pageNumber,
		int pageSize,
		DateTime nowUtc,
		CancellationToken cancellationToken)
	{
		var (where, parameters) = BuildFilter(userId, filter, nowUtc);
		return await Database.QueryPageAsync(
			$"SELECT {SelectColumns} FROM NotificationRecipients nr INNER JOIN Notifications n ON n.Id = nr.NotificationId {where} ORDER BY n.CreatedAtUtc DESC, n.Id DESC",
			$"SELECT COUNT(*) FROM NotificationRecipients nr INNER JOIN Notifications n ON n.Id = nr.NotificationId {where};",
			ReadListItem,
			pageNumber,
			pageSize,
			cancellationToken,
			parameters);
	}

	public Task<NotificationDetails?> GetDetailsAsync(long recipientId, long userId, DateTime nowUtc, CancellationToken cancellationToken) =>
		Database.QuerySingleOrDefaultAsync(
			$"SELECT {SelectColumns} FROM NotificationRecipients nr INNER JOIN Notifications n ON n.Id = nr.NotificationId WHERE nr.Id = $RecipientId AND nr.UserId = $UserId AND (n.ExpiresAtUtc IS NULL OR n.ExpiresAtUtc > $NowUtc);",
			ReadDetails,
			cancellationToken,
			Parameter("$RecipientId", recipientId),
			Parameter("$UserId", userId),
			Parameter("$NowUtc", Format(nowUtc)));

	public Task<IReadOnlyList<long>> GetActiveUserIdsWithPermissionAsync(
		DatabaseTransactionContext transaction,
		ApplicationPermission permission,
		CancellationToken cancellationToken) =>
		transaction.Session.QueryAsync(
			"SELECT DISTINCT u.Id FROM Users u INNER JOIN UserRoles ur ON ur.UserId = u.Id INNER JOIN Roles r ON r.Id = ur.RoleId AND r.IsActive = 1 INNER JOIN RolePermissions rp ON rp.RoleId = r.Id INNER JOIN Permissions p ON p.Id = rp.PermissionId WHERE u.IsActive = 1 AND p.Code = $PermissionCode ORDER BY u.Id;",
			reader => reader.GetInt64(0),
			cancellationToken,
			Parameter("$PermissionCode", PermissionCatalog.Code(permission)));

	public Task<IReadOnlyList<long>> GetActiveAdministratorIdsAsync(
		DatabaseTransactionContext transaction,
		CancellationToken cancellationToken) =>
		transaction.Session.QueryAsync(
			"SELECT DISTINCT u.Id FROM Users u INNER JOIN UserRoles ur ON ur.UserId = u.Id INNER JOIN Roles r ON r.Id = ur.RoleId WHERE u.IsActive = 1 AND r.IsActive = 1 AND r.Code = $RoleCode ORDER BY u.Id;",
			reader => reader.GetInt64(0), cancellationToken,
			Parameter("$RoleCode", SystemRoleCatalog.AdministratorCode));

	public async Task<long> CreateAsync(
		DatabaseTransactionContext transaction,
		Notification notification,
		IReadOnlyCollection<long> recipientUserIds,
		CancellationToken cancellationToken)
	{
		var notificationId = await transaction.Session.InsertAsync(
			"INSERT INTO Notifications (Type, Severity, Title, Message, SourceType, SourceId, SourceNumber, CreatedAtUtc, CreatedByUserId, ExpiresAtUtc, Version) VALUES ($Type, $Severity, $Title, $Message, $SourceType, $SourceId, $SourceNumber, $CreatedAtUtc, $CreatedByUserId, $ExpiresAtUtc, 1);",
			cancellationToken,
			Parameter("$Type", (int)notification.Type), Parameter("$Severity", (int)notification.Severity),
			Parameter("$Title", notification.Title), Parameter("$Message", notification.Message),
			Parameter("$SourceType", notification.SourceType), Parameter("$SourceId", notification.SourceId),
			Parameter("$SourceNumber", notification.SourceNumber), Parameter("$CreatedAtUtc", Format(notification.CreatedAtUtc)),
			Parameter("$CreatedByUserId", notification.CreatedByUserId), Parameter("$ExpiresAtUtc", Format(notification.ExpiresAtUtc)));

		foreach (var userId in recipientUserIds.Distinct().Order())
		{
			await transaction.Session.ExecuteAsync(
				"INSERT INTO NotificationRecipients (NotificationId, UserId, ReadAtUtc, ArchivedAtUtc, CreatedAtUtc, Version) VALUES ($NotificationId, $UserId, NULL, NULL, $CreatedAtUtc, 1);",
				cancellationToken,
				Parameter("$NotificationId", notificationId), Parameter("$UserId", userId),
				Parameter("$CreatedAtUtc", Format(notification.CreatedAtUtc)));
		}
		return notificationId;
	}

	public Task<bool> SetReadStateAsync(long recipientId, long userId, long version, DateTime? readAtUtc, CancellationToken cancellationToken) =>
		UpdateStateAsync(recipientId, userId, version, "ReadAtUtc", readAtUtc, cancellationToken);

	public Task<bool> SetArchivedStateAsync(long recipientId, long userId, long version, DateTime? archivedAtUtc, CancellationToken cancellationToken) =>
		UpdateStateAsync(recipientId, userId, version, "ArchivedAtUtc", archivedAtUtc, cancellationToken);

	public Task<int> MarkVisiblePageReadAsync(long userId, IReadOnlyCollection<long> recipientIds, DateTime readAtUtc, CancellationToken cancellationToken)
	{
		if (recipientIds.Count == 0) return Task.FromResult(0);
		var parameters = new List<DatabaseParameter>
		{
			Parameter("$UserId", userId), Parameter("$ReadAtUtc", Format(readAtUtc))
		};
		var names = recipientIds.Distinct().Select((id, index) =>
		{
			var name = $"$Id{index}";
			parameters.Add(Parameter(name, id));
			return name;
		});
		return Database.ExecuteAsync(
			$"UPDATE NotificationRecipients SET ReadAtUtc = $ReadAtUtc, Version = Version + 1 WHERE UserId = $UserId AND ReadAtUtc IS NULL AND Id IN ({string.Join(", ", names)});",
			cancellationToken,
			parameters.ToArray());
	}

	private Task<long> CountAsync(NotificationFilter? filter, long userId, DateTime nowUtc, CancellationToken cancellationToken)
	{
		var effective = filter ?? new NotificationFilter(null, NotificationInboxFilter.Unread, null, null, null, null);
		var (where, parameters) = BuildFilter(userId, effective, nowUtc);
		return CountCoreAsync(where, parameters, cancellationToken);
	}

	private async Task<long> CountCoreAsync(string where, DatabaseParameter[] parameters, CancellationToken cancellationToken) =>
		Convert.ToInt64(await Database.ExecuteScalarAsync(
			$"SELECT COUNT(*) FROM NotificationRecipients nr INNER JOIN Notifications n ON n.Id = nr.NotificationId {where};",
			cancellationToken, parameters), CultureInfo.InvariantCulture);

	private async Task<bool> UpdateStateAsync(long recipientId, long userId, long version, string column, DateTime? value, CancellationToken cancellationToken) =>
		await Database.ExecuteAsync(
			$"UPDATE NotificationRecipients SET {column} = $Value, Version = Version + 1 WHERE Id = $RecipientId AND UserId = $UserId AND Version = $Version;",
			cancellationToken,
			Parameter("$Value", Format(value)), Parameter("$RecipientId", recipientId),
			Parameter("$UserId", userId), Parameter("$Version", version)) == 1;

	private static (string Where, DatabaseParameter[] Parameters) BuildFilter(long userId, NotificationFilter filter, DateTime nowUtc)
	{
		var predicates = new List<string> { "nr.UserId = $UserId", "(n.ExpiresAtUtc IS NULL OR n.ExpiresAtUtc > $NowUtc)" };
		var parameters = new List<DatabaseParameter> { Parameter("$UserId", userId), Parameter("$NowUtc", Format(nowUtc)) };
		switch (filter.Inbox)
		{
			case NotificationInboxFilter.All: predicates.Add("nr.ArchivedAtUtc IS NULL"); break;
			case NotificationInboxFilter.Unread: predicates.Add("nr.ReadAtUtc IS NULL AND nr.ArchivedAtUtc IS NULL"); break;
			case NotificationInboxFilter.Archived: predicates.Add("nr.ArchivedAtUtc IS NOT NULL"); break;
		}
		if (!string.IsNullOrWhiteSpace(filter.SearchText))
		{
			predicates.Add("(n.Title LIKE $Search OR n.Message LIKE $Search OR n.SourceNumber LIKE $Search)");
			parameters.Add(Parameter("$Search", $"%{filter.SearchText.Trim()}%"));
		}
		if (filter.Type is not null) { predicates.Add("n.Type = $Type"); parameters.Add(Parameter("$Type", (int)filter.Type)); }
		if (filter.Severity is not null) { predicates.Add("n.Severity = $Severity"); parameters.Add(Parameter("$Severity", (int)filter.Severity)); }
		if (filter.FromUtc is not null) { predicates.Add("n.CreatedAtUtc >= $FromUtc"); parameters.Add(Parameter("$FromUtc", Format(filter.FromUtc))); }
		if (filter.ToUtcExclusive is not null) { predicates.Add("n.CreatedAtUtc < $ToUtc"); parameters.Add(Parameter("$ToUtc", Format(filter.ToUtcExclusive))); }
		return ($"WHERE {string.Join(" AND ", predicates)}", parameters.ToArray());
	}

	private static NotificationListItem ReadListItem(DbDataReader reader) => new()
	{
		RecipientId = reader.GetInt64(0), NotificationId = reader.GetInt64(1),
		Type = (NotificationType)reader.GetInt32(2), Severity = (NotificationSeverity)reader.GetInt32(3),
		Title = reader.GetString(4), MessagePreview = Preview(reader.GetString(5)),
		SourceType = NullableString(reader, 6), SourceId = NullableInt64(reader, 7), SourceNumber = NullableString(reader, 8),
		CreatedAtUtc = ReadDateTime(reader, 9), ReadAtUtc = ReadNullableDateTime(reader, 11),
		ArchivedAtUtc = ReadNullableDateTime(reader, 12), RecipientVersion = reader.GetInt64(13)
	};

	private static NotificationDetails ReadDetails(DbDataReader reader) => new(
		reader.GetInt64(0), reader.GetInt64(1), (NotificationType)reader.GetInt32(2),
		(NotificationSeverity)reader.GetInt32(3), reader.GetString(4), reader.GetString(5),
		NullableString(reader, 6), NullableInt64(reader, 7), NullableString(reader, 8),
		ReadDateTime(reader, 9), ReadNullableDateTime(reader, 10), ReadNullableDateTime(reader, 11),
		ReadNullableDateTime(reader, 12), reader.GetInt64(13));

	private static string Preview(string value) => value.Length <= 180 ? value : value[..177] + "...";
	private static string? NullableString(DbDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
	private static long? NullableInt64(DbDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);
	private static string? Format(DateTime? value) => value?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
	private static DateTime ReadDateTime(DbDataReader reader, int ordinal) => DateTime.Parse(Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture) ?? string.Empty, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
	private static DateTime? ReadNullableDateTime(DbDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : ReadDateTime(reader, ordinal);
}
