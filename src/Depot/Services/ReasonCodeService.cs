// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class ReasonCodeService
{
	private readonly ReasonCodeRepository _repository;
	private readonly AuditService _auditService;
	private readonly AsyncCache<IReadOnlyList<ReasonCode>> _activeCache = new(TimeSpan.FromMinutes(5));

	public ReasonCodeService(ReasonCodeRepository repository, AuditService auditService)
	{
		_repository = repository;
		_auditService = auditService;
	}

	public Task<IReadOnlyList<ReasonCode>> SearchAsync(
		string? searchText,
		CancellationToken cancellationToken = default) =>
		_repository.SearchAsync(searchText, cancellationToken);

	public Task<IReadOnlyList<ReasonCode>> GetActiveAsync(CancellationToken cancellationToken = default) =>
		_activeCache.GetAsync(_repository.ListActiveAsync, cancellationToken);

	public async Task<ReasonCode> SaveAsync(
		long id,
		long version,
		string code,
		string name,
		string? description,
		CancellationToken cancellationToken = default)
	{
		_auditService.RequirePermission(ApplicationPermission.MasterDataManage);
		code = NormalizeCode(code);
		name = name.Trim();
		description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
		if (string.IsNullOrWhiteSpace(code))
			throw new ArgumentException("Reason code is required.", nameof(code));
		if (code.Length > 50)
			throw new ArgumentException("Reason code must not exceed 50 characters.", nameof(code));
		if (code.Any(character => character is not (>= 'A' and <= 'Z') and not (>= '0' and <= '9') and not '_'))
			throw new ArgumentException("Reason code may only contain uppercase letters, numbers and underscores.", nameof(code));
		if (string.IsNullOrWhiteSpace(name))
			throw new ArgumentException("Reason code name is required.", nameof(name));
		if (name.Length > 200)
			throw new ArgumentException("Reason code name must not exceed 200 characters.", nameof(name));
		if (description?.Length > 500)
			throw new ArgumentException("Reason code description must not exceed 500 characters.", nameof(description));

		var duplicate = await _repository.GetByCodeAsync(code, cancellationToken);
		if (duplicate is not null && duplicate.Id != id)
			throw new InvalidOperationException($"Reason code '{code}' already exists.");

		if (id == 0)
		{
			var created = new ReasonCode { Code = code, Name = name, Description = description, IsActive = true };
			created.Id = await _repository.CreateAsync(created, cancellationToken);
			await _auditService.RecordCreatedAsync(created.Id, created, cancellationToken);
			_activeCache.Invalidate();
			return created;
		}

		var existing = await _repository.GetByIdAsync(id, cancellationToken)
			?? throw new InvalidOperationException($"Reason code with id '{id}' was not found.");
		if (existing.Version != version) throw new ConcurrencyConflictException("reason code");
		if (!string.Equals(existing.Code, code, StringComparison.Ordinal))
			throw new InvalidOperationException("The technical reason code cannot be changed after creation.");
		var before = Copy(existing);
		existing.Name = name;
		existing.Description = description;
		if (!await _repository.UpdateAsync(existing, cancellationToken))
			throw new ConcurrencyConflictException("reason code");
		existing.Version++;
		await _auditService.RecordUpdatedAsync(existing.Id, before, existing, cancellationToken);
		_activeCache.Invalidate();
		return existing;
	}

	public async Task<ReasonCode> SetActiveAsync(
		long id,
		long version,
		bool isActive,
		CancellationToken cancellationToken = default)
	{
		_auditService.RequirePermission(ApplicationPermission.MasterDataManage);
		var reasonCode = await _repository.GetByIdAsync(id, cancellationToken)
			?? throw new InvalidOperationException($"Reason code with id '{id}' was not found.");
		if (!isActive && reasonCode.IsSystem && ReasonCodeSystemCodes.IsRequiredByActiveWorkflow(reasonCode.Code))
			throw new InvalidOperationException($"System reason code '{reasonCode.Code}' is required by an active workflow and cannot be deactivated.");
		if (reasonCode.Version != version ||
			!await _repository.SetActiveAsync(id, version, isActive, cancellationToken))
			throw new ConcurrencyConflictException("reason code");
		var before = Copy(reasonCode);
		reasonCode.IsActive = isActive;
		reasonCode.Version++;
		await _auditService.RecordUpdatedAsync(reasonCode.Id, before, reasonCode, cancellationToken);
		_activeCache.Invalidate();
		return reasonCode;
	}

	private static ReasonCode Copy(ReasonCode source) => new()
	{
		Id = source.Id,
		Code = source.Code,
		Name = source.Name,
		Description = source.Description,
		IsSystem = source.IsSystem,
		IsActive = source.IsActive,
		Version = source.Version
	};

	private static string NormalizeCode(string value) => value.Trim().ToUpperInvariant();
}
