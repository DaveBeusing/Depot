// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

namespace Depot.ViewModels;

public sealed class ImportResultViewModel
	: BaseViewModel
{
	private int _importedItems;
	private int _importedMovements;
	private int _skippedItems;
	private bool _hasResult;

	public int ImportedItems
	{
		get => _importedItems;

		private set
		{
			_importedItems = value;
			OnPropertyChanged();
		}
	}

	public int ImportedMovements
	{
		get => _importedMovements;

		private set
		{
			_importedMovements = value;
			OnPropertyChanged();
		}
	}

	public int SkippedItems
	{
		get => _skippedItems;

		private set
		{
			_skippedItems = value;
			OnPropertyChanged();
		}
	}

	public bool HasResult
	{
		get => _hasResult;

		private set
		{
			_hasResult = value;
			OnPropertyChanged();
		}
	}

	public void Load(
		ImportResult result)
	{
		ImportedItems =
			result.ImportedItems;

		ImportedMovements =
			result.ImportedMovements;

		SkippedItems =
			result.SkippedItems;

		HasResult = true;
	}

	public void Clear()
	{
		ImportedItems = 0;
		ImportedMovements = 0;
		SkippedItems = 0;
		HasResult = false;
	}
}