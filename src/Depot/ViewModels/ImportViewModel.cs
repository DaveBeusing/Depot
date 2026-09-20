// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Commands;
using Depot.Models;
using Depot.Services;
using Depot.Services.Import;

namespace Depot.ViewModels;

public sealed class ImportViewModel
	: BaseViewModel
{
	private readonly ImportService _importService;
	private readonly IFileDialogService _fileDialogService;

	private ImportPreview? _currentPreview;
	private ImportMapping? _currentMapping;
	private bool _suppressMappingChanges;
	private string _filePath = string.Empty;
	private int _totalItems;
	private int _newItems;
	private int _existingItems;
	private int _warnings;
	private int _blockingRowErrors;
	private int _mappingErrorCount;
	private int _mappingWarningCount;
	private int _totalQuantity;
	private decimal _totalValue;

	public ImportViewModel(
		ImportService importService,
		IFileDialogService fileDialogService)
	{
		_importService = importService;
		_fileDialogService = fileDialogService;

		BrowseCommand = new RelayCommand(Browse);
		ResetMappingCommand = new RelayCommand(
			ResetMapping,
			() => MappingItems.Count > 0);
		PreviewCommand = new AsyncRelayCommand(
			GeneratePreviewAsync,
			CanGeneratePreview);
		ImportCommand = new AsyncRelayCommand(
			ExecuteImportAsync,
			CanExecuteImport);
	}

	public RelayCommand BrowseCommand { get; }

	public RelayCommand ResetMappingCommand { get; }

	public AsyncRelayCommand PreviewCommand { get; }

	public AsyncRelayCommand ImportCommand { get; }

	public ImportResultViewModel Result { get; } =
		new();

	public ObservableCollection<ImportMappingItemViewModel> MappingItems { get; } =
		new();

	public ObservableCollection<ImportMappingIssue> MappingIssues { get; } =
		new();

	public ObservableCollection<ImportPreviewItemViewModel> Items { get; } =
		new();

	public ObservableCollection<ImportWarningViewModel> WarningItems { get; } =
		new();

	public string FilePath
	{
		get => _filePath;
		private set
		{
			if (_filePath == value)
			{
				return;
			}

			_filePath = value;
			OnPropertyChanged();
		}
	}

	public bool IsMappingValid =>
		_currentMapping?.IsValid == true;

	public bool HasPreview =>
		_currentPreview is not null;

	public int MappingErrorCount
	{
		get => _mappingErrorCount;
		private set
		{
			if (_mappingErrorCount == value)
			{
				return;
			}

			_mappingErrorCount = value;
			OnPropertyChanged();
		}
	}

	public int MappingWarningCount
	{
		get => _mappingWarningCount;
		private set
		{
			if (_mappingWarningCount == value)
			{
				return;
			}

			_mappingWarningCount = value;
			OnPropertyChanged();
		}
	}

	public int BlockingRowErrors
	{
		get => _blockingRowErrors;
		private set
		{
			if (_blockingRowErrors == value)
			{
				return;
			}

			_blockingRowErrors = value;
			OnPropertyChanged();
			ImportCommand.RaiseCanExecuteChanged();
		}
	}

	public int TotalItems
	{
		get => _totalItems;
		private set
		{
			_totalItems = value;
			OnPropertyChanged();
		}
	}

	public int NewItems
	{
		get => _newItems;
		private set
		{
			_newItems = value;
			OnPropertyChanged();
		}
	}

	public int ExistingItems
	{
		get => _existingItems;
		private set
		{
			_existingItems = value;
			OnPropertyChanged();
		}
	}

	public int Warnings
	{
		get => _warnings;
		private set
		{
			_warnings = value;
			OnPropertyChanged();
		}
	}

	public int TotalQuantity
	{
		get => _totalQuantity;
		private set
		{
			_totalQuantity = value;
			OnPropertyChanged();
		}
	}

	public decimal TotalValue
	{
		get => _totalValue;
		private set
		{
			_totalValue = value;
			OnPropertyChanged();
		}
	}

	private void Browse()
	{
		var filePath = _fileDialogService.ShowOpenFile(
			new OpenFileDialogRequest(
				"Select inventory workbook",
				"Excel Files (*.xlsx)|*.xlsx"));

		if (filePath is null)
		{
			return;
		}

		_ = LoadMappingAsync(filePath);
	}

	public async Task LoadMappingAsync(
		string filePath,
		CancellationToken cancellationToken = default)
	{
		BeginOperation("Reading workbook columns...");

		try
		{
			var mapping = await Task.Run(
				() => _importService.InspectMapping(filePath, cancellationToken),
				cancellationToken);

			FilePath = filePath;
			Result.Clear();
			ReplaceMapping(mapping);
			ClearPreview();

			var status = MappingErrorCount == 0
				? "Mapping ready. Generate a preview before importing."
				: $"Mapping loaded with {MappingErrorCount:N0} blocking issue(s).";
			CompleteOperation(MappingItems.Count == 0, status);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			CompleteOperation(statusText: "Mapping load cancelled.");
		}
		catch (Exception ex)
		{
			FailOperation(ex, "The import file could not be read.");
		}
	}

	private void ReplaceMapping(ImportMapping mapping)
	{
		foreach (var existing in MappingItems)
		{
			existing.MappingChanged -= OnMappingChanged;
		}

		MappingItems.Clear();

		foreach (var column in mapping.Columns)
		{
			var item = new ImportMappingItemViewModel(column);
			item.MappingChanged += OnMappingChanged;
			MappingItems.Add(item);
		}

		RebuildMapping(invalidatePreview: false);
		ResetMappingCommand.RaiseCanExecuteChanged();
	}

	private void ResetMapping()
	{
		if (MappingItems.Count == 0)
		{
			return;
		}

		_suppressMappingChanges = true;
		try
		{
			foreach (var item in MappingItems)
			{
				item.ResetDefault();
			}
		}
		finally
		{
			_suppressMappingChanges = false;
		}

		RebuildMapping(invalidatePreview: true);
	}

	private void OnMappingChanged(object? sender, EventArgs e)
	{
		if (_suppressMappingChanges)
		{
			return;
		}

		RebuildMapping(invalidatePreview: true);
	}

	private void RebuildMapping(bool invalidatePreview)
	{
		_currentMapping = new ImportMapping(
			MappingItems
				.Select(item => item.ToModel())
				.ToList());

		MappingIssues.Clear();
		foreach (var issue in _currentMapping.Issues)
		{
			MappingIssues.Add(issue);
		}

		MappingErrorCount = _currentMapping.Issues.Count(
			issue => issue.Severity == ImportMappingIssueSeverity.Error);
		MappingWarningCount = _currentMapping.Issues.Count(
			issue => issue.Severity == ImportMappingIssueSeverity.Warning);

		OnPropertyChanged(nameof(IsMappingValid));
		PreviewCommand.RaiseCanExecuteChanged();
		ImportCommand.RaiseCanExecuteChanged();

		if (invalidatePreview)
		{
			ClearPreview();
			UpdateOperationStatus("Mapping changed. Generate a new preview before importing.");
		}
	}

	private bool CanGeneratePreview() =>
		_currentMapping?.IsValid == true &&
		MappingItems.Count > 0 &&
		!string.IsNullOrWhiteSpace(FilePath);

	private async Task GeneratePreviewAsync(CancellationToken cancellationToken)
	{
		if (_currentMapping is null || !_currentMapping.IsValid)
		{
			return;
		}

		BeginOperation("Generating import preview...");

		try
		{
			var mapping = _currentMapping;
			var preview = await Task.Run(
				() => _importService.CreatePreview(
					FilePath,
					mapping,
					cancellationToken),
				cancellationToken);

			_currentPreview = preview;
			Result.Clear();
			LoadPreview(preview);
			OnPropertyChanged(nameof(HasPreview));
			ImportCommand.RaiseCanExecuteChanged();

			var status = BlockingRowErrors == 0
				? "Import preview ready."
				: $"Preview contains {BlockingRowErrors:N0} blocking row error(s).";
			CompleteOperation(Items.Count == 0, status);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			CompleteOperation(statusText: "Preview generation cancelled.");
		}
		catch (Exception ex)
		{
			ClearPreview();
			FailOperation(ex, "The import preview could not be generated.");
		}
	}

	private void LoadPreview(ImportPreview preview)
	{
		TotalItems = preview.TotalItems;
		NewItems = preview.NewItems;
		ExistingItems = preview.ExistingItems;
		Warnings = preview.Warnings.Count;
		BlockingRowErrors = preview.Warnings.Count(warning => warning.BlocksImport);
		TotalQuantity = preview.TotalQuantity;
		TotalValue = preview.TotalValue;

		Items.Clear();
		foreach (var item in preview.Items)
		{
			Items.Add(new ImportPreviewItemViewModel(item));
		}

		WarningItems.Clear();
		foreach (var warning in preview.Warnings)
		{
			WarningItems.Add(new ImportWarningViewModel(warning));
		}
	}

	private void ClearPreview()
	{
		_currentPreview = null;
		Items.Clear();
		WarningItems.Clear();
		TotalItems = 0;
		NewItems = 0;
		ExistingItems = 0;
		Warnings = 0;
		BlockingRowErrors = 0;
		TotalQuantity = 0;
		TotalValue = 0;
		OnPropertyChanged(nameof(HasPreview));
		ImportCommand.RaiseCanExecuteChanged();
	}

	private bool CanExecuteImport() =>
		_currentPreview is not null &&
		IsMappingValid &&
		BlockingRowErrors == 0 &&
		Items.Count > 0;

	private async Task ExecuteImportAsync(CancellationToken cancellationToken)
	{
		if (_currentPreview is null)
		{
			return;
		}

		BeginOperation("Importing inventory...");

		try
		{
			var preview = _currentPreview;
			var result = await _importService.ExecuteImportAsync(
				preview,
				cancellationToken);

			Result.Load(result);
			_currentPreview = null;
			OnPropertyChanged(nameof(HasPreview));
			ImportCommand.RaiseCanExecuteChanged();
			CompleteOperation(statusText: "Import completed.");
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			CompleteOperation(statusText: "Import cancelled.");
		}
		catch (Exception ex)
		{
			FailOperation(ex, "The import could not be completed.");
		}
	}
}
