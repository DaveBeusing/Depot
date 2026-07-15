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

	private static Purpose Copy(Purpose purpose) =>
		new()
		{
			Id = purpose.Id,
			Name = purpose.Name,
			Description = purpose.Description,
			IsActive = purpose.IsActive,
			Version = purpose.Version
		};

}
