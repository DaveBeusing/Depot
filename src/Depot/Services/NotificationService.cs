// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public interface INotificationService
{
	event EventHandler? NotificationsChanged;
	Task<long> NotifyUserAsync(NotificationRequest request, long userId, CancellationToken cancellationToken = default);
	Task<long> NotifyUsersAsync(NotificationRequest request, IEnumerable<long> userIds, CancellationToken cancellationToken = default);
	Task<long> NotifyPermissionHoldersAsync(NotificationRequest request, ApplicationPermission permission, IEnumerable<long>? excludedUserIds = null, CancellationToken cancellationToken = default);
	Task<long> GetUnreadCountAsync(CancellationToken cancellationToken = default);
	Task<PageResult<NotificationListItem>> GetPageAsync(NotificationFilter filter, int pageNumber, int pageSize, CancellationToken cancellationToken = default);
	Task<NotificationDetails?> GetDetailsAsync(long recipientId, CancellationToken cancellationToken = default);
	Task MarkReadAsync(long recipientId, long version, CancellationToken cancellationToken = default);
	Task MarkUnreadAsync(long recipientId, long version, CancellationToken cancellationToken = default);
	Task ArchiveAsync(long recipientId, long version, CancellationToken cancellationToken = default);
	Task RestoreAsync(long recipientId, long version, CancellationToken cancellationToken = default);
	Task MarkVisiblePageReadAsync(IEnumerable<long> recipientIds, CancellationToken cancellationToken = default);
}

public sealed class NotificationService : INotificationService
{
	public const int MaximumTitleLength = 200;
	public const int MaximumMessageLength = 4000;
	private readonly IDatabaseTransactionRunner _transactions;
	private readonly NotificationRepository _notifications;
	private readonly IAuthorizationService _authorization;
	public event EventHandler? NotificationsChanged;

	public NotificationService(IDatabaseTransactionRunner transactions, NotificationRepository notifications, IAuthorizationService authorization)
	{
		_transactions = transactions;
		_notifications = notifications;
		_authorization = authorization;
	}

	public Task<long> NotifyUserAsync(NotificationRequest request, long userId, CancellationToken cancellationToken = default) => NotifyUsersAsync(request, [userId], cancellationToken);

	public async Task<long> NotifyUsersAsync(NotificationRequest request, IEnumerable<long> userIds, CancellationToken cancellationToken = default)
	{
		var recipients = userIds.Where(id => id > 0).Distinct().ToArray();
		ValidateRequest(request);
		if (recipients.Length == 0) return 0L;
		var id = await _transactions.ExecuteAsync((transaction, token) => CreateAsync(transaction, request, recipients, token), cancellationToken);
		RaiseChanged();
		return id;
	}

	public async Task<long> NotifyPermissionHoldersAsync(NotificationRequest request, ApplicationPermission permission, IEnumerable<long>? excludedUserIds = null, CancellationToken cancellationToken = default)
	{
		ValidateRequest(request);
		var excluded = excludedUserIds?.ToHashSet() ?? [];
		var id = await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var users = await _notifications.GetActiveUserIdsWithPermissionAsync(transaction, permission, token);
			return await CreateAsync(transaction, request, users.Where(id => !excluded.Contains(id)).ToArray(), token);
		}, cancellationToken);
		RaiseChanged();
		return id;
	}

	public async Task<long> NotifyAdministratorsAsync(NotificationRequest request, CancellationToken cancellationToken = default)
	{
		ValidateRequest(request);
		var id = await _transactions.ExecuteAsync(async (transaction, token) => await CreateAsync(transaction, request, await ResolveAdministratorsAsync(transaction, token), token), cancellationToken);
		RaiseChanged();
		return id;
	}

	public void RaiseChanged() => NotificationsChanged?.Invoke(this, EventArgs.Empty);
	public Task<long> GetUnreadCountAsync(CancellationToken cancellationToken = default) => _notifications.GetUnreadCountAsync(CurrentUserId(), DateTime.UtcNow, cancellationToken);
	public Task<PageResult<NotificationListItem>> GetPageAsync(NotificationFilter filter, int pageNumber, int pageSize, CancellationToken cancellationToken = default) => _notifications.GetPageAsync(CurrentUserId(), filter, pageNumber, pageSize, DateTime.UtcNow, cancellationToken);
	public Task<NotificationDetails?> GetDetailsAsync(long recipientId, CancellationToken cancellationToken = default) => _notifications.GetDetailsAsync(recipientId, CurrentUserId(), DateTime.UtcNow, cancellationToken);

	public async Task MarkReadAsync(long recipientId, long version, CancellationToken cancellationToken = default) { await SetReadStateAsync(recipientId, version, DateTime.UtcNow, cancellationToken); RaiseChanged(); }
	public async Task MarkUnreadAsync(long recipientId, long version, CancellationToken cancellationToken = default) { await SetReadStateAsync(recipientId, version, null, cancellationToken); RaiseChanged(); }
	public async Task ArchiveAsync(long recipientId, long version, CancellationToken cancellationToken = default) { await SetArchivedStateAsync(recipientId, version, DateTime.UtcNow, cancellationToken); RaiseChanged(); }
	public async Task RestoreAsync(long recipientId, long version, CancellationToken cancellationToken = default) { await SetArchivedStateAsync(recipientId, version, null, cancellationToken); RaiseChanged(); }
	public async Task MarkVisiblePageReadAsync(IEnumerable<long> recipientIds, CancellationToken cancellationToken = default) { await _notifications.MarkVisiblePageReadAsync(CurrentUserId(), recipientIds.Distinct().ToArray(), DateTime.UtcNow, cancellationToken); RaiseChanged(); }

	public async Task<long> CreateAsync(DatabaseTransactionContext transaction, NotificationRequest request, IReadOnlyCollection<long> recipientUserIds, CancellationToken cancellationToken)
	{
		ValidateRequest(request);
		if (recipientUserIds.Count == 0) return 0;
		var notification = new Notification
		{
			Type = request.Type,
			Severity = request.Severity,
			Title = request.Title.Trim(),
			Message = request.Message.Trim(),
			SourceType = Normalize(request.SourceType),
			SourceId = request.SourceId,
			SourceNumber = Normalize(request.SourceNumber),
			CreatedAtUtc = DateTime.UtcNow,
			CreatedByUserId = request.CreatedByUserId,
			ExpiresAtUtc = request.ExpiresAtUtc?.ToUniversalTime()
		};
		return await _notifications.CreateAsync(transaction, notification, recipientUserIds, cancellationToken);
	}

	public Task<IReadOnlyList<long>> ResolvePermissionHoldersAsync(DatabaseTransactionContext transaction, ApplicationPermission permission, CancellationToken cancellationToken) => _notifications.GetActiveUserIdsWithPermissionAsync(transaction, permission, cancellationToken);
	public Task<IReadOnlyList<long>> ResolveAdministratorsAsync(DatabaseTransactionContext transaction, CancellationToken cancellationToken) => _notifications.GetActiveAdministratorIdsAsync(transaction, cancellationToken);

	private async Task SetReadStateAsync(long recipientId, long version, DateTime? value, CancellationToken cancellationToken)
	{
		if (!await _notifications.SetReadStateAsync(recipientId, CurrentUserId(), version, value, cancellationToken)) throw new ConcurrencyConflictException("notification");
	}

	private async Task SetArchivedStateAsync(long recipientId, long version, DateTime? value, CancellationToken cancellationToken)
	{
		if (!await _notifications.SetArchivedStateAsync(recipientId, CurrentUserId(), version, value, cancellationToken)) throw new ConcurrencyConflictException("notification");
	}

	private long CurrentUserId() => _authorization.CurrentUser is { IsActive: true } user ? user.Id : throw new UnauthorizedAccessException("An active signed-in user is required to access notifications.");

	private static void ValidateRequest(NotificationRequest request)
	{
		if (string.IsNullOrWhiteSpace(request.Title)) throw new ArgumentException("A notification title is required.");
		if (request.Title.Trim().Length > MaximumTitleLength) throw new ArgumentException($"Notification titles must not exceed {MaximumTitleLength} characters.");
		if (string.IsNullOrWhiteSpace(request.Message)) throw new ArgumentException("A notification message is required.");
		if (request.Message.Trim().Length > MaximumMessageLength) throw new ArgumentException($"Notification messages must not exceed {MaximumMessageLength} characters.");
		if (request.SourceType is not null && request.SourceType.Trim().Length > 100) throw new ArgumentException("Notification source types must not exceed 100 characters.");
		if (request.SourceNumber is not null && request.SourceNumber.Trim().Length > 100) throw new ArgumentException("Notification source numbers must not exceed 100 characters.");
	}

	private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record NotificationNavigationTarget(string SourceType, long? SourceId, string? SourceNumber);

public interface INotificationNavigationService
{
	void SetNavigationHandler(Func<NotificationNavigationTarget, CancellationToken, Task>? handler);
	Task NavigateAsync(NotificationDetails notification, CancellationToken cancellationToken = default);
}

public sealed class NotificationNavigationService : INotificationNavigationService
{
	private readonly IAuthorizationService _authorization;
	private Func<NotificationNavigationTarget, CancellationToken, Task>? _handler;

	public NotificationNavigationService(IAuthorizationService authorization) => _authorization = authorization;
	public void SetNavigationHandler(Func<NotificationNavigationTarget, CancellationToken, Task>? handler) => _handler = handler;

	public Task NavigateAsync(NotificationDetails notification, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(notification.SourceType)) throw new InvalidOperationException("This notification has no related Depot record.");
		var requiredPermission = notification.SourceType switch
		{
			NotificationSourceTypes.PurchaseOrder => ApplicationPermission.PurchaseOrdersView,
			NotificationSourceTypes.PurchaseOrderApproval => ApplicationPermission.PurchaseOrdersApprove,
			NotificationSourceTypes.InventoryCount => ApplicationPermission.InventoryCountsView,
			NotificationSourceTypes.DatabaseAdministration => ApplicationPermission.DatabaseView,
			NotificationSourceTypes.SalesOrder => ApplicationPermission.SalesOrdersView,
			NotificationSourceTypes.SalesOrderApproval => ApplicationPermission.SalesOrdersApprove,
			NotificationSourceTypes.Shipment => ApplicationPermission.ShipmentsView,
			NotificationSourceTypes.CustomerReturn => ApplicationPermission.CustomerReturnsView,
			NotificationSourceTypes.SalesInvoice => ApplicationPermission.SalesInvoicesView,
			NotificationSourceTypes.SalesCreditNote => ApplicationPermission.CreditNotesView,
			_ => throw new InvalidOperationException("The notification target is not supported by this version of Depot.")
		};
		_authorization.RequirePermission(requiredPermission);
		var handler = _handler ?? throw new InvalidOperationException("Notification navigation is not available.");
		return handler(new NotificationNavigationTarget(notification.SourceType, notification.SourceId, notification.SourceNumber), cancellationToken);
	}
}
