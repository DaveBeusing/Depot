// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.Concurrent;

using Depot.Models;

namespace Depot.Services;

/// <summary>
/// Process-local staging for serial/lot text entered in posting grids. Draft business records stay
/// free of tracking assignments; values are validated and persisted only by the posting transaction.
/// </summary>
public static class TrackingCaptureSession
{
	private static readonly ConcurrentDictionary<string, string> Values = new(StringComparer.Ordinal);
	private static readonly ConcurrentDictionary<string, byte> InventoryLinks = new(StringComparer.Ordinal);

	public static void Set(string scope, long lineKey, string? text, params long[] inventoryIds)
	{
		var normalizedScope = NormalizeScope(scope);
		var key = Key(normalizedScope, lineKey);
		var normalizedInventories = inventoryIds.Where(id => id > 0).Distinct().ToArray();

		if (string.IsNullOrWhiteSpace(text))
		{
			Values.TryRemove(key, out _);
			RemoveLineLinks(normalizedScope, lineKey);
			return;
		}

		// A physical workflow may only have one active capture for the same inventory in a scope.
		// Replacing an older link prevents a posted/abandoned document from being reused by a later one.
		foreach (var inventoryId in normalizedInventories)
		{
			RemoveOtherInventoryLinks(normalizedScope, inventoryId, lineKey);
		}

		Values[key] = text;
		foreach (var inventoryId in normalizedInventories)
		{
			InventoryLinks[InventoryKey(normalizedScope, inventoryId, lineKey)] = 0;
		}
	}

	public static string? GetText(string scope, long lineKey) =>
		Values.TryGetValue(Key(NormalizeScope(scope), lineKey), out var value) ? value : null;

	public static IReadOnlyList<TrackingAllocationInput> GetAllocations(string scope, long lineKey) =>
		TrackingAllocationTextParser.ParseUnspecified(GetText(scope, lineKey));

	public static IReadOnlyList<TrackingAllocationInput> ResolveForInventory(string scope, long inventoryId, int movementQuantity)
	{
		if (inventoryId <= 0 || movementQuantity == 0) return [];
		var normalizedScope = NormalizeScope(scope);
		var prefix = $"{normalizedScope}:{inventoryId}:";
		var candidates = new List<IReadOnlyList<TrackingAllocationInput>>();
		foreach (var link in InventoryLinks.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)))
		{
			if (!long.TryParse(link[prefix.Length..], out var lineKey)) continue;
			var allocations = GetAllocations(normalizedScope, lineKey);
			if (allocations.Sum(allocation => allocation.Quantity) == Math.Abs(movementQuantity)) candidates.Add(allocations);
		}
		if (candidates.Count == 0) return [];
		if (candidates.Count > 1) throw new InvalidOperationException("Multiple serial/lot captures match this stock movement. Keep only the tracking input for the line being posted.");
		return candidates[0];
	}

	public static IReadOnlyDictionary<long, IReadOnlyList<TrackingAllocationInput>> BuildMap(string scope, IEnumerable<long> lineKeys)
	{
		var result = new Dictionary<long, IReadOnlyList<TrackingAllocationInput>>();
		foreach (var lineKey in lineKeys.Distinct())
		{
			var allocations = GetAllocations(scope, lineKey);
			if (allocations.Count > 0) result[lineKey] = allocations;
		}
		return result;
	}

	public static void Clear(string scope, IEnumerable<long> lineKeys)
	{
		var normalizedScope = NormalizeScope(scope);
		foreach (var lineKey in lineKeys.Distinct())
		{
			Values.TryRemove(Key(normalizedScope, lineKey), out _);
			RemoveLineLinks(normalizedScope, lineKey);
		}
	}

	private static void RemoveOtherInventoryLinks(string scope, long inventoryId, long lineKey)
	{
		var prefix = $"{scope}:{inventoryId}:";
		foreach (var existing in InventoryLinks.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)).ToArray())
		{
			if (!long.TryParse(existing[prefix.Length..], out var existingLineKey) || existingLineKey == lineKey) continue;
			InventoryLinks.TryRemove(existing, out _);
			if (!InventoryLinks.Keys.Any(link => link.EndsWith($":{existingLineKey}", StringComparison.Ordinal) && link.StartsWith($"{scope}:", StringComparison.Ordinal)))
			{
				Values.TryRemove(Key(scope, existingLineKey), out _);
			}
		}
	}

	private static void RemoveLineLinks(string scope, long lineKey)
	{
		var prefix = $"{scope}:";
		var suffix = $":{lineKey}";
		foreach (var link in InventoryLinks.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal) && key.EndsWith(suffix, StringComparison.Ordinal)).ToArray())
		{
			InventoryLinks.TryRemove(link, out _);
		}
	}

	private static string NormalizeScope(string scope)
	{
		if (string.IsNullOrWhiteSpace(scope)) throw new ArgumentException("A tracking capture scope is required.", nameof(scope));
		return scope.Trim();
	}

	private static string Key(string scope, long lineKey)
	{
		if (lineKey <= 0) throw new ArgumentOutOfRangeException(nameof(lineKey));
		return $"{scope}:{lineKey}";
	}

	private static string InventoryKey(string scope, long inventoryId, long lineKey) => $"{scope}:{inventoryId}:{lineKey}";
}
