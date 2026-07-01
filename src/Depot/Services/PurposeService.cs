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

	public PurposeService(
		PurposeRepository purposeRepository)
	{
		_purposeRepository = purposeRepository;
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

		return purpose;
	}

	public Purpose UpdatePurpose(
		long id,
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

		_purposeRepository.Update(
			purpose);

		return purpose;
	}

	public void DeactivatePurpose(
		long id)
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

		_purposeRepository.Deactivate(
			id);
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

}