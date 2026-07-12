// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;
using System.Windows;

using Depot.Commands;
using Depot.Services;
using Depot.Services.Import;
using Depot.ViewModels.Administration;

namespace Depot.ViewModels;

public sealed class MainViewModel : BaseViewModel
{
	private NavigationItem? _selectedNavigationItem;
	private BaseViewModel? _currentViewModel;
	private readonly AuthorizationService _authorizationService;
	private readonly SessionService _sessionService;
	
	public MainViewModel(
		ItemService itemService,
		StockService stockService,
		MovementService movementService,
		ReportService reportService,
		PurposeService purposeService,
		LocationService locationService,
		UserService userService,
		AuthorizationService authorizationService,
		SessionService sessionService,
		ImportService importService,
		IFileDialogService fileDialogService)
		{
		
		_authorizationService =	authorizationService;
		_sessionService = sessionService;
		LogoutCommand = new RelayCommand(Logout);
	
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
				movementService);

		ReportsViewModel =
			new ReportsViewModel(
				reportService,
				fileDialogService);

		ImportViewModel =
			new ImportViewModel(
				importService,
				fileDialogService);

		AdministrationViewModel =
			new AdministrationViewModel(
				ImportViewModel, 
				purposeService,
				locationService,
				userService);

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
				Name = "Reports",
				Icon = "📈",
				Section = ShellSection.Reports
			});

		// Only show the Administration section if the user has permission to manage users
		// CanManageUsers() represents the administrator role in version 1.0.
		if (_authorizationService.CanManageUsers())
		{
			NavigationItems.Add(
				new NavigationItem
				{
					Name = "Administration",
					Icon = "⚙",
					Section = ShellSection.Administration,
					Margin = new Thickness(8, 24, 8, 4)
				});
		}

		SelectedNavigationItem = NavigationItems[0];
	}

	public ObservableCollection<NavigationItem> NavigationItems { get; } = new();

	public RelayCommand LogoutCommand { get; }

	public event EventHandler? LogoutRequested;

	public DashboardViewModel DashboardViewModel { get; }

	public InventoryViewModel InventoryViewModel { get; }

	public ItemsViewModel ItemsViewModel { get; }

	public MovementsViewModel MovementsViewModel { get; }

	public ReportsViewModel ReportsViewModel { get; }

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

		CurrentViewModel = (ShellSection)SelectedNavigationItem.Section switch
		{
			ShellSection.Dashboard => DashboardViewModel,
			ShellSection.Inventory => InventoryViewModel,
			ShellSection.Items => ItemsViewModel,
			ShellSection.Movements => MovementsViewModel,
			ShellSection.Reports => ReportsViewModel,
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

		if (CurrentViewModel == ReportsViewModel)
		{
			ReportsViewModel.Load();
		}
	}

	public string CurrentUserDisplayName => _authorizationService.CurrentUser?.DisplayName ?? string.Empty;

	public string CurrentUserRole => _authorizationService.CurrentUser?.IsAdministrator == true ? "Administrator" : "User";

	private void Logout()
	{
		_sessionService.Logout();
		LogoutRequested?.Invoke(this, EventArgs.Empty);
	}
}
