// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Commands;
using Depot.Services;

namespace Depot.ViewModels.Purposes;

/// <summary>
/// Represents the purpose management module.
/// </summary>
public sealed class PurposeViewModel
	: BaseViewModel
{
	private readonly PurposeService _purposeService;

	private PurposeListItemViewModel? _selectedPurpose;
	private string _searchText = string.Empty;
	private string? _errorMessage;

	public PurposeViewModel(
		PurposeService purposeService)
	{
		_purposeService = purposeService;

		Editor =
			new PurposeEditorViewModel();

		NewPurposeCommand =
			new RelayCommand(
				NewPurpose);

		SavePurposeCommand = new AsyncRelayCommand(SavePurposeAsync);

		DeactivatePurposeCommand = new AsyncRelayCommand(DeactivatePurposeAsync, CanDeactivatePurpose);
	}

	public ObservableCollection<PurposeListItemViewModel> Purposes { get; }
		= new();

	public PurposeEditorViewModel Editor { get; }

	public RelayCommand NewPurposeCommand { get; }

	public AsyncRelayCommand SavePurposeCommand { get; }

	public AsyncRelayCommand DeactivatePurposeCommand { get; }

	public string SearchText
	{
		get => _searchText;

		set
		{
			_searchText = value;

			OnPropertyChanged();

			_ = LoadPurposesAsync();
		}
	}

	public PurposeListItemViewModel? SelectedPurpose
	{
		get => _selectedPurpose;

		set
		{
			_selectedPurpose = value;

			OnPropertyChanged();

			LoadSelectedPurpose();

			DeactivatePurposeCommand.RaiseCanExecuteChanged();
		}
	}

	public string? ErrorMessage
	{
		get => _errorMessage;

		private set
		{
			_errorMessage = value;

			OnPropertyChanged();
			OnPropertyChanged(nameof(HasErrorMessage));
		}
	}

	public bool HasErrorMessage =>
		!string.IsNullOrWhiteSpace(
			ErrorMessage);

	public async Task LoadPurposesAsync(CancellationToken cancellationToken = default)
	{
		BeginOperation("Loading purposes");
		var selectedId =
			SelectedPurpose?.Id;

		Purposes.Clear();

		try
		{
		var purposes = await _purposeService.GetPurposesAsync(cancellationToken);

		if (!string.IsNullOrWhiteSpace(SearchText))
		{
			purposes =
				purposes
					.Where(
						x =>
							x.Name.Contains(
								SearchText,
								StringComparison.OrdinalIgnoreCase) ||

							(x.Description?.Contains(
								SearchText,
								StringComparison.OrdinalIgnoreCase) ?? false))
					.ToList();
		}

		foreach (var purpose in purposes)
		{
			Purposes.Add(
				new PurposeListItemViewModel(
					purpose));
		}

		if (selectedId is not null)
		{
			SelectedPurpose =
				Purposes.FirstOrDefault(
						x => x.Id == selectedId.Value);
		}
		CompleteOperation(Purposes.Count == 0, $"{Purposes.Count:N0} purposes");
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
		catch (Exception exception)
		{
			ErrorMessage = exception.Message;
			FailOperation(exception, "Purposes could not be loaded");
		}
	}

	private void LoadSelectedPurpose()
	{
		ClearError();

		if (SelectedPurpose is null)
		{
			return;
		}

		Editor.Id =
			SelectedPurpose.Id;

		Editor.Name =
			SelectedPurpose.Name;

		Editor.Description =
			SelectedPurpose.Description;

		Editor.Version =
			SelectedPurpose.Version;
	}

	private void NewPurpose()
	{
		ClearError();

		SelectedPurpose = null;

		Editor.Clear();

		DeactivatePurposeCommand.RaiseCanExecuteChanged();
	}

	private async Task SavePurposeAsync(CancellationToken cancellationToken)
	{
		ClearError();

		try
		{
			var purpose = Editor.Id == 0
				? await _purposeService.CreatePurposeAsync(
					Editor.Name,
					Editor.Description,
					cancellationToken)
				: await _purposeService.UpdatePurposeAsync(
					Editor.Id,
					Editor.Version,
					Editor.Name,
					Editor.Description,
					cancellationToken);
			var existing = Purposes.FirstOrDefault(x => x.Id == purpose.Id);
			if (existing is null) Purposes.Add(new PurposeListItemViewModel(purpose));
			else Purposes[Purposes.IndexOf(existing)] = new PurposeListItemViewModel(purpose);

			Editor.Clear();

			SelectedPurpose = null;
		}
		catch (Exception ex)
		{
			ErrorMessage =
				ex.Message;
		}
	}

	private bool CanDeactivatePurpose()
	{
		return Editor.IsExistingPurpose;
	}

	private async Task DeactivatePurposeAsync(CancellationToken cancellationToken)
	{
		ClearError();

		if (!Editor.IsExistingPurpose)
		{
			return;
		}

		try
		{
			await _purposeService.DeactivatePurposeAsync(
				Editor.Id,
				Editor.Version,
				cancellationToken);
			var existing = Purposes.FirstOrDefault(x => x.Id == Editor.Id);
			if (existing is not null) Purposes.Remove(existing);

			Editor.Clear();

			SelectedPurpose = null;

			DeactivatePurposeCommand.RaiseCanExecuteChanged();
		}
		catch (Exception ex)
		{
			ErrorMessage =
				ex.Message;
		}
	}

	private void ClearError()
	{
		ErrorMessage = null;
	}
}
