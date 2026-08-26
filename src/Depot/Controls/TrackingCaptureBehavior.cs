// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;

using Depot.Services;

namespace Depot.Controls;

/// <summary>
/// Adds one transient Serial / lot capture column to supported posting grids. Captured values remain
/// process-local until the corresponding posting transaction validates and persists them.
/// </summary>
public static class TrackingCaptureBehavior
{
	private const string ColumnHeader = "Serial / lot";
	private static bool _registered;

	public static void Register()
	{
		if (_registered) return;
		_registered = true;
		EventManager.RegisterClassHandler(typeof(DataGrid), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnGridLoaded));
	}

	private static void OnGridLoaded(object sender, RoutedEventArgs args)
	{
		if (sender is not DataGrid grid) return;
		grid.LoadingRow -= OnLoadingRow;
		grid.LoadingRow += OnLoadingRow;
		TryScheduleColumn(grid, grid.Items.Cast<object>().FirstOrDefault(item => item is not CollectionViewGroup));
	}

	private static void OnLoadingRow(object? sender, DataGridRowEventArgs args)
	{
		if (sender is DataGrid grid) TryScheduleColumn(grid, args.Row.Item);
	}

	private static void TryScheduleColumn(DataGrid grid, object? row)
	{
		if (row is null || grid.Columns.Any(column => string.Equals(column.Header?.ToString(), ColumnHeader, StringComparison.Ordinal))) return;
		var target = ResolveTarget(row.GetType());
		if (target is null) return;
		grid.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
		{
			if (grid.Columns.Any(column => string.Equals(column.Header?.ToString(), ColumnHeader, StringComparison.Ordinal))) return;
			grid.Columns.Add(CreateColumn(target.Value.Scope, target.Value.KeyProperty));
		});
	}

	private static DataGridTemplateColumn CreateColumn(string scope, string keyProperty)
	{
		var factory = new FrameworkElementFactory(typeof(TextBox));
		factory.SetValue(FrameworkElement.MarginProperty, new Thickness(2));
		factory.SetValue(FrameworkElement.ToolTipProperty, TrackingAllocationTextParser.GenericFormatHint);
		factory.AddHandler(FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, _) => LoadText(sender, scope, keyProperty)));
		factory.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler((sender, _) => SaveText(sender, scope, keyProperty)));
		return new DataGridTemplateColumn
		{
			Header = ColumnHeader,
			Width = new DataGridLength(220),
			CellTemplate = new DataTemplate { VisualTree = factory },
			CellEditingTemplate = new DataTemplate { VisualTree = factory },
			IsReadOnly = false
		};
	}

	private static void LoadText(object sender, string scope, string keyProperty)
	{
		if (sender is not TextBox textBox || !TryGetLong(textBox.DataContext, keyProperty, out var key)) return;
		var stored = TrackingCaptureSession.GetText(scope, key);
		if (!string.Equals(textBox.Text, stored, StringComparison.Ordinal)) textBox.Text = stored ?? string.Empty;
	}

	private static void SaveText(object sender, string scope, string keyProperty)
	{
		if (sender is not TextBox textBox || !TryGetLong(textBox.DataContext, keyProperty, out var key)) return;
		TrackingCaptureSession.Set(scope, key, textBox.Text, [.. ResolveInventoryIds(textBox.DataContext)]);
	}

	private static IEnumerable<long> ResolveInventoryIds(object? row)
	{
		if (row is null) yield break;
		foreach (var propertyName in new[] { "InventoryId", "SourceInventoryId", "DestinationInventoryId" })
		{
			if (TryGetLong(row, propertyName, out var value)) yield return value;
		}
		var selectedInventory = row.GetType().GetProperty("SelectedInventory", BindingFlags.Instance | BindingFlags.Public)?.GetValue(row);
		if (TryGetLong(selectedInventory, "InventoryId", out var selectedInventoryId)) yield return selectedInventoryId;
	}

	private static bool TryGetLong(object? row, string propertyName, out long value)
	{
		value = 0;
		if (row is null) return false;
		var raw = row.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)?.GetValue(row);
		if (raw is null) return false;
		try { value = Convert.ToInt64(raw, System.Globalization.CultureInfo.InvariantCulture); }
		catch (Exception) { return false; }
		return value > 0;
	}

	private static (string Scope, string KeyProperty)? ResolveTarget(Type type)
	{
		var name = type.Name;
		if (name.Contains("StockTransferLine", StringComparison.Ordinal)) return ("stock-transfer", "Id");
		if (name.Contains("MaterialIssueLine", StringComparison.Ordinal)) return ("material-issue", "Id");
		if (name.Contains("MaterialReturnLine", StringComparison.Ordinal)) return ("material-return", "Id");
		if (name.Contains("SupplierReturnLine", StringComparison.Ordinal)) return ("supplier-return", "Id");
		if (name.Contains("InventoryCountLine", StringComparison.Ordinal)) return ("inventory-count", "Id");
		if (name.Contains("CustomerReturnLine", StringComparison.Ordinal)) return ("customer-return", "Id");
		if (name.Contains("ShipmentLine", StringComparison.Ordinal)) return ("shipment", "Id");
		if (name.Contains("GoodsReceiptLine", StringComparison.Ordinal)) return ("goods-receipt", "PurchaseOrderLineId");
		return null;
	}
}
