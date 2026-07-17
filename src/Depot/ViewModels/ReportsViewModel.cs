// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Commands;
using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels;

public sealed class ReportsViewModel
	: BaseViewModel,
		IDisposable
{
	private const string InventoryValueReportName = "Inventory Value";
	private const string StockByLocationReportName = "Stock by Storage Location";
	private const string StockByWarehouseReportName = "Stock by Warehouse";
	private const string StockByPurposeReportName = "Stock by Purpose";
	private const string StockByCategoryReportName = "Stock by Category";
	private const string StockByManufacturerReportName = "Stock by Manufacturer";

	private readonly ReportService _reportService;
	private readonly IFileDialogService _fileDialogService;
	private readonly IReadOnlyDictionary<string, ReportDefinition> _reportDefinitions;
	private readonly AsyncDebouncer _searchDebouncer = new(TimeSpan.FromMilliseconds(300));

	private string _selectedReport = InventoryValueReportName;
	private string _searchText = string.Empty;
	private string _exportStatusText = string.Empty;
	private bool _isExportStatusError;
	private int _totalInventoryRows;
	private int _totalItems;
	private int _totalStockQuantity;
	private decimal _totalInventoryValue;

	public ReportsViewModel(
		ReportService reportService,
		IFileDialogService fileDialogService)
	{
		_reportService =
			reportService;
		_fileDialogService = fileDialogService;

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
			new AsyncRelayCommand(
				ExportAsync,
				CanExport);
	}

	public AsyncRelayCommand ExportCommand { get; }

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

			ClearExportStatus();

			_ = LoadAsync();
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

			ClearExportStatus();

			_ = _searchDebouncer.DebounceAsync(LoadAsync);
		}
	}

	public string ExportStatusText
	{
		get => _exportStatusText;

		private set
		{
			if (_exportStatusText == value)
			{
				return;
			}

			_exportStatusText =
				value;

			OnPropertyChanged();
			OnPropertyChanged(
				nameof(HasExportStatus));
			OnPropertyChanged(nameof(HasExportSuccessStatus));
			OnPropertyChanged(nameof(HasExportErrorStatus));
		}
	}

	public bool HasExportStatus =>
		!string.IsNullOrWhiteSpace(
			ExportStatusText);

	public bool HasExportSuccessStatus =>
		HasExportStatus && !IsExportStatusError;

	public bool HasExportErrorStatus =>
		HasExportStatus && IsExportStatusError;

	public bool IsExportStatusError
	{
		get => _isExportStatusError;

		private set
		{
			if (_isExportStatusError == value)
			{
				return;
			}

			_isExportStatusError =
				value;

			OnPropertyChanged();
			OnPropertyChanged(nameof(HasExportSuccessStatus));
			OnPropertyChanged(nameof(HasExportErrorStatus));
		}
	}

	public bool HasReportRows =>
		IsGroupedReportSelected
			? GroupedItems.Count > 0
			: InventoryValueItems.Count > 0;

	public bool HasNoReportRows =>
		!HasReportRows;

	public bool ShowInventoryValueRows =>
		IsInventoryValueReportSelected &&
		HasReportRows;

	public bool ShowGroupedRows =>
		IsGroupedReportSelected &&
		HasReportRows;

	public string EmptyReportMessage =>
		string.IsNullOrWhiteSpace(
			SearchText)
			? "No report data available."
			: "No rows match the current filter.";

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

	public async Task LoadAsync(CancellationToken cancellationToken = default)
	{
		BeginOperation("Loading report...");

		try
		{
			await SelectedReportDefinition.Load(cancellationToken);

			RaiseReportRowsChanged();
			CompleteOperation(!HasReportRows, "Report loaded.");
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			return;
		}
		catch (Exception ex)
		{
			FailOperation(ex, "The report could not be loaded.");
		}

		ExportCommand.RaiseCanExecuteChanged();
	}

	private async Task LoadInventoryValueReportAsync(CancellationToken cancellationToken)
	{
		var report =
			await _reportService.GetInventoryValueReportAsync(
				SearchText,
				cancellationToken);

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

	private async Task LoadGroupedInventoryReportAsync(
		GroupedInventoryReportType reportType,
		CancellationToken cancellationToken)
	{
		var report =
			await _reportService.GetGroupedInventoryReportAsync(
				SearchText,
				reportType,
				cancellationToken);

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
							x.GroupName,
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
		return HasReportRows;
	}

	private async Task ExportAsync(CancellationToken cancellationToken)
	{
		var filePath = _fileDialogService.ShowSaveFile(
			new SaveFileDialogRequest(
				"Export report",
				"Excel Files (*.xlsx)|*.xlsx",
				".xlsx",
				GetDefaultExportFileName()));

		if (filePath is null)
		{
			return;
		}

		try
		{
			BeginOperation("Exporting report...");

			await SelectedReportDefinition.Export(
				SearchText,
				filePath,
				cancellationToken);

			SetExportStatus(
				$"Exported to {filePath}",
				isError: false);
			CompleteOperation(statusText: "Report exported.");
		}
		catch (Exception ex)
		{
			SetExportStatus(
				$"Export failed: {ex.Message}",
				isError: true);
			FailOperation(ex, "The report could not be exported.");
		}
	}

	private string GetDefaultExportFileName()
	{
		return SelectedReportDefinition.DefaultExportFileName;
	}

	private void ClearExportStatus()
	{
		SetExportStatus(
			string.Empty,
			isError: false);
	}

	private void SetExportStatus(
		string message,
		bool isError)
	{
		IsExportStatusError =
			isError;

		ExportStatusText =
			message;
	}

	private void RaiseReportRowsChanged()
	{
		OnPropertyChanged(
			nameof(HasReportRows));
		OnPropertyChanged(
			nameof(HasNoReportRows));
		OnPropertyChanged(
			nameof(ShowInventoryValueRows));
		OnPropertyChanged(
			nameof(ShowGroupedRows));
		OnPropertyChanged(
			nameof(EmptyReportMessage));
	}

	private IReadOnlyList<ReportDefinition> CreateReportDefinitions()
	{
		return new[]
		{
			new ReportDefinition(
				InventoryValueReportName,
				"Inventory Value Report.xlsx",
				isInventoryValueReport: true,
				LoadInventoryValueReportAsync,
				(searchText, filePath, cancellationToken) =>
					_reportService.ExportInventoryValueReportAsync(
						searchText,
						filePath,
						cancellationToken)),

			new ReportDefinition(
				StockByLocationReportName,
				"Stock by Storage Location Report.xlsx",
				isInventoryValueReport: false,
				cancellationToken =>
					LoadGroupedInventoryReportAsync(
						GroupedInventoryReportType.Location,
						cancellationToken),
				CreateGroupedExport(GroupedInventoryReportType.Location)),

			new ReportDefinition(
				StockByWarehouseReportName,
				"Stock by Warehouse Report.xlsx",
				isInventoryValueReport: false,
				cancellationToken =>
					LoadGroupedInventoryReportAsync(
						GroupedInventoryReportType.Warehouse,
						cancellationToken),
				CreateGroupedExport(GroupedInventoryReportType.Warehouse)),

			new ReportDefinition(
				StockByPurposeReportName,
				"Stock by Purpose Report.xlsx",
				isInventoryValueReport: false,
				cancellationToken =>
					LoadGroupedInventoryReportAsync(
						GroupedInventoryReportType.Purpose,
						cancellationToken),
				CreateGroupedExport(GroupedInventoryReportType.Purpose)),

			new ReportDefinition(
				StockByCategoryReportName,
				"Stock by Category Report.xlsx",
				isInventoryValueReport: false,
				cancellationToken =>
					LoadGroupedInventoryReportAsync(
						GroupedInventoryReportType.Category,
						cancellationToken),
				CreateGroupedExport(GroupedInventoryReportType.Category)),

			new ReportDefinition(
				StockByManufacturerReportName,
				"Stock by Manufacturer Report.xlsx",
				isInventoryValueReport: false,
				cancellationToken =>
					LoadGroupedInventoryReportAsync(
						GroupedInventoryReportType.Manufacturer,
						cancellationToken),
				CreateGroupedExport(GroupedInventoryReportType.Manufacturer))
		};
	}

	private Func<string?, string, CancellationToken, Task> CreateGroupedExport(
		GroupedInventoryReportType reportType)
	{
		return (searchText, filePath, cancellationToken) =>
			_reportService.ExportGroupedInventoryReportAsync(
					searchText,
					reportType,
					filePath,
					cancellationToken);
	}

	public void Dispose()
	{
		_searchDebouncer.Dispose();
		ExportCommand.Dispose();
	}

	private sealed class ReportDefinition
	{
		public ReportDefinition(
			string name,
			string defaultExportFileName,
			bool isInventoryValueReport,
			Func<CancellationToken, Task> load,
			Func<string?, string, CancellationToken, Task> export)
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

		public Func<CancellationToken, Task> Load { get; }

		public Func<string?, string, CancellationToken, Task> Export { get; }
	}
}
