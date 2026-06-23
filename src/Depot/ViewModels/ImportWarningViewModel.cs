// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

namespace Depot.ViewModels;

public sealed class ImportWarningViewModel
	: BaseViewModel
{
	public ImportWarningViewModel(
		ImportWarning warning)
	{
		RowNumber = warning.RowNumber;
		Message = warning.Message;
	}

	public int RowNumber { get; }

	public string Message { get; }
}