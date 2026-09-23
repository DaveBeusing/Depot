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
	private Guid _januaryPeriodId;
	private Guid _februaryPeriodId;
	private long _capitalizationProfileId;
	private long _depreciationProfileId;
	private long _impairmentProfileId;
	private long _disposalProfileId;

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
		SeedLedger();
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
	public async Task CapitalizationAndDepreciationAreBalancedAndIdempotent()
	{
		var classId=await InsertClassAsync(FinanceDepreciationMethod.StraightLine,3);
		var assetId=await InsertAssetAsync(classId,900m,0m,3,FinanceDepreciationMethod.StraightLine);
		var schedule=await _service.RecalculateScheduleAsync(assetId);
		var capitalizationOperation=Guid.NewGuid();

		var capitalization=await _service.CapitalizeAsync(assetId,capitalizationOperation,_januaryPeriodId,new DateOnly(2026,1,5));
		var capitalizationRetry=await _service.CapitalizeAsync(assetId,capitalizationOperation,_januaryPeriodId,new DateOnly(2026,1,5));
		Assert.Equal(capitalization.Id,capitalizationRetry.Id);
		AssertBalanced(capitalization.JournalEntryId);

		var depreciationOperation=Guid.NewGuid();
		var depreciation=await _service.PostDepreciationAsync(schedule[0].Id,depreciationOperation,FinanceClosedPeriodPolicy.Fail);
		var depreciationRetry=await _service.PostDepreciationAsync(schedule[0].Id,depreciationOperation,FinanceClosedPeriodPolicy.Fail);
		Assert.Equal(depreciation.Id,depreciationRetry.Id);
		Assert.Equal(300m,depreciation.Amount);
		AssertBalanced(depreciation.JournalEntryId);
		Assert.Equal(2L,Convert.ToInt64(await _database.ExecuteScalarAsync("SELECT COUNT(*) FROM FinanceAssetTransactions WHERE AssetId=$Asset;",CancellationToken.None,new DatabaseParameter("$Asset",assetId))));
	}

	[Fact]
	public async Task ClosedDepreciationPeriodFailsOrMovesToNextOpenPeriodExplicitly()
	{
		var classId=await InsertClassAsync(FinanceDepreciationMethod.StraightLine,2);
		var assetId=await InsertAssetAsync(classId,200m,0m,2,FinanceDepreciationMethod.StraightLine);
		var schedule=await _service.RecalculateScheduleAsync(assetId);
		await _service.CapitalizeAsync(assetId,Guid.NewGuid(),_januaryPeriodId,new DateOnly(2026,1,5));
		_database.Execute("UPDATE FinanceAccountingPeriods SET Status=$Closed WHERE Id=$Id;",new DatabaseParameter("$Closed",(int)AccountingPeriodStatus.Closed),new DatabaseParameter("$Id",_januaryPeriodId.ToString("D")));

		await Assert.ThrowsAsync<InvalidOperationException>(()=>_service.PostDepreciationAsync(schedule[0].Id,Guid.NewGuid(),FinanceClosedPeriodPolicy.Fail));

		var posted=await _service.PostDepreciationAsync(schedule[0].Id,Guid.NewGuid(),FinanceClosedPeriodPolicy.NextOpenPeriod);
		Assert.Contains("ClosedPeriodPolicy=NextOpenPeriod",posted.Evidence,StringComparison.Ordinal);
		Assert.Equal(_februaryPeriodId,Guid.Parse(Convert.ToString(await _database.ExecuteScalarAsync("SELECT AccountingPeriodId FROM FinanceJournalEntries WHERE Id=$Id;",CancellationToken.None,new DatabaseParameter("$Id",posted.JournalEntryId!.Value)))!));
	}

	[Fact]
	public async Task ImpairmentCorrectionCreatesGlReversalAndRestoresReconciliation()
	{
		var classId=await InsertClassAsync(FinanceDepreciationMethod.NoDepreciation,1);
		var assetId=await InsertAssetAsync(classId,1000m,0m,1,FinanceDepreciationMethod.NoDepreciation);
		await _service.CapitalizeAsync(assetId,Guid.NewGuid(),_januaryPeriodId,new DateOnly(2026,1,5));
		var impairment=await _service.ImpairAsync(assetId,Guid.NewGuid(),_januaryPeriodId,new DateOnly(2026,1,10),125m,"Damage identified");
		var correctionOperation=Guid.NewGuid();

		var correction=await _service.CorrectImpairmentAsync(impairment.Id,correctionOperation,_januaryPeriodId,new DateOnly(2026,1,12),"Inspection corrected the estimate");
		var retry=await _service.CorrectImpairmentAsync(impairment.Id,correctionOperation,_januaryPeriodId,new DateOnly(2026,1,12),"Inspection corrected the estimate");

		Assert.Equal(correction.Id,retry.Id);
		Assert.Equal(FinanceAssetTransactionKind.Correction,correction.Kind);
		Assert.Equal(125m,correction.Amount);
		AssertBalanced(correction.JournalEntryId);
		Assert.Equal(correction.JournalEntryId,Convert.ToInt64(await _database.ExecuteScalarAsync("SELECT ReversalEntryId FROM FinanceJournalReversals WHERE OriginalEntryId=$Id;",CancellationToken.None,new DatabaseParameter("$Id",impairment.JournalEntryId!.Value))));
		await Assert.ThrowsAsync<InvalidOperationException>(()=>_service.CorrectImpairmentAsync(impairment.Id,Guid.NewGuid(),_januaryPeriodId,new DateOnly(2026,1,13),"Duplicate correction"));

		var row=Assert.Single((await _service.SearchReconciliationAsync(_legalEntityId,1,20)).Items);
		Assert.Equal(1000m,row.SubledgerCarryingValue);
		Assert.Equal(row.SubledgerCarryingValue,row.GeneralLedgerCarryingValue);
		Assert.Equal(0m,row.Difference);
	}

	[Fact]
	public async Task DisposalRetainsHistoryAndClearsConfiguredAssetAccounts()
	{
		var classId=await InsertClassAsync(FinanceDepreciationMethod.NoDepreciation,1);
		var assetId=await InsertAssetAsync(classId,1000m,0m,1,FinanceDepreciationMethod.NoDepreciation);
		await _service.CapitalizeAsync(assetId,Guid.NewGuid(),_januaryPeriodId,new DateOnly(2026,1,5));
		var impairment=await _service.ImpairAsync(assetId,Guid.NewGuid(),_januaryPeriodId,new DateOnly(2026,1,10),100m,"Temporary impairment");
		await _service.CorrectImpairmentAsync(impairment.Id,Guid.NewGuid(),_januaryPeriodId,new DateOnly(2026,1,11),"Impairment no longer supported");
		var disposal=await _service.DisposeAsync(assetId,Guid.NewGuid(),_januaryPeriodId,new DateOnly(2026,1,20),900m,"Asset sold");
		AssertBalanced(disposal.JournalEntryId);

		var asset=await _service.GetAssetAsync(assetId);
		Assert.NotNull(asset);
		Assert.Equal(FinanceAssetStatus.Disposed,asset.Status);
		var history=await _service.SearchTransactionsAsync(assetId,1,20);
		Assert.Equal(4,history.TotalCount);
		Assert.Contains(history.Items,value=>value.Kind==FinanceAssetTransactionKind.Capitalization);
		Assert.Contains(history.Items,value=>value.Kind==FinanceAssetTransactionKind.Impairment);
		Assert.Contains(history.Items,value=>value.Kind==FinanceAssetTransactionKind.Correction);
		Assert.Contains(history.Items,value=>value.Kind==FinanceAssetTransactionKind.Disposal);

		var row=Assert.Single((await _service.SearchReconciliationAsync(_legalEntityId,1,20)).Items);
		Assert.Equal(0m,row.SubledgerCarryingValue);
		Assert.Equal(0m,row.GeneralLedgerCarryingValue);
		Assert.Equal(0m,row.Difference);
	}

	[Fact]
	public async Task PostedSupplierInvoiceLineCanBeSelectedAsAssetSource()
	{
		var classId=await InsertClassAsync(FinanceDepreciationMethod.NoDepreciation,1);
		var postedLine=InsertSupplierLine(FinancePayableDocumentStatus.Posted,"EUR");
		var draftLine=InsertSupplierLine(FinancePayableDocumentStatus.Draft,"EUR");
		var foreignLine=InsertSupplierLine(FinancePayableDocumentStatus.Posted,"USD");

		var options=await _service.GetCapitalizableSupplierLinesAsync();
		Assert.Contains(options,value=>value.Id==postedLine);
		Assert.DoesNotContain(options,value=>value.Id==draftLine);

		var saved=await _service.SaveAssetAsync(NewAsset(classId,postedLine,"EUR"));
		Assert.Equal(postedLine,saved.SourceSupplierDocumentLineId);
		await Assert.ThrowsAsync<InvalidOperationException>(()=>_service.SaveAssetAsync(NewAsset(classId,draftLine,"EUR")));
		await Assert.ThrowsAsync<InvalidOperationException>(()=>_service.SaveAssetAsync(NewAsset(classId,foreignLine,"EUR")));
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
			var id=Guid.NewGuid();if(month==1)_januaryPeriodId=id;if(month==2)_februaryPeriodId=id;var start=new DateOnly(2026,month,1);var end=start.AddMonths(1).AddDays(-1);
			_database.Execute("INSERT INTO FinanceAccountingPeriods (Id,FiscalCalendarId,Code,StartDate,EndDate,Status) VALUES ($Id,$Calendar,$Code,$Start,$End,0);",
				new DatabaseParameter("$Id",id.ToString("D")),new DatabaseParameter("$Calendar",_calendarId.ToString("D")),new DatabaseParameter("$Code",$"2026-{month:00}"),new DatabaseParameter("$Start",start.ToString("yyyy-MM-dd")),new DatabaseParameter("$End",end.ToString("yyyy-MM-dd")));
		}
	}

	private Task<long> InsertClassAsync(FinanceDepreciationMethod method,int life) =>
		_database.InsertAsync("INSERT INTO FinanceAssetClasses (Version,LegalEntityId,FiscalCalendarId,Code,Name,CapitalizationPostingProfileId,DepreciationPostingProfileId,ImpairmentPostingProfileId,DisposalPostingProfileId,DefaultUsefulLifeMonths,DefaultMethod,IsActive) VALUES (1,$Entity,$Calendar,$Code,'Test',$Capitalization,$Depreciation,$Impairment,$Disposal,$Life,$Method,1);",CancellationToken.None,
			new DatabaseParameter("$Entity",_legalEntityId.ToString("D")),new DatabaseParameter("$Calendar",_calendarId.ToString("D")),new DatabaseParameter("$Code",$"CLASS-{Guid.NewGuid():N}"),new DatabaseParameter("$Capitalization",_capitalizationProfileId),new DatabaseParameter("$Depreciation",_depreciationProfileId),new DatabaseParameter("$Impairment",_impairmentProfileId),new DatabaseParameter("$Disposal",_disposalProfileId),new DatabaseParameter("$Life",life),new DatabaseParameter("$Method",(int)method));

	private void SeedLedger()
	{
		var chartId=Guid.NewGuid();var assetCost=Guid.NewGuid();var accumulatedDepreciation=Guid.NewGuid();var accumulatedImpairment=Guid.NewGuid();var offset=Guid.NewGuid();var depreciationExpense=Guid.NewGuid();var impairmentExpense=Guid.NewGuid();var cash=Guid.NewGuid();var gain=Guid.NewGuid();var loss=Guid.NewGuid();var bookId=Guid.NewGuid();var journalId=Guid.NewGuid();var sequenceId=Guid.NewGuid();
		_database.Execute("INSERT INTO FinanceChartsOfAccounts (Id,Code,Name,IsActive) VALUES ($Id,'FA-COA','Fixed Asset Chart',1);",new DatabaseParameter("$Id",chartId.ToString("D")));
		InsertAccount(assetCost,chartId,"1500","Fixed assets",FinanceAccountType.Asset);InsertAccount(accumulatedDepreciation,chartId,"1590","Accumulated depreciation",FinanceAccountType.Asset);InsertAccount(accumulatedImpairment,chartId,"1595","Accumulated impairment",FinanceAccountType.Asset);InsertAccount(offset,chartId,"2000","Capitalization offset",FinanceAccountType.Liability);InsertAccount(depreciationExpense,chartId,"6500","Depreciation expense",FinanceAccountType.Expense);InsertAccount(impairmentExpense,chartId,"6510","Impairment expense",FinanceAccountType.Expense);InsertAccount(cash,chartId,"1000","Cash",FinanceAccountType.Asset);InsertAccount(gain,chartId,"4700","Asset disposal gain",FinanceAccountType.Revenue);InsertAccount(loss,chartId,"6520","Asset disposal loss",FinanceAccountType.Expense);
		_database.Execute("INSERT INTO FinanceAccountingBooks (Id,LegalEntityId,ChartOfAccountsId,Code,Name,ReportingCurrencyCode,AccountingStandardCode,IsPrimary,IsActive) VALUES ($Id,$Entity,$Chart,'FA-BOOK','Fixed Asset Book','EUR','TEST',1,1);",new DatabaseParameter("$Id",bookId.ToString("D")),new DatabaseParameter("$Entity",_legalEntityId.ToString("D")),new DatabaseParameter("$Chart",chartId.ToString("D")));
		_database.Execute("INSERT INTO FinanceJournals (Id,AccountingBookId,Code,Name,IsActive) VALUES ($Id,$Book,'FA','Fixed Assets',1);",new DatabaseParameter("$Id",journalId.ToString("D")),new DatabaseParameter("$Book",bookId.ToString("D")));
		_database.Execute("INSERT INTO FinanceNumberSequences (Id,LegalEntityId,Code,DocumentType,Prefix,NumericLength,NextNumber,IsActive) VALUES ($Id,$Entity,'FA-GL',$Type,'FA-',6,1,1);",new DatabaseParameter("$Id",sequenceId.ToString("D")),new DatabaseParameter("$Entity",_legalEntityId.ToString("D")),new DatabaseParameter("$Type",FinanceNumberSequenceDocumentTypes.GeneralLedger));
		_capitalizationProfileId=InsertProfile(bookId,journalId,"FA-CAP","Capitalization",(assetCost,FinancePostingDirection.Debit,"AssetCost"),(offset,FinancePostingDirection.Credit,"AssetCost"));
		_depreciationProfileId=InsertProfile(bookId,journalId,"FA-DEP","Depreciation",(depreciationExpense,FinancePostingDirection.Debit,"Depreciation"),(accumulatedDepreciation,FinancePostingDirection.Credit,"Depreciation"));
		_impairmentProfileId=InsertProfile(bookId,journalId,"FA-IMP","Impairment",(impairmentExpense,FinancePostingDirection.Debit,"Impairment"),(accumulatedImpairment,FinancePostingDirection.Credit,"Impairment"));
		_disposalProfileId=InsertProfile(bookId,journalId,"FA-DIS","Disposal",(accumulatedDepreciation,FinancePostingDirection.Debit,"AccumulatedDepreciation"),(accumulatedImpairment,FinancePostingDirection.Debit,"Impairment"),(cash,FinancePostingDirection.Debit,"Proceeds"),(loss,FinancePostingDirection.Debit,"Loss"),(assetCost,FinancePostingDirection.Credit,"AssetCost"),(gain,FinancePostingDirection.Credit,"Gain"));
	}

	private void InsertAccount(Guid id,Guid chartId,string number,string name,FinanceAccountType type) =>
		_database.Execute("INSERT INTO FinanceAccounts (Id,ChartOfAccountsId,Number,Name,AccountType,AllowDirectPosting,IsActive) VALUES ($Id,$Chart,$Number,$Name,$Type,1,1);",new DatabaseParameter("$Id",id.ToString("D")),new DatabaseParameter("$Chart",chartId.ToString("D")),new DatabaseParameter("$Number",number),new DatabaseParameter("$Name",name),new DatabaseParameter("$Type",(int)type));

	private long InsertProfile(Guid bookId,Guid journalId,string code,string eventName,params (Guid AccountId,FinancePostingDirection Direction,string AmountKey)[] lines)
	{
		var id=_database.Insert("INSERT INTO FinancePostingProfiles (Version,LegalEntityId,AccountingBookId,JournalId,Code,Name,SourceType,SourceEvent,NumberSequenceCode,IsActive) VALUES (1,$Entity,$Book,$Journal,$Code,$Code,'FixedAssets',$Event,'FA-GL',1);",new DatabaseParameter("$Entity",_legalEntityId.ToString("D")),new DatabaseParameter("$Book",bookId.ToString("D")),new DatabaseParameter("$Journal",journalId.ToString("D")),new DatabaseParameter("$Code",code),new DatabaseParameter("$Event",eventName));
		for(var index=0;index<lines.Length;index++){var line=lines[index];_database.Execute("INSERT INTO FinancePostingProfileLines (PostingProfileId,LineNumber,AccountId,Direction,AmountKey,Multiplier,Description) VALUES ($Profile,$Line,$Account,$Direction,$Key,1,NULL);",new DatabaseParameter("$Profile",id),new DatabaseParameter("$Line",index+1),new DatabaseParameter("$Account",line.AccountId.ToString("D")),new DatabaseParameter("$Direction",(int)line.Direction),new DatabaseParameter("$Key",line.AmountKey));}
		return id;
	}

	private void AssertBalanced(long? journalEntryId)
	{
		Assert.True(journalEntryId.HasValue);
		var debit=Convert.ToDecimal(_database.ExecuteScalarAsync("SELECT COALESCE(SUM(TransactionDebit),0) FROM FinanceJournalEntryLines WHERE JournalEntryId=$Id;",CancellationToken.None,new DatabaseParameter("$Id",journalEntryId.Value)).GetAwaiter().GetResult(),System.Globalization.CultureInfo.InvariantCulture);
		var credit=Convert.ToDecimal(_database.ExecuteScalarAsync("SELECT COALESCE(SUM(TransactionCredit),0) FROM FinanceJournalEntryLines WHERE JournalEntryId=$Id;",CancellationToken.None,new DatabaseParameter("$Id",journalEntryId.Value)).GetAwaiter().GetResult(),System.Globalization.CultureInfo.InvariantCulture);
		Assert.Equal(debit,credit);
	}


	private FinanceFixedAsset NewAsset(long classId,long sourceLineId,string currency) => new()
	{
		AssetNumber=$"FA-{Guid.NewGuid():N}",LegalEntityId=_legalEntityId,AssetClassId=classId,Description="AP-assisted asset",
		AcquisitionDate=new DateOnly(2026,1,1),DepreciationStartDate=new DateOnly(2026,1,1),Currency=new CurrencyCode(currency),
		OriginalCost=250m,SalvageValue=0m,UsefulLifeMonths=1,DepreciationMethod=FinanceDepreciationMethod.NoDepreciation,
		Status=FinanceAssetStatus.Draft,SourceSupplierDocumentLineId=sourceLineId
	};

	private long InsertSupplierLine(FinancePayableDocumentStatus status,string currency)
	{
		var suffix=Guid.NewGuid().ToString("N");var userId=Convert.ToInt64(_database.ExecuteScalarAsync("SELECT Id FROM Users WHERE Email='admin@depot.local';",CancellationToken.None).GetAwaiter().GetResult(),System.Globalization.CultureInfo.InvariantCulture);
		var supplierId=_database.Insert("INSERT INTO Suppliers (SupplierNumber,AccountNumber,Name,Loyalty,Quality,IsActive) VALUES ($Number,$Account,'Fixed Asset Supplier',100,100,1);",new DatabaseParameter("$Number",$"FA-{suffix[..12]}"),new DatabaseParameter("$Account",Math.Abs(DateTime.UtcNow.Ticks%1000000000L)));
		var documentId=_database.Insert("INSERT INTO FinanceSupplierDocuments (Version,Kind,SupplierId,SupplierDocumentNumber,DocumentDate,DueDate,CurrencyCode,Status,NetAmount,TaxAmount,GrossAmount,CreatedByUserId,CreatedAtUtc,MatchExceptionApproved) VALUES (1,$Kind,$Supplier,$Number,'2026-01-01','2026-01-31',$Currency,$Status,250,0,250,$User,$Created,0);",new DatabaseParameter("$Kind",(int)FinancePayableDocumentKind.Invoice),new DatabaseParameter("$Supplier",supplierId),new DatabaseParameter("$Number",$"INV-{suffix[..12]}"),new DatabaseParameter("$Currency",currency),new DatabaseParameter("$Status",(int)status),new DatabaseParameter("$User",userId),new DatabaseParameter("$Created","2026-01-01T00:00:00.0000000Z"));
		return _database.Insert("INSERT INTO FinanceSupplierDocumentLines (DocumentId,LineNumber,Description,Quantity,UnitPrice,NetAmount,TaxAmount,GrossAmount,MatchStatus,QuantityVariance,PriceVariance) VALUES ($Document,1,'Capital equipment',1,250,250,0,250,$Match,0,0);",new DatabaseParameter("$Document",documentId),new DatabaseParameter("$Match",(int)FinancePayableMatchStatus.NotRequired));
	}

	private Task<long> InsertAssetAsync(long classId,decimal cost,decimal salvage,int life,FinanceDepreciationMethod method) =>
		_database.InsertAsync("INSERT INTO FinanceFixedAssets (Version,AssetNumber,LegalEntityId,AssetClassId,Description,AcquisitionDate,CapitalizationDate,DepreciationStartDate,CurrencyCode,OriginalCost,SalvageValue,UsefulLifeMonths,DepreciationMethod,Location,Custodian,Status,SourceSupplierDocumentLineId) VALUES (1,$Number,$Entity,$Class,'Test asset','2026-01-01',NULL,'2026-01-01','EUR',$Cost,$Salvage,$Life,$Method,NULL,NULL,1,NULL);",CancellationToken.None,
			new DatabaseParameter("$Number",$"FA-{Guid.NewGuid():N}"),new DatabaseParameter("$Entity",_legalEntityId.ToString("D")),new DatabaseParameter("$Class",classId),new DatabaseParameter("$Cost",cost),new DatabaseParameter("$Salvage",salvage),new DatabaseParameter("$Life",life),new DatabaseParameter("$Method",(int)method));

	public void Dispose(){SqliteConnection.ClearAllPools();try{File.Delete(_path);}catch(IOException){}}
}
