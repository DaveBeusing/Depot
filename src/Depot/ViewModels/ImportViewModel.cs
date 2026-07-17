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

	private string _filePath = string.Empty;

	private int _totalItems;
	private int _newItems;
	private int _existingItems;
	private int _warnings;
	private int _totalQuantity;
	private decimal _totalValue;

	public ImportViewModel(
		ImportService importService,
		IFileDialogService fileDialogService)
	{
		_importService = importService;
		_fileDialogService = fileDialogService;

		BrowseCommand =
			new RelayCommand(
				Browse);

		ImportCommand =
			new AsyncRelayCommand(
				ExecuteImportAsync,
				CanExecuteImport);
	}

	public RelayCommand BrowseCommand { get; }

	public AsyncRelayCommand ImportCommand { get; }

	public ImportResultViewModel Result { get; }
		= new();

	public ObservableCollection<ImportPreviewItemViewModel> Items { get; }
		= new();

	public ObservableCollection<ImportWarningViewModel> WarningItems { get; }
		= new();

	public string FilePath
	{
		get => _filePath;

		private set
		{
			_filePath = value;
			OnPropertyChanged();
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
			ImportCommand.RaiseCanExecuteChanged();
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

		_ = LoadPreviewAsync(filePath);
	}

	public async Task LoadPreviewAsync(
		string filePath,
		CancellationToken cancellationToken = default)
	{
		BeginOperation("Reading import file...");

		try
		{
			var preview = await Task.Run(
				() => _importService.CreatePreview(filePath, cancellationToken),
				cancellationToken);

			_currentPreview =
				preview;

		Result.Clear();

		FilePath =
			filePath;

		TotalItems =
			preview.TotalItems;

		NewItems =
			preview.NewItems;

		ExistingItems =
			preview.ExistingItems;

		Warnings =
			preview.Warnings.Count;

		TotalQuantity =
			preview.TotalQuantity;

		TotalValue =
			preview.TotalValue;

		Items.Clear();

		foreach (var item in preview.Items)
		{
			Items.Add(
				new ImportPreviewItemViewModel(
					item));
		}

		WarningItems.Clear();

		foreach (var warning in preview.Warnings)
		{
			WarningItems.Add(
				new ImportWarningViewModel(
					warning));
		}

			ImportCommand.RaiseCanExecuteChanged();
			CompleteOperation(Items.Count == 0, "Import preview ready.");
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			return;
		}
		catch (Exception ex)
		{
			FailOperation(ex, "The import file could not be read.");
		}
	}

	private bool CanExecuteImport()
	{
		return
			_currentPreview is not null &&
			//Warnings == 0 &&
			Items.Count > 0;
	}

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

			Result.Load(
				result);

			ImportCommand.RaiseCanExecuteChanged();
			CompleteOperation(statusText: "Import completed.");
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			return;
		}
		catch (Exception ex)
		{
			FailOperation(ex, "The import could not be completed.");
		}
	}
}
