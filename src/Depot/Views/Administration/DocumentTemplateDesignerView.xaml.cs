// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using Depot.DocumentRendering;
using Depot.ViewModels.Administration;

namespace Depot.Views.Administration;

public partial class DocumentTemplateDesignerView : UserControl
{
	private Point? _lastDragPoint;

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

	private void CanvasElements_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		ViewModel?.SetSelectedElements(CanvasElements.SelectedItems.Cast<DocumentDesignerElementViewModel>());
	}

	private void CanvasElements_PreviewKeyDown(object sender, KeyEventArgs e)
	{
		var viewModel = ViewModel;
		if (viewModel is null) return;
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
		if (ViewModel?.IsEditing != true) return;
		var item = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
		if (item is null) return;
		if (!item.IsSelected && !Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
		{
			CanvasElements.SelectedItems.Clear();
			item.IsSelected = true;
		}
		_lastDragPoint = e.GetPosition(DesignerPage);
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
		if (CanvasElements.IsMouseCaptured) CanvasElements.ReleaseMouseCapture();
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
