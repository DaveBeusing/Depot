// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using Depot.ViewModels;
using Depot.ViewModels.Administration;

namespace Depot.Views;

public partial class DashboardView : UserControl
{
	public DashboardView() => InitializeComponent();

	private async void OnDashboardActionClick(object sender, RoutedEventArgs e)
	{
		if (sender is not FrameworkElement { Tag: string target } || Window.GetWindow(this)?.DataContext is not MainViewModel viewModel) return;
		switch (target)
		{
			case "Approvals": await NavigateTopLevelAsync(viewModel, "Approvals"); break;
			case "Purchasing": await NavigateModulePageAsync(viewModel, "Purchasing", "Purchase Orders"); break;
			case "Warehouse": await NavigateModulePageAsync(viewModel, "Warehouse", "Inventory Counts"); break;
			case "Sales": await NavigateModulePageAsync(viewModel, "Sales", "Overview"); break;
			case "InventoryMovements": await NavigateModulePageAsync(viewModel, "Inventory", "Movements"); break;
			case "AdministrationUsers":
				await NavigateTopLevelAsync(viewModel, "Administration");
				await viewModel.AdministrationViewModel.NavigateToAsync(AdministrationSection.Users);
				break;
		}
	}

	private async void OnRecentMovementDoubleClick(object sender, MouseButtonEventArgs e)
	{
		if (Window.GetWindow(this)?.DataContext is MainViewModel viewModel) await NavigateModulePageAsync(viewModel, "Inventory", "Movements");
	}

	private static Task NavigateTopLevelAsync(MainViewModel viewModel, string name)
	{
		var item = viewModel.NavigationItems.FirstOrDefault(candidate => candidate.Name == name);
		return item is null ? Task.CompletedTask : viewModel.NavigateAsync(item);
	}

	private static async Task NavigateModulePageAsync(MainViewModel viewModel, string moduleName, string pageName)
	{
		var item = viewModel.NavigationItems.FirstOrDefault(candidate => candidate.Name == moduleName);
		if (item is null) return;
		if (item.Content is ShellModuleViewModel module && module.Pages.FirstOrDefault(candidate => candidate.Name == pageName) is { } page) module.SetSelectedPage(page);
		await viewModel.NavigateAsync(item);
	}
}
