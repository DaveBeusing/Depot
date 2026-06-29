// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.ViewModels.Shared;
using Depot.ViewModels.Purposes;
using Depot.Services;

namespace Depot.ViewModels.MasterData;

/// <summary>
/// Represents the master data module.
/// </summary>
public sealed class MasterDataViewModel
	: BaseViewModel
{
	private NavigationItem? _selectedNavigationItem;
	private BaseViewModel? _currentViewModel;
	private readonly PurposeViewModel _purposeViewModel;

	public MasterDataViewModel(PurposeService purposeService)
	{
		_purposeViewModel = new PurposeViewModel(purposeService);

		NavigationItems.Add(
			new NavigationItem
			{
				Name = "Purposes",
				Section = MasterDataSection.Purposes
			});

		NavigationItems.Add(
			new NavigationItem
			{
				Name = "Locations",
				Section = MasterDataSection.Locations
			});

		SelectedNavigationItem =
			NavigationItems[0];
	}

	public ObservableCollection<NavigationItem> NavigationItems { get; }
		= new();

	public NavigationItem? SelectedNavigationItem
	{
		get => _selectedNavigationItem;

		set
		{
			if (_selectedNavigationItem == value)
			{
				return;
			}

			_selectedNavigationItem = value;

			OnPropertyChanged();

			UpdateCurrentViewModel();
		}
	}

	public BaseViewModel? CurrentViewModel
	{
		get => _currentViewModel;

		private set
		{
			_currentViewModel = value;

			OnPropertyChanged();
		}
	}

	private void UpdateCurrentViewModel()
	{
		if (SelectedNavigationItem is null)
		{
			CurrentViewModel = null;
			return;
		}

		var section =
			(MasterDataSection)SelectedNavigationItem.Section;

		CurrentViewModel =
			section switch
			{
				MasterDataSection.Purposes =>
					_purposeViewModel,

				MasterDataSection.Locations =>
					new PlaceholderViewModel(
						"Locations",
						"Manage inventory locations."),

				_ =>
					new PlaceholderViewModel(
						"Master Data",
						"This module is currently under development.")
			};
	}
}