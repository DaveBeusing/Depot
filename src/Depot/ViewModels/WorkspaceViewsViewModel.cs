// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Commands;
using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels;

public interface IWorkspaceViewSurface
{
	WorkspaceViewDefinition Capture(WorkspaceGridDensity density);
	void Apply(WorkspaceViewDefinition definition);
	void ResetCanonical();
}

public sealed class WorkspaceViewsViewModel : BaseViewModel, IDisposable
{
	private readonly WorkspaceViewService _service;
	private readonly string _workspaceId;
	private readonly IWorkspaceViewSurface _surface;
	private SavedWorkspaceView? _selectedView;
	private string _viewName = string.Empty;
	private string _message = string.Empty;
	private WorkspaceGridDensity _selectedDensity = WorkspaceGridDensity.Standard;
	private bool _disposed;

	public WorkspaceViewsViewModel(WorkspaceViewService service, string workspaceId, IWorkspaceViewSurface surface)
	{
		_service = service;
		_workspaceId = workspaceId;
		_surface = surface;
		NewViewCommand = new RelayCommand(NewView);
		SaveViewCommand = new AsyncRelayCommand(SaveAsync, CanSave);
		ApplyViewCommand = new RelayCommand(ApplySelected, () => SelectedView is not null);
		SetDefaultCommand = new AsyncRelayCommand(SetDefaultAsync, () => SelectedView is not null);
		DeleteViewCommand = new AsyncRelayCommand(DeleteAsync, () => SelectedView is not null);
		ResetCommand = new RelayCommand(ResetCanonical);
	}

	public ObservableCollection<SavedWorkspaceView> Views { get; } = [];
	public IReadOnlyList<WorkspaceGridDensity> DensityOptions { get; } = Enum.GetValues<WorkspaceGridDensity>();
	public RelayCommand NewViewCommand { get; }
	public AsyncRelayCommand SaveViewCommand { get; }
	public RelayCommand ApplyViewCommand { get; }
	public AsyncRelayCommand SetDefaultCommand { get; }
	public AsyncRelayCommand DeleteViewCommand { get; }
	public RelayCommand ResetCommand { get; }

	public SavedWorkspaceView? SelectedView
	{
		get => _selectedView;
		set
		{
			if (ReferenceEquals(_selectedView, value)) return;
			_selectedView = value;
			OnPropertyChanged();
			if (value is not null)
			{
				ViewName = value.Name;
				SelectedDensity = value.Definition.GridDensity;
			}
			RaiseCommands();
		}
	}

	public string ViewName
	{
		get => _viewName;
		set
		{
			if (_viewName == value) return;
			_viewName = value;
			OnPropertyChanged();
			SaveViewCommand.RaiseCanExecuteChanged();
		}
	}

	public WorkspaceGridDensity SelectedDensity
	{
		get => _selectedDensity;
		set
		{
			if (_selectedDensity == value) return;
			_selectedDensity = value;
			OnPropertyChanged();
		}
	}

	public string Message
	{
		get => _message;
		private set
		{
			if (_message == value) return;
			_message = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(HasMessage));
		}
	}

	public bool HasMessage => !string.IsNullOrWhiteSpace(Message);

	public async Task InitializeAsync(CancellationToken cancellationToken = default)
	{
		await ReloadAsync(cancellationToken);
		var defaultView = Views.FirstOrDefault(view => view.IsDefault);
		if (defaultView is null) return;
		SelectedView = defaultView;
		_surface.Apply(defaultView.Definition);
		Message = $"Default view '{defaultView.Name}' applied.";
	}

	private void NewView()
	{
		SelectedView = null;
		ViewName = string.Empty;
		Message = "Capture the current layout and enter a name for the new view.";
	}

	private bool CanSave() => !string.IsNullOrWhiteSpace(ViewName);

	private async Task SaveAsync(CancellationToken cancellationToken)
	{
		try
		{
			var definition = _surface.Capture(SelectedDensity);
			var saved = await _service.SaveAsync(
				_workspaceId,
				SelectedView?.ViewId,
				ViewName,
				definition,
				SelectedView?.Version,
				cancellationToken);
			await ReloadAsync(cancellationToken);
			SelectedView = Views.FirstOrDefault(view => view.ViewId == saved.ViewId);
			Message = $"View '{saved.Name}' saved.";
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			Message = exception.Message;
		}
	}

	private void ApplySelected()
	{
		if (SelectedView is null) return;
		_surface.Apply(SelectedView.Definition);
		SelectedDensity = SelectedView.Definition.GridDensity;
		Message = $"View '{SelectedView.Name}' applied.";
	}

	private async Task SetDefaultAsync(CancellationToken cancellationToken)
	{
		if (SelectedView is null) return;
		try
		{
			var selectedId = SelectedView.ViewId;
			await _service.SetDefaultAsync(_workspaceId, selectedId, cancellationToken);
			await ReloadAsync(cancellationToken);
			SelectedView = Views.FirstOrDefault(view => view.ViewId == selectedId);
			Message = SelectedView is null ? "Default view updated." : $"'{SelectedView.Name}' is now the default view.";
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			Message = exception.Message;
		}
	}

	private async Task DeleteAsync(CancellationToken cancellationToken)
	{
		if (SelectedView is null) return;
		try
		{
			var name = SelectedView.Name;
			await _service.DeleteAsync(_workspaceId, SelectedView.ViewId, SelectedView.Version, cancellationToken);
			SelectedView = null;
			await ReloadAsync(cancellationToken);
			ViewName = string.Empty;
			Message = $"View '{name}' deleted.";
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			Message = exception.Message;
		}
	}

	private void ResetCanonical()
	{
		_surface.ResetCanonical();
		SelectedView = null;
		SelectedDensity = WorkspaceGridDensity.Standard;
		ViewName = string.Empty;
		Message = "Canonical workspace layout restored.";
	}

	private async Task ReloadAsync(CancellationToken cancellationToken)
	{
		var selectedId = SelectedView?.ViewId;
		var views = await _service.ListAsync(_workspaceId, cancellationToken);
		Views.Clear();
		foreach (var view in views) Views.Add(view);
		if (selectedId is not null) _selectedView = Views.FirstOrDefault(view => view.ViewId == selectedId);
		OnPropertyChanged(nameof(SelectedView));
		RaiseCommands();
	}

	private void RaiseCommands()
	{
		SaveViewCommand.RaiseCanExecuteChanged();
		ApplyViewCommand.RaiseCanExecuteChanged();
		SetDefaultCommand.RaiseCanExecuteChanged();
		DeleteViewCommand.RaiseCanExecuteChanged();
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		SaveViewCommand.Dispose();
		SetDefaultCommand.Dispose();
		DeleteViewCommand.Dispose();
	}
}
