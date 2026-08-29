// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class NotificationCenterTests : IDisposable
{
	private readonly string _path = Path.Combine(Path.GetTempPath(), $"depot-notifications-{Guid.NewGuid():N}.db");
	private readonly DatabaseAccess _data;
	private readonly AuthorizationService _authorization = new();
	private readonly NotificationService _service;
	private readonly User _administrator;

	public NotificationCenterTests()
	{
		var factory = new SqliteConnectionFactory(_path);
		new DepotDatabase(factory).Initialize();
		_data = new DatabaseAccess(factory);
		_administrator = new UserRepository(_data).GetByEmailAsync("admin@depot.local", CancellationToken.None).GetAwaiter().GetResult()
			?? throw new InvalidOperationException("Default administrator was not created.");
		var roles = new RoleRepository(_data);
		_administrator.Roles = roles.GetUserRolesAsync(_administrator.Id, CancellationToken.None).GetAwaiter().GetResult();
		_administrator.EffectivePermissions = roles.GetEffectivePermissionsAsync(_administrator.Id, CancellationToken.None).GetAwaiter().GetResult();
		_authorization.SignIn(_administrator, _administrator.EffectivePermissions);
		_service = new NotificationService(new DatabaseTransactionRunner(_data), new NotificationRepository(_data), _authorization);
	}

	[Fact]
	public async Task CurrentSchemaCreatesNotificationTablesAndIndexes()
	{
		Assert.Equal(DatabaseVersion.CurrentVersion, await ScalarAsync("SELECT Version FROM DatabaseInfo;"));
		Assert.Equal(1, await ScalarAsync("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'Notifications';"));
		Assert.Equal(1, await ScalarAsync("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'NotificationRecipients';"));
		Assert.Equal(1, await ScalarAsync("SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'IX_NotificationRecipients_Inbox';"));
	}

	[Fact]
	public async Task VersionTwentyEightMigratesToCurrentSchema()
	{
		await _data.ExecuteAsync("DROP TABLE NotificationRecipients;", CancellationToken.None);
		await _data.ExecuteAsync("DROP TABLE Notifications;", CancellationToken.None);
		await _data.ExecuteAsync("UPDATE DatabaseInfo SET Version = 28;", CancellationToken.None);
		new DepotDatabase(new SqliteConnectionFactory(_path)).Initialize();
		Assert.Equal(DatabaseVersion.CurrentVersion, await ScalarAsync("SELECT Version FROM DatabaseInfo;"));
		Assert.Equal(1, await ScalarAsync("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'NotificationRecipients';"));
	}

	[Fact]
	public async Task DuplicateRecipientsAreMaterializedOnlyOnce()
	{
		await _service.NotifyUsersAsync(Request("One recipient"), [_administrator.Id, _administrator.Id]);
		Assert.Equal(1, await ScalarAsync("SELECT COUNT(*) FROM NotificationRecipients;"));
		Assert.Equal(1, await _service.GetUnreadCountAsync());
	}

	[Fact]
	public async Task InboxSupportsPagingFiltersReadUnreadArchiveAndRestore()
	{
		await _service.NotifyUserAsync(Request("First", NotificationSeverity.Warning), _administrator.Id);
		await _service.NotifyUserAsync(Request("Second", NotificationSeverity.Success), _administrator.Id);
		var page = await _service.GetPageAsync(new NotificationFilter("First", NotificationInboxFilter.All, null, NotificationSeverity.Warning, null, null), 1, 1);
		var item = Assert.Single(page.Items);
		Assert.Equal("First", item.Title);
		var details = await _service.GetDetailsAsync(item.RecipientId) ?? throw new InvalidOperationException();

		await _service.MarkReadAsync(details.RecipientId, details.RecipientVersion);
		Assert.Equal(1, await _service.GetUnreadCountAsync());
		details = await _service.GetDetailsAsync(item.RecipientId) ?? throw new InvalidOperationException();
		await _service.MarkUnreadAsync(details.RecipientId, details.RecipientVersion);
		Assert.Equal(2, await _service.GetUnreadCountAsync());
		details = await _service.GetDetailsAsync(item.RecipientId) ?? throw new InvalidOperationException();
		await _service.ArchiveAsync(details.RecipientId, details.RecipientVersion);
		var archived = await _service.GetPageAsync(new NotificationFilter(null, NotificationInboxFilter.Archived, null, null, null, null), 1, 50);
		Assert.Contains(archived.Items, value => value.RecipientId == item.RecipientId);
		details = await _service.GetDetailsAsync(item.RecipientId) ?? throw new InvalidOperationException();
		await _service.RestoreAsync(details.RecipientId, details.RecipientVersion);
		Assert.DoesNotContain((await _service.GetPageAsync(new NotificationFilter(null, NotificationInboxFilter.Archived, null, null, null, null), 1, 50)).Items, value => value.RecipientId == item.RecipientId);
	}

	[Fact]
	public async Task UsersCannotReadOrChangeAnotherUsersRecipient()
	{
		var otherUserId = await CreateUserAsync("other@depot.test");
		await _service.NotifyUserAsync(Request("Private"), _administrator.Id);
		var recipientId = await ScalarAsync("SELECT Id FROM NotificationRecipients;");
		_authorization.SignIn(new User { Id = otherUserId, Email = "other@depot.test", IsActive = true }, []);
		Assert.Null(await _service.GetDetailsAsync(recipientId));
		await Assert.ThrowsAsync<ConcurrencyConflictException>(() => _service.MarkReadAsync(recipientId, 1));
		Assert.Equal(0, await _service.GetUnreadCountAsync());
	}

	[Fact]
	public async Task ExpiredNotificationsAreExcludedFromInboxAndUnreadCount()
	{
		await _service.NotifyUserAsync(Request("Expired") with { ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1) }, _administrator.Id);
		Assert.Equal(0, await _service.GetUnreadCountAsync());
		Assert.Empty((await _service.GetPageAsync(new NotificationFilter(null, NotificationInboxFilter.All, null, null, null, null), 1, 50)).Items);
	}

	[Fact]
	public async Task PermissionResolutionUsesActiveRbacAssignmentsAndSupportsExclusions()
	{
		var approverId = await CreateUserAsync("approver@depot.test");
		await _data.ExecuteAsync(
			"INSERT INTO UserRoles (UserId, RoleId) SELECT $UserId, Id FROM Roles WHERE Code = $Code;",
			CancellationToken.None,
			new DatabaseParameter("$UserId", approverId),
			new DatabaseParameter("$Code", SystemRoleCatalog.ApproverCode));
		await _service.NotifyPermissionHoldersAsync(Request("Approval"), ApplicationPermission.PurchaseOrdersApprove, [_administrator.Id]);
		Assert.Equal(1, await ScalarAsync("SELECT COUNT(*) FROM NotificationRecipients WHERE UserId = $UserId;", new DatabaseParameter("$UserId", approverId)));
		Assert.Equal(0, await ScalarAsync("SELECT COUNT(*) FROM NotificationRecipients WHERE UserId = $UserId;", new DatabaseParameter("$UserId", _administrator.Id)));
	}

	[Fact]
	public async Task ControlledNavigationChecksPermissionsAndRejectsUnknownTargets()
	{
		var navigation = new NotificationNavigationService(_authorization);
		NotificationNavigationTarget? target = null;
		navigation.SetNavigationHandler((value, _) => { target = value; return Task.CompletedTask; });
		await navigation.NavigateAsync(Details(NotificationSourceTypes.DatabaseAdministration));
		Assert.Equal(NotificationSourceTypes.DatabaseAdministration, target?.SourceType);

		_authorization.SignIn(new User { Id = 77, IsActive = true }, []);
		await Assert.ThrowsAsync<UnauthorizedAccessException>(() => navigation.NavigateAsync(Details(NotificationSourceTypes.DatabaseAdministration)));
		await Assert.ThrowsAsync<InvalidOperationException>(() => navigation.NavigateAsync(Details("UnsafeUrl")));
	}

	[Fact]
	public async Task PurchaseOrderWorkflowCreatesIdempotentNotificationsForMaterializedRecipients()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var draft = await context.Orders.SaveDraftAsync(context.NewOrder());
		var submitted = await context.Orders.SubmitForApprovalAsync(draft.Id, draft.Version);
		Assert.True(await context.ScalarAsync("SELECT COUNT(*) FROM NotificationRecipients WHERE UserId = $UserId;", new DatabaseParameter("$UserId", context.ApproverUserId)) >= 1);
		Assert.True(await context.ScalarAsync("SELECT COUNT(*) FROM NotificationRecipients WHERE UserId = $UserId;", new DatabaseParameter("$UserId", submitted.CreatedByUserId)) >= 1);

		context.SignInApprover();
		var operationId = Guid.NewGuid();
		var approved = await context.Orders.ApproveAsync(submitted.Id, submitted.Version, "Approved", operationId);
		await context.Orders.ApproveAsync(submitted.Id, submitted.Version, "Approved", operationId);
		Assert.Equal(PurchaseOrderStatus.Approved, approved.Status);
		Assert.Equal(1, await context.ScalarAsync("SELECT COUNT(*) FROM Notifications WHERE SourceId = $Id AND Title LIKE '%approved%';", new DatabaseParameter("$Id", submitted.Id)));
	}

	[Fact]
	public async Task NotificationFailureRollsBackPurchaseOrderSubmissionAndAudit()
	{
		await using var context = await ProcurementTestContext.CreateSqliteAsync();
		var draft = await context.Orders.SaveDraftAsync(context.NewOrder());
		var auditBefore = await context.ScalarAsync("SELECT COUNT(*) FROM AuditEntries WHERE EntityType = 'PurchaseOrder' AND EntityId = $Id;", new DatabaseParameter("$Id", draft.Id));
		await context.Data.ExecuteAsync(
			"CREATE TRIGGER FailNotification BEFORE INSERT ON Notifications BEGIN SELECT RAISE(ABORT, 'notification failed'); END;",
			CancellationToken.None);
		await Assert.ThrowsAnyAsync<Exception>(() => context.Orders.SubmitForApprovalAsync(draft.Id, draft.Version));
		var stored = await context.Orders.GetByIdAsync(draft.Id) ?? throw new InvalidOperationException();
		Assert.Equal(PurchaseOrderStatus.Draft, stored.Status);
		Assert.Equal(auditBefore, await context.ScalarAsync("SELECT COUNT(*) FROM AuditEntries WHERE EntityType = 'PurchaseOrder' AND EntityId = $Id;", new DatabaseParameter("$Id", draft.Id)));
	}

	[Fact]
	public void PollingLifecycleCanBeDisposedWithoutLeavingActiveOperations()
	{
		var viewModel = new Depot.ViewModels.NotificationCenterViewModel(
			_service,
			new NotificationNavigationService(_authorization));
		viewModel.SetApplicationActive(false);
		viewModel.Dispose();
	}

	private static NotificationRequest Request(string title, NotificationSeverity severity = NotificationSeverity.Information) =>
		new(NotificationType.System, severity, title, $"{title} message");

	private static NotificationDetails Details(string sourceType) =>
		new(1, 1, NotificationType.Workflow, NotificationSeverity.Information, "Title", "Message", sourceType, 42, "REF-42", DateTime.UtcNow, null, null, null, 1);

	private async Task<long> CreateUserAsync(string email) => await _data.InsertAsync(
		"INSERT INTO Users (Email, DisplayName, PasswordHash, IsAdministrator, CanApprovePurchaseOrders, Role, IsActive, CreatedUtc, Version) VALUES ($Email, 'Notification User', 'unused', 0, 0, 0, 1, $CreatedUtc, 1);",
		CancellationToken.None,
		new DatabaseParameter("$Email", email),
		new DatabaseParameter("$CreatedUtc", DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture)));

	private async Task<long> ScalarAsync(string sql, params DatabaseParameter[] parameters) =>
		Convert.ToInt64(await _data.ExecuteScalarAsync(sql, CancellationToken.None, parameters), System.Globalization.CultureInfo.InvariantCulture);

	public void Dispose()
	{
		SqliteConnection.ClearAllPools();
		if (File.Exists(_path)) File.Delete(_path);
	}
}
