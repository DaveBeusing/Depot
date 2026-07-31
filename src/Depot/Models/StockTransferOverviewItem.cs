// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed class StockTransferOverviewItem
{
	public long Id { get; set; }
	public string TransferNumber { get; set; } = string.Empty;
	public long SourceWarehouseId { get; set; }
	public string SourceWarehouseName { get; set; } = string.Empty;
	public long DestinationWarehouseId { get; set; }
	public string DestinationWarehouseName { get; set; } = string.Empty;
	public DateTime TransferDate { get; set; }
	public StockTransferStatus Status { get; set; }
	public string CreatedByUserName { get; set; } = string.Empty;
	public int LineCount { get; set; }
	public string? Notes { get; set; }
	public long Version { get; set; }
	public string StatusDisplayName => Status.ToString();
}
