// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed class StockTransfer
{
	public long Id { get; set; }
	public string TransferNumber { get; set; } = string.Empty;
	public long SourceWarehouseId { get; set; }
	public long DestinationWarehouseId { get; set; }
	public DateTime TransferDate { get; set; } = DateTime.Today;
	public StockTransferStatus Status { get; set; } = StockTransferStatus.Draft;
	public long CreatedByUserId { get; set; }
	public long? PostedByUserId { get; set; }
	public string? Notes { get; set; }
	public long Version { get; set; } = 1;
	public IReadOnlyList<StockTransferLine> Lines { get; set; } = [];
}
