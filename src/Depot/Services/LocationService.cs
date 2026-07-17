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
	private readonly AsyncCache<IReadOnlyList<Location>> _cache = new(TimeSpan.FromMinutes(5));

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

	public Task<IReadOnlyList<Location>> GetLocationsAsync(CancellationToken cancellationToken) =>
		_cache.GetAsync(_locationRepository.ListActiveAsync, cancellationToken);

	public async Task<Location> CreateLocationAsync(
		string name,
		string? description,
		CancellationToken cancellationToken)
	{
		(name, description) = Normalize(name, description);
		ValidateName(name);
		if (await _locationRepository.GetByNameAsync(name, cancellationToken) is not null)
			throw new InvalidOperationException($"Location '{name}' already exists.");
		var location = new Location { Name = name, Description = description, IsActive = true };
		location.Id = await _locationRepository.CreateAsync(location, cancellationToken);
		await _auditService.RecordCreatedAsync(location.Id, location, cancellationToken);
		_cache.Invalidate();
		return location;
	}

	public async Task<Location> GetOrCreateLocationAsync(
		string name,
		CancellationToken cancellationToken = default)
	{
		var (normalizedName, _) = Normalize(name, null);
		ValidateName(normalizedName);
		var existing = await _locationRepository.GetByNameAsync(normalizedName, cancellationToken);
		return existing ?? await CreateLocationAsync(normalizedName, null, cancellationToken);
	}

	public async Task<Location> UpdateLocationAsync(
		long id,
		long expectedVersion,
		string name,
		string? description,
		CancellationToken cancellationToken)
	{
		(name, description) = Normalize(name, description);
		if (id <= 0) throw new ArgumentException("Location id is required.", nameof(id));
		ValidateName(name);
		var location = await _locationRepository.GetByIdAsync(id, cancellationToken)
			?? throw new InvalidOperationException($"Location with id '{id}' was not found.");
		if (location.Version != expectedVersion) throw new ConcurrencyConflictException("location");
		var duplicate = await _locationRepository.GetByNameAsync(name, cancellationToken);
		if (duplicate is not null && duplicate.Id != id)
			throw new InvalidOperationException($"Location '{name}' already exists.");
		var before = Copy(location);
		location.Name = name;
		location.Description = description;
		if (!await _locationRepository.UpdateAsync(location, cancellationToken))
			throw new ConcurrencyConflictException("location");
		location.Version++;
		await _auditService.RecordUpdatedAsync(location.Id, before, location, cancellationToken);
		_cache.Invalidate();
		return location;
	}

	public async Task DeactivateLocationAsync(long id, long expectedVersion, CancellationToken cancellationToken)
	{
		var location = await _locationRepository.GetByIdAsync(id, cancellationToken)
			?? throw new InvalidOperationException($"Location with id '{id}' was not found.");
		if (location.Version != expectedVersion ||
			!await _locationRepository.DeactivateAsync(id, expectedVersion, cancellationToken))
			throw new ConcurrencyConflictException("location");
		var before = Copy(location);
		location.IsActive = false;
		location.Version++;
		await _auditService.RecordDeactivatedAsync(location.Id, before, location, cancellationToken);
		_cache.Invalidate();
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

	private static (string Name, string? Description) Normalize(string name, string? description) =>
		(name.Trim(), string.IsNullOrWhiteSpace(description) ? null : description.Trim());

	private static void ValidateName(string name)
	{
		if (string.IsNullOrWhiteSpace(name))
			throw new ArgumentException("Location name is required.", nameof(name));
	}


}
