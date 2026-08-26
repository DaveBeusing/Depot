// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;

using Depot.Models;

namespace Depot.Services;

public static class TrackingAllocationTextParser
{
	public static IReadOnlyList<TrackingAllocationInput> Parse(string? text, ItemTrackingMode trackingMode)
	{
		if (trackingMode == ItemTrackingMode.None) return [];
		if (string.IsNullOrWhiteSpace(text)) return [];
		var result = new List<TrackingAllocationInput>();
		foreach (var rawLine in SplitLines(text))
		{
			var parts = rawLine.Split('|', StringSplitOptions.TrimEntries);
			if (parts.Length == 0 || string.IsNullOrWhiteSpace(parts[0])) throw new ArgumentException("Each tracking line requires a serial/lot code.", nameof(text));
			var quantity = 1;
			DateTime? expiry = null;
			if (trackingMode == ItemTrackingMode.Lot && parts.Length >= 2 && !string.IsNullOrWhiteSpace(parts[1]))
			{
				if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out quantity) || quantity <= 0) throw new ArgumentException($"Invalid lot quantity in '{rawLine}'.", nameof(text));
			}
			if (trackingMode == ItemTrackingMode.SerialNumber && parts.Length >= 2 && !string.IsNullOrWhiteSpace(parts[1]))
			{
				if (!TryParseDate(parts[1], out var parsedSerialExpiry)) throw new ArgumentException($"Invalid expiry date in '{rawLine}'. Use yyyy-MM-dd.", nameof(text));
				expiry = parsedSerialExpiry;
			}
			else if (trackingMode == ItemTrackingMode.Lot && parts.Length >= 3 && !string.IsNullOrWhiteSpace(parts[2]))
			{
				if (!TryParseDate(parts[2], out var parsedLotExpiry)) throw new ArgumentException($"Invalid expiry date in '{rawLine}'. Use yyyy-MM-dd.", nameof(text));
				expiry = parsedLotExpiry;
			}
			result.Add(new TrackingAllocationInput { Code = parts[0], Quantity = quantity, ExpiryDate = expiry });
		}
		return result;
	}

	/// <summary>
	/// Parses UI capture text without requiring the presentation layer to load the item policy first.
	/// One field means quantity one (serial-compatible); a numeric second field means lot quantity;
	/// a date second field means serial with expiry; a third field is the lot expiry.
	/// The posting service still validates the parsed shape against the authoritative tracking mode.
	/// </summary>
	public static IReadOnlyList<TrackingAllocationInput> ParseUnspecified(string? text)
	{
		if (string.IsNullOrWhiteSpace(text)) return [];
		var result = new List<TrackingAllocationInput>();
		foreach (var rawLine in SplitLines(text))
		{
			var parts = rawLine.Split('|', StringSplitOptions.TrimEntries);
			if (parts.Length is < 1 or > 3 || string.IsNullOrWhiteSpace(parts[0])) throw new ArgumentException($"Invalid tracking line '{rawLine}'.", nameof(text));
			var quantity = 1;
			DateTime? expiry = null;
			if (parts.Length >= 2 && !string.IsNullOrWhiteSpace(parts[1]))
			{
				if (int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedQuantity))
				{
					if (parsedQuantity <= 0) throw new ArgumentException($"Invalid quantity in '{rawLine}'.", nameof(text));
					quantity = parsedQuantity;
				}
				else if (TryParseDate(parts[1], out var parsedExpiry)) expiry = parsedExpiry;
				else throw new ArgumentException($"Invalid quantity or expiry in '{rawLine}'.", nameof(text));
			}
			if (parts.Length == 3 && !string.IsNullOrWhiteSpace(parts[2]))
			{
				if (!TryParseDate(parts[2], out var parsedExpiry)) throw new ArgumentException($"Invalid expiry date in '{rawLine}'. Use yyyy-MM-dd.", nameof(text));
				expiry = parsedExpiry;
			}
			result.Add(new TrackingAllocationInput { Code = parts[0], Quantity = quantity, ExpiryDate = expiry });
		}
		return result;
	}

	public static string FormatHint(ItemTrackingMode trackingMode) => trackingMode switch
	{
		ItemTrackingMode.SerialNumber => "One serial per line; optional expiry: SERIAL|yyyy-MM-dd",
		ItemTrackingMode.Lot => "One lot per line: LOT|quantity|yyyy-MM-dd (expiry optional)",
		_ => "No tracking data required"
	};

	public const string GenericFormatHint = "Serial: SERIAL or SERIAL|yyyy-MM-dd · Lot: LOT|quantity or LOT|quantity|yyyy-MM-dd";

	private static string[] SplitLines(string text) => text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
	private static bool TryParseDate(string text, out DateTime value)
	{
		if (DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
		{
			value = parsed.Date;
			return true;
		}
		value = default;
		return false;
	}
}
