// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed class StockTransferLine
{
	public long Id { get; set; }
	public long StockTransferId { get; set; }
	public int LineNumber { get; set; }
	public long SourceInventoryId { get; set; }
	public long DestinationInventoryId { get; set; }
	public int Quantity { get; set; }
	public long Version { get; set; } = 1;
}
