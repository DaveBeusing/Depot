// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Commands;
using Depot.Models;
using Depot.Services.Import;

using Microsoft.Win32;

namespace Depot.ViewModels;

public sealed class ImportViewModel
	: BaseViewModel
{
	private readonly ImportService _importService;

	private ImportPreview? _currentPreview;

	private string _filePath = string.Empty;

	private int _totalItems;
	private int _newItems;
	private int _existingItems;
	private int _warnings;
	private int _totalQuantity;
	private decimal _totalValue;

	public ImportViewModel(
		ImportService importService)
	{
		_importService = importService;

		BrowseCommand =
			new RelayCommand(
				Browse);

		ImportCommand =
			new RelayCommand(
				ExecuteImport,
				CanExecuteImport);
	}

	public RelayCommand BrowseCommand { get; }

	public RelayCommand ImportCommand { get; }

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
		var dialog =
			new OpenFileDialog
			{
				Filter =
					"Excel Files (*.xlsx)|*.xlsx"
			};

		if (dialog.ShowDialog() != true)
		{
			return;
		}

		LoadPreview(
			dialog.FileName);
	}

	public void LoadPreview(
		string filePath)
	{
		var preview =
			_importService.CreatePreview(
				filePath);

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
	}

	private bool CanExecuteImport()
	{
		return
			_currentPreview is not null &&
			//Warnings == 0 &&
			Items.Count > 0;
	}

	private void ExecuteImport()
	{
		if (_currentPreview is null)
		{
			return;
		}

		var result =
			_importService.ExecuteImport(
				_currentPreview);

		Result.Load(
			result);

		ImportCommand.RaiseCanExecuteChanged();
	}
}