// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public enum FinanceLocalizationHierarchyState
{
	Valid,
	Inactive,
	MissingParent,
	Invalid,
	Cycle
}

public sealed record FinanceLocalizationHierarchyRow(
	FinanceLocalizationPack Pack,
	int Depth,
	string Path,
	FinanceLocalizationHierarchyState State,
	string ValidationMessage,
	int RegistryCount,
	int SoftwareCapabilityCount,
	int ConfigurationRequiredCount,
	int ExternalProcedureCount,
	int ReferenceOnlyCount)
{
	public string Code => Pack.Code;
	public string Name => Pack.Name;
	public FinanceLocalizationLayer Layer => Pack.Layer;
	public string? CountryCode => Pack.CountryCode;
	public string? ParentPackCode => Pack.ParentPackCode;
	public bool IsBuiltIn => Pack.IsBuiltIn;
	public bool IsActive => Pack.IsActive;
	public bool IsReadOnly => Pack.IsBuiltIn;
	public string StateText => State.ToString();
	public string DepthMarker => Depth == 0 ? "●" : string.Concat(Enumerable.Repeat("↳ ", Depth));
	public string RegistrySummary => $"{RegistryCount} registry entr{(RegistryCount == 1 ? "y" : "ies")} · {SoftwareCapabilityCount} capability · {ConfigurationRequiredCount} configuration · {ExternalProcedureCount} external · {ReferenceOnlyCount} reference";
}

public sealed record FinanceLocalizationHierarchyProjection(
	IReadOnlyList<FinanceLocalizationHierarchyRow> Rows,
	IReadOnlyList<string> Warnings);

public static class FinanceLocalizationHierarchyRules
{
	public static IReadOnlyList<string> ValidatePack(FinanceLocalizationPack value, FinanceLocalizationPack? parent)
	{
		ArgumentNullException.ThrowIfNull(value);
		var errors = new List<string>();
		if (value.Layer == FinanceLocalizationLayer.Generic && (value.CountryCode is not null || parent is not null))
			errors.Add("Generic localization packs cannot target a country or have a parent.");
		if (value.Layer == FinanceLocalizationLayer.Regional && (value.CountryCode is not null || parent is null))
			errors.Add("Regional localization packs require a broader parent and cannot target one country.");
		if (value.Layer == FinanceLocalizationLayer.Country && (value.CountryCode is null || parent is null))
			errors.Add("Country localization packs require a country code and a broader parent.");
		if (parent is not null && parent.Layer >= value.Layer)
			errors.Add("A localization pack parent must belong to a broader layer.");
		if (parent is not null && string.Equals(parent.Code, value.Code, StringComparison.Ordinal))
			errors.Add("A localization pack cannot depend on itself.");
		return errors;
	}

	public static void ThrowIfInvalidPack(FinanceLocalizationPack value, FinanceLocalizationPack? parent)
	{
		var errors = ValidatePack(value, parent);
		if (errors.Count > 0) throw new InvalidOperationException(errors[0]);
	}

	public static bool RangesOverlap(DateOnly leftFrom, DateOnly? leftTo, DateOnly rightFrom, DateOnly? rightTo) =>
		(!leftTo.HasValue || rightFrom <= leftTo.Value) &&
		(!rightTo.HasValue || leftFrom <= rightTo.Value);
}

public static class FinanceLocalizationHierarchyProjector
{
	public static FinanceLocalizationHierarchyProjection Project(
		IEnumerable<FinanceLocalizationPack> packs,
		IEnumerable<FinanceLocalizationRegistryEntry> registryEntries)
	{
		ArgumentNullException.ThrowIfNull(packs);
		ArgumentNullException.ThrowIfNull(registryEntries);
		var packList = packs.OrderBy(value => value.Layer).ThenBy(value => value.Code, StringComparer.Ordinal).ThenBy(value => value.Id).ToArray();
		var byCode = packList.ToDictionary(value => value.Code, StringComparer.Ordinal);
		var registry = registryEntries.GroupBy(value => value.PackCode, StringComparer.Ordinal)
			.ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
		var rows = new List<FinanceLocalizationHierarchyRow>();
		var warnings = new List<string>();
		var completed = new HashSet<string>(StringComparer.Ordinal);
		var stack = new HashSet<string>(StringComparer.Ordinal);

		foreach (var root in packList.Where(value => string.IsNullOrWhiteSpace(value.ParentPackCode)))
			Visit(root, 0, root.Code);

		foreach (var pack in packList.Where(value => !completed.Contains(value.Code)))
			Visit(pack, 0, pack.Code);

		return new FinanceLocalizationHierarchyProjection(rows, warnings.Distinct(StringComparer.Ordinal).ToArray());

		void Visit(FinanceLocalizationPack pack, int depth, string path)
		{
			if (completed.Contains(pack.Code)) return;
			if (!stack.Add(pack.Code))
			{
				rows.Add(CreateRow(pack, depth, path, FinanceLocalizationHierarchyState.Cycle, "Localization pack dependency cycle detected."));
				warnings.Add($"Localization pack '{pack.Code}' participates in a dependency cycle.");
				return;
			}

			FinanceLocalizationPack? parent = null;
			var missingParent = false;
			if (!string.IsNullOrWhiteSpace(pack.ParentPackCode))
				missingParent = !byCode.TryGetValue(pack.ParentPackCode, out parent);

			var validation = missingParent
				? ["Parent localization pack was not found."]
				: FinanceLocalizationHierarchyRules.ValidatePack(pack, parent);
			var state = missingParent
				? FinanceLocalizationHierarchyState.MissingParent
				: validation.Count > 0
					? FinanceLocalizationHierarchyState.Invalid
					: !pack.IsActive
						? FinanceLocalizationHierarchyState.Inactive
						: FinanceLocalizationHierarchyState.Valid;
			rows.Add(CreateRow(pack, depth, path, state, validation.Count == 0 ? "Hierarchy semantics are valid." : string.Join(Environment.NewLine, validation)));
			if (state is FinanceLocalizationHierarchyState.MissingParent or FinanceLocalizationHierarchyState.Invalid)
				warnings.Add($"Localization pack '{pack.Code}' has invalid hierarchy metadata.");

			foreach (var child in packList.Where(value => string.Equals(value.ParentPackCode, pack.Code, StringComparison.Ordinal))
				.OrderBy(value => value.Layer).ThenBy(value => value.Code, StringComparer.Ordinal).ThenBy(value => value.Id))
			{
				if (stack.Contains(child.Code))
				{
					rows.Add(CreateRow(child, depth + 1, $"{path} → {child.Code}", FinanceLocalizationHierarchyState.Cycle, "Localization pack dependency cycle detected."));
					warnings.Add($"Localization pack '{child.Code}' participates in a dependency cycle.");
					continue;
				}
				Visit(child, depth + 1, $"{path} → {child.Code}");
			}
			stack.Remove(pack.Code);
			completed.Add(pack.Code);
		}

		FinanceLocalizationHierarchyRow CreateRow(FinanceLocalizationPack pack, int depth, string path, FinanceLocalizationHierarchyState state, string validationMessage)
		{
			var entries = registry.GetValueOrDefault(pack.Code) ?? [];
			return new FinanceLocalizationHierarchyRow(
				pack,
				depth,
				path,
				state,
				validationMessage,
				entries.Length,
				entries.Count(value => value.SupportLevel == FinanceLocalizationSupportLevel.SoftwareCapability),
				entries.Count(value => value.SupportLevel == FinanceLocalizationSupportLevel.ConfigurationRequired),
				entries.Count(value => value.SupportLevel == FinanceLocalizationSupportLevel.ExternalProcedureRequired),
				entries.Count(value => value.SupportLevel == FinanceLocalizationSupportLevel.ReferenceOnly));
		}
	}
}


public enum FinanceLocalizationAssignmentState
{
	Valid,
	Inactive,
	Overlap,
	CountryMismatch,
	MissingPack,
	InactivePack
}

public sealed record FinanceLocalizationAssignmentTimelineRow(
	FinanceLocalizationAssignment Assignment,
	FinanceLocalizationPack? Pack,
	FinanceLocalizationAssignmentState State,
	string ValidationMessage)
{
	public string PackCode => Assignment.PackCode;
	public DateOnly EffectiveFrom => Assignment.EffectiveFrom;
	public DateOnly? EffectiveTo => Assignment.EffectiveTo;
	public bool IsActive => Assignment.IsActive;
	public bool HasConflict => State is FinanceLocalizationAssignmentState.Overlap or FinanceLocalizationAssignmentState.CountryMismatch or FinanceLocalizationAssignmentState.MissingPack or FinanceLocalizationAssignmentState.InactivePack;
	public string StateText => State.ToString();
	public string RangeText => $"{EffectiveFrom:yyyy-MM-dd} → {(EffectiveTo.HasValue ? EffectiveTo.Value.ToString("yyyy-MM-dd") : "open")}";
}

public sealed record FinanceLocalizationAssignmentValidationResult(
	bool IsValid,
	IReadOnlyList<string> Errors)
{
	public string Summary => IsValid ? "Assignment is valid for the existing localization service rules." : string.Join(Environment.NewLine, Errors);
}

public static class FinanceLocalizationAssignmentProjector
{
	public static IReadOnlyList<FinanceLocalizationAssignmentTimelineRow> Project(
		LegalEntity legalEntity,
		IEnumerable<FinanceLocalizationAssignment> assignments,
		IEnumerable<FinanceLocalizationPack> packs)
	{
		ArgumentNullException.ThrowIfNull(legalEntity);
		ArgumentNullException.ThrowIfNull(assignments);
		ArgumentNullException.ThrowIfNull(packs);
		var source = assignments.OrderBy(value => value.EffectiveFrom).ThenBy(value => value.Id).ToArray();
		var byCode = packs.ToDictionary(value => value.Code, StringComparer.Ordinal);
		return source.Select(assignment =>
		{
			byCode.TryGetValue(assignment.PackCode, out var pack);
			var overlap = assignment.IsActive && source.Any(other =>
				other.Id != assignment.Id &&
				other.IsActive &&
				FinanceLocalizationHierarchyRules.RangesOverlap(assignment.EffectiveFrom, assignment.EffectiveTo, other.EffectiveFrom, other.EffectiveTo));
			var countryMismatch = pack is not null &&
				!string.IsNullOrWhiteSpace(pack.CountryCode) &&
				!string.Equals(pack.CountryCode, legalEntity.CountryCode, StringComparison.Ordinal);
			var state = pack is null
				? FinanceLocalizationAssignmentState.MissingPack
				: !pack.IsActive
					? FinanceLocalizationAssignmentState.InactivePack
					: countryMismatch
						? FinanceLocalizationAssignmentState.CountryMismatch
						: overlap
							? FinanceLocalizationAssignmentState.Overlap
							: !assignment.IsActive
								? FinanceLocalizationAssignmentState.Inactive
								: FinanceLocalizationAssignmentState.Valid;
			var message = state switch
			{
				FinanceLocalizationAssignmentState.MissingPack => "Assigned localization pack was not found.",
				FinanceLocalizationAssignmentState.InactivePack => "Assigned localization pack is inactive.",
				FinanceLocalizationAssignmentState.CountryMismatch => $"Localization pack country '{pack!.CountryCode}' does not match legal entity country '{legalEntity.CountryCode}'.",
				FinanceLocalizationAssignmentState.Overlap => "Another active localization assignment overlaps this effective date range.",
				FinanceLocalizationAssignmentState.Inactive => "Assignment is inactive and retained as historical evidence.",
				_ => "Assignment is consistent with the current hierarchy, country and effective-date projection."
			};
			return new FinanceLocalizationAssignmentTimelineRow(assignment, pack, state, message);
		}).ToArray();
	}
}
