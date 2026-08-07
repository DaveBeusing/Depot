// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;
using Depot.Services;
using Depot.ViewModels;

using Xunit;

namespace Depot.Tests;

public sealed class RequestAndNotificationEfficiencyTests
{
	[Fact]
	public void NewRequestCancelsAndInvalidatesThePreviousGeneration()
	{
		using var requests = new LatestRequest();
		var first = requests.Begin();
		var second = requests.Begin();

		Assert.True(first.Token.IsCancellationRequested);
		Assert.False(first.IsCurrent);
		Assert.True(second.IsCurrent);
	}

	[Fact]
	public async Task SlowOlderResponseCannotOverwriteTheNewerResult()
	{
		using var requests = new LatestRequest();
		var oldResponse = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
		var newResponse = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
		var displayed = string.Empty;

		async Task ApplyAsync(Task<string> response)
		{
			var request = requests.Begin();
			var value = await response;
			if (request.IsCurrent) displayed = value;
		}

		var oldLoad = ApplyAsync(oldResponse.Task);
		var newLoad = ApplyAsync(newResponse.Task);
		newResponse.SetResult("new");
		await newLoad;
		oldResponse.SetResult("old");
		await oldLoad;

		Assert.Equal("new", displayed);
	}

	[Fact]
	public async Task RepeatedNotificationRefreshSignalsNeverRunDuplicateCountQueries()
	{
		var service = new ControlledNotificationService { BlockUnreadCount = true };
		using var summary = new NotificationSummaryViewModel(service);
		await service.FirstUnreadRequest.Task.WaitAsync(TimeSpan.FromSeconds(5));

		service.RaiseChanged();
		service.RaiseChanged();
		summary.SetApplicationActive(true);
		service.ReleaseUnreadCount.TrySetResult();
		await WaitUntilAsync(() => service.UnreadCalls >= 2);

		Assert.Equal(1, service.MaximumConcurrentUnreadCalls);
		Assert.Equal(2, service.UnreadCalls);
	}

	[Fact]
	public async Task InactiveApplicationDoesNotQueryUnreadCountForLocalEvents()
	{
		var service = new ControlledNotificationService();
		using var summary = new NotificationSummaryViewModel(service);
		await WaitUntilAsync(() => service.UnreadCalls == 1);
		summary.SetApplicationActive(false);

		service.RaiseChanged();
		await Task.Delay(100);

		Assert.Equal(1, service.UnreadCalls);
	}

	[Fact]
	public async Task ConnectivityFailureBackoffSuppressesImmediateRepeatedCountQueries()
	{
		var service = new ControlledNotificationService { FailUnreadCount = true };
		using var summary = new NotificationSummaryViewModel(service);
		await WaitUntilAsync(() => service.UnreadCalls == 1);

		service.RaiseChanged();
		service.RaiseChanged();
		await Task.Delay(100);

		Assert.Equal(1, service.UnreadCalls);
	}

	[Fact]
	public async Task NotificationStateChangesUpdateListAndDetailsWithoutReloadingPageOrDetails()
	{
		var service = new ControlledNotificationService();
		using var viewModel = new NotificationCenterViewModel(service, new NoOpNotificationNavigationService());
		await viewModel.LoadAsync();
		viewModel.SelectedItem = viewModel.Items.Single();
		await WaitUntilAsync(() => viewModel.Details is not null);

		await viewModel.MarkReadCommand.ExecuteAsync();

		Assert.False(viewModel.Details?.IsUnread);
		Assert.Equal(1, service.PageCalls);
		Assert.Equal(1, service.DetailCalls);

		await viewModel.ArchiveCommand.ExecuteAsync();

		Assert.Empty(viewModel.Items);
		Assert.Equal(1, service.PageCalls);
		Assert.Equal(1, service.DetailCalls);
	}

	private static async Task WaitUntilAsync(Func<bool> condition)
	{
		var timeout = DateTime.UtcNow.AddSeconds(5);
		while (!condition())
		{
			if (DateTime.UtcNow >= timeout) throw new TimeoutException("The expected asynchronous state was not reached.");
			await Task.Delay(10);
		}
	}

	private sealed class ControlledNotificationService : INotificationService
	{
		private int _concurrentUnreadCalls;
		public event EventHandler? NotificationsChanged;
		public TaskCompletionSource FirstUnreadRequest { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public TaskCompletionSource ReleaseUnreadCount { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public bool BlockUnreadCount { get; init; }
		public bool FailUnreadCount { get; init; }
		public int UnreadCalls { get; private set; }
		public int MaximumConcurrentUnreadCalls { get; private set; }
		public int PageCalls { get; private set; }
		public int DetailCalls { get; private set; }

		public void RaiseChanged() => NotificationsChanged?.Invoke(this, EventArgs.Empty);

		public async Task<long> GetUnreadCountAsync(CancellationToken cancellationToken = default)
		{
			UnreadCalls++;
			var concurrent = Interlocked.Increment(ref _concurrentUnreadCalls);
			MaximumConcurrentUnreadCalls = Math.Max(MaximumConcurrentUnreadCalls, concurrent);
			FirstUnreadRequest.TrySetResult();
			try { if (BlockUnreadCount) await ReleaseUnreadCount.Task.WaitAsync(cancellationToken); if (FailUnreadCount) throw new InvalidOperationException("offline"); return 1; }
			finally { Interlocked.Decrement(ref _concurrentUnreadCalls); }
		}

		public Task<PageResult<NotificationListItem>> GetPageAsync(NotificationFilter filter, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
		{
			PageCalls++;
			return Task.FromResult(new PageResult<NotificationListItem>([Item()], pageNumber, pageSize, 1));
		}

		public Task<NotificationDetails?> GetDetailsAsync(long recipientId, CancellationToken cancellationToken = default)
		{
			DetailCalls++;
			return Task.FromResult<NotificationDetails?>(new NotificationDetails(recipientId, 10, NotificationType.System, NotificationSeverity.Information, "Title", "Message", null, null, null, DateTime.UtcNow, null, null, null, 1));
		}

		public Task MarkReadAsync(long recipientId, long version, CancellationToken cancellationToken = default) { RaiseChanged(); return Task.CompletedTask; }
		public Task MarkUnreadAsync(long recipientId, long version, CancellationToken cancellationToken = default) { RaiseChanged(); return Task.CompletedTask; }
		public Task ArchiveAsync(long recipientId, long version, CancellationToken cancellationToken = default) { RaiseChanged(); return Task.CompletedTask; }
		public Task RestoreAsync(long recipientId, long version, CancellationToken cancellationToken = default) { RaiseChanged(); return Task.CompletedTask; }
		public Task MarkVisiblePageReadAsync(IEnumerable<long> recipientIds, CancellationToken cancellationToken = default) { RaiseChanged(); return Task.CompletedTask; }
		public Task<long> NotifyUserAsync(NotificationRequest request, long userId, CancellationToken cancellationToken = default) => Task.FromResult(1L);
		public Task<long> NotifyUsersAsync(NotificationRequest request, IEnumerable<long> userIds, CancellationToken cancellationToken = default) => Task.FromResult(1L);
		public Task<long> NotifyPermissionHoldersAsync(NotificationRequest request, ApplicationPermission permission, IEnumerable<long>? excludedUserIds = null, CancellationToken cancellationToken = default) => Task.FromResult(1L);

		private static NotificationListItem Item() => new()
		{
			RecipientId = 1,
			NotificationId = 10,
			Type = NotificationType.System,
			Severity = NotificationSeverity.Information,
			Title = "Title",
			MessagePreview = "Message",
			CreatedAtUtc = DateTime.UtcNow,
			RecipientVersion = 1
		};
	}

	private sealed class NoOpNotificationNavigationService : INotificationNavigationService
	{
		public void SetNavigationHandler(Func<NotificationNavigationTarget, CancellationToken, Task>? handler) { }
		public Task NavigateAsync(NotificationDetails notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
	}
}
