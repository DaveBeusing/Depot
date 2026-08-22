// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using Depot.ViewModels;

namespace Depot.Controls;

public sealed class WorkspaceTabClosingEventArgs(ShellNavigationItem item) : EventArgs
{
	public ShellNavigationItem Item { get; } = item;
	public bool Cancel { get; set; }
}

public sealed class WorkspaceTabControl : TabControl
{
	private const string WelcomeTabKey = "__welcome__";
	private ShellNavigationItem? _welcomeItem;

	public static readonly DependencyProperty ActiveItemProperty = DependencyProperty.Register(
		nameof(ActiveItem), typeof(ShellNavigationItem), typeof(WorkspaceTabControl),
		new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnActiveItemChanged));

	public static readonly RoutedCommand CloseTabCommand = new(nameof(CloseTabCommand), typeof(WorkspaceTabControl));
	public static readonly RoutedCommand CloseOtherTabsCommand = new(nameof(CloseOtherTabsCommand), typeof(WorkspaceTabControl));
	public static readonly RoutedCommand CloseTabsToRightCommand = new(nameof(CloseTabsToRightCommand), typeof(WorkspaceTabControl));

	public WorkspaceTabControl()
	{
		CommandBindings.Add(new CommandBinding(CloseTabCommand, OnCloseTabExecuted, OnCloseTabCanExecute));
		CommandBindings.Add(new CommandBinding(CloseOtherTabsCommand, OnCloseOtherTabsExecuted, OnCloseOtherTabsCanExecute));
		CommandBindings.Add(new CommandBinding(CloseTabsToRightCommand, OnCloseTabsToRightExecuted, OnCloseTabsToRightCanExecute));
	}

	public event EventHandler<WorkspaceTabClosingEventArgs>? TabClosing;

	public ShellNavigationItem? ActiveItem
	{
		get => (ShellNavigationItem?)GetValue(ActiveItemProperty);
		set => SetValue(ActiveItemProperty, value);
	}

	public ShellNavigationItem OpenOrActivate(ShellNavigationItem item)
	{
		ArgumentNullException.ThrowIfNull(item);
		if (string.Equals(item.TabKey, WelcomeTabKey, StringComparison.OrdinalIgnoreCase)) return item;
		var existing = Items.OfType<ShellNavigationItem>().FirstOrDefault(candidate =>
			string.Equals(candidate.TabKey, item.TabKey, StringComparison.OrdinalIgnoreCase));
		if (existing is not null)
		{
			if (!ReferenceEquals(existing, item)) item.Dispose();
			SelectedItem = existing;
			if (!ReferenceEquals(ActiveItem, existing)) SetCurrentValue(ActiveItemProperty, existing);
			return existing;
		}

		Items.Add(item);
		SelectedItem = item;
		if (!ReferenceEquals(ActiveItem, item)) SetCurrentValue(ActiveItemProperty, item);
		return item;
	}

	public void CloseActiveTab()
	{
		if (SelectedItem is ShellNavigationItem item) Close(item);
	}

	public void SelectRelativeTab(int offset)
	{
		if (Items.Count < 2) return;
		var index = SelectedIndex < 0 ? 0 : SelectedIndex;
		index = (index + offset + Items.Count) % Items.Count;
		SelectedIndex = index;
		if (SelectedItem is ShellNavigationItem item) SetCurrentValue(ActiveItemProperty, item);
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
		if (e.ChangedButton != MouseButton.Middle) return;
		var tab = FindAncestor<TabItem>(e.OriginalSource as DependencyObject);
		if (tab?.DataContext is not ShellNavigationItem item || !Items.Contains(item)) return;
		Close(item);
		e.Handled = true;
	}

	private static void OnActiveItemChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
	{
		if (dependencyObject is not WorkspaceTabControl control || e.NewValue is not ShellNavigationItem item) return;
		if (string.Equals(item.TabKey, WelcomeTabKey, StringComparison.OrdinalIgnoreCase)) return;
		control.OpenOrActivate(item);
	}

	private void OnCloseTabCanExecute(object sender, CanExecuteRoutedEventArgs e)
	{
		e.CanExecute = e.Parameter is ShellNavigationItem item && Items.Contains(item);
		e.Handled = true;
	}

	private void OnCloseTabExecuted(object sender, ExecutedRoutedEventArgs e)
	{
		if (e.Parameter is ShellNavigationItem item) Close(item);
		e.Handled = true;
	}

	private void OnCloseOtherTabsCanExecute(object sender, CanExecuteRoutedEventArgs e)
	{
		e.CanExecute = e.Parameter is ShellNavigationItem item && Items.Contains(item) && Items.Count > 1;
		e.Handled = true;
	}

	private void OnCloseOtherTabsExecuted(object sender, ExecutedRoutedEventArgs e)
	{
		if (e.Parameter is not ShellNavigationItem keep) return;
		foreach (var item in Items.OfType<ShellNavigationItem>().Where(item => !ReferenceEquals(item, keep)).ToArray()) Close(item);
		if (Items.Contains(keep))
		{
			SelectedItem = keep;
			SetCurrentValue(ActiveItemProperty, keep);
		}
		e.Handled = true;
	}

	private void OnCloseTabsToRightCanExecute(object sender, CanExecuteRoutedEventArgs e)
	{
		if (e.Parameter is not ShellNavigationItem item) { e.CanExecute = false; return; }
		var index = Items.IndexOf(item);
		e.CanExecute = index >= 0 && index < Items.Count - 1;
		e.Handled = true;
	}

	private void OnCloseTabsToRightExecuted(object sender, ExecutedRoutedEventArgs e)
	{
		if (e.Parameter is not ShellNavigationItem item) return;
		var index = Items.IndexOf(item);
		foreach (var candidate in Items.OfType<ShellNavigationItem>().Skip(index + 1).ToArray()) Close(candidate);
		if (Items.Contains(item))
		{
			SelectedItem = item;
			SetCurrentValue(ActiveItemProperty, item);
		}
		e.Handled = true;
	}

	private void Close(ShellNavigationItem item)
	{
		var index = Items.IndexOf(item);
		if (index < 0 || !CanClose(item)) return;
		var wasSelected = ReferenceEquals(SelectedItem, item);
		Items.Remove(item);
		if (item.IsDocument) item.Dispose();

		if (Items.Count == 0)
		{
			SelectedItem = null;
			SetCurrentValue(ActiveItemProperty, null);
			ShowWelcome();
			return;
		}

		if (!wasSelected) return;
		var nextIndex = index < Items.Count ? index : Items.Count - 1;
		if (Items[nextIndex] is ShellNavigationItem next)
		{
			SelectedItem = next;
			SetCurrentValue(ActiveItemProperty, next);
		}
	}

	private void ShowWelcome()
	{
		if (DataContext is not MainViewModel viewModel) return;
		_welcomeItem ??= new ShellNavigationItem(
			"Welcome",
			string.Empty,
			() => viewModel.WelcomeViewModel,
			(_, _) => Task.CompletedTask,
			"getting-started.first-login",
			ownsLoadState: false,
			tabKey: WelcomeTabKey,
			ownsContent: false);
		_ = viewModel.NavigateAsync(_welcomeItem);
	}

	private bool CanClose(ShellNavigationItem item)
	{
		var args = new WorkspaceTabClosingEventArgs(item);
		TabClosing?.Invoke(this, args);
		return !args.Cancel;
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
