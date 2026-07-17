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

		SaveLocationCommand = new AsyncRelayCommand(SaveLocationAsync);

		DeactivateLocationCommand = new AsyncRelayCommand(DeactivateLocationAsync, CanDeactivateLocation);
	}

	public ObservableCollection<LocationListItemViewModel> Locations { get; }
		= new();

	public LocationEditorViewModel Editor { get; }

	public RelayCommand NewLocationCommand { get; }

	public AsyncRelayCommand SaveLocationCommand { get; }

	public AsyncRelayCommand DeactivateLocationCommand { get; }

	public string SearchText
	{
		get => _searchText;

		set
		{
			_searchText = value;

			OnPropertyChanged();

			_ = LoadLocationsAsync();
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

	public async Task LoadLocationsAsync(CancellationToken cancellationToken = default)
	{
		BeginOperation("Loading locations");
		var selectedId =
			SelectedLocation?.Id;

		Locations.Clear();

		try
		{
		var locations = await _locationService.GetLocationsAsync(cancellationToken);

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
		CompleteOperation(Locations.Count == 0, $"{Locations.Count:N0} locations");
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
		catch (Exception exception)
		{
			ErrorMessage = exception.Message;
			FailOperation(exception, "Locations could not be loaded");
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

	private async Task SaveLocationAsync(CancellationToken cancellationToken)
	{
		ClearError();

		try
		{
			var location = Editor.Id == 0
				? await _locationService.CreateLocationAsync(
					Editor.Name,
					Editor.Description,
					cancellationToken)
				: await _locationService.UpdateLocationAsync(
					Editor.Id,
					Editor.Version,
					Editor.Name,
					Editor.Description,
					cancellationToken);
			var existing = Locations.FirstOrDefault(x => x.Id == location.Id);
			if (existing is null) Locations.Add(new LocationListItemViewModel(location));
			else Locations[Locations.IndexOf(existing)] = new LocationListItemViewModel(location);

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

	private async Task DeactivateLocationAsync(CancellationToken cancellationToken)
	{
		ClearError();

		if (!Editor.IsExistingLocation)
		{
			return;
		}

		try
		{
			await _locationService.DeactivateLocationAsync(
				Editor.Id,
				Editor.Version,
				cancellationToken);
			var existing = Locations.FirstOrDefault(x => x.Id == Editor.Id);
			if (existing is not null) Locations.Remove(existing);

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
