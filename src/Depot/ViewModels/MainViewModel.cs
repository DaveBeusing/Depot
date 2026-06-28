// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Repositories;
using Depot.Services;
using Depot.Services.Import;
using Depot.ViewModels.Administration;

namespace Depot.ViewModels;

public sealed class MainViewModel
	: BaseViewModel
{
	private NavigationItem? _selectedNavigationItem;
	private BaseViewModel? _currentViewModel;

	public MainViewModel(
		ItemService itemService,
		StockService stockService,
		MovementService movementService,
		ItemRepository itemRepository,
		StockMovementRepository stockMovementRepository,
		ImportService importService)
	{
		DashboardViewModel =
			new DashboardViewModel(
				stockService);

		InventoryViewModel =
			new InventoryViewModel(
				stockService);

		ItemsViewModel =
			new ItemsViewModel(
				itemService);

		MovementsViewModel =
			new MovementsViewModel(
				itemRepository,
				stockMovementRepository,
				movementService);

		ImportViewModel =
			new ImportViewModel(
				importService);

		AdministrationViewModel =
			new AdministrationViewModel(
				ImportViewModel);

		NavigationItems.Add(
			new NavigationItem
			{
				Name = "Dashboard",
				Icon = "📊",
				Section = ShellSection.Dashboard
			});

		NavigationItems.Add(
			new NavigationItem
			{
				Name = "Inventory",
				Icon = "📦",
				Section = ShellSection.Inventory
			});

		NavigationItems.Add(
			new NavigationItem
			{
				Name = "Items",
				Icon = "📋",
				Section = ShellSection.Items
			});

		NavigationItems.Add(
			new NavigationItem
			{
				Name = "Movements",
				Icon = "🔄",
				Section = ShellSection.Movements
			});

		NavigationItems.Add(
			new NavigationItem
			{
				Name = "Administration",
				Icon = "⚙",
				Section = ShellSection.Administration
			});

		SelectedNavigationItem =
			NavigationItems[0];
	}

	public ObservableCollection<NavigationItem> NavigationItems { get; }
		= new();

	public DashboardViewModel DashboardViewModel { get; }

	public InventoryViewModel InventoryViewModel { get; }

	public ItemsViewModel ItemsViewModel { get; }

	public MovementsViewModel MovementsViewModel { get; }

	public ImportViewModel ImportViewModel { get; }

	public AdministrationViewModel AdministrationViewModel { get; }

	public NavigationItem? SelectedNavigationItem
	{
		get => _selectedNavigationItem;

		set
		{
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

		CurrentViewModel =
			(ShellSection)SelectedNavigationItem.Section switch
			{
				ShellSection.Dashboard => DashboardViewModel,
				ShellSection.Inventory => InventoryViewModel,
				ShellSection.Items => ItemsViewModel,
				ShellSection.Movements => MovementsViewModel,
				ShellSection.Administration => AdministrationViewModel,
				_ => DashboardViewModel
			};

		if (CurrentViewModel == DashboardViewModel)
		{
			DashboardViewModel.Load();
		}

		if (CurrentViewModel == InventoryViewModel)
		{
			InventoryViewModel.Load();
		}

		if (CurrentViewModel == MovementsViewModel)
		{
			MovementsViewModel.Load();
		}
	}
}