// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;

using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class LocationRepository : DatabaseRepository
{
	private const string SelectColumns = "Id, Name, Description, IsActive, Version";

	public LocationRepository(DatabaseAccess database)
		: base(database)
	{
	}

	public IReadOnlyList<Location> GetAll() =>
		Database.Query(
			$"SELECT {SelectColumns} FROM Locations WHERE IsActive = 1 ORDER BY Name;",
			ReadLocation);

	public Location? GetById(long id) =>
		Database.QuerySingleOrDefault(
			$"SELECT {SelectColumns} FROM Locations WHERE Id = $Id;",
			ReadLocation,
			Parameter("$Id", id));

	public Location? GetByName(string name) =>
		Database.QuerySingleOrDefault(
			$"SELECT {SelectColumns} FROM Locations WHERE Name = $Name;",
			ReadLocation,
			Parameter("$Name", name));

	public long Create(Location location) =>
		Database.Insert(
			"""
			INSERT INTO Locations (Name, Description, IsActive)
			VALUES ($Name, $Description, $IsActive);
			""",
			Parameter("$Name", location.Name),
			Parameter("$Description", location.Description),
			Parameter("$IsActive", location.IsActive));

	public bool Update(Location location) =>
		Database.Execute(
			"""
			UPDATE Locations
			SET Name = $Name, Description = $Description, Version = Version + 1
			WHERE Id = $Id AND Version = $Version;
			""",
			Parameter("$Id", location.Id),
			Parameter("$Name", location.Name),
			Parameter("$Description", location.Description),
			Parameter("$Version", location.Version)) == 1;

	public bool Deactivate(long id, long version) =>
		Database.Execute(
			"""
			UPDATE Locations
			SET IsActive = 0, Version = Version + 1
			WHERE Id = $Id AND Version = $Version;
			""",
			Parameter("$Id", id),
			Parameter("$Version", version)) == 1;

	private static Location ReadLocation(DbDataReader reader) =>
		new()
		{
			Id = reader.GetInt64(0),
			Name = reader.GetString(1),
			Description = reader.IsDBNull(2) ? null : reader.GetString(2),
			IsActive = reader.GetBoolean(3),
			Version = reader.GetInt64(4)
		};
}
