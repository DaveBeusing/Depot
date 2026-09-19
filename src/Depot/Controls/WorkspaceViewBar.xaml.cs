// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

using Depot.Models;
using Depot.Services;
using Depot.ViewModels;

namespace Depot.Controls;

public enum WorkspaceFilterKind
{
	Text = 1,
	NullableBooleanSelection = 2,
	SelectedValue = 3
}

public static class WorkspaceViewPersistence
{
	public static readonly DependencyProperty WorkspaceIdProperty = DependencyProperty.RegisterAttached(
		"WorkspaceId", typeof(string), typeof(WorkspaceViewPersistence), new PropertyMetadata(null));
	public static readonly DependencyProperty ColumnIdProperty = DependencyProperty.RegisterAttached(
		"ColumnId", typeof(string), typeof(WorkspaceViewPersistence), new PropertyMetadata(null));
	public static readonly DependencyProperty FilterIdProperty = DependencyProperty.RegisterAttached(
		"FilterId", typeof(string), typeof(WorkspaceViewPersistence), new PropertyMetadata(null));
	public static readonly DependencyProperty FilterKindProperty = DependencyProperty.RegisterAttached(
		"FilterKind", typeof(WorkspaceFilterKind), typeof(WorkspaceViewPersistence), new PropertyMetadata(WorkspaceFilterKind.Text));

	public static void SetWorkspaceId(DependencyObject element, string value) => element.SetValue(WorkspaceIdProperty, value);
	public static string? GetWorkspaceId(DependencyObject element) => element.GetValue(WorkspaceIdProperty) as string;
	public static void SetColumnId(DependencyObject element, string value) => element.SetValue(ColumnIdProperty, value);
	public static string? GetColumnId(DependencyObject element) => element.GetValue(ColumnIdProperty) as string;
	public static void SetFilterId(DependencyObject element, string value) => element.SetValue(FilterIdProperty, value);
	public static string? GetFilterId(DependencyObject element) => element.GetValue(FilterIdProperty) as string;
	public static void SetFilterKind(DependencyObject element, WorkspaceFilterKind value) => element.SetValue(FilterKindProperty, value);
	public static WorkspaceFilterKind GetFilterKind(DependencyObject element) => (WorkspaceFilterKind)element.GetValue(FilterKindProperty);
}

public partial class WorkspaceViewBar : UserControl
{
	public static readonly DependencyProperty WorkspaceIdProperty = DependencyProperty.Register(
		nameof(WorkspaceId), typeof(string), typeof(WorkspaceViewBar), new PropertyMetadata(null));

	private WorkspaceViewsViewModel? _viewModel;
	private WorkspaceSurface? _surface;

	public WorkspaceViewBar()
	{
		InitializeComponent();
		Loaded += OnLoaded;
		Unloaded += OnUnloaded;
	}

	public string WorkspaceId
	{
		get => (string)GetValue(WorkspaceIdProperty);
		set => SetValue(WorkspaceIdProperty, value);
	}

	private async void OnLoaded(object sender, RoutedEventArgs e)
	{
		if (_viewModel is not null || string.IsNullOrWhiteSpace(WorkspaceId)) return;
		try
		{
			await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
			var parent = VisualTreeHelper.GetParent(this);
			var root = parent is null ? null : FindAncestor<UserControl>(parent);
			if (root is null)
			{
				StatusText.Text = "Saved views are unavailable for this workspace.";
				return;
			}
			_surface = WorkspaceSurface.TryCreate(root, WorkspaceId);
			if (_surface is null)
			{
				StatusText.Text = "Saved views are unavailable for this workspace.";
				return;
			}
			_viewModel = new WorkspaceViewsViewModel(WorkspaceViewRuntime.Current, WorkspaceId, _surface);
			DataContext = _viewModel;
			await _viewModel.InitializeAsync();
		}
		catch (Exception exception)
		{
			StatusText.Text = $"Saved views unavailable: {exception.Message}";
		}
	}

	private void OnUnloaded(object sender, RoutedEventArgs e)
	{
		_viewModel?.Dispose();
		_viewModel = null;
		_surface = null;
		DataContext = null;
	}

	private void OnDensitySelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (_surface is not null && e.AddedItems.OfType<WorkspaceGridDensity>().FirstOrDefault() is var density && Enum.IsDefined(density))
			_surface.SetDensity(density);
	}

	private void OnColumnsClick(object sender, RoutedEventArgs e)
	{
		if (_surface is null || sender is not FrameworkElement target) return;
		_surface.ShowColumnMenu(target);
	}

	private static T? FindAncestor<T>(DependencyObject start) where T : DependencyObject
	{
		for (DependencyObject? current = start; current is not null; current = VisualTreeHelper.GetParent(current))
			if (current is T typed) return typed;
		return null;
	}

	private sealed class WorkspaceSurface : IWorkspaceViewSurface
	{
		private readonly IReadOnlyList<DataGrid> _grids;
		private readonly DataGrid _primaryGrid;
		private readonly IReadOnlyList<FrameworkElement> _filters;
		private readonly IReadOnlyDictionary<DataGrid, IReadOnlyList<ColumnBaseline>> _columns;
		private readonly IReadOnlyList<FilterBaseline> _filterBaseline;
		private readonly IReadOnlyDictionary<DataGrid, object> _rowHeightLocal;
		private readonly IReadOnlyDictionary<DataGrid, object> _rowStyleLocal;

		private WorkspaceSurface(IReadOnlyList<DataGrid> grids, IReadOnlyList<FrameworkElement> filters)
		{
			_grids = grids;
			_primaryGrid = grids[0];
			_filters = filters;
			_columns = grids.ToDictionary(
				grid => grid,
				grid => (IReadOnlyList<ColumnBaseline>)grid.Columns.Select(column => new ColumnBaseline(
					column,
					column.DisplayIndex,
					column.Width,
					column.Visibility,
					column.SortDirection)).ToArray());
			_filterBaseline = filters.Select(filter => new FilterBaseline(filter, CaptureFilter(filter))).ToArray();
			_rowHeightLocal = grids.ToDictionary(grid => grid, grid => grid.ReadLocalValue(DataGrid.RowHeightProperty));
			_rowStyleLocal = grids.ToDictionary(grid => grid, grid => grid.ReadLocalValue(DataGrid.RowStyleProperty));
		}

		public static WorkspaceSurface? TryCreate(DependencyObject root, string workspaceId)
		{
			var descendants = Enumerate(root).OfType<FrameworkElement>().ToArray();
			var grids = descendants.OfType<DataGrid>().Where(candidate =>
				string.Equals(WorkspaceViewPersistence.GetWorkspaceId(candidate), workspaceId, StringComparison.Ordinal)).ToArray();
			if (grids.Length == 0) return null;
			var filters = descendants.Where(candidate =>
				string.Equals(WorkspaceViewPersistence.GetWorkspaceId(candidate), workspaceId, StringComparison.Ordinal) &&
				!string.IsNullOrWhiteSpace(WorkspaceViewPersistence.GetFilterId(candidate))).ToArray();
			return new WorkspaceSurface(grids, filters);
		}

		public WorkspaceViewDefinition Capture(WorkspaceGridDensity density)
		{
			var columns = _primaryGrid.Columns
				.Select(column => (Column: column, Id: WorkspaceViewPersistence.GetColumnId(column)))
				.Where(entry => !string.IsNullOrWhiteSpace(entry.Id))
				.Select(entry => new WorkspaceColumnPreference(
					entry.Id!, entry.Column.DisplayIndex, Math.Round(Math.Max(40d, entry.Column.ActualWidth), 2), entry.Column.Visibility == Visibility.Visible))
				.ToList();
			var sorts = _primaryGrid.Columns
				.Select(column => (Column: column, Id: WorkspaceViewPersistence.GetColumnId(column)))
				.Where(entry => !string.IsNullOrWhiteSpace(entry.Id) && entry.Column.SortDirection is not null)
				.OrderBy(entry => entry.Column.DisplayIndex)
				.Select((entry, priority) => new WorkspaceSortPreference(
					entry.Id!, entry.Column.SortDirection == ListSortDirection.Ascending ? WorkspaceSortDirection.Ascending : WorkspaceSortDirection.Descending, priority))
				.ToList();
			var filters = _filters.Select(filter => new WorkspaceFilterPreference(
				WorkspaceViewPersistence.GetFilterId(filter)!, CaptureFilter(filter))).ToList();
			return new WorkspaceViewDefinition { GridDensity = density, Columns = columns, Sorts = sorts, Filters = filters };
		}

		public void Apply(WorkspaceViewDefinition definition)
		{
			foreach (var grid in _grids)
			{
				var byId = grid.Columns
					.Select(column => (Column: column, Id: WorkspaceViewPersistence.GetColumnId(column)))
					.Where(entry => !string.IsNullOrWhiteSpace(entry.Id))
					.ToDictionary(entry => entry.Id!, entry => entry.Column, StringComparer.Ordinal);

				foreach (var preference in definition.Columns)
				{
					if (!byId.TryGetValue(preference.ColumnId, out var column)) continue;
					column.Visibility = preference.IsVisible ? Visibility.Visible : Visibility.Collapsed;
					if (double.IsFinite(preference.Width) && preference.Width is >= 40 and <= 2000)
						column.Width = new DataGridLength(preference.Width);
				}

				var preferredIds = definition.Columns.Select(column => column.ColumnId).ToHashSet(StringComparer.Ordinal);
				var ordered = definition.Columns.OrderBy(column => column.DisplayIndex)
					.Select(column => byId.GetValueOrDefault(column.ColumnId))
					.Where(column => column is not null)
					.Cast<DataGridColumn>()
					.Concat(grid.Columns.Where(column => !preferredIds.Contains(WorkspaceViewPersistence.GetColumnId(column) ?? string.Empty)))
					.Distinct()
					.ToArray();
				for (var index = 0; index < ordered.Length; index++) ordered[index].DisplayIndex = index;

				ApplySorts(grid, definition.Sorts, byId);
			}

			foreach (var preference in definition.Filters)
			{
				var filter = _filters.FirstOrDefault(candidate => string.Equals(
					WorkspaceViewPersistence.GetFilterId(candidate), preference.FilterId, StringComparison.Ordinal));
				if (filter is not null) ApplyFilter(filter, preference.Value);
			}
			SetDensity(definition.GridDensity);
		}

		public void ResetCanonical()
		{
			foreach (var grid in _grids)
			{
				var baselines = _columns[grid];
				foreach (var baseline in baselines)
				{
					baseline.Column.Visibility = baseline.Visibility;
					baseline.Column.Width = baseline.Width;
					baseline.Column.SortDirection = baseline.SortDirection;
				}
				foreach (var baseline in baselines.OrderBy(column => column.DisplayIndex)) baseline.Column.DisplayIndex = baseline.DisplayIndex;
				if (grid.ItemsSource is not null) CollectionViewSource.GetDefaultView(grid.ItemsSource)?.SortDescriptions.Clear();
				if (_rowHeightLocal[grid] == DependencyProperty.UnsetValue) grid.ClearValue(DataGrid.RowHeightProperty); else grid.SetValue(DataGrid.RowHeightProperty, _rowHeightLocal[grid]);
				if (_rowStyleLocal[grid] == DependencyProperty.UnsetValue) grid.ClearValue(DataGrid.RowStyleProperty); else grid.SetValue(DataGrid.RowStyleProperty, _rowStyleLocal[grid]);
			}
			foreach (var baseline in _filterBaseline) ApplyFilter(baseline.Element, baseline.Value);
		}

		public void SetDensity(WorkspaceGridDensity density)
		{
			foreach (var grid in _grids)
			{
				switch (density)
				{
					case WorkspaceGridDensity.Compact:
						grid.RowHeight = 32;
						if (Application.Current?.TryFindResource("AppDataGridCompactRowStyle") is Style compact) grid.RowStyle = compact;
						break;
					case WorkspaceGridDensity.Comfortable:
						grid.RowHeight = 44;
						if (Application.Current?.TryFindResource("AppDataGridRowStyle") is Style comfortable) grid.RowStyle = comfortable;
						break;
					default:
						grid.RowHeight = 36;
						if (Application.Current?.TryFindResource("AppDataGridRowStyle") is Style standard) grid.RowStyle = standard;
						break;
				}
			}
		}

		public void ShowColumnMenu(FrameworkElement target)
		{
			var menu = new ContextMenu { PlacementTarget = target, Placement = PlacementMode.Bottom };
			foreach (var column in _primaryGrid.Columns.Where(column => !string.IsNullOrWhiteSpace(WorkspaceViewPersistence.GetColumnId(column))).OrderBy(column => column.DisplayIndex))
			{
				var columnId = WorkspaceViewPersistence.GetColumnId(column)!;
				var item = new MenuItem
				{
					Header = Convert.ToString(column.Header, CultureInfo.CurrentCulture) ?? columnId,
					IsCheckable = true,
					IsChecked = column.Visibility == Visibility.Visible,
					StaysOpenOnClick = true,
					Tag = columnId
				};
				item.Click += (_, _) =>
				{
					if (item.Tag is not string selectedId) return;
					if (!item.IsChecked && _primaryGrid.Columns.Count(candidate => candidate.Visibility == Visibility.Visible) <= 1)
					{
						item.IsChecked = true;
						return;
					}
					foreach (var grid in _grids)
					{
						var selected = grid.Columns.FirstOrDefault(candidate => string.Equals(
							WorkspaceViewPersistence.GetColumnId(candidate), selectedId, StringComparison.Ordinal));
						if (selected is not null) selected.Visibility = item.IsChecked ? Visibility.Visible : Visibility.Collapsed;
					}
				};
				menu.Items.Add(item);
			}
			menu.IsOpen = true;
		}

		private static void ApplySorts(DataGrid grid, IReadOnlyList<WorkspaceSortPreference> sorts, IReadOnlyDictionary<string, DataGridColumn> byId)
		{
			foreach (var column in grid.Columns) column.SortDirection = null;
			if (grid.ItemsSource is null) return;
			var view = CollectionViewSource.GetDefaultView(grid.ItemsSource);
			if (view is null) return;
			view.SortDescriptions.Clear();
			foreach (var sort in sorts.OrderBy(sort => sort.Priority))
			{
				if (!byId.TryGetValue(sort.ColumnId, out var column) || string.IsNullOrWhiteSpace(column.SortMemberPath)) continue;
				var direction = sort.Direction == WorkspaceSortDirection.Ascending ? ListSortDirection.Ascending : ListSortDirection.Descending;
				column.SortDirection = direction;
				view.SortDescriptions.Add(new SortDescription(column.SortMemberPath, direction));
			}
		}

		private static string? CaptureFilter(FrameworkElement filter)
		{
			if (filter is TextBox textBox) return textBox.Text;
			if (filter is Selector selector)
			{
				return WorkspaceViewPersistence.GetFilterKind(filter) switch
				{
					WorkspaceFilterKind.NullableBooleanSelection => CaptureNullableBoolean(selector.SelectedItem),
					WorkspaceFilterKind.SelectedValue => Convert.ToString(selector.SelectedValue, CultureInfo.InvariantCulture),
					_ => Convert.ToString(selector.SelectedValue, CultureInfo.InvariantCulture)
				};
			}
			return null;
		}

		private static void ApplyFilter(FrameworkElement filter, string? value)
		{
			if (filter is TextBox textBox)
			{
				textBox.SetCurrentValue(TextBox.TextProperty, value ?? string.Empty);
				return;
			}
			if (filter is not Selector selector) return;
			if (WorkspaceViewPersistence.GetFilterKind(filter) == WorkspaceFilterKind.NullableBooleanSelection)
			{
				var expected = ParseNullableBoolean(value);
				foreach (var item in selector.Items)
				{
					var property = item?.GetType().GetProperty("IsActive");
					if (property is null) continue;
					var raw = property.GetValue(item);
					bool? actual = raw is null ? null : Convert.ToBoolean(raw, CultureInfo.InvariantCulture);
					if (actual == expected)
					{
						selector.SetCurrentValue(Selector.SelectedItemProperty, item);
						return;
					}
				}
				return;
			}
			if (selector is ComboBox comboBox) comboBox.SetCurrentValue(ComboBox.SelectedValueProperty, value);
		}

		private static string CaptureNullableBoolean(object? item)
		{
			var property = item?.GetType().GetProperty("IsActive");
			var raw = property?.GetValue(item);
			bool? value = raw is null ? null : Convert.ToBoolean(raw, CultureInfo.InvariantCulture);
			return value switch { true => "true", false => "false", null => "all" };
		}

		private static bool? ParseNullableBoolean(string? value) => value?.ToLowerInvariant() switch
		{
			"true" => true,
			"false" => false,
			_ => null
		};

		private static IEnumerable<DependencyObject> Enumerate(DependencyObject root)
		{
			yield return root;
			var count = VisualTreeHelper.GetChildrenCount(root);
			for (var index = 0; index < count; index++)
			{
				foreach (var descendant in Enumerate(VisualTreeHelper.GetChild(root, index))) yield return descendant;
			}
		}

		private sealed record ColumnBaseline(DataGridColumn Column, int DisplayIndex, DataGridLength Width, Visibility Visibility, ListSortDirection? SortDirection);
		private sealed record FilterBaseline(FrameworkElement Element, string? Value);
	}
}
