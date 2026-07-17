// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed record DatabaseOverview(
	DatabaseProvider Provider,
	string ProviderDisplayName,
	string ConnectionDisplayName,
	int SchemaVersion,
	long SizeBytes,
	DateTime? LastSuccessfulBackupUtc);

public sealed record DatabaseBackupInfo(
	string FilePath,
	DateTime CreatedUtc,
	long SizeBytes,
	DatabaseProvider Provider,
	int SchemaVersion,
	bool IsValid,
	string ValidationMessage)
{
	public string CreatedDisplay =>
		CreatedUtc == DateTime.MinValue ? "Unknown" : CreatedUtc.ToLocalTime().ToString("g");

	public string SizeDisplay => FormatSize(SizeBytes);

	private static string FormatSize(long bytes)
	{
		string[] units = ["B", "KB", "MB", "GB", "TB"];
		var size = (double)Math.Max(0, bytes);
		var unit = 0;
		while (size >= 1024 && unit < units.Length - 1)
		{
			size /= 1024;
			unit++;
		}

		return $"{size:0.##} {units[unit]}";
	}
}

public sealed record DatabaseOperationProgress(
	int Percentage,
	string Message);

public sealed record DatabaseIntegrityResult(
	bool IsValid,
	string Message);
