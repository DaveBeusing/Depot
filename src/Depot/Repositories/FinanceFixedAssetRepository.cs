// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;
using Depot.Data;
using Depot.Models;
using Depot.Services;

namespace Depot.Repositories;

public sealed class FinanceFixedAssetRepository : DatabaseRepository
{
	private const string AssetColumns = "Id,Version,AssetNumber,LegalEntityId,AssetClassId,Description,AcquisitionDate,CapitalizationDate,DepreciationStartDate,CurrencyCode,OriginalCost,SalvageValue,UsefulLifeMonths,DepreciationMethod,Location,Custodian,Status,SourceSupplierDocumentLineId";
	private const string ClassColumns = "Id,Version,LegalEntityId,FiscalCalendarId,Code,Name,CapitalizationPostingProfileId,DepreciationPostingProfileId,ImpairmentPostingProfileId,DisposalPostingProfileId,DefaultUsefulLifeMonths,DefaultMethod,IsActive";
	private const string ScheduleColumns = "Id,AssetId,AccountingPeriodId,PeriodStart,PeriodEnd,PlannedAmount,PostedAmount,JournalEntryId,OperationId";
	private const string TransactionColumns = "Id,AssetId,Kind,OperationId,TransactionDate,Amount,JournalEntryId,Reason,Evidence,CreatedAtUtc,CreatedByUserId";

	public FinanceFixedAssetRepository(DatabaseAccess database) : base(database) { }

	public Task<PageResult<FinanceFixedAsset>> SearchAssetsAsync(Guid? legalEntityId,string? searchText,FinanceAssetStatus? status,int pageNumber,int pageSize,CancellationToken cancellationToken=default)
	{
		var filters=new List<string>(); var parameters=new List<DatabaseParameter>();
		if(legalEntityId.HasValue){filters.Add("LegalEntityId=$Entity");parameters.Add(Parameter("$Entity",legalEntityId.Value.ToString("D")));}
		if(status.HasValue){filters.Add("Status=$Status");parameters.Add(Parameter("$Status",(int)status.Value));}
		if(!string.IsNullOrWhiteSpace(searchText)){filters.Add("(AssetNumber LIKE $Search OR Description LIKE $Search OR Location LIKE $Search OR Custodian LIKE $Search)");parameters.Add(Parameter("$Search",$"%{searchText.Trim()}%"));}
		var where=filters.Count==0?string.Empty:" WHERE "+string.Join(" AND ",filters);
		return Database.QueryPageAsync($"SELECT {AssetColumns} FROM FinanceFixedAssets{where} ORDER BY AssetNumber","SELECT COUNT(*) FROM FinanceFixedAssets"+where+";",ReadAsset,pageNumber,Math.Clamp(pageSize,1,200),cancellationToken,parameters.ToArray());
	}

	public Task<FinanceFixedAsset?> GetAssetAsync(long id,CancellationToken cancellationToken=default) =>
		Database.QuerySingleOrDefaultAsync($"SELECT {AssetColumns} FROM FinanceFixedAssets WHERE Id=$Id;",ReadAsset,cancellationToken,Parameter("$Id",id));

	public Task<IReadOnlyList<FinanceAssetLegalEntityOption>> GetLegalEntitiesAsync(CancellationToken cancellationToken=default) =>
		Database.QueryAsync("SELECT Id,Code,Name,FunctionalCurrencyCode FROM FinanceLegalEntities WHERE IsActive=1 ORDER BY Code;",r=>new FinanceAssetLegalEntityOption(Guid.Parse(r.GetString(0)),r.GetString(1),r.GetString(2),new CurrencyCode(r.GetString(3))),cancellationToken);

	public Task<IReadOnlyList<FinanceAssetFiscalCalendarOption>> GetFiscalCalendarsAsync(CancellationToken cancellationToken=default) =>
		Database.QueryAsync("SELECT Id,LegalEntityId,Code,Name,IsActive FROM FinanceFiscalCalendars ORDER BY LegalEntityId,Code;",r=>new FinanceAssetFiscalCalendarOption(Guid.Parse(r.GetString(0)),Guid.Parse(r.GetString(1)),r.GetString(2),r.GetString(3),ReadBool(r,4)),cancellationToken);

	public Task<IReadOnlyList<FinanceAssetPostingProfileOption>> GetPostingProfilesAsync(CancellationToken cancellationToken=default) =>
		Database.QueryAsync("SELECT Id,LegalEntityId,Code,Name,SourceEvent,IsActive FROM FinancePostingProfiles WHERE SourceType='FixedAssets' ORDER BY LegalEntityId,SourceEvent,Code;",r=>new FinanceAssetPostingProfileOption(r.GetInt64(0),Guid.Parse(r.GetString(1)),r.GetString(2),r.GetString(3),r.GetString(4),ReadBool(r,5)),cancellationToken);

	public Task<IReadOnlyList<FinanceAssetPeriodOption>> GetPeriodOptionsAsync(CancellationToken cancellationToken=default) =>
		Database.QueryAsync("SELECT Id,FiscalCalendarId,Code,StartDate,EndDate,Status FROM FinanceAccountingPeriods ORDER BY StartDate,Code;",r=>new FinanceAssetPeriodOption(Guid.Parse(r.GetString(0)),Guid.Parse(r.GetString(1)),r.GetString(2),ReadDate(r,3),ReadDate(r,4),(AccountingPeriodStatus)Convert.ToInt32(r.GetValue(5),CultureInfo.InvariantCulture)),cancellationToken);

	public Task<IReadOnlyList<FinanceAssetSupplierLineOption>> GetCapitalizableSupplierLinesAsync(int limit=200,CancellationToken cancellationToken=default) =>
		Database.QuerySliceAsync("SELECT l.Id,d.SupplierDocumentNumber,l.LineNumber,l.Description,l.NetAmount,d.CurrencyCode FROM FinanceSupplierDocumentLines l INNER JOIN FinanceSupplierDocuments d ON d.Id=l.DocumentId WHERE d.Kind=$Invoice AND d.Status=$Posted ORDER BY d.DocumentDate DESC,d.Id DESC,l.LineNumber",ReadSupplierLineOption,0,Math.Clamp(limit,1,500),cancellationToken,Parameter("$Invoice",(int)FinancePayableDocumentKind.Invoice),Parameter("$Posted",(int)FinancePayableDocumentStatus.Posted));

	public Task<IReadOnlyList<FinanceAssetClass>> GetClassesAsync(Guid? legalEntityId=null,CancellationToken cancellationToken=default) =>
		legalEntityId.HasValue
			? Database.QueryAsync($"SELECT {ClassColumns} FROM FinanceAssetClasses WHERE LegalEntityId=$Entity ORDER BY Code;",ReadClass,cancellationToken,Parameter("$Entity",legalEntityId.Value.ToString("D")))
			: Database.QueryAsync($"SELECT {ClassColumns} FROM FinanceAssetClasses ORDER BY LegalEntityId,Code;",ReadClass,cancellationToken);

	public Task<IReadOnlyList<FinanceAssetDepreciationPeriod>> GetScheduleAsync(long assetId,CancellationToken cancellationToken=default) =>
		Database.QueryAsync($"SELECT {ScheduleColumns} FROM FinanceAssetDepreciationPeriods WHERE AssetId=$Asset ORDER BY PeriodStart,Id;",ReadSchedule,cancellationToken,Parameter("$Asset",assetId));

	public Task<PageResult<FinanceAssetTransaction>> SearchTransactionsAsync(long assetId,int pageNumber,int pageSize,CancellationToken cancellationToken=default) =>
		Database.QueryPageAsync($"SELECT {TransactionColumns} FROM FinanceAssetTransactions WHERE AssetId=$Asset ORDER BY TransactionDate DESC,Id DESC","SELECT COUNT(*) FROM FinanceAssetTransactions WHERE AssetId=$Asset;",ReadTransaction,pageNumber,Math.Clamp(pageSize,1,200),cancellationToken,Parameter("$Asset",assetId));

	public Task<IReadOnlyList<FinanceAssetDepreciationPeriod>> GetPendingDepreciationAsync(Guid accountingPeriodId,int limit=5000,CancellationToken cancellationToken=default) =>
		Database.QuerySliceAsync($"SELECT {ScheduleColumns} FROM FinanceAssetDepreciationPeriods WHERE AccountingPeriodId=$Period AND JournalEntryId IS NULL ORDER BY AssetId,Id",ReadSchedule,0,Math.Clamp(limit,1,5000),cancellationToken,Parameter("$Period",accountingPeriodId.ToString("D")));

	public async Task<PageResult<FinanceAssetReconciliationRow>> SearchReconciliationAsync(Guid legalEntityId,int pageNumber,int pageSize,CancellationToken cancellationToken=default)
	{
		var assets=await SearchAssetsAsync(legalEntityId,null,null,pageNumber,pageSize,cancellationToken);
		if(assets.Items.Count==0)return new PageResult<FinanceAssetReconciliationRow>([],pageNumber,pageSize,assets.TotalCount);

		var classIds=assets.Items.Select(value=>value.AssetClassId).Distinct().ToArray();
		var classParameters=new List<DatabaseParameter>{Parameter("$AssetType",(int)FinanceAccountType.Asset)};
		var classNames=new List<string>(classIds.Length);
		for(var i=0;i<classIds.Length;i++){var name=$"$Class{i}";classNames.Add(name);classParameters.Add(Parameter(name,classIds[i]));}
		var configuredAccounts=await Database.QueryAsync(
			$"SELECT c.Id,l.AccountId FROM FinanceAssetClasses c INNER JOIN FinancePostingProfileLines l ON l.PostingProfileId IN (c.CapitalizationPostingProfileId,c.DepreciationPostingProfileId,c.ImpairmentPostingProfileId) INNER JOIN FinanceAccounts a ON a.Id=l.AccountId WHERE c.Id IN ({string.Join(",",classNames)}) AND a.AccountType=$AssetType GROUP BY c.Id,l.AccountId;",
			r=>new { ClassId=r.GetInt64(0),AccountId=Guid.Parse(r.GetString(1)) },cancellationToken,classParameters.ToArray());
		var accountsByClass=configuredAccounts.GroupBy(value=>value.ClassId).ToDictionary(group=>group.Key,group=>group.Select(value=>value.AccountId).ToHashSet());

		var ids=assets.Items.Select(value=>value.Id).ToArray();
		var idParameters=new List<DatabaseParameter>();var idNames=new List<string>(ids.Length);
		for(var i=0;i<ids.Length;i++){var name=$"$Id{i}";idNames.Add(name);idParameters.Add(Parameter(name,ids[i]));}
		var journalBalances=await Database.QueryAsync(
			$"SELECT t.AssetId,jl.AccountId,COALESCE(SUM(jl.TransactionDebit-jl.TransactionCredit),0) FROM FinanceAssetTransactions t INNER JOIN FinanceJournalEntryLines jl ON jl.JournalEntryId=t.JournalEntryId WHERE t.AssetId IN ({string.Join(",",idNames)}) AND t.JournalEntryId IS NOT NULL GROUP BY t.AssetId,jl.AccountId;",
			r=>new { AssetId=r.GetInt64(0),AccountId=Guid.Parse(r.GetString(1)),Balance=ReadDecimal(r,2) },cancellationToken,idParameters.ToArray());
		var classByAsset=assets.Items.ToDictionary(value=>value.Id,value=>value.AssetClassId);
		var glByAsset=journalBalances
			.Where(value=>accountsByClass.TryGetValue(classByAsset[value.AssetId],out var accounts)&&accounts.Contains(value.AccountId))
			.GroupBy(value=>value.AssetId)
			.ToDictionary(group=>group.Key,group=>group.Sum(value=>value.Balance));

		var balances=await Database.QueryAsync(
			$"SELECT AssetId,COALESCE(SUM(CASE WHEN Kind=1 THEN Amount WHEN Kind IN (2,3) THEN -Amount WHEN Kind=4 THEN Amount ELSE 0 END),0),MAX(CASE WHEN Kind=6 THEN 1 ELSE 0 END) FROM FinanceAssetTransactions WHERE AssetId IN ({string.Join(",",idNames)}) GROUP BY AssetId;",
			r=>new { AssetId=r.GetInt64(0),Carrying=ReadDecimal(r,1),Disposed=Convert.ToInt32(r.GetValue(2),CultureInfo.InvariantCulture)!=0 },cancellationToken,idParameters.ToArray());
		var subledger=balances.ToDictionary(value=>value.AssetId,value=>value.Disposed?0m:value.Carrying);
		var rows=assets.Items.Select(asset=>new FinanceAssetReconciliationRow(asset.Id,asset.AssetNumber,subledger.GetValueOrDefault(asset.Id),glByAsset.GetValueOrDefault(asset.Id))).ToArray();
		return new PageResult<FinanceAssetReconciliationRow>(rows,pageNumber,pageSize,assets.TotalCount);
	}

	internal Task<FinanceAssetClass?> GetClassAsync(DatabaseTransactionContext transaction,long id,CancellationToken token) =>
		transaction.Session.QuerySingleOrDefaultAsync($"SELECT {ClassColumns} FROM FinanceAssetClasses WHERE Id=$Id;",ReadClass,token,Parameter("$Id",id));

	internal Task<FinanceFixedAsset?> GetAssetAsync(DatabaseTransactionContext transaction,long id,CancellationToken token) =>
		transaction.Session.QuerySingleOrDefaultAsync($"SELECT {AssetColumns} FROM FinanceFixedAssets WHERE Id=$Id;",ReadAsset,token,Parameter("$Id",id));

	internal Task<FinanceAssetDepreciationPeriod?> GetSchedulePeriodAsync(DatabaseTransactionContext transaction,long id,CancellationToken token) =>
		transaction.Session.QuerySingleOrDefaultAsync($"SELECT {ScheduleColumns} FROM FinanceAssetDepreciationPeriods WHERE Id=$Id;",ReadSchedule,token,Parameter("$Id",id));

	internal Task<FinanceAssetTransaction?> GetTransactionAsync(DatabaseTransactionContext transaction,long id,CancellationToken token) =>
		transaction.Session.QuerySingleOrDefaultAsync($"SELECT {TransactionColumns} FROM FinanceAssetTransactions WHERE Id=$Id;",ReadTransaction,token,Parameter("$Id",id));

	internal Task<long> CreateClassAsync(DatabaseTransactionContext transaction,FinanceAssetClass value,CancellationToken token) =>
		transaction.Session.InsertAsync("INSERT INTO FinanceAssetClasses (Version,LegalEntityId,FiscalCalendarId,Code,Name,CapitalizationPostingProfileId,DepreciationPostingProfileId,ImpairmentPostingProfileId,DisposalPostingProfileId,DefaultUsefulLifeMonths,DefaultMethod,IsActive) VALUES (1,$Entity,$Calendar,$Code,$Name,$Capitalization,$Depreciation,$Impairment,$Disposal,$Life,$Method,$Active);",token,ClassParameters(value));

	internal Task<int> UpdateClassAsync(DatabaseTransactionContext transaction,FinanceAssetClass value,long expectedVersion,CancellationToken token) =>
		transaction.Session.ExecuteAsync("UPDATE FinanceAssetClasses SET Version=Version+1,LegalEntityId=$Entity,FiscalCalendarId=$Calendar,Code=$Code,Name=$Name,CapitalizationPostingProfileId=$Capitalization,DepreciationPostingProfileId=$Depreciation,ImpairmentPostingProfileId=$Impairment,DisposalPostingProfileId=$Disposal,DefaultUsefulLifeMonths=$Life,DefaultMethod=$Method,IsActive=$Active WHERE Id=$Id AND Version=$Expected;",token,ClassParameters(value).Append(Parameter("$Id",value.Id)).Append(Parameter("$Expected",expectedVersion)).ToArray());

	internal Task<long> CreateAssetAsync(DatabaseTransactionContext transaction,FinanceFixedAsset value,CancellationToken token) =>
		transaction.Session.InsertAsync("INSERT INTO FinanceFixedAssets (Version,AssetNumber,LegalEntityId,AssetClassId,Description,AcquisitionDate,CapitalizationDate,DepreciationStartDate,CurrencyCode,OriginalCost,SalvageValue,UsefulLifeMonths,DepreciationMethod,Location,Custodian,Status,SourceSupplierDocumentLineId) VALUES (1,$Number,$Entity,$Class,$Description,$Acquisition,$Capitalization,$DepStart,$Currency,$Cost,$Salvage,$Life,$Method,$Location,$Custodian,$Status,$SourceLine);",token,AssetParameters(value));

	internal Task<int> UpdateAssetAsync(DatabaseTransactionContext transaction,FinanceFixedAsset value,long expectedVersion,CancellationToken token) =>
		transaction.Session.ExecuteAsync("UPDATE FinanceFixedAssets SET Version=Version+1,AssetClassId=$Class,Description=$Description,AcquisitionDate=$Acquisition,CapitalizationDate=$Capitalization,DepreciationStartDate=$DepStart,CurrencyCode=$Currency,OriginalCost=$Cost,SalvageValue=$Salvage,UsefulLifeMonths=$Life,DepreciationMethod=$Method,Location=$Location,Custodian=$Custodian,Status=$Status,SourceSupplierDocumentLineId=$SourceLine WHERE Id=$Id AND Version=$Expected;",token,AssetParameters(value).Append(Parameter("$Id",value.Id)).Append(Parameter("$Expected",expectedVersion)).ToArray());

	internal Task<IReadOnlyList<FinanceAccountingPeriodRecord>> GetPeriodsAsync(DatabaseTransactionContext transaction,Guid fiscalCalendarId,DateOnly fromDate,CancellationToken token) =>
		transaction.Session.QueryAsync("SELECT Id,StartDate,EndDate,Status FROM FinanceAccountingPeriods WHERE FiscalCalendarId=$Calendar AND EndDate>=$From ORDER BY StartDate,Id;",reader=>new FinanceAccountingPeriodRecord(Guid.Parse(reader.GetString(0)),ReadDate(reader,1),ReadDate(reader,2),(AccountingPeriodStatus)Convert.ToInt32(reader.GetValue(3),CultureInfo.InvariantCulture)),token,Parameter("$Calendar",fiscalCalendarId.ToString("D")),Parameter("$From",fromDate.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)));

	internal async Task<bool> FiscalCalendarMatchesEntityAsync(DatabaseTransactionContext transaction,Guid calendarId,Guid entityId,CancellationToken token) =>
		Convert.ToInt64(await transaction.Session.ExecuteScalarAsync("SELECT COUNT(*) FROM FinanceFiscalCalendars WHERE Id=$Calendar AND LegalEntityId=$Entity AND IsActive=1;",token,Parameter("$Calendar",calendarId.ToString("D")),Parameter("$Entity",entityId.ToString("D"))) ?? 0,CultureInfo.InvariantCulture)==1;

	internal Task<FinanceAssetSupplierLineOption?> GetCapitalizableSupplierLineAsync(DatabaseTransactionContext transaction,long lineId,CancellationToken token) =>
		transaction.Session.QuerySingleOrDefaultAsync("SELECT l.Id,d.SupplierDocumentNumber,l.LineNumber,l.Description,l.NetAmount,d.CurrencyCode FROM FinanceSupplierDocumentLines l INNER JOIN FinanceSupplierDocuments d ON d.Id=l.DocumentId WHERE l.Id=$Id AND d.Kind=$Invoice AND d.Status=$Posted;",ReadSupplierLineOption,token,Parameter("$Id",lineId),Parameter("$Invoice",(int)FinancePayableDocumentKind.Invoice),Parameter("$Posted",(int)FinancePayableDocumentStatus.Posted));

	internal Task ReplaceScheduleAsync(DatabaseTransactionContext transaction,long assetId,IReadOnlyList<FinanceAssetDepreciationPeriod> periods,CancellationToken token) =>
		ReplaceScheduleCoreAsync(transaction,assetId,periods,token);

	internal async Task MarkSchedulePostedAsync(DatabaseTransactionContext transaction,long scheduleId,decimal amount,long journalEntryId,Guid operationId,CancellationToken token)
	{
		var affected=await transaction.Session.ExecuteAsync("UPDATE FinanceAssetDepreciationPeriods SET PostedAmount=$Amount,JournalEntryId=$Journal,OperationId=$Operation WHERE Id=$Id AND JournalEntryId IS NULL;",token,Parameter("$Amount",amount),Parameter("$Journal",journalEntryId),Parameter("$Operation",operationId.ToString("D")),Parameter("$Id",scheduleId));
		if(affected!=1) throw new ConcurrencyConflictException("fixed asset depreciation period");
	}

	internal Task<long> CreateTransactionAsync(DatabaseTransactionContext transaction,FinanceAssetTransaction value,CancellationToken token) =>
		transaction.Session.InsertAsync("INSERT INTO FinanceAssetTransactions (AssetId,Kind,OperationId,TransactionDate,Amount,JournalEntryId,Reason,Evidence,CreatedAtUtc,CreatedByUserId) VALUES ($Asset,$Kind,$Operation,$Date,$Amount,$Journal,$Reason,$Evidence,$At,$User);",token,
			Parameter("$Asset",value.AssetId),Parameter("$Kind",(int)value.Kind),Parameter("$Operation",value.OperationId.ToString("D")),Parameter("$Date",value.TransactionDate.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)),Parameter("$Amount",value.Amount),Parameter("$Journal",value.JournalEntryId),Parameter("$Reason",value.Reason),Parameter("$Evidence",value.Evidence),Parameter("$At",value.CreatedAtUtc),Parameter("$User",value.CreatedByUserId));

	internal Task<FinanceAssetTransaction?> FindTransactionByOperationAsync(DatabaseTransactionContext transaction,Guid operationId,CancellationToken token) =>
		transaction.Session.QuerySingleOrDefaultAsync($"SELECT {TransactionColumns} FROM FinanceAssetTransactions WHERE OperationId=$Operation;",ReadTransaction,token,Parameter("$Operation",operationId.ToString("D")));

	internal async Task<decimal> SumPostedAsync(DatabaseTransactionContext transaction,long assetId,FinanceAssetTransactionKind kind,CancellationToken token) =>
		Convert.ToDecimal(await transaction.Session.ExecuteScalarAsync("SELECT COALESCE(SUM(Amount),0) FROM FinanceAssetTransactions WHERE AssetId=$Asset AND Kind=$Kind;",token,Parameter("$Asset",assetId),Parameter("$Kind",(int)kind)) ?? 0m,CultureInfo.InvariantCulture);

	private static async Task ReplaceScheduleCoreAsync(DatabaseTransactionContext transaction,long assetId,IReadOnlyList<FinanceAssetDepreciationPeriod> periods,CancellationToken token)
	{
		var posted=Convert.ToInt64(await transaction.Session.ExecuteScalarAsync("SELECT COUNT(*) FROM FinanceAssetDepreciationPeriods WHERE AssetId=$Asset AND JournalEntryId IS NOT NULL;",token,Parameter("$Asset",assetId)) ?? 0,CultureInfo.InvariantCulture);
		if(posted>0) throw new InvalidOperationException("A depreciation schedule with posted periods cannot be replaced.");
		await transaction.Session.ExecuteAsync("DELETE FROM FinanceAssetDepreciationPeriods WHERE AssetId=$Asset;",token,Parameter("$Asset",assetId));
		foreach(var p in periods)
			await transaction.Session.InsertAsync("INSERT INTO FinanceAssetDepreciationPeriods (AssetId,AccountingPeriodId,PeriodStart,PeriodEnd,PlannedAmount,PostedAmount,JournalEntryId,OperationId) VALUES ($Asset,$Period,$Start,$End,$Planned,0,NULL,NULL);",token,Parameter("$Asset",assetId),Parameter("$Period",p.AccountingPeriodId.ToString("D")),Parameter("$Start",p.PeriodStart.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)),Parameter("$End",p.PeriodEnd.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)),Parameter("$Planned",p.PlannedAmount));
	}

	private static DatabaseParameter[] ClassParameters(FinanceAssetClass v) => [
		Parameter("$Entity",v.LegalEntityId.ToString("D")),Parameter("$Calendar",v.FiscalCalendarId.ToString("D")),Parameter("$Code",v.Code),Parameter("$Name",v.Name),Parameter("$Capitalization",v.CapitalizationPostingProfileId),Parameter("$Depreciation",v.DepreciationPostingProfileId),Parameter("$Impairment",v.ImpairmentPostingProfileId),Parameter("$Disposal",v.DisposalPostingProfileId),Parameter("$Life",v.DefaultUsefulLifeMonths),Parameter("$Method",(int)v.DefaultMethod),Parameter("$Active",v.IsActive?1:0)
	];
	private static DatabaseParameter[] AssetParameters(FinanceFixedAsset v) => [
		Parameter("$Number",v.AssetNumber),Parameter("$Entity",v.LegalEntityId.ToString("D")),Parameter("$Class",v.AssetClassId),Parameter("$Description",v.Description),Parameter("$Acquisition",v.AcquisitionDate.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)),Parameter("$Capitalization",v.CapitalizationDate?.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)),Parameter("$DepStart",v.DepreciationStartDate.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)),Parameter("$Currency",v.Currency.Value),Parameter("$Cost",v.OriginalCost),Parameter("$Salvage",v.SalvageValue),Parameter("$Life",v.UsefulLifeMonths),Parameter("$Method",(int)v.DepreciationMethod),Parameter("$Location",v.Location),Parameter("$Custodian",v.Custodian),Parameter("$Status",(int)v.Status),Parameter("$SourceLine",v.SourceSupplierDocumentLineId)
	];

	private static FinanceAssetClass ReadClass(DbDataReader r)=>new(){Id=r.GetInt64(0),Version=r.GetInt64(1),LegalEntityId=Guid.Parse(r.GetString(2)),FiscalCalendarId=Guid.Parse(r.GetString(3)),Code=r.GetString(4),Name=r.GetString(5),CapitalizationPostingProfileId=r.GetInt64(6),DepreciationPostingProfileId=r.GetInt64(7),ImpairmentPostingProfileId=r.GetInt64(8),DisposalPostingProfileId=r.GetInt64(9),DefaultUsefulLifeMonths=r.GetInt32(10),DefaultMethod=(FinanceDepreciationMethod)r.GetInt32(11),IsActive=ReadBool(r,12)};
	private static FinanceFixedAsset ReadAsset(DbDataReader r)=>new(){Id=r.GetInt64(0),Version=r.GetInt64(1),AssetNumber=r.GetString(2),LegalEntityId=Guid.Parse(r.GetString(3)),AssetClassId=r.GetInt64(4),Description=r.GetString(5),AcquisitionDate=ReadDate(r,6),CapitalizationDate=r.IsDBNull(7)?null:ReadDate(r,7),DepreciationStartDate=ReadDate(r,8),Currency=new CurrencyCode(r.GetString(9)),OriginalCost=ReadDecimal(r,10),SalvageValue=ReadDecimal(r,11),UsefulLifeMonths=r.GetInt32(12),DepreciationMethod=(FinanceDepreciationMethod)r.GetInt32(13),Location=r.IsDBNull(14)?null:r.GetString(14),Custodian=r.IsDBNull(15)?null:r.GetString(15),Status=(FinanceAssetStatus)r.GetInt32(16),SourceSupplierDocumentLineId=r.IsDBNull(17)?null:r.GetInt64(17)};
	private static FinanceAssetDepreciationPeriod ReadSchedule(DbDataReader r)=>new(){Id=r.GetInt64(0),AssetId=r.GetInt64(1),AccountingPeriodId=Guid.Parse(r.GetString(2)),PeriodStart=ReadDate(r,3),PeriodEnd=ReadDate(r,4),PlannedAmount=ReadDecimal(r,5),PostedAmount=ReadDecimal(r,6),JournalEntryId=r.IsDBNull(7)?null:r.GetInt64(7),OperationId=r.IsDBNull(8)?null:Guid.Parse(r.GetString(8))};
	private static FinanceAssetTransaction ReadTransaction(DbDataReader r)=>new(){Id=r.GetInt64(0),AssetId=r.GetInt64(1),Kind=(FinanceAssetTransactionKind)r.GetInt32(2),OperationId=Guid.Parse(r.GetString(3)),TransactionDate=ReadDate(r,4),Amount=ReadDecimal(r,5),JournalEntryId=r.IsDBNull(6)?null:r.GetInt64(6),Reason=r.IsDBNull(7)?null:r.GetString(7),Evidence=r.IsDBNull(8)?null:r.GetString(8),CreatedAtUtc=ReadDateTime(r,9),CreatedByUserId=r.GetInt64(10)};
	private static FinanceAssetSupplierLineOption ReadSupplierLineOption(DbDataReader r)=>new(r.GetInt64(0),r.GetString(1),Convert.ToInt32(r.GetValue(2),CultureInfo.InvariantCulture),r.GetString(3),ReadDecimal(r,4),new CurrencyCode(r.GetString(5)));
	private static DateOnly ReadDate(DbDataReader r,int i)=>r.GetValue(i) is DateTime d?DateOnly.FromDateTime(d):DateOnly.Parse(r.GetString(i),CultureInfo.InvariantCulture);
	private static DateTime ReadDateTime(DbDataReader r,int i)=>r.GetValue(i) is DateTime d?DateTime.SpecifyKind(d,DateTimeKind.Utc):DateTime.Parse(r.GetString(i),CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal);
	private static decimal ReadDecimal(DbDataReader r,int i)=>Convert.ToDecimal(r.GetValue(i),CultureInfo.InvariantCulture);
	private static bool ReadBool(DbDataReader r,int i)=>Convert.ToBoolean(r.GetValue(i),CultureInfo.InvariantCulture);
}
