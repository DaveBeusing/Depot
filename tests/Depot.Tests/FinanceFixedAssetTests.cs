// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Depot.Tests;

public sealed class FinanceFixedAssetTests : IDisposable
{
	private readonly string _path=Path.Combine(Path.GetTempPath(),$"depot-fixed-assets-{Guid.NewGuid():N}.db");
	private readonly DatabaseAccess _database;
	private readonly FinanceFixedAssetService _service;
	private readonly Guid _legalEntityId=Guid.NewGuid();
	private readonly Guid _calendarId=Guid.NewGuid();

	public FinanceFixedAssetTests()
	{
		var factory=new SqliteConnectionFactory(_path);
		new DepotDatabase(factory).Initialize();
		FinanceInventoryAccountingSchemaMigration.Migrate(factory);
		_database=new DatabaseAccess(factory);
		var authorization=new AuthorizationService();
		var users=new UserRepository(_database);
		var admin=users.GetByEmailAsync("admin@depot.local",CancellationToken.None).GetAwaiter().GetResult()??throw new InvalidOperationException("Default administrator missing.");
		authorization.SignIn(admin,PermissionCatalog.All);
		var auditRepository=new AuditRepository(_database);
		var audit=new AuditService(auditRepository,authorization);
		var ledger=new FinanceGeneralLedgerService(new DatabaseTransactionRunner(_database),new FinanceGeneralLedgerRepository(_database),new FinancePostingProfileRepository(_database),auditRepository,audit,authorization);
		_service=new FinanceFixedAssetService(new DatabaseTransactionRunner(_database),new FinanceFixedAssetRepository(_database),ledger,auditRepository,audit,authorization);
		SeedCalendar();
	}

	[Fact]
	public async Task MigrationCreatesFixedAssetSchemaAtFinanceVersionTen()
	{
		Assert.Equal(10L,Convert.ToInt64(await _database.ExecuteScalarAsync("SELECT Version FROM DepotFeatureVersions WHERE Name='Finance';",CancellationToken.None)));
		foreach(var table in new[]{"FinanceAssetClasses","FinanceFixedAssets","FinanceAssetDepreciationPeriods","FinanceAssetTransactions"})
			Assert.Equal(1L,Convert.ToInt64(await _database.ExecuteScalarAsync("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$Name;",CancellationToken.None,new DatabaseParameter("$Name",table))));
	}

	[Fact]
	public async Task StraightLineScheduleIsDeterministicAndPreservesRemainder()
	{
		var classId=await InsertClassAsync(FinanceDepreciationMethod.StraightLine,3);
		var assetId=await InsertAssetAsync(classId,1000m,100m,3,FinanceDepreciationMethod.StraightLine);

		var schedule=await _service.RecalculateScheduleAsync(assetId);

		Assert.Equal(3,schedule.Count);
		Assert.Equal(new[]{300m,300m,300m},schedule.Select(value=>value.PlannedAmount).ToArray());
		Assert.Equal(900m,schedule.Sum(value=>value.PlannedAmount));
		Assert.All(schedule,value=>Assert.False(value.IsPosted));
		var persisted=await _service.GetScheduleAsync(assetId);
		Assert.Equal(schedule.Select(value=>value.AccountingPeriodId),persisted.Select(value=>value.AccountingPeriodId));
		Assert.Equal(schedule.Select(value=>value.PlannedAmount),persisted.Select(value=>value.PlannedAmount));
	}

	[Fact]
	public async Task NoDepreciationCreatesEmptySchedule()
	{
		var classId=await InsertClassAsync(FinanceDepreciationMethod.NoDepreciation,1);
		var assetId=await InsertAssetAsync(classId,500m,0m,1,FinanceDepreciationMethod.NoDepreciation);

		var schedule=await _service.RecalculateScheduleAsync(assetId);

		Assert.Empty(schedule);
		Assert.Empty(await _service.GetScheduleAsync(assetId));
	}

	[Fact]
	public async Task InvalidSalvageValueFailsBeforePersistence()
	{
		var classId=await InsertClassAsync(FinanceDepreciationMethod.StraightLine,12);
		var asset=new FinanceFixedAsset
		{
			AssetNumber="FA-INVALID",LegalEntityId=_legalEntityId,AssetClassId=classId,Description="Invalid salvage",
			AcquisitionDate=new DateOnly(2026,1,1),DepreciationStartDate=new DateOnly(2026,1,1),Currency=new CurrencyCode("EUR"),
			OriginalCost=100m,SalvageValue=101m,UsefulLifeMonths=12,DepreciationMethod=FinanceDepreciationMethod.StraightLine
		};

		await Assert.ThrowsAsync<ArgumentException>(()=>_service.SaveAssetAsync(asset));
		Assert.Equal(0L,Convert.ToInt64(await _database.ExecuteScalarAsync("SELECT COUNT(*) FROM FinanceFixedAssets WHERE AssetNumber='FA-INVALID';",CancellationToken.None)));
	}

	[Fact]
	public void FixedAssetRecordsAreClassifiedAsRetainedAccountingEvidence()
	{
		Assert.Equal(BusinessRecordRetentionCategory.AccountingRelevant,BusinessRecordCatalog.Require(nameof(FinanceFixedAsset)).RetentionCategory);
		Assert.Equal(BusinessRecordRetentionCategory.AccountingRelevant,BusinessRecordCatalog.Require(nameof(FinanceAssetTransaction)).RetentionCategory);
	}

	[Fact]
	public void FinanceRolesExposeSeparatedFixedAssetPermissions()
	{
		var finance=SystemRoleCatalog.Definitions.Single(value=>value.Code==SystemRoleCatalog.FinanceCode).Permissions;
		Assert.Contains(ApplicationPermission.FinanceFixedAssetsView,finance);
		Assert.Contains(ApplicationPermission.FinanceFixedAssetsConfigure,finance);
		Assert.Contains(ApplicationPermission.FinanceFixedAssetsManage,finance);
		Assert.Contains(ApplicationPermission.FinanceFixedAssetsDepreciationPost,finance);

		var management=SystemRoleCatalog.Definitions.Single(value=>value.Code==SystemRoleCatalog.ManagementViewerCode).Permissions;
		Assert.Contains(ApplicationPermission.FinanceFixedAssetsView,management);
		Assert.DoesNotContain(ApplicationPermission.FinanceFixedAssetsManage,management);
		Assert.DoesNotContain(ApplicationPermission.FinanceFixedAssetsDepreciationPost,management);
	}

	private void SeedCalendar()
	{
		_database.Execute("INSERT INTO FinanceCurrencies (Code,Name,MinorUnits,IsActive) VALUES ('EUR','Euro',2,1);");
		_database.Execute("INSERT INTO FinanceLegalEntities (Id,Code,Name,CountryCode,FunctionalCurrencyCode,IsActive) VALUES ($Id,'FA','Fixed Assets','DE','EUR',1);",new DatabaseParameter("$Id",_legalEntityId.ToString("D")));
		_database.Execute("INSERT INTO FinanceFiscalCalendars (Id,LegalEntityId,Code,Name,IsActive) VALUES ($Id,$Entity,'FA-CAL','Fixed Asset Calendar',1);",new DatabaseParameter("$Id",_calendarId.ToString("D")),new DatabaseParameter("$Entity",_legalEntityId.ToString("D")));
		for(var month=1;month<=12;month++)
		{
			var id=Guid.NewGuid();var start=new DateOnly(2026,month,1);var end=start.AddMonths(1).AddDays(-1);
			_database.Execute("INSERT INTO FinanceAccountingPeriods (Id,FiscalCalendarId,Code,StartDate,EndDate,Status) VALUES ($Id,$Calendar,$Code,$Start,$End,0);",
				new DatabaseParameter("$Id",id.ToString("D")),new DatabaseParameter("$Calendar",_calendarId.ToString("D")),new DatabaseParameter("$Code",$"2026-{month:00}"),new DatabaseParameter("$Start",start.ToString("yyyy-MM-dd")),new DatabaseParameter("$End",end.ToString("yyyy-MM-dd")));
		}
	}

	private Task<long> InsertClassAsync(FinanceDepreciationMethod method,int life) =>
		_database.InsertAsync("INSERT INTO FinanceAssetClasses (Version,LegalEntityId,FiscalCalendarId,Code,Name,CapitalizationPostingProfileId,DepreciationPostingProfileId,ImpairmentPostingProfileId,DisposalPostingProfileId,DefaultUsefulLifeMonths,DefaultMethod,IsActive) VALUES (1,$Entity,$Calendar,$Code,'Test',1,2,3,4,$Life,$Method,1);",CancellationToken.None,
			new DatabaseParameter("$Entity",_legalEntityId.ToString("D")),new DatabaseParameter("$Calendar",_calendarId.ToString("D")),new DatabaseParameter("$Code",$"CLASS-{Guid.NewGuid():N}"),new DatabaseParameter("$Life",life),new DatabaseParameter("$Method",(int)method));

	private Task<long> InsertAssetAsync(long classId,decimal cost,decimal salvage,int life,FinanceDepreciationMethod method) =>
		_database.InsertAsync("INSERT INTO FinanceFixedAssets (Version,AssetNumber,LegalEntityId,AssetClassId,Description,AcquisitionDate,CapitalizationDate,DepreciationStartDate,CurrencyCode,OriginalCost,SalvageValue,UsefulLifeMonths,DepreciationMethod,Location,Custodian,Status,SourceSupplierDocumentLineId) VALUES (1,$Number,$Entity,$Class,'Test asset','2026-01-01',NULL,'2026-01-01','EUR',$Cost,$Salvage,$Life,$Method,NULL,NULL,1,NULL);",CancellationToken.None,
			new DatabaseParameter("$Number",$"FA-{Guid.NewGuid():N}"),new DatabaseParameter("$Entity",_legalEntityId.ToString("D")),new DatabaseParameter("$Class",classId),new DatabaseParameter("$Cost",cost),new DatabaseParameter("$Salvage",salvage),new DatabaseParameter("$Life",life),new DatabaseParameter("$Method",(int)method));

	public void Dispose(){SqliteConnection.ClearAllPools();try{File.Delete(_path);}catch(IOException){}}
}
