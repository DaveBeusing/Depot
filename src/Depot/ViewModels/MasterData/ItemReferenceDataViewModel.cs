// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Commands;
using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels.MasterData;

public sealed class ItemReferenceDataViewModel : BaseViewModel, IDisposable
{
	private readonly IItemReferenceDataService _service;
	private readonly AsyncDebouncer _searchDebouncer = new(TimeSpan.FromMilliseconds(300));
	private ItemReferenceData? _selectedItem;
	private string _searchText = string.Empty;
	private string _name = string.Empty;
	private string? _description;
	private long _editorVersion;

	public ItemReferenceDataViewModel(IItemReferenceDataService service)
	{
		_service = service;
		NewCommand = new RelayCommand(New);
		SaveCommand = new AsyncRelayCommand(SaveAsync);
		ToggleActiveCommand = new AsyncRelayCommand(ToggleActiveAsync, () => SelectedItem is not null);
	}

	public ObservableCollection<ItemReferenceData> Items { get; } = new();
	public string Title => _service.PluralName;
	public string Subtitle => $"Maintain reusable {Title.ToLowerInvariant()} assigned to items.";
	public string SearchPlaceholder => $"Search {Title.ToLowerInvariant()}";
	public RelayCommand NewCommand { get; }
	public AsyncRelayCommand SaveCommand { get; }
	public AsyncRelayCommand ToggleActiveCommand { get; }
	public string EditorTitle => SelectedItem is null ? $"New {_service.SingularName}" : $"Edit {_service.SingularName}";
	public string ActionText => SelectedItem?.IsActive == true ? "Deactivate" : "Activate";

	public string SearchText
	{
		get => _searchText;
		set
		{
			if (_searchText == value) return;
			_searchText = value;
			OnPropertyChanged();
			_ = _searchDebouncer.DebounceAsync(LoadAsync);
		}
	}

	public ItemReferenceData? SelectedItem
	{
		get => _selectedItem;
		set
		{
			if (_selectedItem == value) return;
			_selectedItem = value;
			OnPropertyChanged();
			Name = value?.Name ?? string.Empty;
			Description = value?.Description;
			_editorVersion = value?.Version ?? 0;
			OnPropertyChanged(nameof(EditorTitle));
			OnPropertyChanged(nameof(ActionText));
			ToggleActiveCommand.RaiseCanExecuteChanged();
		}
	}

	public string Name
	{
		get => _name;
		set { if (_name == value) return; _name = value; OnPropertyChanged(); }
	}

	public string? Description
	{
		get => _description;
		set { if (_description == value) return; _description = value; OnPropertyChanged(); }
	}

	public async Task LoadAsync(CancellationToken cancellationToken = default)
	{
		BeginOperation($"Loading {Title.ToLowerInvariant()}");
		try
		{
			var selectedId = SelectedItem?.Id;
			var values = await _service.SearchAsync(SearchText, cancellationToken);
			Items.Clear();
			foreach (var value in values) Items.Add(value);
			SelectedItem = Items.FirstOrDefault(item => item.Id == selectedId);
			CompleteOperation(Items.Count == 0, $"{Items.Count:N0} {Title.ToLowerInvariant()}");
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			CompleteOperation(Items.Count == 0);
		}
		catch (Exception exception)
		{
			FailOperation(exception, $"{Title} could not be loaded");
		}
	}

	private void New()
	{
		SelectedItem = null;
		Name = string.Empty;
		Description = null;
		_editorVersion = 0;
	}

	private async Task SaveAsync(CancellationToken cancellationToken)
	{
		BeginOperation($"Saving {_service.SingularName.ToLowerInvariant()}");
		try
		{
			var saved = await _service.SaveAsync(SelectedItem?.Id ?? 0, _editorVersion, Name, Description, cancellationToken);
			Replace(saved);
			SelectedItem = saved;
			CompleteOperation(false, $"{_service.SingularName} saved");
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			FailOperation(exception, $"{_service.SingularName} could not be saved");
		}
	}

	private async Task ToggleActiveAsync(CancellationToken cancellationToken)
	{
		if (SelectedItem is null) return;
		BeginOperation(SelectedItem.IsActive ? "Deactivating master data" : "Activating master data");
		try
		{
			var saved = await _service.SetActiveAsync(SelectedItem.Id, SelectedItem.Version, !SelectedItem.IsActive, cancellationToken);
			Replace(saved);
			SelectedItem = saved;
			CompleteOperation(false, saved.IsActive ? $"{_service.SingularName} activated" : $"{_service.SingularName} deactivated");
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			FailOperation(exception, $"{_service.SingularName} status could not be changed");
		}
	}

	private void Replace(ItemReferenceData value)
	{
		var existing = Items.FirstOrDefault(item => item.Id == value.Id);
		if (existing is null) Items.Add(value);
		else Items[Items.IndexOf(existing)] = value;
	}

	public void Dispose()
	{
		_searchDebouncer.Dispose();
		SaveCommand.Dispose();
		ToggleActiveCommand.Dispose();
	}
}
