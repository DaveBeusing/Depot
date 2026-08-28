// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public enum FinanceLocalizationLayer
{
	Generic,
	Regional,
	Country
}

public enum FinanceLocalizationRequirementCategory
{
	Accounting,
	Tax,
	ElectronicInvoicing,
	Reporting,
	Retention,
	Privacy,
	Payments,
	MasterData
}

public enum FinanceLocalizationSupportLevel
{
	SoftwareCapability,
	ConfigurationRequired,
	ExternalProcedureRequired,
	ReferenceOnly
}

public sealed record FinanceLocalizationPack
{
	public long Id { get; init; }
	public long Version { get; init; } = 1;
	public string Code { get; init; } = string.Empty;
	public string Name { get; init; } = string.Empty;
	public FinanceLocalizationLayer Layer { get; init; }
	public string? CountryCode { get; init; }
	public string? ParentPackCode { get; init; }
	public string Description { get; init; } = string.Empty;
	public bool IsBuiltIn { get; init; }
	public bool IsActive { get; init; } = true;
}

public sealed record FinanceLocalizationAssignment
{
	public long Id { get; init; }
	public long Version { get; init; } = 1;
	public Guid LegalEntityId { get; init; }
	public string PackCode { get; init; } = string.Empty;
	public DateOnly EffectiveFrom { get; init; }
	public DateOnly? EffectiveTo { get; init; }
	public bool IsActive { get; init; } = true;
	public DateTime CreatedAtUtc { get; init; }
	public long CreatedByUserId { get; init; }
}

public sealed record FinanceLocalizationRegistryEntry
{
	public long Id { get; init; }
	public long Version { get; init; } = 1;
	public string PackCode { get; init; } = string.Empty;
	public string RequirementCode { get; init; } = string.Empty;
	public FinanceLocalizationRequirementCategory Category { get; init; }
	public FinanceLocalizationSupportLevel SupportLevel { get; init; }
	public DateOnly EffectiveFrom { get; init; }
	public DateOnly? EffectiveTo { get; init; }
	public string Title { get; init; } = string.Empty;
	public string Description { get; init; } = string.Empty;
	public string Reference { get; init; } = string.Empty;
	public bool IsBuiltIn { get; init; }
	public bool IsActive { get; init; } = true;
}

public sealed record FinanceLocalizationProfile
{
	public Guid LegalEntityId { get; init; }
	public string LegalEntityCode { get; init; } = string.Empty;
	public string CountryCode { get; init; } = string.Empty;
	public DateOnly AsOfDate { get; init; }
	public IReadOnlyList<FinanceLocalizationPack> Packs { get; init; } = [];
	public IReadOnlyList<FinanceLocalizationRegistryEntry> Requirements { get; init; } = [];
	public IReadOnlyList<string> Warnings { get; init; } = [];
}

public static class FinanceLocalizationPackCodes
{
	public const string Generic = "GENERIC";
	public const string EuropeanUnion = "EU";
	public const string Germany = "DE";
}
