// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.ComponentModel;

using Depot.Services;
using Depot.ViewModels;

namespace Depot;

public partial class MainWindow
{
	private WorkspaceProductivityViewModel? _workspaceProductivityViewModel;
	private ShellModuleViewModel? _workspaceProductivityObservedModule;
	private CancellationTokenSource? _workspaceProductivityCancellation;
	private string? _lastWorkspaceProductivityRoute;
	private bool _workspaceProductivityInitialized;

	public WorkspaceProductivityViewModel? WorkspaceProductivity => _workspaceProductivityViewModel;

	protected override async void OnContentRendered(EventArgs e)
	{
		base.OnContentRendered(e);
		if (_workspaceProductivityInitialized) return;
		_workspaceProductivityInitialized = true;
		if (DataContext is not MainViewModel main) return;

		try
		{
			var productivity = new WorkspaceProductivityViewModel(WorkspaceProductivityRuntime.Current, main);
			_workspaceProductivityViewModel = productivity;
			await productivity.InitializeAsync();
			main.PropertyChanged += OnWorkspaceProductivityMainChanged;
			Closed += OnWorkspaceProductivityClosed;
			ObserveWorkspaceProductivityModule(main);
			await productivity.NavigateToDefaultOrFallbackAsync();
			await RecordCurrentWorkspaceRouteAsync(main);
		}
		catch
		{
			// Productivity preferences are optional shell state. A failure must not block the ERP shell.
		}
	}

	private void OnWorkspaceProductivityMainChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (sender is not MainViewModel main) return;
		if (e.PropertyName is not (nameof(MainViewModel.SelectedNavigationItem) or nameof(MainViewModel.CurrentViewModel))) return;
		ObserveWorkspaceProductivityModule(main);
		_ = RecordCurrentWorkspaceRouteAsync(main);
	}

	private void ObserveWorkspaceProductivityModule(MainViewModel main)
	{
		var next = main.SelectedNavigationItem is { IsContentCreated: true } selected && selected.Content is ShellModuleViewModel module
			? module
			: null;
		if (ReferenceEquals(next, _workspaceProductivityObservedModule)) return;
		if (_workspaceProductivityObservedModule is not null) _workspaceProductivityObservedModule.PropertyChanged -= OnWorkspaceProductivityModuleChanged;
		_workspaceProductivityObservedModule = next;
		if (_workspaceProductivityObservedModule is not null) _workspaceProductivityObservedModule.PropertyChanged += OnWorkspaceProductivityModuleChanged;
	}

	private void OnWorkspaceProductivityModuleChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(ShellModuleViewModel.SelectedPage) && DataContext is MainViewModel main)
			_ = RecordCurrentWorkspaceRouteAsync(main);
	}

	private async Task RecordCurrentWorkspaceRouteAsync(MainViewModel main)
	{
		var route = ResolveCurrentWorkspaceRoute(main);
		if (route is null || string.Equals(route, _lastWorkspaceProductivityRoute, StringComparison.OrdinalIgnoreCase)) return;
		_lastWorkspaceProductivityRoute = route;

		_workspaceProductivityCancellation?.Cancel();
		_workspaceProductivityCancellation?.Dispose();
		_workspaceProductivityCancellation = new CancellationTokenSource();
		var cancellation = _workspaceProductivityCancellation;
		try
		{
			if (_workspaceProductivityViewModel is not null)
				await _workspaceProductivityViewModel.RecordVisitAsync(route, cancellation.Token);
		}
		catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
		{
		}
		catch
		{
			// Recording recents is best-effort and must never break navigation.
		}
	}

	private static string? ResolveCurrentWorkspaceRoute(MainViewModel main)
	{
		var selected = main.SelectedNavigationItem;
		if (selected is null) return null;
		if (selected.IsContentCreated && selected.Content is ShellModuleViewModel module && module.SelectedPage is { } page)
			return page.Route.Value;
		return selected.Route.Value;
	}

	private void OnWorkspaceProductivityClosed(object? sender, EventArgs e)
	{
		Closed -= OnWorkspaceProductivityClosed;
		if (DataContext is MainViewModel main) main.PropertyChanged -= OnWorkspaceProductivityMainChanged;
		if (_workspaceProductivityObservedModule is not null) _workspaceProductivityObservedModule.PropertyChanged -= OnWorkspaceProductivityModuleChanged;
		_workspaceProductivityObservedModule = null;
		_workspaceProductivityCancellation?.Cancel();
		_workspaceProductivityCancellation?.Dispose();
		_workspaceProductivityCancellation = null;
		_workspaceProductivityViewModel = null;
	}
}
