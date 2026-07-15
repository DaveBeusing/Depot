// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Commands;
using Depot.Services;

namespace Depot.ViewModels.Locations;

/// <summary>
/// Represents the location management module.
/// </summary>
public sealed class LocationViewModel
	: BaseViewModel
{
	private readonly LocationService _locationService;

	private LocationListItemViewModel? _selectedLocation;
	private string _searchText = string.Empty;
	private string? _errorMessage;

	public LocationViewModel(
		LocationService locationService)
	{
		_locationService = locationService;

		Editor =
			new LocationEditorViewModel();

		NewLocationCommand =
			new RelayCommand(
				NewLocation);

		SaveLocationCommand =
			new RelayCommand(
				SaveLocation);

		DeactivateLocationCommand =
			new RelayCommand(
				DeactivateLocation,
				CanDeactivateLocation);

		LoadLocations();
	}

	public ObservableCollection<LocationListItemViewModel> Locations { get; }
		= new();

	public LocationEditorViewModel Editor { get; }

	public RelayCommand NewLocationCommand { get; }

	public RelayCommand SaveLocationCommand { get; }

	public RelayCommand DeactivateLocationCommand { get; }

	public string SearchText
	{
		get => _searchText;

		set
		{
			_searchText = value;

			OnPropertyChanged();

			LoadLocations();
		}
	}

	public LocationListItemViewModel? SelectedLocation
	{
		get => _selectedLocation;

		set
		{
			_selectedLocation = value;

			OnPropertyChanged();

			LoadSelectedLocation();

			DeactivateLocationCommand.RaiseCanExecuteChanged();
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

	public void LoadLocations()
	{
		var selectedId =
			SelectedLocation?.Id;

		Locations.Clear();

		var locations =
			_locationService.GetLocations();

		if (!string.IsNullOrWhiteSpace(SearchText))
		{
			locations =
				locations
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

		foreach (var location in locations)
		{
			Locations.Add(
				new LocationListItemViewModel(
					location));
		}

		if (selectedId is not null)
		{
			SelectedLocation =
				Locations.FirstOrDefault(
					x => x.Id == selectedId.Value);
		}
	}

	private void LoadSelectedLocation()
	{
		ClearError();

		if (SelectedLocation is null)
		{
			return;
		}

		Editor.Id =
			SelectedLocation.Id;

		Editor.Name =
			SelectedLocation.Name;

		Editor.Description =
			SelectedLocation.Description;

		Editor.Version =
			SelectedLocation.Version;
	}

	private void NewLocation()
	{
		ClearError();

		SelectedLocation = null;

		Editor.Clear();

		DeactivateLocationCommand.RaiseCanExecuteChanged();
	}

	private void SaveLocation()
	{
		ClearError();

		try
		{
			if (Editor.Id == 0)
			{
				_locationService.CreateLocation(
					Editor.Name,
					Editor.Description);
			}
			else
			{
				_locationService.UpdateLocation(
					Editor.Id,
					Editor.Version,
					Editor.Name,
					Editor.Description);
			}

			LoadLocations();

			Editor.Clear();

			SelectedLocation = null;
		}
		catch (Exception ex)
		{
			ErrorMessage =
				ex.Message;
		}
	}

	private bool CanDeactivateLocation()
	{
		return Editor.IsExistingLocation;
	}

	private void DeactivateLocation()
	{
		ClearError();

		if (!Editor.IsExistingLocation)
		{
			return;
		}

		try
		{
			_locationService.DeactivateLocation(
				Editor.Id,
				Editor.Version);

			LoadLocations();

			Editor.Clear();

			SelectedLocation = null;

			DeactivateLocationCommand.RaiseCanExecuteChanged();
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
