// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed record ReportExportProgress(int ProcessedRows, long TotalRows)
{
	public int Percentage => TotalRows <= 0
		? 100
		: (int)Math.Min(100, ProcessedRows * 100L / TotalRows);
}
