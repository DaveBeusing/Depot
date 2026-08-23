// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class AuditLogService
{
	private const int ExportPageSize = 500;
	private const int MaximumEvidenceEvents = 10000;
	private static readonly JsonSerializerOptions EvidenceJsonOptions = new()
	{
		WriteIndented = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	private readonly AuditRepository _repository;
	private readonly IAuthorizationService _authorization;
	private readonly AuditJsonSanitizer _sanitizer;

	public AuditLogService(
		AuditRepository repository,
		IAuthorizationService authorization,
		AuditJsonSanitizer sanitizer)
	{
		_repository = repository;
		_authorization = authorization;
		_sanitizer = sanitizer;
	}

	public Task<PageResult<AuditLogListItem>> SearchAsync(
		AuditLogFilter filter, int pageNumber, int pageSize, CancellationToken cancellationToken)
	{
		EnsureAuthorized();
		return _repository.SearchPageAsync(filter, pageNumber, pageSize, cancellationToken);
	}

	public Task<AuditLogFilterOptions> GetFilterOptionsAsync(CancellationToken cancellationToken)
	{
		EnsureAuthorized();
		return _repository.GetFilterOptionsAsync(cancellationToken);
	}

	public async Task<SanitizedAuditDetails?> GetDetailsAsync(long id, CancellationToken cancellationToken)
	{
		EnsureAuthorized();
		var entry = await _repository.GetDetailsAsync(id, cancellationToken);
		return entry is null
			? null
			: new SanitizedAuditDetails(
				entry,
				_sanitizer.Sanitize(entry.BeforeJson),
				_sanitizer.Sanitize(entry.AfterJson),
				_sanitizer.Compare(entry.BeforeJson, entry.AfterJson));
	}

	public async Task ExportCsvAsync(
		AuditLogFilter filter,
		string filePath,
		IProgress<int>? progress,
		CancellationToken cancellationToken)
	{
		EnsureExportAuthorized();
		await using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true);
		await using var writer = new StreamWriter(stream, new UTF8Encoding(true));
		await writer.WriteLineAsync("Id,TimestampUtc,TimestampLocal,User,Action,EntityType,EntityId,BeforeJson,AfterJson");
		var offset = 0;
		while (true)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var entries = await _repository.GetExportSliceAsync(filter, offset, ExportPageSize, cancellationToken);
			foreach (var entry in entries)
			{
				var values = new[]
				{
					entry.Id.ToString(CultureInfo.InvariantCulture),
					entry.TimestampUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
					entry.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture),
					entry.UserEmail, entry.Action, entry.EntityType,
					entry.EntityId.ToString(CultureInfo.InvariantCulture),
					_sanitizer.Sanitize(entry.BeforeJson),
					_sanitizer.Sanitize(entry.AfterJson)
				};
				await writer.WriteLineAsync(string.Join(',', values.Select(EscapeCsv)));
			}
			offset += entries.Count;
			progress?.Report(offset);
			if (entries.Count < ExportPageSize) break;
		}
	}

	public async Task ExportBusinessRecordEvidenceAsync(
		string entityType,
		long entityId,
		string filePath,
		CancellationToken cancellationToken)
	{
		EnsureExportAuthorized();
		if (entityId <= 0) throw new ArgumentOutOfRangeException(nameof(entityId));
		var classification = BusinessRecordCatalog.Require(entityType);
		var history = await _repository.GetEntityHistoryAsync(
			entityType,
			entityId,
			MaximumEvidenceEvents,
			cancellationToken);
		if (history.Count == 0) throw new InvalidOperationException("No audit history exists for the selected business record.");
		var chronological = history.OrderBy(entry => entry.TimestampUtc).ThenBy(entry => entry.Id).ToArray();
		var package = new
		{
			schema = "depot-business-record-evidence/1.0",
			exportedAtUtc = DateTime.UtcNow,
			entityType,
			entityId,
			classification,
			eventCount = chronological.Length,
			currentSnapshot = ParseJson(chronological.Last().AfterJson),
			events = chronological.Select(entry => new
			{
				entry.Id,
				timestampUtc = entry.TimestampUtc.ToUniversalTime(),
				entry.UserId,
				entry.UserEmail,
				entry.Action,
				before = ParseJson(entry.BeforeJson),
				after = ParseJson(entry.AfterJson)
			}).ToArray()
		};

		await using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true);
		await JsonSerializer.SerializeAsync(stream, package, EvidenceJsonOptions, cancellationToken);
	}

	private JsonElement? ParseJson(string? value)
	{
		if (string.IsNullOrWhiteSpace(value)) return null;
		var sanitized = _sanitizer.Sanitize(value);
		using var document = JsonDocument.Parse(sanitized);
		return document.RootElement.Clone();
	}

	private void EnsureAuthorized()
	{
		_authorization.RequirePermission(ApplicationPermission.AuditLogView);
	}

	private void EnsureExportAuthorized()
	{
		EnsureAuthorized();
		_authorization.RequirePermission(ApplicationPermission.AuditLogExport);
	}

	private static string EscapeCsv(string value) =>
		$"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}
