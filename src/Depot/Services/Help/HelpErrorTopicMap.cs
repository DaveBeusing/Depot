// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;

using Depot.Data;

namespace Depot.Services.Help;

public enum HelpErrorCategory
{
	ConcurrencyConflict,
	InsufficientStock,
	DatabaseConnectionFailure
}

public static class HelpErrorTopicMap
{
	private static readonly IReadOnlyDictionary<HelpErrorCategory, string> Topics =
		new Dictionary<HelpErrorCategory, string>
		{
			[HelpErrorCategory.ConcurrencyConflict] = "troubleshooting.concurrency-conflict",
			[HelpErrorCategory.InsufficientStock] = "troubleshooting.insufficient-stock",
			[HelpErrorCategory.DatabaseConnectionFailure] = "troubleshooting.database-connection-failures"
		};

	public static string GetTopicId(HelpErrorCategory category) => Topics[category];

	public static string? TryGetTopicId(Exception exception)
	{
		ArgumentNullException.ThrowIfNull(exception);
		if (exception is ConcurrencyConflictException) return GetTopicId(HelpErrorCategory.ConcurrencyConflict);
		if (exception is DatabaseConnectionException or DbException or TimeoutException)
			return GetTopicId(HelpErrorCategory.DatabaseConnectionFailure);
		return exception.Message.Contains("insufficient stock", StringComparison.OrdinalIgnoreCase) ||
			exception.Message.Contains("negative stock", StringComparison.OrdinalIgnoreCase)
			? GetTopicId(HelpErrorCategory.InsufficientStock)
			: null;
	}
}
