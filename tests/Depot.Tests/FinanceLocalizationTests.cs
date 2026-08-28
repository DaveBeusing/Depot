// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Depot.Tests;

public sealed class FinanceLocalizationTests
{
	[Fact]
	public void CurrentFinanceMigrationCreatesLocalizationSchemaVersionNineAndReferencePacks()
	{
		using var context=TestContext.Create();
		Assert.Equal(9L,context.Scalar("SELECT Version FROM DepotFeatureVersions WHERE Name='Finance';"));
		Assert.Equal(1L,context.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='FinanceLocalizationPacks';"));
		Assert.Equal(1L,context.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='FinanceLocalizationAssignments';"));
		Assert.Equal(1L,context.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='FinanceLocalizationRegistryEntries';"));
		Assert.Equal(3L,context.Scalar("SELECT COUNT(*) FROM FinanceLocalizationPacks WHERE IsBuiltIn=1;"));
		Assert.Equal(1L,context.Scalar("SELECT COUNT(*) FROM FinanceLocalizationPacks WHERE Code='DE' AND ParentPackCode='EU' AND CountryCode='DE';"));
		Assert.True(context.Scalar("SELECT COUNT(*) FROM FinanceLocalizationRegistryEntries WHERE IsBuiltIn=1;")>=8L);
	}

	[Fact]
	public void FinanceRoleIncludesLocalizationPermissions()
	{
		var finance=SystemRoleCatalog.Definitions.Single(value=>value.Code==SystemRoleCatalog.FinanceCode);
		Assert.Contains(ApplicationPermission.FinanceLocalizationView,finance.Permissions);
		Assert.Contains(ApplicationPermission.FinanceLocalizationManage,finance.Permissions);
		Assert.Equal("FinanceLocalization.View",PermissionCatalog.Code(ApplicationPermission.FinanceLocalizationView));
		Assert.Equal("FinanceLocalization.Manage",PermissionCatalog.Code(ApplicationPermission.FinanceLocalizationManage));
	}

	[Fact]
	public void LocalizationAssignmentsAndRegistryEntriesAreRetainedAuditEvidence()
	{
		Assert.Equal(BusinessRecordRetentionCategory.AuditEvidence,BusinessRecordCatalog.Require(nameof(FinanceLocalizationAssignment)).RetentionCategory);
		Assert.Equal(BusinessRecordRetentionCategory.AuditEvidence,BusinessRecordCatalog.Require(nameof(FinanceLocalizationRegistryEntry)).RetentionCategory);
	}

	[Fact]
	public async Task LegalEntityCountryDoesNotActivateLocalizationAutomatically()
	{
		using var context=TestContext.Create();
		var entity=context.CreateLegalEntity("DE01","Germany Entity","DE");
		var profile=await context.Service.GetEffectiveProfileAsync(entity,new DateOnly(2026,8,28));
		Assert.Empty(profile.Packs);
		Assert.Empty(profile.Requirements);
		Assert.Contains(profile.Warnings,value=>value.Contains("does not activate",StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public async Task GermanyAssignmentResolvesGenericEuAndGermanReferenceLayers()
	{
		using var context=TestContext.Create();
		var entity=context.CreateLegalEntity("DE01","Germany Entity","DE");
		await context.Service.SaveAssignmentAsync(new FinanceLocalizationAssignment{LegalEntityId=entity,PackCode=FinanceLocalizationPackCodes.Germany,EffectiveFrom=new DateOnly(2026,8,28),IsActive=true});
		var profile=await context.Service.GetEffectiveProfileAsync(entity,new DateOnly(2026,8,28));
		Assert.Equal([FinanceLocalizationPackCodes.Generic,FinanceLocalizationPackCodes.EuropeanUnion,FinanceLocalizationPackCodes.Germany],profile.Packs.Select(value=>value.Code).ToArray());
		Assert.Contains(profile.Requirements,value=>value.RequirementCode=="DE-XRECHNUNG" && value.SupportLevel==FinanceLocalizationSupportLevel.SoftwareCapability);
		Assert.Contains(profile.Requirements,value=>value.RequirementCode=="DE-GOBD-PROCESS" && value.SupportLevel==FinanceLocalizationSupportLevel.ExternalProcedureRequired);
		Assert.NotEmpty(profile.Warnings);
	}

	[Fact]
	public async Task CountryPackRejectsMismatchedLegalEntityCountry()
	{
		using var context=TestContext.Create();
		var entity=context.CreateLegalEntity("FR01","France Entity","FR");
		await Assert.ThrowsAsync<InvalidOperationException>(()=>context.Service.SaveAssignmentAsync(new FinanceLocalizationAssignment{LegalEntityId=entity,PackCode=FinanceLocalizationPackCodes.Germany,EffectiveFrom=new DateOnly(2026,8,28),IsActive=true}));
	}

	[Fact]
	public async Task OverlappingRootAssignmentsAreRejected()
	{
		using var context=TestContext.Create();
		var entity=context.CreateLegalEntity("DE01","Germany Entity","DE");
		await context.Service.SaveAssignmentAsync(new FinanceLocalizationAssignment{LegalEntityId=entity,PackCode=FinanceLocalizationPackCodes.Generic,EffectiveFrom=new DateOnly(2026,1,1),EffectiveTo=new DateOnly(2026,12,31),IsActive=true});
		await Assert.ThrowsAsync<InvalidOperationException>(()=>context.Service.SaveAssignmentAsync(new FinanceLocalizationAssignment{LegalEntityId=entity,PackCode=FinanceLocalizationPackCodes.Germany,EffectiveFrom=new DateOnly(2026,8,28),IsActive=true}));
	}

	[Fact]
	public async Task CustomCountryPackAndEffectiveRegistryEntryExtendFrameworkWithoutSchemaChange()
	{
		using var context=TestContext.Create();
		var entity=context.CreateLegalEntity("FR01","France Entity","FR");
		var pack=await context.Service.SavePackAsync(new FinanceLocalizationPack{Code="FR-REF",Name="France Reference",Layer=FinanceLocalizationLayer.Country,CountryCode="FR",ParentPackCode=FinanceLocalizationPackCodes.EuropeanUnion,Description="Deployment-specific French reference extension.",IsActive=true});
		Assert.False(pack.IsBuiltIn);
		await context.Service.SaveRegistryEntryAsync(new FinanceLocalizationRegistryEntry{PackCode=pack.Code,RequirementCode="FR-LOCAL-REVIEW",Category=FinanceLocalizationRequirementCategory.Tax,SupportLevel=FinanceLocalizationSupportLevel.ExternalProcedureRequired,EffectiveFrom=new DateOnly(2026,8,28),Title="French local review",Description="Requires deployment-specific qualified review.",Reference="deployment policy",IsActive=true});
		await context.Service.SaveAssignmentAsync(new FinanceLocalizationAssignment{LegalEntityId=entity,PackCode=pack.Code,EffectiveFrom=new DateOnly(2026,8,28),IsActive=true});
		var profile=await context.Service.GetEffectiveProfileAsync(entity,new DateOnly(2026,8,28));
		Assert.Equal([FinanceLocalizationPackCodes.Generic,FinanceLocalizationPackCodes.EuropeanUnion,"FR-REF"],profile.Packs.Select(value=>value.Code).ToArray());
		Assert.Contains(profile.Requirements,value=>value.RequirementCode=="FR-LOCAL-REVIEW");
		Assert.Equal(9L,context.Scalar("SELECT Version FROM DepotFeatureVersions WHERE Name='Finance';"));
	}

	[Fact]
	public async Task BuiltInPackDefinitionsAndRegistryRowsAreImmutable()
	{
		using var context=TestContext.Create();
		var de=(await context.Service.GetPacksAsync()).Single(value=>value.Code==FinanceLocalizationPackCodes.Germany);
		await Assert.ThrowsAsync<InvalidOperationException>(()=>context.Service.SavePackAsync(de with { Description="changed" }));
		var builtIn=(await context.Service.GetRegistryAsync(FinanceLocalizationPackCodes.Germany)).First(value=>value.IsBuiltIn);
		await Assert.ThrowsAsync<InvalidOperationException>(()=>context.Service.SaveRegistryEntryAsync(builtIn with { Description="changed" }));
	}

	private sealed class TestContext : IDisposable
	{
		private readonly string _path;
		private readonly DatabaseAccess _database;
		private TestContext(string path,DatabaseAccess database,FinanceLocalizationService service){_path=path;_database=database;Service=service;}
		public FinanceLocalizationService Service { get; }

		public static TestContext Create()
		{
			var path=Path.Combine(Path.GetTempPath(),$"depot-finance-f7-{Guid.NewGuid():N}.db");
			var factory=new SqliteConnectionFactory(path);
			new DepotDatabase(factory).Initialize();
			FinanceInventoryAccountingSchemaMigration.Migrate(factory);
			var database=new DatabaseAccess(factory);
			var userId=database.Insert("INSERT INTO Users (Email,DisplayName,PasswordHash,IsAdministrator,CanApprovePurchaseOrders,Role,IsActive,CreatedUtc) VALUES ('localization@depot.test','Localization','test',0,0,0,1,'2026-08-28T00:00:00.0000000Z');");
			var authorization=new AuthorizationService();
			authorization.SignIn(new User{Id=userId,Email="localization@depot.test",DisplayName="Localization",IsActive=true,CreatedUtc=DateTime.UtcNow},[ApplicationPermission.FinanceLocalizationView,ApplicationPermission.FinanceLocalizationManage]);
			var auditRepository=new AuditRepository(database);
			var audit=new AuditService(auditRepository,authorization);
			var service=new FinanceLocalizationService(new DatabaseTransactionRunner(database),new FinanceLocalizationRepository(database),auditRepository,audit,authorization);
			return new TestContext(path,database,service);
		}

		public Guid CreateLegalEntity(string code,string name,string country)
		{
			var id=Guid.NewGuid();
			_database.Execute("INSERT OR IGNORE INTO FinanceCurrencies (Code,Name,MinorUnits,IsActive) VALUES ('EUR','Euro',2,1);");
			_database.Execute("INSERT INTO FinanceLegalEntities (Id,Code,Name,CountryCode,FunctionalCurrencyCode,IsActive) VALUES ($Id,$Code,$Name,$Country,'EUR',1);",new DatabaseParameter("$Id",id.ToString("D")),new DatabaseParameter("$Code",code),new DatabaseParameter("$Name",name),new DatabaseParameter("$Country",country));
			return id;
		}

		public long Scalar(string sql)
		{
			using var connection=new SqliteConnection($"Data Source={_path}");connection.Open();using var command=connection.CreateCommand();command.CommandText=sql;return Convert.ToInt64(command.ExecuteScalar(),System.Globalization.CultureInfo.InvariantCulture);
		}
		public void Dispose(){SqliteConnection.ClearAllPools();try{File.Delete(_path);}catch(IOException){}}
	}
}
