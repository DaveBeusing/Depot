// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.ViewModels;

using Xunit;

namespace Depot.Tests;

public sealed class NavigationLifecycleTests
{
	[Fact]
	public async Task OneModuleNavigationActionLoadsTheSelectedPageExactlyOnce()
	{
		var loads = 0;
		using var first = Item("First", () => loads++);
		using var second = Item("Second", () => loads++);
		using var module = new ShellModuleViewModel("Module", "Description", [first, second]);

		module.SelectedPage = second;
		Assert.Equal(0, loads);

		await module.ActivateAsync();

		Assert.Equal(1, loads);
	}

	[Fact]
	public async Task SwitchingSecondaryPagesLoadsOnlyTheNewPage()
	{
		var firstLoads = 0;
		var secondLoads = 0;
		using var first = Item("First", () => firstLoads++);
		using var second = Item("Second", () => secondLoads++);
		using var module = new ShellModuleViewModel("Module", "Description", [first, second]);
		using var shellItem = new ShellNavigationItem(
			"Module",
			string.Empty,
			() => module,
			(viewModel, token) => ((ShellModuleViewModel)viewModel).ActivateAsync(token),
			"module",
			ownsLoadState: false,
			refreshAsync: (viewModel, token) => ((ShellModuleViewModel)viewModel).RefreshAsync(token));

		await shellItem.ActivateAsync();
		module.SetSelectedPage(second);
		await shellItem.ActivateAsync();

		Assert.Equal(1, firstLoads);
		Assert.Equal(1, secondLoads);
	}

	[Fact]
	public async Task NavigationContentIsCreatedOnlyOnFirstActivationAndThenReused()
	{
		var creations = 0;
		var loads = 0;
		using var item = new ShellNavigationItem(
			"Lazy",
			string.Empty,
			() => { creations++; return new StubViewModel(); },
			(_, _) => { loads++; return Task.CompletedTask; },
			"lazy");

		Assert.False(item.IsContentCreated);
		await item.ActivateAsync();
		await item.ActivateAsync();

		Assert.True(item.IsContentCreated);
		Assert.Equal(1, creations);
		Assert.Equal(1, loads);
	}

	[Fact]
	public void DisposingNavigationItemDoesNotCreateItsContent()
	{
		var creations = 0;
		var item = new ShellNavigationItem(
			"Lazy",
			string.Empty,
			() => { creations++; return new StubViewModel(); },
			(_, _) => Task.CompletedTask,
			"lazy");

		item.Dispose();

		Assert.Equal(0, creations);
	}

	[Fact]
	public async Task StalePageReloadsOnceAndReturnsToLoaded()
	{
		var loads = 0;
		using var state = new NavigationLoadState();
		Task Load(CancellationToken _) { loads++; return Task.CompletedTask; }

		await state.ActivateAsync(Load);
		state.MarkStale();
		await state.ActivateAsync(Load);
		await state.ActivateAsync(Load);

		Assert.Equal(2, loads);
		Assert.Equal(NavigationLoadStatus.Loaded, state.Status);
	}

	[Fact]
	public async Task ExplicitRefreshAlwaysReloads()
	{
		var loads = 0;
		using var state = new NavigationLoadState();
		Task Load(CancellationToken _) { loads++; return Task.CompletedTask; }

		await state.ActivateAsync(Load);
		await state.RefreshAsync(Load);
		await state.RefreshAsync(Load);

		Assert.Equal(3, loads);
	}

	[Fact]
	public async Task CancelledFirstLoadRemainsNotLoadedAndCanBeRetried()
	{
		var loads = 0;
		using var state = new NavigationLoadState();
		using var cancellation = new CancellationTokenSource();
		async Task Load(CancellationToken token)
		{
			loads++;
			await Task.Delay(Timeout.InfiniteTimeSpan, token);
		}

		var pending = state.ActivateAsync(Load, cancellation.Token);
		cancellation.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);

		Assert.Equal(NavigationLoadStatus.NotLoaded, state.Status);
		await state.ActivateAsync(_ => { loads++; return Task.CompletedTask; });
		Assert.Equal(2, loads);
		Assert.Equal(NavigationLoadStatus.Loaded, state.Status);
	}

	[Fact]
	public async Task CancelledRefreshPreservesThePreviouslyLoadedState()
	{
		using var state = new NavigationLoadState();
		await state.ActivateAsync(_ => Task.CompletedTask);
		using var cancellation = new CancellationTokenSource();

		var refresh = state.RefreshAsync(
			async token => await Task.Delay(Timeout.InfiniteTimeSpan, token),
			cancellation.Token);
		cancellation.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => refresh);

		Assert.Equal(NavigationLoadStatus.Loaded, state.Status);
	}

	private static SecondaryNavigationItem Item(string name, Action onLoad) =>
		new(name, () => new StubViewModel(), (_, _) => { onLoad(); return Task.CompletedTask; }, name);

	private sealed class StubViewModel : BaseViewModel;
}
