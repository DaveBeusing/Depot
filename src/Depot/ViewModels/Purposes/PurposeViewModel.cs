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

		SavePurposeCommand =
			new RelayCommand(
				SavePurpose);

		DeactivatePurposeCommand =
			new RelayCommand(
				DeactivatePurpose,
				CanDeactivatePurpose);

		LoadPurposes();
	}

	public ObservableCollection<PurposeListItemViewModel> Purposes { get; }
		= new();

	public PurposeEditorViewModel Editor { get; }

	public RelayCommand NewPurposeCommand { get; }

	public RelayCommand SavePurposeCommand { get; }

	public RelayCommand DeactivatePurposeCommand { get; }

	public string SearchText
	{
		get => _searchText;

		set
		{
			_searchText = value;

			OnPropertyChanged();

			LoadPurposes();
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

	public void LoadPurposes()
	{
		var selectedId =
			SelectedPurpose?.Id;

		Purposes.Clear();

		var purposes =
			_purposeService.GetPurposes();

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
	}

	private void NewPurpose()
	{
		ClearError();

		SelectedPurpose = null;

		Editor.Clear();

		DeactivatePurposeCommand.RaiseCanExecuteChanged();
	}

	private void SavePurpose()
	{
		ClearError();

		try
		{
			if (Editor.Id == 0)
			{
				_purposeService.CreatePurpose(
					Editor.Name,
					Editor.Description);
			}
			else
			{
				_purposeService.UpdatePurpose(
					Editor.Id,
					Editor.Name,
					Editor.Description);
			}

			LoadPurposes();

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

	private void DeactivatePurpose()
	{
		ClearError();

		if (!Editor.IsExistingPurpose)
		{
			return;
		}

		try
		{
			_purposeService.DeactivatePurpose(
				Editor.Id);

			LoadPurposes();

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