// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.IO;
using System.Text.Json.Serialization;

namespace Depot.Models;

public sealed record DatabaseSchemaIndex(
	string Name, string[] Columns, bool IsUnique, string? Filter = null, string? Definition = null, bool IsEnabled = true);

/// <summary>A provider-neutral requirement or a complete, read-only observation of a database.</summary>
public sealed class DatabaseSchemaCompatibility
{
	public const int CurrentContractVersion = 1;
	[JsonRequired]
	public int ContractVersion { get; init; } = CurrentContractVersion;
	[JsonRequired]
	public int CoreVersion { get; init; }
	public DatabaseProvider? Provider { get; init; }
	[JsonRequired]
	public Dictionary<string, int> FeatureVersions { get; init; } = new(StringComparer.Ordinal);
	[JsonRequired]
	public Dictionary<string, string[]> Tables { get; init; } = new(StringComparer.OrdinalIgnoreCase);
	public Dictionary<string, string> TableDefinitions { get; init; } = new(StringComparer.OrdinalIgnoreCase);
	public Dictionary<string, string> OtherDefinitions { get; init; } = new(StringComparer.Ordinal);
	[JsonRequired]
	public Dictionary<string, DatabaseSchemaIndex[]> Indexes { get; init; } = new(StringComparer.OrdinalIgnoreCase);

	public void Validate(bool requirement = false)
	{
		if (ContractVersion != CurrentContractVersion || CoreVersion < 1 || (Provider is { } provider && !Enum.IsDefined(provider)) ||
			FeatureVersions is null || Tables is null || TableDefinitions is null || OtherDefinitions is null || Indexes is null || Tables.Count == 0 ||
			TableDefinitions.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null) ||
			OtherDefinitions.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null) ||
			Tables.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != Tables.Count ||
			Indexes.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != Indexes.Count ||
			FeatureVersions.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Value < 1) ||
			Tables.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null || pair.Value.Length == 0 ||
				pair.Value.Any(string.IsNullOrWhiteSpace) || pair.Value.Distinct(StringComparer.OrdinalIgnoreCase).Count() != pair.Value.Length) ||
			Indexes.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null || pair.Value.Any(index =>
				index is null || string.IsNullOrWhiteSpace(index.Name) || index.Columns is null || index.Columns.Length == 0 || index.Columns.Any(string.IsNullOrWhiteSpace)) ||
				pair.Value.Select(index => index.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != pair.Value.Length))
			throw new InvalidDataException("The complete database compatibility contract is missing, malformed or unsupported.");
		if (requirement && (FeatureVersions.Count == 0 || !Tables.ContainsKey("DepotFeatureVersions")))
			throw new InvalidDataException("The release does not declare its feature schema requirements.");
	}

	/// <summary>Reject unsupported/future versions; report every compatible forward migration, including structural repairs.</summary>
	public bool RequiresMigrationTo(DatabaseSchemaCompatibility target)
	{
		ArgumentNullException.ThrowIfNull(target);
		Validate();
		target.Validate(requirement: true);
		if (CoreVersion > target.CoreVersion || FeatureVersions.Any(pair =>
			!target.FeatureVersions.TryGetValue(pair.Key, out var expected) || pair.Value > expected))
			throw new InvalidOperationException("The database contains a newer or unknown core/feature schema. Automatic downgrade is not supported.");
		return CoreVersion != target.CoreVersion || !VersionsEqual(FeatureVersions, target.FeatureVersions) || !ContainsStructure(target);
	}

	public void RequireCompatibleWith(DatabaseSchemaCompatibility target)
	{
		if (RequiresMigrationTo(target))
			throw new InvalidOperationException("The database does not match the required core, feature and structural schemas. Complete the protected migration before starting Depot.");
	}

	public bool HasSameState(DatabaseSchemaCompatibility? other)
	{
		if (other is null) return false;
		try { Validate(); other.Validate(); }
		catch (InvalidDataException) { return false; }
		return Provider == other.Provider && CoreVersion == other.CoreVersion &&
			VersionsEqual(FeatureVersions, other.FeatureVersions) && Tables.Count == other.Tables.Count &&
			Tables.All(pair => other.Tables.TryGetValue(pair.Key, out var columns) &&
				pair.Value.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(columns)) &&
			TableDefinitions.Count == other.TableDefinitions.Count && TableDefinitions.All(pair => other.TableDefinitions.TryGetValue(pair.Key, out var definition) && pair.Value == definition) &&
			OtherDefinitions.Count == other.OtherDefinitions.Count && OtherDefinitions.All(pair => other.OtherDefinitions.TryGetValue(pair.Key, out var definition) && pair.Value == definition) &&
			Indexes.Count == other.Indexes.Count && Indexes.All(pair => other.Indexes.TryGetValue(pair.Key, out var indexes) &&
				pair.Value.Length == indexes.Length && pair.Value.All(index => indexes.Any(candidate =>
					SameIndex(index, candidate) && string.Equals(index.Definition, candidate.Definition, StringComparison.Ordinal))));
	}

	private bool ContainsStructure(DatabaseSchemaCompatibility target) =>
		target.Tables.All(pair => Tables.TryGetValue(pair.Key, out var columns) &&
			pair.Value.All(columns.ToHashSet(StringComparer.OrdinalIgnoreCase).Contains)) &&
		target.Indexes.All(pair => Indexes.TryGetValue(pair.Key, out var indexes) && pair.Value.All(required =>
			indexes.Any(actual => SameIndex(actual, required, Provider == DatabaseProvider.MySql))));

	private static bool SameIndex(DatabaseSchemaIndex actual, DatabaseSchemaIndex expected, bool mysql = false) =>
		string.Equals(actual.Name, expected.Name, StringComparison.OrdinalIgnoreCase) && actual.IsUnique == expected.IsUnique && actual.IsEnabled == expected.IsEnabled &&
		actual.Columns.SequenceEqual(expected.Columns, StringComparer.OrdinalIgnoreCase) &&
		NormalizeFilter(actual.Filter) == NormalizeFilter(mysql && expected.Filter == "Gtin IS NOT NULL" ? null : expected.Filter);

	private static string NormalizeFilter(string? filter) => new((filter ?? string.Empty)
		.Where(character => !char.IsWhiteSpace(character) && character is not '(' and not ')' and not '[' and not ']' and not '`' and not '"')
		.Select(char.ToUpperInvariant).ToArray());

	private static bool VersionsEqual(IReadOnlyDictionary<string, int> actual, IReadOnlyDictionary<string, int> expected) =>
		actual.Count == expected.Count && expected.All(pair => actual.TryGetValue(pair.Key, out var version) && version == pair.Value);
}
