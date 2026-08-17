// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Models;
using Depot.Services;
using Depot.Services.Help;
using Depot.ViewModels.MasterData;
using Depot.ViewModels.Suppliers;
using Depot.ViewModels.Users;
using Depot.ViewModels.Warehouses;

namespace Depot.ViewModels.Administration;

public sealed class AdministrationViewModel : BaseViewModel, IDisposable
{
	private readonly ImportViewModel _importViewModel;
	private readonly MasterDataViewModel _masterDataViewModel;
	private readonly UserViewModel _userViewModel;
	private readonly RoleViewModel _roleViewModel;
	private readonly DatabaseSettingsViewModel _databaseSettingsViewModel;
	private readonly AuditLogViewModel _auditLogViewModel;
	private readonly AboutViewModel _aboutViewModel;
	private readonly Dictionary<AdministrationSection, NavigationLoadState> _loadStates = [];
	private CancellationTokenSource? _navigationCancellation;
	private NavigationItem? _selectedNavigationItem;
	private BaseViewModel? _currentViewModel;

	public AdministrationViewModel(
		ImportViewModel importViewModel,
		ItemService itemService,
		PurposeService purposeService,
		ReasonCodeService reasonCodeService,
		ManufacturerService manufacturerService,
		CategoryService categoryService,
		UnitOfMeasureService unitOfMeasureService,
		PackagingService packagingService,
		SupplierCategoryService supplierCategoryService,
		SupplierService supplierService,
		SupplierItemService supplierItemService,
		WarehouseService warehouseService,
		StorageLocationService storageLocationService,
		UserService userService,
		RoleService roleService,
		IAuthorizationService authorization,
		SettingsService settingsService,
		ConnectionStatusService connectionStatusService,
		DatabaseConnectionTester databaseConnectionTester,
		DatabaseManagementService databaseManagementService,
		AuditLogService auditLogService,
		IFileDialogService fileDialogService,
		ApplicationInformationService applicationInformationService)
	{
		_importViewModel = importViewModel;
		_masterDataViewModel = new MasterDataViewModel(purposeService, reasonCodeService, manufacturerService, categoryService, unitOfMeasureService, packagingService, supplierCategoryService, supplierService, supplierItemService, itemService, warehouseService, storageLocationService);
		_userViewModel = new UserViewModel(userService);
		_roleViewModel = new RoleViewModel(roleService);
		_databaseSettingsViewModel = new DatabaseSettingsViewModel(settingsService, connectionStatusService, databaseConnectionTester, databaseManagementService, fileDialogService);
		_aboutViewModel = new AboutViewModel(applicationInformationService);
		_auditLogViewModel = new AuditLogViewModel(auditLogService, fileDialogService);

		AddIf(authorization, ApplicationPermission.MasterDataView, "Master Data", AdministrationSection.MasterData);
		AddIf(authorization, ApplicationPermission.MasterDataView, "Warehouses & Locations", AdministrationSection.Warehouses);
		AddIf(authorization, ApplicationPermission.SuppliersView, "Suppliers", AdministrationSection.Suppliers);
		AddIf(authorization, ApplicationPermission.UsersView, "Users", AdministrationSection.Users);
		AddIf(authorization, ApplicationPermission.RolesView, "Roles", AdministrationSection.Roles);
		AddIf(authorization, ApplicationPermission.ImportManage, "Import", AdministrationSection.Import);
		AddIf(authorization, ApplicationPermission.AuditLogView, "Audit Log", AdministrationSection.AuditLog);
		AddIf(authorization, ApplicationPermission.DatabaseView, "Database", AdministrationSection.Database);
		if (authorization.HasPermission(ApplicationPermission.AdministrationView))
			Add("About", AdministrationSection.About, HelpService.FallbackTopicId);
		SetSelection(NavigationItems.FirstOrDefault());
	}

	public string Title => "Administration";
	public ObservableCollection<NavigationItem> NavigationItems { get; } = [];
	public ObservableCollection<NavigationItem> Pages => NavigationItems;
	public Func<BaseViewModel?, bool>? NavigationGuard { get; set; }
	public NavigationItem? SelectedPage
	{
		get => SelectedNavigationItem;
		set => SelectedNavigationItem = value;
	}

	public NavigationItem? SelectedNavigationItem
	{
		get => _selectedNavigationItem;
		set
		{
			if (value is null || _selectedNavigationItem == value) return;
			_ = NavigateAsync(value);
		}
	}

	public BaseViewModel? CurrentViewModel
	{
		get => _currentViewModel;
		private set
		{
			if (_currentViewModel == value) return;
			_currentViewModel = value;
			OnPropertyChanged();
		}
	}

	public string HelpTopicId => SelectedNavigationItem?.HelpTopicId ?? HelpService.FallbackTopicId;

	public Task ActivateAsync(CancellationToken cancellationToken = default) => ActivateCurrentAsync(false, cancellationToken);
	public Task RefreshAsync(CancellationToken cancellationToken = default) => ActivateCurrentAsync(true, cancellationToken);

	public async Task NavigateToAsync(AdministrationSection section, CancellationToken cancellationToken = default)
	{
		var target = NavigationItems.FirstOrDefault(item => item.Section is AdministrationSection value && value == section)
			?? throw new UnauthorizedAccessException("The requested administration page is not available.");
		await NavigateAsync(target, cancellationToken);
	}

	private async Task NavigateAsync(NavigationItem target, CancellationToken cancellationToken = default)
	{
		if (_selectedNavigationItem != target && NavigationGuard?.Invoke(CurrentViewModel) == false)
		{
			OnPropertyChanged(nameof(SelectedNavigationItem));
			OnPropertyChanged(nameof(SelectedPage));
			return;
		}

		_navigationCancellation?.Cancel();
		_navigationCancellation?.Dispose();
		var navigation = cancellationToken.CanBeCanceled
			? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
			: new CancellationTokenSource();
		_navigationCancellation = navigation;
		SetSelection(target);
		try { await ActivateCurrentAsync(false, navigation.Token); }
		catch (OperationCanceledException) when (navigation.IsCancellationRequested) { }
		catch (Exception exception) { FailOperation(exception, $"{target.Name} could not be loaded"); }
	}

	private Task ActivateCurrentAsync(bool refresh, CancellationToken cancellationToken)
	{
		if (SelectedNavigationItem?.Section is not AdministrationSection section) return Task.CompletedTask;
		var state = _loadStates[section];
		return refresh ? state.RefreshAsync(LoadCurrentViewModelAsync, cancellationToken) : state.ActivateAsync(LoadCurrentViewModelAsync, cancellationToken);
	}

	private void SetSelection(NavigationItem? target)
	{
		_selectedNavigationItem = target;
		OnPropertyChanged(nameof(SelectedNavigationItem));
		OnPropertyChanged(nameof(SelectedPage));
		CurrentViewModel = target?.Section is AdministrationSection section ? ViewModelFor(section) : null;
		OnPropertyChanged(nameof(HelpTopicId));
	}

	private BaseViewModel ViewModelFor(AdministrationSection section) => section switch
	{
		AdministrationSection.Import => _importViewModel,
		AdministrationSection.MasterData => _masterDataViewModel,
		AdministrationSection.Warehouses => _masterDataViewModel.WarehouseStructureViewModel,
		AdministrationSection.Suppliers => _masterDataViewModel.SupplierViewModel,
		AdministrationSection.Users => _userViewModel,
		AdministrationSection.Roles => _roleViewModel,
		AdministrationSection.Database => _databaseSettingsViewModel,
		AdministrationSection.AuditLog => _auditLogViewModel,
		AdministrationSection.About => _aboutViewModel,
		_ => _aboutViewModel
	};

	private Task LoadCurrentViewModelAsync(CancellationToken cancellationToken) => CurrentViewModel switch
	{
		MasterDataViewModel masterData => masterData.ActivateAsync(cancellationToken),
		WarehouseStructureViewModel warehouses => warehouses.LoadAsync(cancellationToken),
		SupplierViewModel suppliers => suppliers.LoadAsync(cancellationToken),
		UserViewModel users => users.LoadUsersAsync(cancellationToken),
		RoleViewModel roles => roles.LoadAsync(cancellationToken),
		DatabaseSettingsViewModel database => database.LoadAsync(cancellationToken),
		AuditLogViewModel auditLog => auditLog.LoadAsync(cancellationToken),
		_ => Task.CompletedTask
	};

	private void AddIf(IAuthorizationService authorization, ApplicationPermission permission, string name, AdministrationSection section)
	{
		if (authorization.HasPermission(permission)) Add(name, section, TopicFor(section));
	}

	private void Add(string name, AdministrationSection section, string helpTopicId)
	{
		NavigationItems.Add(new NavigationItem { Name = name, Section = section, HelpTopicId = helpTopicId });
		_loadStates.Add(section, new NavigationLoadState());
	}

	private static string TopicFor(AdministrationSection section) => section switch
	{
		AdministrationSection.Users or AdministrationSection.Roles => "administration.users",
		AdministrationSection.Database => "administration.database",
		AdministrationSection.AuditLog => "administration.audit-log",
		AdministrationSection.MasterData => "inventory.items",
		AdministrationSection.Warehouses => "warehouse.transfers",
		AdministrationSection.Suppliers => "purchasing.purchase-orders",
		_ => HelpService.FallbackTopicId
	};

	public void Dispose()
	{
		_navigationCancellation?.Cancel();
		_navigationCancellation?.Dispose();
		foreach (var state in _loadStates.Values) state.Dispose();
		_masterDataViewModel.Dispose();
		if (_userViewModel is IDisposable users) users.Dispose();
		if (_roleViewModel is IDisposable roles) roles.Dispose();
		if (_databaseSettingsViewModel is IDisposable database) database.Dispose();
		if (_auditLogViewModel is IDisposable audit) audit.Dispose();
	}
}
