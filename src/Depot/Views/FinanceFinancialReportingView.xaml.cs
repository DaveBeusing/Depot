// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using Depot.Models;
using Depot.ViewModels;

namespace Depot.Views;

public partial class FinanceFinancialReportingView : UserControl
{
	private Point _mappingDragStart;
	private FinanceReportingMappingProjectionRow? _mappingDragRow;

	public FinanceFinancialReportingView() => InitializeComponent();

	private void OnMappingAccountMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		_mappingDragStart = e.GetPosition(MappingAccountList);
		_mappingDragRow = ItemsControl.ContainerFromElement(MappingAccountList, e.OriginalSource as DependencyObject) is ListBoxItem item
			? item.DataContext as FinanceReportingMappingProjectionRow
			: null;
	}

	private void OnMappingAccountMouseMove(object sender, MouseEventArgs e)
	{
		if (e.LeftButton != MouseButtonState.Pressed || _mappingDragRow is not { } row)
			return;
		var position = e.GetPosition(MappingAccountList);
		if (Math.Abs(position.X - _mappingDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
			Math.Abs(position.Y - _mappingDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
			return;
		DragDrop.DoDragDrop(MappingAccountList, row, DragDropEffects.Copy);
		_mappingDragRow = null;
	}

	private void OnMappingTargetDrop(object sender, DragEventArgs e)
	{
		if (DataContext is not FinanceFinancialReportingViewModel viewModel ||
			!e.Data.GetDataPresent(typeof(FinanceReportingMappingProjectionRow)) ||
			e.Data.GetData(typeof(FinanceReportingMappingProjectionRow)) is not FinanceReportingMappingProjectionRow row ||
			ItemsControl.ContainerFromElement(MappingTargetList, e.OriginalSource as DependencyObject) is not ListBoxItem targetItem ||
			targetItem.DataContext is not FinanceReportingMappingTarget target)
		{
			e.Effects = DragDropEffects.None;
			return;
		}
		e.Effects = viewModel.ApplyMappingDesignerTarget(row, target) ? DragDropEffects.Copy : DragDropEffects.None;
		e.Handled = true;
	}
}
