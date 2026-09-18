// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

namespace Depot.Services;

public interface IMyWorkProvider
{
	string Name { get; }
	bool CanQuery(IAuthorizationService authorization);
	Task<IReadOnlyList<MyWorkItem>> GetAsync(MyWorkQuery query, CancellationToken cancellationToken);
}

public sealed class MyWorkService
{
	public const int ProviderItemLimit = 12;
	public const int MaximumItemsPerSection = 50;

	private static readonly MyWorkSectionKind[] SectionOrder =
	[
		MyWorkSectionKind.NeedsMyAction,
		MyWorkSectionKind.MyDrafts,
		MyWorkSectionKind.Waiting,
		MyWorkSectionKind.Exceptions,
		MyWorkSectionKind.RecentlyCompleted
	];

	private readonly IAuthorizationService _authorization;
	private readonly IReadOnlyList<IMyWorkProvider> _providers;

	public MyWorkService(IAuthorizationService authorization, IEnumerable<IMyWorkProvider> providers)
	{
		_authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
		_providers = providers?.ToArray() ?? throw new ArgumentNullException(nameof(providers));
	}

	public async Task<MyWorkSnapshot> GetAsync(CancellationToken cancellationToken = default)
	{
		var user = _authorization.CurrentUser is { IsActive: true } active
			? active
			: throw new UnauthorizedAccessException("An active signed-in user is required for My Work.");

		cancellationToken.ThrowIfCancellationRequested();
		var query = new MyWorkQuery(user.Id, DateTime.UtcNow, ProviderItemLimit);
		var eligible = _providers.Where(provider => provider.CanQuery(_authorization)).ToArray();
		var results = await Task.WhenAll(eligible.Select(provider => LoadProviderAsync(provider, query, cancellationToken)));

		var items = results
			.SelectMany(result => result.Items)
			.GroupBy(item => (item.Section, item.Kind, item.EntityId, item.RouteId))
			.Select(group => group.First())
			.ToArray();

		var sections = SectionOrder
			.Select(kind => new MyWorkSection(kind, Title(kind), Order(items.Where(item => item.Section == kind), kind)
				.Take(MaximumItemsPerSection)
				.ToArray()))
			.ToArray();
		var failures = results.Where(result => result.Failure is not null).Select(result => result.Failure!).ToArray();
		return new MyWorkSnapshot(sections, failures);
	}

	private static async Task<ProviderResult> LoadProviderAsync(IMyWorkProvider provider, MyWorkQuery query, CancellationToken cancellationToken)
	{
		try
		{
			var items = await provider.GetAsync(query, cancellationToken);
			return new ProviderResult(items.Take(query.ProviderLimit * 5).ToArray(), null);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception exception)
		{
			return new ProviderResult([], new MyWorkProviderFailure(provider.Name, exception.Message));
		}
	}

	private static IOrderedEnumerable<MyWorkItem> Order(IEnumerable<MyWorkItem> items, MyWorkSectionKind section) =>
		section == MyWorkSectionKind.RecentlyCompleted
			? items.OrderByDescending(item => item.CompletedAtUtc ?? DateTime.MinValue)
				.ThenByDescending(item => item.Priority)
				.ThenBy(item => item.DisplayNumber, StringComparer.CurrentCultureIgnoreCase)
			: items.OrderByDescending(item => item.Priority)
				.ThenBy(item => item.DueAt ?? DateTime.MaxValue)
				.ThenByDescending(item => item.AgeDays ?? -1)
				.ThenBy(item => item.DisplayNumber, StringComparer.CurrentCultureIgnoreCase);

	private static string Title(MyWorkSectionKind kind) => kind switch
	{
		MyWorkSectionKind.NeedsMyAction => "Needs my action",
		MyWorkSectionKind.MyDrafts => "My drafts",
		MyWorkSectionKind.Waiting => "Waiting",
		MyWorkSectionKind.Exceptions => "Exceptions",
		MyWorkSectionKind.RecentlyCompleted => "Recently completed",
		_ => kind.ToString()
	};

	private sealed record ProviderResult(IReadOnlyList<MyWorkItem> Items, MyWorkProviderFailure? Failure);
}
