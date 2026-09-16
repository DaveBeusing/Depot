// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Text.Json;

using Depot.Data;
using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class WorkspaceViewService
{
	private const int MaximumViewsPerWorkspace = 20;
	private const int MaximumDefinitionBytes = 64 * 1024;
	private readonly IDatabaseTransactionRunner _transactions;
	private readonly WorkspaceViewRepository _repository;
	private readonly IAuthorizationService _authorization;
	private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

	public WorkspaceViewService(
		IDatabaseTransactionRunner transactions,
		WorkspaceViewRepository repository,
		IAuthorizationService authorization)
	{
		_transactions = transactions;
		_repository = repository;
		_authorization = authorization;
	}

	public async Task<IReadOnlyList<SavedWorkspaceView>> ListAsync(string workspaceId, CancellationToken cancellationToken = default)
	{
		var userId = RequireUser();
		workspaceId = NormalizeId(workspaceId, nameof(workspaceId), 160);
		var records = await _repository.ListAsync(userId, workspaceId, cancellationToken);
		var result = new List<SavedWorkspaceView>(records.Count);
		foreach (var record in records)
		{
			if (!TryDeserialize(record.DefinitionJson, out var definition)) continue;
			result.Add(ToSaved(record, definition));
		}
		return result;
	}

	public async Task<SavedWorkspaceView> SaveAsync(
		string workspaceId,
		Guid? viewId,
		string name,
		WorkspaceViewDefinition definition,
		long? expectedVersion,
		CancellationToken cancellationToken = default)
	{
		var userId = RequireUser();
		workspaceId = NormalizeId(workspaceId, nameof(workspaceId), 160);
		name = NormalizeName(name);
		ValidateDefinition(definition);
		var json = JsonSerializer.Serialize(definition, _jsonOptions);
		if (System.Text.Encoding.UTF8.GetByteCount(json) > MaximumDefinitionBytes)
			throw new InvalidOperationException("The saved view definition is too large.");

		var id = viewId.GetValueOrDefault();
		var isNew = viewId is null || id == Guid.Empty;
		if (isNew)
		{
			var existing = await _repository.ListAsync(userId, workspaceId, cancellationToken);
			if (existing.Count >= MaximumViewsPerWorkspace)
				throw new InvalidOperationException($"A workspace can contain at most {MaximumViewsPerWorkspace} saved views.");
			id = Guid.NewGuid();
		}
		else if (expectedVersion is null or <= 0)
		{
			throw new InvalidOperationException("An existing saved view requires its current version.");
		}

		var now = DateTime.UtcNow;
		await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			if (isNew)
			{
				await _repository.InsertAsync(transaction, userId, workspaceId, id, name, json, now, token);
			}
			else
			{
				var affected = await _repository.UpdateAsync(transaction, userId, workspaceId, id, name, json, expectedVersion!.Value, now, token);
				if (affected != 1) throw new InvalidOperationException("The saved view changed in another session. Reload it and try again.");
			}
			return true;
		}, cancellationToken);

		var saved = await _repository.GetAsync(userId, workspaceId, id, cancellationToken)
			?? throw new InvalidOperationException("The saved view could not be reloaded after saving.");
		if (!TryDeserialize(saved.DefinitionJson, out var persistedDefinition))
			throw new InvalidOperationException("The saved view definition could not be reloaded after saving.");
		return ToSaved(saved, persistedDefinition);
	}

	public async Task SetDefaultAsync(string workspaceId, Guid viewId, CancellationToken cancellationToken = default)
	{
		var userId = RequireUser();
		workspaceId = NormalizeId(workspaceId, nameof(workspaceId), 160);
		if (viewId == Guid.Empty) throw new ArgumentException("A saved view id is required.", nameof(viewId));
		var existing = await _repository.GetAsync(userId, workspaceId, viewId, cancellationToken);
		if (existing is null) throw new InvalidOperationException("The saved view does not exist for the current user and workspace.");
		await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			await _repository.SetDefaultAsync(transaction, userId, workspaceId, viewId, DateTime.UtcNow, token);
			return true;
		}, cancellationToken);
	}

	public async Task DeleteAsync(string workspaceId, Guid viewId, long expectedVersion, CancellationToken cancellationToken = default)
	{
		var userId = RequireUser();
		workspaceId = NormalizeId(workspaceId, nameof(workspaceId), 160);
		if (viewId == Guid.Empty) throw new ArgumentException("A saved view id is required.", nameof(viewId));
		if (expectedVersion <= 0) throw new ArgumentOutOfRangeException(nameof(expectedVersion));
		await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			await _repository.ClearDefaultAsync(transaction, userId, workspaceId, viewId, token);
			var affected = await _repository.DeleteAsync(transaction, userId, workspaceId, viewId, expectedVersion, token);
			if (affected != 1) throw new InvalidOperationException("The saved view changed in another session. Reload it and try again.");
			return true;
		}, cancellationToken);
	}

	private long RequireUser()
	{
		var user = _authorization.CurrentUser;
		if (user is not { IsActive: true } || user.Id <= 0)
			throw new UnauthorizedAccessException("A signed-in active user is required for workspace preferences.");
		return user.Id;
	}

	private bool TryDeserialize(string json, out WorkspaceViewDefinition definition)
	{
		definition = new WorkspaceViewDefinition();
		try
		{
			var candidate = JsonSerializer.Deserialize<WorkspaceViewDefinition>(json, _jsonOptions);
			if (candidate is null || candidate.FormatVersion != WorkspaceViewDefinition.CurrentFormatVersion) return false;
			ValidateDefinition(candidate);
			definition = candidate;
			return true;
		}
		catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException or InvalidOperationException)
		{
			return false;
		}
	}

	private static void ValidateDefinition(WorkspaceViewDefinition definition)
	{
		ArgumentNullException.ThrowIfNull(definition);
		if (definition.FormatVersion != WorkspaceViewDefinition.CurrentFormatVersion)
			throw new InvalidOperationException($"Workspace view format {definition.FormatVersion} is not supported.");
		if (!Enum.IsDefined(definition.GridDensity)) throw new InvalidOperationException("The grid density is invalid.");
		if (definition.Columns.Count > 128 || definition.Sorts.Count > 16 || definition.Filters.Count > 32)
			throw new InvalidOperationException("The workspace view contains too many state entries.");
		ValidateIds(definition.Columns.Select(column => column.ColumnId), "column");
		ValidateIds(definition.Sorts.Select(sort => sort.ColumnId), "sort column");
		ValidateIds(definition.Filters.Select(filter => filter.FilterId), "filter");
		if (definition.Columns.GroupBy(column => column.ColumnId, StringComparer.Ordinal).Any(group => group.Count() > 1))
			throw new InvalidOperationException("A workspace view cannot contain duplicate column ids.");
		if (definition.Filters.GroupBy(filter => filter.FilterId, StringComparer.Ordinal).Any(group => group.Count() > 1))
			throw new InvalidOperationException("A workspace view cannot contain duplicate filter ids.");
		if (definition.Sorts.Any(sort => !Enum.IsDefined(sort.Direction)))
			throw new InvalidOperationException("A workspace view contains an invalid sort direction.");
	}

	private static void ValidateIds(IEnumerable<string> ids, string description)
	{
		foreach (var id in ids) NormalizeId(id, description, 160);
	}

	private static string NormalizeId(string value, string parameterName, int maximumLength)
	{
		if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A semantic id is required.", parameterName);
		var result = value.Trim();
		if (result.Length > maximumLength) throw new ArgumentException($"The semantic id exceeds {maximumLength} characters.", parameterName);
		if (result.Any(character => !(char.IsLetterOrDigit(character) || character is '.' or '-' or '_' or ':')))
			throw new ArgumentException("Semantic ids may only contain letters, digits, '.', '-', '_' and ':'.", parameterName);
		return result;
	}

	private static string NormalizeName(string value)
	{
		if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A saved view name is required.", nameof(value));
		var result = value.Trim();
		if (result.Length > 120) throw new ArgumentException("A saved view name cannot exceed 120 characters.", nameof(value));
		return result;
	}

	private static SavedWorkspaceView ToSaved(UserWorkspaceViewRecord record, WorkspaceViewDefinition definition) =>
		new(record.ViewId, record.WorkspaceId, record.Name, definition, record.IsDefault, record.UpdatedUtc, record.Version);
}

internal static class WorkspaceViewRuntime
{
	private static WorkspaceViewService? _service;

	public static WorkspaceViewService Current => _service ?? throw new InvalidOperationException("Workspace view services have not been configured.");

	public static void Configure(WorkspaceViewService service) => _service = service ?? throw new ArgumentNullException(nameof(service));

	public static void Clear(WorkspaceViewService service)
	{
		if (ReferenceEquals(_service, service)) _service = null;
	}
}
