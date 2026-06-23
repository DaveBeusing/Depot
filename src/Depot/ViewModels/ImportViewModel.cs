// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Commands;
using Depot.Services.Import;

using Microsoft.Win32;

namespace Depot.ViewModels;

public sealed class ImportViewModel
	: BaseViewModel
{
	private readonly ImportService _importService;

	private string _filePath = string.Empty;

	private int _totalItems;
	private int _newItems;
	private int _existingItems;
	private int _warnings;
	private int _totalQuantity;
	private decimal _totalValue;

	public ObservableCollection<ImportWarningViewModel> WarningItems { get; }
	= new();

	public ImportViewModel(
		ImportService importService)
	{
		_importService = importService;

		BrowseCommand =
			new RelayCommand(
				Browse);
	}

	public RelayCommand BrowseCommand { get; }

	public string FilePath
	{
		get => _filePath;

		set
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
		}
	}

	public ObservableCollection<ImportPreviewItemViewModel> Items { get; }
		= new();

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

	public void LoadPreview(string filePath)
	{
		var preview =
			_importService.CreatePreview(
				filePath);

		FilePath = filePath;

		TotalItems = preview.TotalItems;
		NewItems = preview.NewItems;
		ExistingItems = preview.ExistingItems;
		Warnings = preview.Warnings.Count;
		TotalQuantity = preview.TotalQuantity;
		TotalValue = preview.TotalValue;

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

}