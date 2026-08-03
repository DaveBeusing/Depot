// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

namespace Depot.ViewModels;

public sealed class ShellModuleViewModel : BaseViewModel, IDisposable
{
	private CancellationTokenSource? _loadCancellation;
	private SecondaryNavigationItem? _selectedPage;

	public ShellModuleViewModel(string title, string subtitle, IEnumerable<SecondaryNavigationItem> pages)
	{
		Title = title;
		Subtitle = subtitle;
		Pages = new ObservableCollection<SecondaryNavigationItem>(pages);
		_selectedPage = Pages.FirstOrDefault();
		_selectedPage?.Activate?.Invoke();
	}

	public string Title { get; }
	public string Subtitle { get; }
	public ObservableCollection<SecondaryNavigationItem> Pages { get; }
	public bool HasMultiplePages => Pages.Count > 1;
	public BaseViewModel? CurrentViewModel => SelectedPage?.Content;

	public SecondaryNavigationItem? SelectedPage
	{
		get => _selectedPage;
		set
		{
			if (_selectedPage == value || value is null) return;
			_selectedPage = value;
			value.Activate?.Invoke();
			OnPropertyChanged();
			OnPropertyChanged(nameof(CurrentViewModel));
			_ = ReloadAsync();
		}
	}

	public Task LoadAsync(CancellationToken cancellationToken = default) =>
		SelectedPage?.LoadAsync(cancellationToken) ?? Task.CompletedTask;

	private async Task ReloadAsync()
	{
		_loadCancellation?.Cancel();
		_loadCancellation?.Dispose();
		_loadCancellation = new CancellationTokenSource();
		try
		{
			await LoadAsync(_loadCancellation.Token);
		}
		catch (OperationCanceledException) when (_loadCancellation.IsCancellationRequested)
		{
		}
	}

	public void Dispose()
	{
		_loadCancellation?.Cancel();
		_loadCancellation?.Dispose();
	}
}
