// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using Depot.ViewModels;

namespace Depot.Views;

public partial class FinancePostingFlowDesignerView : UserControl
{
	private Point _dragStart;

	public FinancePostingFlowDesignerView()
	{
		InitializeComponent();
	}

	private void OnPaletteMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		_dragStart = e.GetPosition(PaletteList);
	}

	private void OnPaletteMouseMove(object sender, MouseEventArgs e)
	{
		if (e.LeftButton != MouseButtonState.Pressed ||
			PaletteList.SelectedItem is not FinancePostingFlowPaletteItem item)
			return;

		var position = e.GetPosition(PaletteList);
		if (Math.Abs(position.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
			Math.Abs(position.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
			return;

		DragDrop.DoDragDrop(PaletteList, item, DragDropEffects.Copy);
	}

	private void OnCanvasDrop(object sender, DragEventArgs e)
	{
		if (DataContext is not FinancePostingFlowDesignerViewModel viewModel ||
			!e.Data.GetDataPresent(typeof(FinancePostingFlowPaletteItem)) ||
			e.Data.GetData(typeof(FinancePostingFlowPaletteItem)) is not FinancePostingFlowPaletteItem item)
		{
			e.Effects = DragDropEffects.None;
			return;
		}

		e.Effects = viewModel.TryAddRule(item) ? DragDropEffects.Copy : DragDropEffects.None;
		e.Handled = true;
	}
}
