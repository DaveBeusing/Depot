// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Commands;
using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels.ReasonCodes;

public sealed class ReasonCodeViewModel : BaseViewModel, IDisposable
{
	private readonly ReasonCodeService _service;
	private readonly AsyncDebouncer _searchDebouncer = new(TimeSpan.FromMilliseconds(300));
	private ReasonCode? _selectedReasonCode;
	private string _searchText = string.Empty;
	private string _name = string.Empty;
	private string? _description;
	private long _editorVersion;

	public ReasonCodeViewModel(ReasonCodeService service)
	{
		_service = service;
		NewCommand = new RelayCommand(New);
		SaveCommand = new AsyncRelayCommand(SaveAsync);
		ToggleActiveCommand = new AsyncRelayCommand(ToggleActiveAsync, () => SelectedReasonCode is not null);
	}

	public ObservableCollection<ReasonCode> ReasonCodes { get; } = new();
	public RelayCommand NewCommand { get; }
	public AsyncRelayCommand SaveCommand { get; }
	public AsyncRelayCommand ToggleActiveCommand { get; }

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

	public ReasonCode? SelectedReasonCode
	{
		get => _selectedReasonCode;
		set
		{
			if (_selectedReasonCode == value) return;
			_selectedReasonCode = value;
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
		set
		{
			if (_name == value) return;
			_name = value;
			OnPropertyChanged();
		}
	}

	public string? Description
	{
		get => _description;
		set
		{
			if (_description == value) return;
			_description = value;
			OnPropertyChanged();
		}
	}
	public string EditorTitle => SelectedReasonCode is null ? "New Reason Code" : "Edit Reason Code";
	public string ActionText => SelectedReasonCode?.IsActive == true ? "Deactivate" : "Activate";

	public async Task LoadAsync(CancellationToken cancellationToken = default)
	{
		BeginOperation("Loading reason codes");
		try
		{
			var selectedId = SelectedReasonCode?.Id;
			var reasonCodes = await _service.SearchAsync(SearchText, cancellationToken);
			ReasonCodes.Clear();
			foreach (var reasonCode in reasonCodes) ReasonCodes.Add(reasonCode);
			SelectedReasonCode = ReasonCodes.FirstOrDefault(item => item.Id == selectedId);
			CompleteOperation(ReasonCodes.Count == 0, $"{ReasonCodes.Count:N0} reason codes");
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			CompleteOperation(ReasonCodes.Count == 0);
		}
		catch (Exception exception)
		{
			FailOperation(exception, "Reason codes could not be loaded");
		}
	}

	private void New()
	{
		SelectedReasonCode = null;
		Name = string.Empty;
		Description = null;
		_editorVersion = 0;
	}

	private async Task SaveAsync(CancellationToken cancellationToken)
	{
		BeginOperation("Saving reason code");
		try
		{
			var saved = await _service.SaveAsync(
				SelectedReasonCode?.Id ?? 0,
				_editorVersion,
				Name,
				Description,
				cancellationToken);
			Replace(saved);
			SelectedReasonCode = saved;
			CompleteOperation(false, "Reason code saved");
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			FailOperation(exception, "Reason code could not be saved");
		}
	}

	private async Task ToggleActiveAsync(CancellationToken cancellationToken)
	{
		if (SelectedReasonCode is null) return;
		BeginOperation(SelectedReasonCode.IsActive ? "Deactivating reason code" : "Activating reason code");
		try
		{
			var saved = await _service.SetActiveAsync(
				SelectedReasonCode.Id,
				SelectedReasonCode.Version,
				!SelectedReasonCode.IsActive,
				cancellationToken);
			Replace(saved);
			SelectedReasonCode = saved;
			CompleteOperation(false, saved.IsActive ? "Reason code activated" : "Reason code deactivated");
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			FailOperation(exception, "Reason code status could not be changed");
		}
	}

	private void Replace(ReasonCode reasonCode)
	{
		var existing = ReasonCodes.FirstOrDefault(item => item.Id == reasonCode.Id);
		if (existing is null) ReasonCodes.Add(reasonCode);
		else ReasonCodes[ReasonCodes.IndexOf(existing)] = reasonCode;
	}

	public void Dispose()
	{
		_searchDebouncer.Dispose();
		SaveCommand.Dispose();
		ToggleActiveCommand.Dispose();
	}
}
