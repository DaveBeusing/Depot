// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

namespace Depot.Services;

public static class ItemOperationalPolicy
{
	public static void EnsurePurchasable(Item item, DateTime orderDate)
	{
		ArgumentNullException.ThrowIfNull(item);
		if (item.LifecycleStatus is ItemLifecycleStatus.Discontinued or ItemLifecycleStatus.Obsolete)
			throw new InvalidOperationException(BuildLifecycleMessage(item, "purchased"));
		if (item.LastBuyDate is not null && orderDate.Date > item.LastBuyDate.Value.Date)
			throw new InvalidOperationException($"Item '{item.PartNumber}' cannot be purchased after its last-buy date {item.LastBuyDate:yyyy-MM-dd}.{ReplacementSuffix(item)}");
	}

	public static void EnsureSellable(Item item)
	{
		ArgumentNullException.ThrowIfNull(item);
		if (item.LifecycleStatus is ItemLifecycleStatus.Discontinued or ItemLifecycleStatus.Obsolete)
			throw new InvalidOperationException(BuildLifecycleMessage(item, "sold"));
	}

	public static void EnsurePhysicalStockItem(Item item, string operation)
	{
		ArgumentNullException.ThrowIfNull(item);
		if (item.ItemType != ItemType.StockItem)
			throw new InvalidOperationException($"Item '{item.PartNumber}' is {item.ItemType} and cannot participate in the physical {operation} workflow.");
	}

	public static string? GetLifecycleWarning(Item item, DateTime today)
	{
		ArgumentNullException.ThrowIfNull(item);
		if (item.LifecycleStatus == ItemLifecycleStatus.EndOfLife)
			return $"Item '{item.PartNumber}' is end-of-life.{ReplacementSuffix(item)}";
		if (item.EndOfSupportDate is not null && today.Date > item.EndOfSupportDate.Value.Date)
			return $"Support for item '{item.PartNumber}' ended on {item.EndOfSupportDate:yyyy-MM-dd}.{ReplacementSuffix(item)}";
		return null;
	}

	private static string BuildLifecycleMessage(Item item, string operation) =>
		$"Item '{item.PartNumber}' is {item.LifecycleStatus} and cannot be {operation}.{ReplacementSuffix(item)}";

	private static string ReplacementSuffix(Item item) => item.ReplacementItemId is null
		? string.Empty
		: $" Replacement item id: {item.ReplacementItemId.Value}.";
}
