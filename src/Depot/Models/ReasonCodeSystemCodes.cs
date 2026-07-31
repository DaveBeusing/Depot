// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public static class ReasonCodeSystemCodes
{
	public const string GoodsReceipt = "GOODS_RECEIPT";
	public const string GoodsIssue = "GOODS_ISSUE";
	public const string InventoryCorrection = "INVENTORY_CORRECTION";
	public const string Damaged = "DAMAGED";
	public const string Lost = "LOST";
	public const string Returned = "RETURNED";
	public const string Consumed = "CONSUMED";
	public const string Demo = "DEMO";
	public const string Repair = "REPAIR";
	public const string Transfer = "TRANSFER";

	public static bool IsRequiredByActiveWorkflow(string code) =>
		string.Equals(code, GoodsReceipt, StringComparison.Ordinal);
}
