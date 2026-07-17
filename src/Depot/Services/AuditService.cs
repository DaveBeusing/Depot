// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Text.Json;

using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class AuditService
{
	private static readonly JsonSerializerOptions SerializerOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	private readonly AuditRepository _auditRepository;
	private readonly AuthorizationService _authorizationService;

	public AuditService(
		AuditRepository auditRepository,
		AuthorizationService authorizationService)
	{
		_auditRepository = auditRepository;
		_authorizationService = authorizationService;
	}

	public void RecordCreated<T>(long entityId, T entity) where T : class =>
		Record(typeof(T).Name, entityId, "Created", null, entity);

	public Task RecordCreatedAsync<T>(
		long entityId,
		T entity,
		CancellationToken cancellationToken) where T : class =>
		RecordAsync(typeof(T).Name, entityId, "Created", null, entity, cancellationToken);

	public AuditEntry CreateCreatedEntry<T>(long entityId, T entity) where T : class =>
		CreateEntry(typeof(T).Name, entityId, "Created", null, entity);

	public void RecordUpdated<T>(long entityId, T before, T after) where T : class =>
		Record(typeof(T).Name, entityId, "Updated", before, after);

	public Task RecordUpdatedAsync<T>(
		long entityId,
		T before,
		T after,
		CancellationToken cancellationToken) where T : class =>
		RecordAsync(typeof(T).Name, entityId, "Updated", before, after, cancellationToken);

	public void RecordDeactivated<T>(long entityId, T before, T after) where T : class =>
		Record(typeof(T).Name, entityId, "Deactivated", before, after);

	public Task RecordDeactivatedAsync<T>(
		long entityId,
		T before,
		T after,
		CancellationToken cancellationToken) where T : class =>
		RecordAsync(typeof(T).Name, entityId, "Deactivated", before, after, cancellationToken);

	private void Record<T>(
		string entityType,
		long entityId,
		string action,
		T? before,
		T? after) where T : class
	{
		_auditRepository.Create(CreateEntry(entityType, entityId, action, before, after));
	}

	private async Task RecordAsync<T>(
		string entityType,
		long entityId,
		string action,
		T? before,
		T? after,
		CancellationToken cancellationToken) where T : class
	{
		await _auditRepository.CreateAsync(
			CreateEntry(entityType, entityId, action, before, after),
			cancellationToken);
	}

	private AuditEntry CreateEntry<T>(
		string entityType,
		long entityId,
		string action,
		T? before,
		T? after) where T : class
	{
		var user = _authorizationService.CurrentUser;
		return new AuditEntry
			{
				TimestampUtc = DateTime.UtcNow,
				UserId = user?.Id,
				UserEmail = user?.Email ?? "system",
				EntityType = entityType,
				EntityId = entityId,
				Action = action,
				BeforeJson = Serialize(before),
				AfterJson = Serialize(after)
			};
	}

	private static string? Serialize<T>(T? value) where T : class =>
		value is null
			? null
			: JsonSerializer.Serialize(value, SerializerOptions);
}
