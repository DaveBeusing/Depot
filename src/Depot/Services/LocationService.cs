// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

/// <summary>
/// Provides business logic for location management.
/// </summary>
public sealed class LocationService
{
	private readonly LocationRepository _locationRepository;
	private readonly AuditService _auditService;

	public LocationService(
		LocationRepository locationRepository,
		AuditService auditService)
	{
		_locationRepository = locationRepository;
		_auditService = auditService;
	}

	public IReadOnlyList<Location> GetLocations()
	{
		return _locationRepository.GetAll();
	}

	public Location CreateLocation(
		string name,
		string? description)
	{
		name = name.Trim();

		description =
			string.IsNullOrWhiteSpace(description)
				? null
				: description.Trim();

		if (string.IsNullOrWhiteSpace(name))
		{
			throw new ArgumentException(
				"Location name is required.",
				nameof(name));
		}

		var existingLocation =
			_locationRepository.GetByName(
				name);

		if (existingLocation is not null)
		{
			throw new InvalidOperationException(
				$"Location '{name}' already exists.");
		}

		var location =
			new Location
			{
				Name = name,
				Description = description,
				IsActive = true
			};

		location.Id =
			_locationRepository.Create(
				location);

		_auditService.RecordCreated(location.Id, location);

		return location;
	}

	public Location UpdateLocation(
		long id,
		long expectedVersion,
		string name,
		string? description)
	{
		name = name.Trim();

		description =
			string.IsNullOrWhiteSpace(description)
				? null
				: description.Trim();

		if (id <= 0)
		{
			throw new ArgumentException(
				"Location id is required.",
				nameof(id));
		}

		if (string.IsNullOrWhiteSpace(name))
		{
			throw new ArgumentException(
				"Location name is required.",
				nameof(name));
		}

		var location =
			_locationRepository.GetById(
				id);

		if (location is null)
		{
			throw new InvalidOperationException(
				$"Location with id '{id}' was not found.");
		}

		if (location.Version != expectedVersion)
		{
			throw new ConcurrencyConflictException("location");
		}

		var before = Copy(location);

		var existingLocation =
			_locationRepository.GetByName(
				name);

		if (existingLocation is not null &&
			existingLocation.Id != id)
		{
			throw new InvalidOperationException(
				$"Location '{name}' already exists.");
		}

		location.Name = name;
		location.Description = description;

		if (!_locationRepository.Update(location))
		{
			throw new ConcurrencyConflictException("location");
		}

		location.Version++;
		_auditService.RecordUpdated(location.Id, before, location);

		return location;
	}

	public void DeactivateLocation(
		long id,
		long expectedVersion)
	{
		if (id <= 0)
		{
			throw new ArgumentException(
				"Location id is required.",
				nameof(id));
		}

		var location =
			_locationRepository.GetById(
				id);

		if (location is null)
		{
			throw new InvalidOperationException(
				$"Location with id '{id}' was not found.");
		}

		if (location.Version != expectedVersion ||
			!_locationRepository.Deactivate(id, expectedVersion))
		{
			throw new ConcurrencyConflictException("location");
		}

		var before = Copy(location);
		location.IsActive = false;
		location.Version++;
		_auditService.RecordDeactivated(location.Id, before, location);
	}

	public Location GetOrCreateLocation(
		string name)
	{
		name = name.Trim();

		if (string.IsNullOrWhiteSpace(name))
		{
			throw new ArgumentException(
				"Location name is required.",
				nameof(name));
		}

		var existingLocation =
			_locationRepository.GetByName(
				name);

		if (existingLocation is not null)
		{
			return existingLocation;
		}

		return CreateLocation(
			name,
			null);
	}

	private static Location Copy(Location location) =>
		new()
		{
			Id = location.Id,
			Name = location.Name,
			Description = location.Description,
			IsActive = location.IsActive,
			Version = location.Version
		};


}
