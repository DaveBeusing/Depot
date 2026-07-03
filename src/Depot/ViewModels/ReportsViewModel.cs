// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Commands;
using Depot.Services;

using Microsoft.Win32;

namespace Depot.ViewModels;

public sealed class ReportsViewModel
	: BaseViewModel
{
	private const string InventoryValueReportName = "Inventory Value";
	private const string StockByLocationReportName = "Stock by Location";
	private const string StockByPurposeReportName = "Stock by Purpose";
	private const string StockByCategoryReportName = "Stock by Category";
	private const string StockByManufacturerReportName = "Stock by Manufacturer";

	private readonly ReportService _reportService;
	private readonly IReadOnlyDictionary<string, ReportDefinition> _reportDefinitions;

	private string _selectedReport = InventoryValueReportName;
	private string _searchText = string.Empty;
	private int _totalInventoryRows;
	private int _totalItems;
	private int _totalStockQuantity;
	private decimal _totalInventoryValue;

	public ReportsViewModel(
		ReportService reportService)
	{
		_reportService =
			reportService;

		var reportDefinitions =
			CreateReportDefinitions();

		_reportDefinitions =
			reportDefinitions.ToDictionary(
				x => x.Name);

		ReportOptions =
			new ObservableCollection<string>(
				reportDefinitions.Select(
					x => x.Name));

		_selectedReport =
			reportDefinitions[0].Name;

		ExportCommand =
			new RelayCommand(
				Export,
				CanExport);

		Load();
	}

	public RelayCommand ExportCommand { get; }

	public ObservableCollection<string> ReportOptions { get; }

	public ObservableCollection<InventoryValueReportItemViewModel> InventoryValueItems { get; }
		= new();

	public ObservableCollection<GroupedInventoryReportItemViewModel> GroupedItems { get; }
		= new();

	public string SelectedReport
	{
		get => _selectedReport;

		set
		{
			if (string.IsNullOrWhiteSpace(
					value) ||
				!_reportDefinitions.ContainsKey(
					value) ||
				_selectedReport == value)
			{
				return;
			}

			_selectedReport =
				value;

			OnPropertyChanged();
			OnPropertyChanged(
				nameof(ReportTitle));
			OnPropertyChanged(
				nameof(SearchToolTip));
			OnPropertyChanged(
				nameof(IsInventoryValueReportSelected));
			OnPropertyChanged(
				nameof(IsGroupedReportSelected));

			Load();
		}
	}

	public string ReportTitle =>
		SelectedReportDefinition.Name;

	public string SearchToolTip =>
		IsInventoryValueReportSelected
			? "Search inventory value report"
			: "Search grouped stock report";

	public bool IsInventoryValueReportSelected =>
		SelectedReportDefinition.IsInventoryValueReport;

	public bool IsGroupedReportSelected =>
		!IsInventoryValueReportSelected;

	private ReportDefinition SelectedReportDefinition =>
		_reportDefinitions[SelectedReport];

	public string SearchText
	{
		get => _searchText;

		set
		{
			if (_searchText == value)
			{
				return;
			}

			_searchText =
				value;

			OnPropertyChanged();

			Load();
		}
	}

	public int TotalInventoryRows
	{
		get => _totalInventoryRows;

		private set
		{
			_totalInventoryRows =
				value;

			OnPropertyChanged();
		}
	}

	public int TotalItems
	{
		get => _totalItems;

		private set
		{
			_totalItems =
				value;

			OnPropertyChanged();
		}
	}

	public int TotalStockQuantity
	{
		get => _totalStockQuantity;

		private set
		{
			_totalStockQuantity =
				value;

			OnPropertyChanged();
		}
	}

	public decimal TotalInventoryValue
	{
		get => _totalInventoryValue;

		private set
		{
			_totalInventoryValue =
				value;

			OnPropertyChanged();
		}
	}

	public void Load()
	{
		SelectedReportDefinition.Load();

		ExportCommand.RaiseCanExecuteChanged();
	}

	private void LoadInventoryValueReport()
	{
		var report =
			_reportService.GetInventoryValueReport(
				SearchText);

		ApplyTotals(
			report.TotalInventoryRows,
			report.TotalItems,
			report.TotalStockQuantity,
			report.TotalInventoryValue);

		InventoryValueItems.Clear();
		GroupedItems.Clear();

		foreach (var item in report.Items)
		{
			InventoryValueItems.Add(
				new InventoryValueReportItemViewModel(
					item));
		}
	}

	private void LoadLocationInventoryReport()
	{
		var report =
			_reportService.GetLocationInventoryReport(
				SearchText);

		ApplyTotals(
			report.TotalInventoryRows,
			report.TotalItems,
			report.TotalStockQuantity,
			report.TotalInventoryValue);

		LoadGroupedItems(
			report.Items
				.Select(
					x =>
						new GroupedInventoryReportItemViewModel(
							x.LocationName,
							x.InventoryRows,
							x.TotalItems,
							x.TotalStockQuantity,
							x.InventoryValue)));
	}

	private void LoadPurposeInventoryReport()
	{
		var report =
			_reportService.GetPurposeInventoryReport(
				SearchText);

		ApplyTotals(
			report.TotalInventoryRows,
			report.TotalItems,
			report.TotalStockQuantity,
			report.TotalInventoryValue);

		LoadGroupedItems(
			report.Items
				.Select(
					x =>
						new GroupedInventoryReportItemViewModel(
							x.PurposeName,
							x.InventoryRows,
							x.TotalItems,
							x.TotalStockQuantity,
							x.InventoryValue)));
	}

	private void LoadCategoryInventoryReport()
	{
		var report =
			_reportService.GetCategoryInventoryReport(
				SearchText);

		ApplyTotals(
			report.TotalInventoryRows,
			report.TotalItems,
			report.TotalStockQuantity,
			report.TotalInventoryValue);

		LoadGroupedItems(
			report.Items
				.Select(
					x =>
						new GroupedInventoryReportItemViewModel(
							x.CategoryName,
							x.InventoryRows,
							x.TotalItems,
							x.TotalStockQuantity,
							x.InventoryValue)));
	}

	private void LoadManufacturerInventoryReport()
	{
		var report =
			_reportService.GetManufacturerInventoryReport(
				SearchText);

		ApplyTotals(
			report.TotalInventoryRows,
			report.TotalItems,
			report.TotalStockQuantity,
			report.TotalInventoryValue);

		LoadGroupedItems(
			report.Items
				.Select(
					x =>
						new GroupedInventoryReportItemViewModel(
							x.ManufacturerName,
							x.InventoryRows,
							x.TotalItems,
							x.TotalStockQuantity,
							x.InventoryValue)));
	}

	private void LoadGroupedItems(
		IEnumerable<GroupedInventoryReportItemViewModel> items)
	{
		InventoryValueItems.Clear();
		GroupedItems.Clear();

		foreach (var item in items)
		{
			GroupedItems.Add(
				item);
		}
	}

	private void ApplyTotals(
		int totalInventoryRows,
		int totalItems,
		int totalStockQuantity,
		decimal totalInventoryValue)
	{
		TotalInventoryRows =
			totalInventoryRows;

		TotalItems =
			totalItems;

		TotalStockQuantity =
			totalStockQuantity;

		TotalInventoryValue =
			totalInventoryValue;
	}

	private bool CanExport()
	{
		return IsGroupedReportSelected
			? GroupedItems.Count > 0
			: InventoryValueItems.Count > 0;
	}

	private void Export()
	{
		var dialog =
			new SaveFileDialog
			{
				DefaultExt =
					".xlsx",

				FileName =
					GetDefaultExportFileName(),

				Filter =
					"Excel Files (*.xlsx)|*.xlsx",

				OverwritePrompt =
					true
			};

		if (dialog.ShowDialog() != true)
		{
			return;
		}

		SelectedReportDefinition.Export(
			SearchText,
			dialog.FileName);
	}

	private string GetDefaultExportFileName()
	{
		return SelectedReportDefinition.DefaultExportFileName;
	}

	private IReadOnlyList<ReportDefinition> CreateReportDefinitions()
	{
		return new[]
		{
			new ReportDefinition(
				InventoryValueReportName,
				"Inventory Value Report.xlsx",
				isInventoryValueReport: true,
				LoadInventoryValueReport,
				_reportService.ExportInventoryValueReport),

			new ReportDefinition(
				StockByLocationReportName,
				"Stock by Location Report.xlsx",
				isInventoryValueReport: false,
				LoadLocationInventoryReport,
				_reportService.ExportLocationInventoryReport),

			new ReportDefinition(
				StockByPurposeReportName,
				"Stock by Purpose Report.xlsx",
				isInventoryValueReport: false,
				LoadPurposeInventoryReport,
				_reportService.ExportPurposeInventoryReport),

			new ReportDefinition(
				StockByCategoryReportName,
				"Stock by Category Report.xlsx",
				isInventoryValueReport: false,
				LoadCategoryInventoryReport,
				_reportService.ExportCategoryInventoryReport),

			new ReportDefinition(
				StockByManufacturerReportName,
				"Stock by Manufacturer Report.xlsx",
				isInventoryValueReport: false,
				LoadManufacturerInventoryReport,
				_reportService.ExportManufacturerInventoryReport)
		};
	}

	private sealed class ReportDefinition
	{
		public ReportDefinition(
			string name,
			string defaultExportFileName,
			bool isInventoryValueReport,
			Action load,
			Action<string?, string> export)
		{
			Name =
				name;

			DefaultExportFileName =
				defaultExportFileName;

			IsInventoryValueReport =
				isInventoryValueReport;

			Load =
				load;

			Export =
				export;
		}

		public string Name { get; }

		public string DefaultExportFileName { get; }

		public bool IsInventoryValueReport { get; }

		public Action Load { get; }

		public Action<string?, string> Export { get; }
	}
}
