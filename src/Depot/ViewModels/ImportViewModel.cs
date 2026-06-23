// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Services.Import;

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

	public ImportViewModel(
		ImportService importService)
	{
		_importService = importService;
	}

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

	public void LoadPreview(
		string filePath)
	{
		var preview =
			_importService.CreatePreview(
				filePath);

		FilePath = filePath;

		TotalItems =
			preview.TotalItems;

		NewItems =
			preview.NewItems;

		ExistingItems =
			preview.ExistingItems;

		Warnings =
			preview.Warnings.Count;

		Items.Clear();

		foreach (var item in preview.Items)
		{
			Items.Add(
				new ImportPreviewItemViewModel(
					item));
		}
	}
}