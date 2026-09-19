// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

using Depot.DocumentRendering;
using Depot.ViewModels.Administration;

namespace Depot.Views.Administration;

public partial class DocumentTemplateDesignerView : UserControl
{
	private Point? _lastDragPoint;
	private bool _dragTransactionActive;
	private bool _resizeTransactionActive;

	public DocumentTemplateDesignerView()
	{
		InitializeComponent();
	}

	private DocumentTemplateDesignerViewModel? ViewModel => DataContext as DocumentTemplateDesignerViewModel;

	private void AddElement_Click(object sender, RoutedEventArgs e)
	{
		if (sender is Button { Tag: string value } && Enum.TryParse<DocumentTemplateElementType>(value, out var type))
			ViewModel?.AddElement(type);
	}

	private void SecondaryActions_Click(object sender, RoutedEventArgs e)
	{
		if (sender is not Button { ContextMenu: { } menu } button) return;
		menu.PlacementTarget = button;
		menu.IsOpen = true;
	}

	private void CanvasElements_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		ViewModel?.SetSelectedElements(CanvasElements.SelectedItems.Cast<DocumentDesignerElementViewModel>());
	}

	private void CanvasElements_PreviewKeyDown(object sender, KeyEventArgs e)
	{
		var viewModel = ViewModel;
		if (viewModel is null) return;
		if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.Z)
		{
			if (viewModel.UndoCommand.CanExecute(null)) viewModel.UndoCommand.Execute(null);
			e.Handled = true;
			return;
		}
		if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.Y)
		{
			if (viewModel.RedoCommand.CanExecute(null)) viewModel.RedoCommand.Execute(null);
			e.Handled = true;
			return;
		}
		if (e.Key == Key.Delete)
		{
			if (viewModel.DeleteElementsCommand.CanExecute(null)) viewModel.DeleteElementsCommand.Execute(null);
			e.Handled = true;
			return;
		}
		if (e.Key == Key.D && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
		{
			if (viewModel.DuplicateElementsCommand.CanExecute(null)) viewModel.DuplicateElementsCommand.Execute(null);
			e.Handled = true;
			return;
		}
		if (e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down)) return;

		var step = viewModel.SnapToGrid ? viewModel.GridSize : 1d;
		if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) step *= 10d;
		var (dx, dy) = e.Key switch
		{
			Key.Left => (-step, 0d),
			Key.Right => (step, 0d),
			Key.Up => (0d, -step),
			Key.Down => (0d, step),
			_ => (0d, 0d)
		};
		viewModel.MoveSelection(dx, dy);
		e.Handled = true;
	}

	private void CanvasElements_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		var viewModel = ViewModel;
		if (viewModel?.IsEditing != true || FindAncestor<Thumb>(e.OriginalSource as DependencyObject) is not null) return;

		var item = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
		if (item is null) return;
		if (!item.IsSelected && !Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
		{
			CanvasElements.SelectedItems.Clear();
			item.IsSelected = true;
		}

		_lastDragPoint = e.GetPosition(DesignerPage);
		viewModel.BeginEditTransaction();
		_dragTransactionActive = true;
		CanvasElements.CaptureMouse();
	}

	private void CanvasElements_PreviewMouseMove(object sender, MouseEventArgs e)
	{
		var viewModel = ViewModel;
		if (viewModel?.IsEditing != true || _lastDragPoint is not Point previous || e.LeftButton != MouseButtonState.Pressed) return;

		var current = e.GetPosition(DesignerPage);
		var dx = current.X - previous.X;
		var dy = current.Y - previous.Y;
		if (viewModel.SnapToGrid && Math.Abs(dx) < viewModel.GridSize && Math.Abs(dy) < viewModel.GridSize) return;

		viewModel.MoveSelection(dx, dy);
		_lastDragPoint = current;
		e.Handled = true;
	}

	private void CanvasElements_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
	{
		_lastDragPoint = null;
		if (_dragTransactionActive)
		{
			ViewModel?.EndEditTransaction();
			_dragTransactionActive = false;
		}
		if (CanvasElements.IsMouseCaptured) CanvasElements.ReleaseMouseCapture();
	}

	private void ResizeThumb_DragStarted(object sender, DragStartedEventArgs e)
	{
		var viewModel = ViewModel;
		if (viewModel?.IsEditing != true || sender is not Thumb { DataContext: DocumentDesignerElementViewModel element } thumb) return;

		var item = FindAncestor<ListBoxItem>(thumb);
		if (item is not null && !item.IsSelected)
		{
			CanvasElements.SelectedItems.Clear();
			item.IsSelected = true;
		}
		viewModel.SetSelectedElements([element]);
		viewModel.BeginEditTransaction();
		_resizeTransactionActive = true;
		e.Handled = true;
	}

	private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
	{
		var viewModel = ViewModel;
		if (viewModel?.IsEditing != true || sender is not Thumb { DataContext: DocumentDesignerElementViewModel element, Tag: string handle }) return;

		var scale = Math.Max(0.01d, viewModel.ZoomScale);
		viewModel.ResizeElement(element, handle, e.HorizontalChange / scale, e.VerticalChange / scale);
		e.Handled = true;
	}

	private void ResizeThumb_DragCompleted(object sender, DragCompletedEventArgs e)
	{
		if (_resizeTransactionActive)
		{
			ViewModel?.EndEditTransaction();
			_resizeTransactionActive = false;
		}
		e.Handled = true;
	}

	private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
	{
		while (source is not null)
		{
			if (source is T match) return match;
			source = VisualTreeHelper.GetParent(source);
		}
		return null;
	}
}
