// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using Depot.ViewModels;

namespace Depot.Controls;

public sealed class WorkspaceTabControl : TabControl
{
	public static readonly DependencyProperty ActiveItemProperty = DependencyProperty.Register(
		nameof(ActiveItem),
		typeof(ShellNavigationItem),
		typeof(WorkspaceTabControl),
		new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnActiveItemChanged));

	public static readonly RoutedCommand CloseTabCommand = new(nameof(CloseTabCommand), typeof(WorkspaceTabControl));

	public WorkspaceTabControl()
	{
		CommandBindings.Add(new CommandBinding(CloseTabCommand, OnCloseTabExecuted, OnCloseTabCanExecute));
	}

	public ShellNavigationItem? ActiveItem
	{
		get => (ShellNavigationItem?)GetValue(ActiveItemProperty);
		set => SetValue(ActiveItemProperty, value);
	}

	protected override void OnSelectionChanged(SelectionChangedEventArgs e)
	{
		base.OnSelectionChanged(e);
		if (SelectedItem is ShellNavigationItem selected && !ReferenceEquals(selected, ActiveItem))
			SetCurrentValue(ActiveItemProperty, selected);
	}

	protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
	{
		base.OnPreviewMouseDown(e);
		if (e.ChangedButton != MouseButton.Middle || Items.Count <= 1) return;
		var tab = FindAncestor<TabItem>(e.OriginalSource as DependencyObject);
		if (tab?.DataContext is not ShellNavigationItem item || !Items.Contains(item)) return;
		Close(item);
		e.Handled = true;
	}

	private static void OnActiveItemChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
	{
		if (dependencyObject is not WorkspaceTabControl control || e.NewValue is not ShellNavigationItem item) return;
		if (!control.Items.Contains(item)) control.Items.Add(item);
		control.SelectedItem = item;
	}

	private void OnCloseTabCanExecute(object sender, CanExecuteRoutedEventArgs e)
	{
		e.CanExecute = e.Parameter is ShellNavigationItem item && Items.Contains(item) && Items.Count > 1;
		e.Handled = true;
	}

	private void OnCloseTabExecuted(object sender, ExecutedRoutedEventArgs e)
	{
		if (e.Parameter is ShellNavigationItem item) Close(item);
		e.Handled = true;
	}

	private void Close(ShellNavigationItem item)
	{
		var index = Items.IndexOf(item);
		if (index < 0 || Items.Count <= 1) return;
		var wasSelected = ReferenceEquals(SelectedItem, item);
		Items.Remove(item);
		if (!wasSelected) return;
		var nextIndex = index < Items.Count ? index : Items.Count - 1;
		if (Items[nextIndex] is ShellNavigationItem next)
		{
			SelectedItem = next;
			SetCurrentValue(ActiveItemProperty, next);
		}
	}

	private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
	{
		while (current is not null)
		{
			if (current is T match) return match;
			current = VisualTreeHelper.GetParent(current);
		}
		return null;
	}
}
