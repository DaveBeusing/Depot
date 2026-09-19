// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Reflection;

using Depot.Models;
using Depot.Repositories;
using Depot.Services;
using Depot.ViewModels;

using Xunit;

namespace Depot.Tests;

public sealed class MyWorkServiceTests
{
	[Fact]
	public async Task PermissionLossRemovesProviderItems()
	{
		var authorization = SignedIn(ApplicationPermission.DashboardView, ApplicationPermission.PurchaseOrdersView);
		var provider = new FakeProvider(
			"Purchasing",
			ApplicationPermission.PurchaseOrdersView,
			(_, _) => Task.FromResult<IReadOnlyList<MyWorkItem>>([Item(MyWorkSectionKind.MyDrafts, 1)]));

		var service = new MyWorkService(authorization, [provider]);
		Assert.Single((await service.GetAsync()).Sections.SelectMany(section => section.Items));

		authorization.SignIn(authorization.CurrentUser!, [ApplicationPermission.DashboardView]);
		Assert.Empty((await service.GetAsync()).Sections.SelectMany(section => section.Items));
	}

	[Fact]
	public async Task SectionsAreDeterministicAndBounded()
	{
		var authorization = SignedIn(ApplicationPermission.DashboardView);
		var values = Enumerable.Range(1, MyWorkService.MaximumItemsPerSection + 5)
			.Select(index => Item(MyWorkSectionKind.NeedsMyAction, index, index % 2 == 0 ? MyWorkPriority.High : MyWorkPriority.Normal))
			.Concat([Item(MyWorkSectionKind.Exceptions, 1000, MyWorkPriority.Critical)])
			.ToArray();
		var service = new MyWorkService(authorization, [new FakeProvider("Test", null, (_, _) => Task.FromResult<IReadOnlyList<MyWorkItem>>(values))]);

		var snapshot = await service.GetAsync();

		Assert.Equal(
			[MyWorkSectionKind.NeedsMyAction, MyWorkSectionKind.MyDrafts, MyWorkSectionKind.Waiting, MyWorkSectionKind.Exceptions, MyWorkSectionKind.RecentlyCompleted],
			snapshot.Sections.Select(section => section.Kind).ToArray());
		Assert.Equal(MyWorkService.MaximumItemsPerSection, snapshot.Sections[0].Items.Count);
		Assert.Equal(MyWorkPriority.High, snapshot.Sections[0].Items[0].Priority);
		Assert.Single(snapshot.Sections.Single(section => section.Kind == MyWorkSectionKind.Exceptions).Items);
	}

	[Fact]
	public async Task ProviderFailureDoesNotSuppressHealthyProviders()
	{
		var authorization = SignedIn(ApplicationPermission.DashboardView);
		var service = new MyWorkService(authorization,
		[
			new FakeProvider("Healthy", null, (_, _) => Task.FromResult<IReadOnlyList<MyWorkItem>>([Item(MyWorkSectionKind.Waiting, 7)])),
			new FakeProvider("Broken", null, (_, _) => throw new InvalidOperationException("Provider failed"))
		]);

		var snapshot = await service.GetAsync();

		Assert.Contains(snapshot.Sections.SelectMany(section => section.Items), item => item.EntityId == 7);
		var failure = Assert.Single(snapshot.Failures);
		Assert.Equal("Broken", failure.Provider);
	}

	[Fact]
	public async Task CancellationPropagatesAcrossProviders()
	{
		var authorization = SignedIn(ApplicationPermission.DashboardView);
		var provider = new FakeProvider("Slow", null, async (_, token) =>
		{
			await Task.Delay(Timeout.InfiniteTimeSpan, token);
			return [];
		});
		var service = new MyWorkService(authorization, [provider]);
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetAsync(cancellation.Token));
	}

	[Fact]
	public void MyWorkServiceHasNoBusinessMutationSurface()
	{
		var forbiddenPrefixes = new[] { "Create", "Save", "Update", "Delete", "Post", "Approve", "Reject", "Reverse", "Execute", "Submit", "Release" };
		var publicMethods = typeof(MyWorkService).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);

		Assert.DoesNotContain(publicMethods, method => forbiddenPrefixes.Any(prefix => method.Name.StartsWith(prefix, StringComparison.Ordinal)));
		Assert.Contains(publicMethods, method => method.Name == nameof(MyWorkService.GetAsync));
	}

	[Fact]
	public void DashboardViewModelDoesNotDependOnRepositories()
	{
		var repositoryNamespace = typeof(DatabaseRepository).Namespace;
		var fields = typeof(DashboardViewModel).GetFields(BindingFlags.Instance | BindingFlags.NonPublic);

		Assert.DoesNotContain(fields, field => string.Equals(field.FieldType.Namespace, repositoryNamespace, StringComparison.Ordinal));
		Assert.Contains(fields, field => field.FieldType == typeof(MyWorkService));
	}

	private static AuthorizationService SignedIn(params ApplicationPermission[] permissions)
	{
		var authorization = new AuthorizationService();
		authorization.SignIn(new User
		{
			Id = 42,
			Email = "my-work@example.test",
			DisplayName = "My Work User",
			Role = UserRole.User,
			IsActive = true,
			CreatedUtc = DateTime.UtcNow
		}, permissions);
		return authorization;
	}

	private static MyWorkItem Item(MyWorkSectionKind section, long id, MyWorkPriority priority = MyWorkPriority.Normal) =>
		new(section, MyWorkItemKind.PurchaseOrder, id, $"REF-{id:N0}", "Work item", "Context", "Open", null, null, null, priority, "dashboard", "Open", 42);

	private sealed class FakeProvider : IMyWorkProvider
	{
		private readonly ApplicationPermission? _permission;
		private readonly Func<MyWorkQuery, CancellationToken, Task<IReadOnlyList<MyWorkItem>>> _load;

		public FakeProvider(string name, ApplicationPermission? permission, Func<MyWorkQuery, CancellationToken, Task<IReadOnlyList<MyWorkItem>>> load)
		{
			Name = name;
			_permission = permission;
			_load = load;
		}

		public string Name { get; }
		public bool CanQuery(IAuthorizationService authorization) => _permission is null || authorization.HasPermission(_permission.Value);
		public Task<IReadOnlyList<MyWorkItem>> GetAsync(MyWorkQuery query, CancellationToken cancellationToken) => _load(query, cancellationToken);
	}
}
