// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using Depot.Models;
using Depot.ViewModels;

namespace Depot.Views;

public partial class FinanceBankingView : UserControl
{
	private Point _reconciliationCandidateDragStart;
	private FinanceBankReconciliationCandidate? _reconciliationDraggedCandidate;

	public FinanceBankingView()
	{
		InitializeComponent();
	}

	private void OnReconciliationCandidateMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		_reconciliationCandidateDragStart = e.GetPosition(ReconciliationCandidateGrid);
		_reconciliationDraggedCandidate = ItemsControl.ContainerFromElement(ReconciliationCandidateGrid, e.OriginalSource as DependencyObject) is DataGridRow row
			? row.DataContext as FinanceBankReconciliationCandidate
			: null;
	}

	private void OnReconciliationCandidateMouseMove(object sender, MouseEventArgs e)
	{
		if (e.LeftButton != MouseButtonState.Pressed || _reconciliationDraggedCandidate is not { } candidate)
			return;
		var position = e.GetPosition(ReconciliationCandidateGrid);
		if (Math.Abs(position.X - _reconciliationCandidateDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
			Math.Abs(position.Y - _reconciliationCandidateDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
			return;
		DragDrop.DoDragDrop(ReconciliationCandidateGrid, candidate, DragDropEffects.Copy);
		_reconciliationDraggedCandidate = null;
	}

	private void OnReconciliationPreviewDrop(object sender, DragEventArgs e)
	{
		if (DataContext is not FinanceBankingViewModel viewModel ||
			!e.Data.GetDataPresent(typeof(FinanceBankReconciliationCandidate)) ||
			e.Data.GetData(typeof(FinanceBankReconciliationCandidate)) is not FinanceBankReconciliationCandidate candidate)
		{
			e.Effects = DragDropEffects.None;
			return;
		}
		e.Effects = viewModel.UseReconciliationCandidate(candidate) ? DragDropEffects.Copy : DragDropEffects.None;
		e.Handled = true;
	}
}
