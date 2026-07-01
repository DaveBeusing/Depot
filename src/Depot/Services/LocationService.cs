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

	public LocationService(
		LocationRepository locationRepository)
	{
		_locationRepository = locationRepository;
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

		return location;
	}

	public Location UpdateLocation(
		long id,
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

		_locationRepository.Update(
			location);

		return location;
	}

	public void DeactivateLocation(
		long id)
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

		_locationRepository.Deactivate(
			id);
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


}