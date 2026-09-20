// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

using Xunit;

namespace Depot.Tests;

public sealed class FinanceLocalizationHierarchyProjectorTests
{
	[Fact]
	public void ProjectsGenericEuGermanyHierarchyDeterministically()
	{
		var packs = new[]
		{
			new FinanceLocalizationPack { Id=3, Code="DE", Name="Germany", Layer=FinanceLocalizationLayer.Country, CountryCode="DE", ParentPackCode="EU", Description="DE", IsBuiltIn=true, IsActive=true },
			new FinanceLocalizationPack { Id=1, Code="GENERIC", Name="Generic", Layer=FinanceLocalizationLayer.Generic, Description="Generic", IsBuiltIn=true, IsActive=true },
			new FinanceLocalizationPack { Id=2, Code="EU", Name="European Union", Layer=FinanceLocalizationLayer.Regional, ParentPackCode="GENERIC", Description="EU", IsBuiltIn=true, IsActive=true }
		};
		var registry = new[]
		{
			new FinanceLocalizationRegistryEntry { PackCode="DE", RequirementCode="DE-CAP", SupportLevel=FinanceLocalizationSupportLevel.SoftwareCapability, EffectiveFrom=new DateOnly(2026,1,1), Title="Capability", Description="Capability", Reference="Ref" },
			new FinanceLocalizationRegistryEntry { PackCode="DE", RequirementCode="DE-EXT", SupportLevel=FinanceLocalizationSupportLevel.ExternalProcedureRequired, EffectiveFrom=new DateOnly(2026,1,1), Title="External", Description="External", Reference="Ref" }
		};

		var projection = FinanceLocalizationHierarchyProjector.Project(packs, registry);

		Assert.Equal(["GENERIC","EU","DE"], projection.Rows.Select(value => value.Code).ToArray());
		Assert.Equal([0,1,2], projection.Rows.Select(value => value.Depth).ToArray());
		Assert.All(projection.Rows, value => Assert.Equal(FinanceLocalizationHierarchyState.Valid, value.State));
		Assert.Equal("GENERIC → EU → DE", projection.Rows[2].Path);
		Assert.Equal(2, projection.Rows[2].RegistryCount);
		Assert.Equal(1, projection.Rows[2].SoftwareCapabilityCount);
		Assert.Equal(1, projection.Rows[2].ExternalProcedureCount);
		Assert.True(projection.Rows[2].IsReadOnly);
		Assert.Empty(projection.Warnings);
	}

	[Fact]
	public void ProjectionSurfacesMissingParentInvalidLayerAndCyclesWithoutHidingPacks()
	{
		var packs = new[]
		{
			new FinanceLocalizationPack { Id=1, Code="MISSING", Name="Missing", Layer=FinanceLocalizationLayer.Country, CountryCode="DE", ParentPackCode="NONE", Description="Missing", IsActive=true },
			new FinanceLocalizationPack { Id=2, Code="A", Name="A", Layer=FinanceLocalizationLayer.Regional, ParentPackCode="B", Description="A", IsActive=true },
			new FinanceLocalizationPack { Id=3, Code="B", Name="B", Layer=FinanceLocalizationLayer.Regional, ParentPackCode="A", Description="B", IsActive=true }
		};

		var projection = FinanceLocalizationHierarchyProjector.Project(packs, []);

		Assert.Equal(3, projection.Rows.Select(value => value.Code).Distinct().Count());
		Assert.Contains(projection.Rows, value => value.Code=="MISSING" && value.State==FinanceLocalizationHierarchyState.MissingParent);
		Assert.Contains(projection.Rows, value => value.State==FinanceLocalizationHierarchyState.Cycle);
		Assert.NotEmpty(projection.Warnings);
	}

	[Fact]
	public void SharedRulesMatchServiceLayerContract()
	{
		var generic = new FinanceLocalizationPack { Code="GENERIC",Name="Generic",Layer=FinanceLocalizationLayer.Generic,Description="Generic" };
		var regional = new FinanceLocalizationPack { Code="EU",Name="EU",Layer=FinanceLocalizationLayer.Regional,ParentPackCode="GENERIC",Description="EU" };
		var invalidCountry = new FinanceLocalizationPack { Code="DE",Name="DE",Layer=FinanceLocalizationLayer.Country,Description="DE" };

		Assert.Empty(FinanceLocalizationHierarchyRules.ValidatePack(generic, null));
		Assert.Empty(FinanceLocalizationHierarchyRules.ValidatePack(regional, generic));
		Assert.Contains(FinanceLocalizationHierarchyRules.ValidatePack(invalidCountry, null), value => value.Contains("country code", StringComparison.OrdinalIgnoreCase));
	}
}
