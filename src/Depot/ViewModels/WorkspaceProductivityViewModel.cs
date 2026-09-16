// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels;

public sealed class WorkspaceProductivityViewModel : BaseViewModel
{
	private static readonly string[] PreferredQuickActionRoutes =
	[
		"inventory.items",
		"purchasing.purchase-orders",
		"sales.orders",
		"warehouse.inventory-counts",
		"reports.overview",
		"dashboard"
	];

	private readonly IWorkspaceProductivityService _service;
	private readonly MainViewModel _main;
	private readonly Dictionary<string, WorkspaceRouteDescriptor> _availableRoutes;
	private string? _defaultLandingRoute;
	private string _statusText = string.Empty;

	public WorkspaceProductivityViewModel(IWorkspaceProductivityService service, MainViewModel main)
	{
		_service = service;
		_main = main;
		_availableRoutes = BuildAvailableRoutes(main);
	}

	public ObservableCollection<WorkspaceShortcut> Favorites { get; } = [];
	public ObservableCollection<WorkspaceShortcut> Recents { get; } = [];
	public ObservableCollection<WorkspaceShortcut> QuickActions { get; } = [];
	public bool HasFavorites => Favorites.Count > 0;
	public bool HasRecents => Recents.Count > 0;
	public bool HasQuickActions => QuickActions.Count > 0;
	public new string StatusText { get => _statusText; private set { if (_statusText == value) return; _statusText = value; OnPropertyChanged(); } }
	public string DefaultLandingLabel => ResolveDescriptor(_defaultLandingRoute)?.Title ?? "Dashboard (fallback)";

	public async Task InitializeAsync(CancellationToken cancellationToken = default)
	{
		var snapshot = await _service.GetAsync(cancellationToken);
		Apply(snapshot);
	}

	public async Task NavigateToDefaultOrFallbackAsync(CancellationToken cancellationToken = default)
	{
		var target = ResolveDescriptor(_defaultLandingRoute)
			?? ResolveDescriptor("dashboard")
			?? _availableRoutes.Values.FirstOrDefault();
		if (target is null) return;
		await _main.NavigateToRouteAsync(new ShellRoute(target.RouteId), cancellationToken);
	}

	public async Task OpenAsync(string routeId, CancellationToken cancellationToken = default)
	{
		if (ResolveDescriptor(routeId) is not { } route) return;
		await _main.NavigateToRouteAsync(new ShellRoute(route.RouteId), cancellationToken);
	}

	public async Task ToggleFavoriteAsync(string routeId, CancellationToken cancellationToken = default)
	{
		if (ResolveDescriptor(routeId) is null) return;
		if (Favorites.Any(item => string.Equals(item.RouteId, routeId, StringComparison.OrdinalIgnoreCase)))
			await _service.UnpinAsync(routeId, cancellationToken);
		else
			await _service.PinAsync(routeId, cancellationToken);
		await ReloadAsync(cancellationToken);
	}

	public async Task SetDefaultAsync(string routeId, CancellationToken cancellationToken = default)
	{
		if (ResolveDescriptor(routeId) is null) return;
		await _service.SetDefaultLandingAsync(routeId, cancellationToken);
		await ReloadAsync(cancellationToken);
	}

	public async Task ClearDefaultAsync(CancellationToken cancellationToken = default)
	{
		await _service.SetDefaultLandingAsync(null, cancellationToken);
		await ReloadAsync(cancellationToken);
	}

	public async Task RecordVisitAsync(string routeId, CancellationToken cancellationToken = default)
	{
		if (ResolveDescriptor(routeId) is null) return;
		await _service.RecordVisitAsync(routeId, cancellationToken);
		await ReloadAsync(cancellationToken);
	}

	private async Task ReloadAsync(CancellationToken cancellationToken)
	{
		try
		{
			Apply(await _service.GetAsync(cancellationToken));
			StatusText = string.Empty;
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch
		{
			StatusText = "Workspace preferences are temporarily unavailable.";
		}
	}

	private void Apply(WorkspaceProductivitySnapshot snapshot)
	{
		_defaultLandingRoute = ResolveDescriptor(snapshot.DefaultLandingRoute)?.RouteId;
		var favoriteIds = snapshot.FavoriteRoutes
			.Where(route => ResolveDescriptor(route) is not null)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.Take(WorkspaceProductivityService.MaximumFavorites)
			.ToArray();
		var favoriteSet = favoriteIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

		Replace(Favorites, favoriteIds.Select(route => CreateShortcut(route, favoriteSet, _defaultLandingRoute)));
		Replace(Recents, snapshot.RecentRoutes
			.Where(route => ResolveDescriptor(route) is not null)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.Take(WorkspaceProductivityService.MaximumRecents)
			.Select(route => CreateShortcut(route, favoriteSet, _defaultLandingRoute)));

		var quickIds = new List<string>();
		foreach (var route in favoriteIds.Concat(PreferredQuickActionRoutes).Concat(snapshot.RecentRoutes))
		{
			if (ResolveDescriptor(route) is null || quickIds.Contains(route, StringComparer.OrdinalIgnoreCase)) continue;
			quickIds.Add(route);
			if (quickIds.Count == 6) break;
		}
		Replace(QuickActions, quickIds.Select(route => CreateShortcut(route, favoriteSet, _defaultLandingRoute)));

		OnPropertyChanged(nameof(HasFavorites));
		OnPropertyChanged(nameof(HasRecents));
		OnPropertyChanged(nameof(HasQuickActions));
		OnPropertyChanged(nameof(DefaultLandingLabel));
	}

	private WorkspaceShortcut CreateShortcut(string routeId, IReadOnlySet<string> favorites, string? defaultRoute)
	{
		var descriptor = ResolveDescriptor(routeId) ?? throw new InvalidOperationException("The workspace route is not available.");
		return new WorkspaceShortcut(
			descriptor.RouteId,
			descriptor.Title,
			descriptor.Subtitle,
			descriptor.IconData,
			favorites.Contains(descriptor.RouteId),
			string.Equals(defaultRoute, descriptor.RouteId, StringComparison.OrdinalIgnoreCase));
	}

	private WorkspaceRouteDescriptor? ResolveDescriptor(string? routeId) =>
		routeId is not null && _availableRoutes.TryGetValue(routeId, out var descriptor) ? descriptor : null;

	private static Dictionary<string, WorkspaceRouteDescriptor> BuildAvailableRoutes(MainViewModel main)
	{
		var result = new Dictionary<string, WorkspaceRouteDescriptor>(StringComparer.OrdinalIgnoreCase);
		foreach (var module in ShellFeatureCatalog.Create(main).Modules)
		{
			result.TryAdd(module.Route.Value, new WorkspaceRouteDescriptor(module.Route.Value, module.Name, "Workspace", module.IconData));
			foreach (var page in module.Pages)
				result.TryAdd(page.Route.Value, new WorkspaceRouteDescriptor(page.Route.Value, page.Name, module.Name, module.IconData));
		}
		return result;
	}

	private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
	{
		target.Clear();
		foreach (var value in values) target.Add(value);
	}
}
