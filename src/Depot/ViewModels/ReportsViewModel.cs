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

	private readonly ReportService _reportService;

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

		ExportCommand =
			new RelayCommand(
				Export,
				CanExport);

		Load();
	}

	public RelayCommand ExportCommand { get; }

	public ObservableCollection<string> ReportOptions { get; }
		= new()
		{
			InventoryValueReportName,
			StockByLocationReportName,
			StockByPurposeReportName
		};

	public ObservableCollection<InventoryValueReportItemViewModel> InventoryValueItems { get; }
		= new();

	public ObservableCollection<LocationInventoryReportItemViewModel> LocationItems { get; }
		= new();

	public ObservableCollection<PurposeInventoryReportItemViewModel> PurposeItems { get; }
		= new();

	public string SelectedReport
	{
		get => _selectedReport;

		set
		{
			if (!ReportOptions.Contains(
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
				nameof(IsStockByLocationReportSelected));
			OnPropertyChanged(
				nameof(IsStockByPurposeReportSelected));

			Load();
		}
	}

	public string ReportTitle =>
		SelectedReport;

	public string SearchToolTip =>
		IsInventoryValueReportSelected
			? "Search inventory value report"
			: "Search grouped stock report";

	public bool IsInventoryValueReportSelected =>
		SelectedReport == InventoryValueReportName;

	public bool IsStockByLocationReportSelected =>
		SelectedReport == StockByLocationReportName;

	public bool IsStockByPurposeReportSelected =>
		SelectedReport == StockByPurposeReportName;

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
		if (IsStockByPurposeReportSelected)
		{
			LoadPurposeInventoryReport();
		}
		else if (IsStockByLocationReportSelected)
		{
			LoadLocationInventoryReport();
		}
		else
		{
			LoadInventoryValueReport();
		}

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
		LocationItems.Clear();
		PurposeItems.Clear();

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

		InventoryValueItems.Clear();
		LocationItems.Clear();
		PurposeItems.Clear();

		foreach (var item in report.Items)
		{
			LocationItems.Add(
				new LocationInventoryReportItemViewModel(
					item));
		}
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

		InventoryValueItems.Clear();
		LocationItems.Clear();
		PurposeItems.Clear();

		foreach (var item in report.Items)
		{
			PurposeItems.Add(
				new PurposeInventoryReportItemViewModel(
					item));
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
		if (IsStockByPurposeReportSelected)
		{
			return PurposeItems.Count > 0;
		}

		return IsStockByLocationReportSelected
			? LocationItems.Count > 0
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

		if (IsStockByPurposeReportSelected)
		{
			_reportService.ExportPurposeInventoryReport(
				SearchText,
				dialog.FileName);
		}
		else if (IsStockByLocationReportSelected)
		{
			_reportService.ExportLocationInventoryReport(
				SearchText,
				dialog.FileName);
		}
		else
		{
			_reportService.ExportInventoryValueReport(
				SearchText,
				dialog.FileName);
		}
	}

	private string GetDefaultExportFileName()
	{
		if (IsStockByPurposeReportSelected)
		{
			return "Stock by Purpose Report.xlsx";
		}

		return IsStockByLocationReportSelected
			? "Stock by Location Report.xlsx"
			: "Inventory Value Report.xlsx";
	}
}
