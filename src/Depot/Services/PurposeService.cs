// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

/// <summary>
/// Provides business logic for purpose management.
/// </summary>
public sealed class PurposeService
{
	private readonly PurposeRepository _purposeRepository;
	private readonly AuditService _auditService;
	private readonly AsyncCache<IReadOnlyList<Purpose>> _cache = new(TimeSpan.FromMinutes(5));

	public PurposeService(
		PurposeRepository purposeRepository,
		AuditService auditService)
	{
		_purposeRepository = purposeRepository;
		_auditService = auditService;
	}

	public IReadOnlyList<Purpose> GetPurposes()
	{
		return _purposeRepository.GetAll();
	}

	public Purpose CreatePurpose(
		string name,
		string? description)
	{
		name =
			name.Trim();

		description =
			string.IsNullOrWhiteSpace(description)
				? null
				: description.Trim();

		if (string.IsNullOrWhiteSpace(name))
		{
			throw new ArgumentException(
				"Purpose name is required.",
				nameof(name));
		}

		var existingPurpose =
			_purposeRepository.GetByName(
				name);

		if (existingPurpose is not null)
		{
			throw new InvalidOperationException(
				$"Purpose '{name}' already exists.");
		}

		var purpose =
			new Purpose
			{
				Name =
					name,

				Description =
					description,

				IsActive =
					true
			};

		purpose.Id =
			_purposeRepository.Create(
				purpose);

		_auditService.RecordCreated(purpose.Id, purpose);

		return purpose;
	}

	public Purpose UpdatePurpose(
		long id,
		long expectedVersion,
		string name,
		string? description)
	{
		name =
			name.Trim();

		description =
			string.IsNullOrWhiteSpace(description)
				? null
				: description.Trim();

		if (id <= 0)
		{
			throw new ArgumentException(
				"Purpose id is required.",
				nameof(id));
		}

		if (string.IsNullOrWhiteSpace(name))
		{
			throw new ArgumentException(
				"Purpose name is required.",
				nameof(name));
		}

		var purpose =
			_purposeRepository.GetById(
				id);

		if (purpose is null)
		{
			throw new InvalidOperationException(
				$"Purpose with id '{id}' was not found.");
		}

		if (purpose.Version != expectedVersion)
		{
			throw new ConcurrencyConflictException("purpose");
		}

		var before = Copy(purpose);

		var existingPurpose =
			_purposeRepository.GetByName(
				name);

		if (existingPurpose is not null &&
			existingPurpose.Id != id)
		{
			throw new InvalidOperationException(
				$"Purpose '{name}' already exists.");
		}

		purpose.Name =
			name;

		purpose.Description =
			description;

		if (!_purposeRepository.Update(purpose))
		{
			throw new ConcurrencyConflictException("purpose");
		}

		purpose.Version++;
		_auditService.RecordUpdated(purpose.Id, before, purpose);

		return purpose;
	}

	public void DeactivatePurpose(
		long id,
		long expectedVersion)
	{
		if (id <= 0)
		{
			throw new ArgumentException(
				"Purpose id is required.",
				nameof(id));
		}

		var purpose =
			_purposeRepository.GetById(
				id);

		if (purpose is null)
		{
			throw new InvalidOperationException(
				$"Purpose with id '{id}' was not found.");
		}

		if (purpose.Version != expectedVersion ||
			!_purposeRepository.Deactivate(id, expectedVersion))
		{
			throw new ConcurrencyConflictException("purpose");
		}

		var before = Copy(purpose);
		purpose.IsActive = false;
		purpose.Version++;
		_auditService.RecordDeactivated(purpose.Id, before, purpose);
	}

	public Purpose GetOrCreatePurpose(
		string name)
	{
		name = name.Trim();

		if (string.IsNullOrWhiteSpace(name))
		{
			throw new ArgumentException(
				"Purpose name is required.",
				nameof(name));
		}

		var existingPurpose =
			_purposeRepository.GetByName(
				name);

		if (existingPurpose is not null)
		{
			return existingPurpose;
		}

		return CreatePurpose(
			name,
			null);
	}

	public Task<IReadOnlyList<Purpose>> GetPurposesAsync(CancellationToken cancellationToken) =>
		_cache.GetAsync(_purposeRepository.ListActiveAsync, cancellationToken);

	public async Task<Purpose> CreatePurposeAsync(
		string name,
		string? description,
		CancellationToken cancellationToken)
	{
		(name, description) = Normalize(name, description);
		ValidateName(name);
		if (await _purposeRepository.GetByNameAsync(name, cancellationToken) is not null)
			throw new InvalidOperationException($"Purpose '{name}' already exists.");
		var purpose = new Purpose { Name = name, Description = description, IsActive = true };
		purpose.Id = await _purposeRepository.CreateAsync(purpose, cancellationToken);
		await _auditService.RecordCreatedAsync(purpose.Id, purpose, cancellationToken);
		_cache.Invalidate();
		return purpose;
	}

	public async Task<Purpose> GetOrCreatePurposeAsync(
		string name,
		CancellationToken cancellationToken = default)
	{
		var (normalizedName, _) = Normalize(name, null);
		ValidateName(normalizedName);
		var existing = await _purposeRepository.GetByNameAsync(normalizedName, cancellationToken);
		return existing ?? await CreatePurposeAsync(normalizedName, null, cancellationToken);
	}

	public async Task<Purpose> UpdatePurposeAsync(
		long id,
		long expectedVersion,
		string name,
		string? description,
		CancellationToken cancellationToken)
	{
		(name, description) = Normalize(name, description);
		if (id <= 0) throw new ArgumentException("Purpose id is required.", nameof(id));
		ValidateName(name);
		var purpose = await _purposeRepository.GetByIdAsync(id, cancellationToken)
			?? throw new InvalidOperationException($"Purpose with id '{id}' was not found.");
		if (purpose.Version != expectedVersion) throw new ConcurrencyConflictException("purpose");
		var duplicate = await _purposeRepository.GetByNameAsync(name, cancellationToken);
		if (duplicate is not null && duplicate.Id != id)
			throw new InvalidOperationException($"Purpose '{name}' already exists.");
		var before = Copy(purpose);
		purpose.Name = name;
		purpose.Description = description;
		if (!await _purposeRepository.UpdateAsync(purpose, cancellationToken))
			throw new ConcurrencyConflictException("purpose");
		purpose.Version++;
		await _auditService.RecordUpdatedAsync(purpose.Id, before, purpose, cancellationToken);
		_cache.Invalidate();
		return purpose;
	}

	public async Task DeactivatePurposeAsync(long id, long expectedVersion, CancellationToken cancellationToken)
	{
		var purpose = await _purposeRepository.GetByIdAsync(id, cancellationToken)
			?? throw new InvalidOperationException($"Purpose with id '{id}' was not found.");
		if (purpose.Version != expectedVersion ||
			!await _purposeRepository.DeactivateAsync(id, expectedVersion, cancellationToken))
			throw new ConcurrencyConflictException("purpose");
		var before = Copy(purpose);
		purpose.IsActive = false;
		purpose.Version++;
		await _auditService.RecordDeactivatedAsync(purpose.Id, before, purpose, cancellationToken);
		_cache.Invalidate();
	}

	private static Purpose Copy(Purpose purpose) =>
		new()
		{
			Id = purpose.Id,
			Name = purpose.Name,
			Description = purpose.Description,
			IsActive = purpose.IsActive,
			Version = purpose.Version
		};

	private static (string Name, string? Description) Normalize(string name, string? description) =>
		(name.Trim(), string.IsNullOrWhiteSpace(description) ? null : description.Trim());

	private static void ValidateName(string name)
	{
		if (string.IsNullOrWhiteSpace(name))
			throw new ArgumentException("Purpose name is required.", nameof(name));
	}

}
