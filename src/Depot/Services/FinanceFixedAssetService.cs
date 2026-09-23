// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Depot.Services;

public sealed class FinanceFixedAssetService
{
	public const string SourceType="FixedAssets";
	public const string CapitalizationEvent="Capitalization";
	public const string DepreciationEvent="Depreciation";
	public const string ImpairmentEvent="Impairment";
	public const string DisposalEvent="Disposal";

	private readonly IDatabaseTransactionRunner _transactions;
	private readonly FinanceFixedAssetRepository _assets;
	private readonly FinanceGeneralLedgerService _ledger;
	private readonly AuditRepository _auditEntries;
	private readonly AuditService _audit;
	private readonly IAuthorizationService _authorization;

	public FinanceFixedAssetService(IDatabaseTransactionRunner transactions,FinanceFixedAssetRepository assets,FinanceGeneralLedgerService ledger,AuditRepository auditEntries,AuditService audit,IAuthorizationService authorization)
	{_transactions=transactions;_assets=assets;_ledger=ledger;_auditEntries=auditEntries;_audit=audit;_authorization=authorization;}

	public bool CanView=>_authorization.HasPermission(ApplicationPermission.FinanceFixedAssetsView);
	public bool CanConfigure=>_authorization.HasPermission(ApplicationPermission.FinanceFixedAssetsConfigure);
	public bool CanManage=>_authorization.HasPermission(ApplicationPermission.FinanceFixedAssetsManage);
	public bool CanPostDepreciation=>_authorization.HasPermission(ApplicationPermission.FinanceFixedAssetsDepreciationPost);

	public Task<PageResult<FinanceFixedAsset>> SearchAssetsAsync(Guid? legalEntityId=null,string? searchText=null,FinanceAssetStatus? status=null,int pageNumber=1,int pageSize=100,CancellationToken token=default)
	{_authorization.RequirePermission(ApplicationPermission.FinanceFixedAssetsView);return _assets.SearchAssetsAsync(legalEntityId,searchText,status,pageNumber,pageSize,token);}
	public Task<FinanceFixedAsset?> GetAssetAsync(long id,CancellationToken token=default)
	{_authorization.RequirePermission(ApplicationPermission.FinanceFixedAssetsView);return _assets.GetAssetAsync(id,token);}
	public Task<IReadOnlyList<FinanceAssetClass>> GetClassesAsync(Guid? legalEntityId=null,CancellationToken token=default)
	{_authorization.RequirePermission(ApplicationPermission.FinanceFixedAssetsView);return _assets.GetClassesAsync(legalEntityId,token);}
	public Task<IReadOnlyList<FinanceAssetLegalEntityOption>> GetLegalEntitiesAsync(CancellationToken token=default)
	{_authorization.RequirePermission(ApplicationPermission.FinanceFixedAssetsView);return _assets.GetLegalEntitiesAsync(token);}
	public Task<IReadOnlyList<FinanceAssetFiscalCalendarOption>> GetFiscalCalendarsAsync(CancellationToken token=default)
	{_authorization.RequirePermission(ApplicationPermission.FinanceFixedAssetsView);return _assets.GetFiscalCalendarsAsync(token);}
	public Task<IReadOnlyList<FinanceAssetPostingProfileOption>> GetPostingProfilesAsync(CancellationToken token=default)
	{_authorization.RequirePermission(ApplicationPermission.FinanceFixedAssetsView);return _assets.GetPostingProfilesAsync(token);}
	public Task<IReadOnlyList<FinanceAssetPeriodOption>> GetPeriodOptionsAsync(CancellationToken token=default)
	{_authorization.RequirePermission(ApplicationPermission.FinanceFixedAssetsView);return _assets.GetPeriodOptionsAsync(token);}
	public Task<IReadOnlyList<FinanceAssetSupplierLineOption>> GetCapitalizableSupplierLinesAsync(int limit=200,CancellationToken token=default)
	{_authorization.RequirePermission(ApplicationPermission.FinanceFixedAssetsView);return _assets.GetCapitalizableSupplierLinesAsync(limit,token);}
	public Task<IReadOnlyList<FinanceAssetDepreciationPeriod>> GetScheduleAsync(long assetId,CancellationToken token=default)
	{_authorization.RequirePermission(ApplicationPermission.FinanceFixedAssetsView);return _assets.GetScheduleAsync(assetId,token);}
	public Task<PageResult<FinanceAssetTransaction>> SearchTransactionsAsync(long assetId,int pageNumber=1,int pageSize=100,CancellationToken token=default)
	{_authorization.RequirePermission(ApplicationPermission.FinanceFixedAssetsView);return _assets.SearchTransactionsAsync(assetId,pageNumber,pageSize,token);}
	public Task<PageResult<FinanceAssetReconciliationRow>> SearchReconciliationAsync(Guid legalEntityId,int pageNumber=1,int pageSize=100,CancellationToken token=default)
	{_authorization.RequirePermission(ApplicationPermission.FinanceFixedAssetsView);if(legalEntityId==Guid.Empty)throw new ArgumentException("A legal entity is required.",nameof(legalEntityId));return _assets.SearchReconciliationAsync(legalEntityId,pageNumber,pageSize,token);}

	public async Task<FinanceDepreciationRunResult> RunDepreciationAsync(Guid accountingPeriodId,Guid runOperationId,FinanceClosedPeriodPolicy closedPeriodPolicy=FinanceClosedPeriodPolicy.Fail,CancellationToken token=default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceFixedAssetsDepreciationPost);RequireUser();if(accountingPeriodId==Guid.Empty)throw new ArgumentException("An accounting period is required.",nameof(accountingPeriodId));RequireOperation(runOperationId);
		var candidates=await _assets.GetPendingDepreciationAsync(accountingPeriodId,5000,token);var posted=new List<FinanceAssetTransaction>(candidates.Count);
		foreach(var candidate in candidates){token.ThrowIfCancellationRequested();var child=DeriveOperationId(runOperationId,candidate.Id);posted.Add(await PostDepreciationAsync(candidate.Id,child,closedPeriodPolicy,token));}
		return new FinanceDepreciationRunResult(runOperationId,accountingPeriodId,candidates.Count,posted.Count,posted.Sum(value=>value.Amount),posted);
	}

	public async Task<FinanceAssetClass> SaveClassAsync(FinanceAssetClass value,CancellationToken token=default)
	{
		ArgumentNullException.ThrowIfNull(value);_authorization.RequirePermission(ApplicationPermission.FinanceFixedAssetsConfigure);var user=RequireUser();var normalized=NormalizeClass(value);
		return await _transactions.ExecuteAsync(async(tx,ct)=>{
			if(!await _assets.FiscalCalendarMatchesEntityAsync(tx,normalized.FiscalCalendarId,normalized.LegalEntityId,ct)) throw new InvalidOperationException("The fiscal calendar is not active for the selected legal entity.");
			await ValidateProfilesAsync(tx,normalized,ct);
			if(normalized.Id==0){var id=await _assets.CreateClassAsync(tx,normalized,ct);var created=await _assets.GetClassAsync(tx,id,ct)??throw new InvalidOperationException("Asset class could not be reloaded.");await _auditEntries.CreateAsync(tx,_audit.CreateCreatedEntry(id,created),ct);return created;}
			var before=await _assets.GetClassAsync(tx,normalized.Id,ct)??throw new InvalidOperationException("Asset class was not found.");if(before.Version!=normalized.Version)throw new ConcurrencyConflictException("fixed asset class");
			if(await _assets.UpdateClassAsync(tx,normalized,before.Version,ct)!=1)throw new ConcurrencyConflictException("fixed asset class");
			var after=await _assets.GetClassAsync(tx,normalized.Id,ct)??throw new InvalidOperationException("Asset class could not be reloaded.");await _auditEntries.CreateAsync(tx,_audit.CreateUpdatedEntry(after.Id,before,after),ct);return after;
		},token);
	}

	public async Task<FinanceFixedAsset> SaveAssetAsync(FinanceFixedAsset value,CancellationToken token=default)
	{
		ArgumentNullException.ThrowIfNull(value);_authorization.RequirePermission(ApplicationPermission.FinanceFixedAssetsManage);RequireUser();var normalized=NormalizeAsset(value);
		return await _transactions.ExecuteAsync(async(tx,ct)=>{
			var assetClass=await _assets.GetClassAsync(tx,normalized.AssetClassId,ct)??throw new InvalidOperationException("Asset class was not found.");if(!assetClass.IsActive||assetClass.LegalEntityId!=normalized.LegalEntityId)throw new InvalidOperationException("Asset class is inactive or belongs to a different legal entity.");
			if(normalized.SourceSupplierDocumentLineId is long sourceLine){var source=await _assets.GetCapitalizableSupplierLineAsync(tx,sourceLine,ct)??throw new InvalidOperationException("The linked supplier document line must belong to a posted supplier invoice.");if(source.Currency!=normalized.Currency)throw new InvalidOperationException("The linked supplier invoice line currency must match the asset currency.");}
			if(normalized.Id==0){var id=await _assets.CreateAssetAsync(tx,normalized,ct);var created=await _assets.GetAssetAsync(tx,id,ct)??throw new InvalidOperationException("Asset could not be reloaded.");await _auditEntries.CreateAsync(tx,_audit.CreateCreatedEntry(id,created),ct);return created;}
			var before=await _assets.GetAssetAsync(tx,normalized.Id,ct)??throw new InvalidOperationException("Asset was not found.");if(before.Version!=normalized.Version)throw new ConcurrencyConflictException("fixed asset");if(before.Status!=FinanceAssetStatus.Draft&&(before.OriginalCost!=normalized.OriginalCost||before.Currency!=normalized.Currency||before.LegalEntityId!=normalized.LegalEntityId))throw new InvalidOperationException("Capitalized asset cost, currency and legal entity cannot be rewritten.");
			if(await _assets.UpdateAssetAsync(tx,normalized,before.Version,ct)!=1)throw new ConcurrencyConflictException("fixed asset");var after=await _assets.GetAssetAsync(tx,normalized.Id,ct)??throw new InvalidOperationException("Asset could not be reloaded.");await _auditEntries.CreateAsync(tx,_audit.CreateUpdatedEntry(after.Id,before,after),ct);return after;
		},token);
	}

	public async Task<IReadOnlyList<FinanceAssetDepreciationPeriod>> RecalculateScheduleAsync(long assetId,CancellationToken token=default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceFixedAssetsManage);RequireUser();
		return await _transactions.ExecuteAsync(async(tx,ct)=>{
			var asset=await _assets.GetAssetAsync(tx,assetId,ct)??throw new InvalidOperationException("Asset was not found.");var cls=await _assets.GetClassAsync(tx,asset.AssetClassId,ct)??throw new InvalidOperationException("Asset class was not found.");
			var schedule=await CalculateScheduleAsync(tx,asset,cls,ct);await _assets.ReplaceScheduleAsync(tx,asset.Id,schedule,ct);await _auditEntries.CreateAsync(tx,_audit.CreateActionEntry(asset.Id,"DepreciationScheduleRecalculated",asset,asset),ct);return (IReadOnlyList<FinanceAssetDepreciationPeriod>)schedule;
		},token);
	}

	public async Task<FinanceAssetTransaction> CapitalizeAsync(long assetId,Guid operationId,Guid accountingPeriodId,DateOnly postingDate,CancellationToken token=default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceFixedAssetsManage);var user=RequireUser();RequireOperation(operationId);
		return await _transactions.ExecuteAsync(async(tx,ct)=>{
			var existing=await _assets.FindTransactionByOperationAsync(tx,operationId,ct);if(existing is not null)return existing;
			var asset=await _assets.GetAssetAsync(tx,assetId,ct)??throw new InvalidOperationException("Asset was not found.");if(asset.Status!=FinanceAssetStatus.Draft)throw new InvalidOperationException("Only draft assets can be capitalized.");
			var cls=await _assets.GetClassAsync(tx,asset.AssetClassId,ct)??throw new InvalidOperationException("Asset class was not found.");await RequireProfileAsync(tx,cls.CapitalizationPostingProfileId,cls.LegalEntityId,CapitalizationEvent,ct);
			var exchange=await _ledger.ResolveExchangeRateIdForProfileAsync(tx,cls.CapitalizationPostingProfileId,asset.Currency,postingDate,ct);
			var journal=await _ledger.PostFromProfileInTransactionAsync(tx,new FinanceProfilePostingRequest{OperationId=operationId,PostingProfileId=cls.CapitalizationPostingProfileId,AccountingPeriodId=accountingPeriodId,PostingDate=postingDate,Description=$"Capitalize asset {asset.AssetNumber}",SourceId=asset.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),SourceReference=asset.AssetNumber,TransactionCurrency=asset.Currency,ExchangeRateId=exchange,Amounts=new Dictionary<string,decimal>(StringComparer.Ordinal){{"AssetCost",asset.OriginalCost}}},user.Id,ct);
			var updated=asset with{CapitalizationDate=postingDate,Status=FinanceAssetStatus.Active};if(await _assets.UpdateAssetAsync(tx,updated,asset.Version,ct)!=1)throw new ConcurrencyConflictException("fixed asset");
			var tr=NewTransaction(asset.Id,FinanceAssetTransactionKind.Capitalization,operationId,postingDate,asset.OriginalCost,journal.Id,null,$"GL {journal.EntryNumber}",user.Id);var id=await _assets.CreateTransactionAsync(tx,tr,ct);tr=tr with{Id=id};await _auditEntries.CreateAsync(tx,_audit.CreateActionEntry(asset.Id,"Capitalized",asset,updated),ct);return tr;
		},token);
	}

	public async Task<FinanceAssetTransaction> PostDepreciationAsync(long schedulePeriodId,Guid operationId,FinanceClosedPeriodPolicy closedPeriodPolicy=FinanceClosedPeriodPolicy.Fail,CancellationToken token=default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceFixedAssetsDepreciationPost);var user=RequireUser();RequireOperation(operationId);
		return await _transactions.ExecuteAsync(async(tx,ct)=>{
			var existing=await _assets.FindTransactionByOperationAsync(tx,operationId,ct);if(existing is not null)return existing;
			var schedule=await _assets.GetSchedulePeriodAsync(tx,schedulePeriodId,ct)??throw new InvalidOperationException("Depreciation period was not found.");if(schedule.IsPosted)throw new InvalidOperationException("The depreciation period is already posted.");
			var asset=await _assets.GetAssetAsync(tx,schedule.AssetId,ct)??throw new InvalidOperationException("Asset was not found.");if(asset.Status!=FinanceAssetStatus.Active)throw new InvalidOperationException("Only active assets can be depreciated.");var cls=await _assets.GetClassAsync(tx,asset.AssetClassId,ct)??throw new InvalidOperationException("Asset class was not found.");
			var target=await ResolvePostingPeriodAsync(tx,cls.FiscalCalendarId,schedule.AccountingPeriodId,schedule.PeriodEnd,closedPeriodPolicy,ct);await RequireProfileAsync(tx,cls.DepreciationPostingProfileId,cls.LegalEntityId,DepreciationEvent,ct);
			var amount=schedule.PlannedAmount-schedule.PostedAmount;if(amount<=0m)throw new InvalidOperationException("There is no depreciation amount remaining to post.");
			var exchange=await _ledger.ResolveExchangeRateIdForProfileAsync(tx,cls.DepreciationPostingProfileId,asset.Currency,target.PostingDate,ct);
			var journal=await _ledger.PostFromProfileInTransactionAsync(tx,new FinanceProfilePostingRequest{OperationId=operationId,PostingProfileId=cls.DepreciationPostingProfileId,AccountingPeriodId=target.PeriodId,PostingDate=target.PostingDate,Description=$"Depreciation {asset.AssetNumber} {schedule.PeriodStart:yyyy-MM-dd}..{schedule.PeriodEnd:yyyy-MM-dd}",SourceId=asset.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),SourceReference=asset.AssetNumber,TransactionCurrency=asset.Currency,ExchangeRateId=exchange,Amounts=new Dictionary<string,decimal>(StringComparer.Ordinal){{"Depreciation",amount}}},user.Id,ct);
			await _assets.MarkSchedulePostedAsync(tx,schedule.Id,amount,journal.Id,operationId,ct);var evidence=closedPeriodPolicy==FinanceClosedPeriodPolicy.NextOpenPeriod?$"ClosedPeriodPolicy=NextOpenPeriod; GL {journal.EntryNumber}":$"GL {journal.EntryNumber}";var tr=NewTransaction(asset.Id,FinanceAssetTransactionKind.Depreciation,operationId,target.PostingDate,amount,journal.Id,closedPeriodPolicy==FinanceClosedPeriodPolicy.NextOpenPeriod?"Explicit next-open-period policy":null,evidence,user.Id);var id=await _assets.CreateTransactionAsync(tx,tr,ct);tr=tr with{Id=id};await _auditEntries.CreateAsync(tx,_audit.CreateActionEntry(asset.Id,"DepreciationPosted",asset,asset),ct);return tr;
		},token);
	}

	public Task<FinanceAssetTransaction> ImpairAsync(long assetId,Guid operationId,Guid accountingPeriodId,DateOnly postingDate,decimal amount,string reason,CancellationToken token=default) =>
		PostAdjustmentAsync(assetId,operationId,accountingPeriodId,postingDate,amount,reason,FinanceAssetTransactionKind.Impairment,ImpairmentEvent,ApplicationPermission.FinanceFixedAssetsManage,token);

	public async Task<FinanceAssetTransaction> CorrectImpairmentAsync(long transactionId,Guid operationId,Guid accountingPeriodId,DateOnly postingDate,string reason,CancellationToken token=default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceFixedAssetsManage);var user=RequireUser();RequireOperation(operationId);if(transactionId<=0)throw new ArgumentOutOfRangeException(nameof(transactionId));if(accountingPeriodId==Guid.Empty)throw new ArgumentException("An accounting period is required.",nameof(accountingPeriodId));if(string.IsNullOrWhiteSpace(reason))throw new ArgumentException("A correction reason is required.",nameof(reason));
		return await _transactions.ExecuteAsync(async(tx,ct)=>{
			var existing=await _assets.FindTransactionByOperationAsync(tx,operationId,ct);if(existing is not null)return existing;
			var original=await _assets.GetTransactionAsync(tx,transactionId,ct)??throw new InvalidOperationException("Asset transaction was not found.");if(original.Kind!=FinanceAssetTransactionKind.Impairment||!original.JournalEntryId.HasValue)throw new InvalidOperationException("Only a posted impairment can be corrected.");
			var asset=await _assets.GetAssetAsync(tx,original.AssetId,ct)??throw new InvalidOperationException("Asset was not found.");if(asset.Status!=FinanceAssetStatus.Active)throw new InvalidOperationException("Only an active asset impairment can be corrected.");
			var cls=await _assets.GetClassAsync(tx,asset.AssetClassId,ct)??throw new InvalidOperationException("Asset class was not found.");var profile=await RequireProfileAsync(tx,cls.ImpairmentPostingProfileId,cls.LegalEntityId,ImpairmentEvent,ct);
			var normalizedReason=Required(reason,500);var journal=await _ledger.ReverseInTransactionAsync(tx,original.JournalEntryId.Value,operationId,accountingPeriodId,postingDate,profile.NumberSequenceCode,normalizedReason,user.Id,ct);
			var tr=NewTransaction(asset.Id,FinanceAssetTransactionKind.Correction,operationId,postingDate,original.Amount,journal.Id,normalizedReason,$"Correction of asset transaction {original.Id}; GL {journal.EntryNumber}",user.Id);var id=await _assets.CreateTransactionAsync(tx,tr,ct);tr=tr with{Id=id};await _auditEntries.CreateAsync(tx,_audit.CreateActionEntry(asset.Id,"ImpairmentCorrected",original,tr),ct);return tr;
		},token);
	}

	public async Task<FinanceAssetTransaction> TransferAsync(long assetId,Guid operationId,DateOnly date,string? location,string? custodian,string reason,CancellationToken token=default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceFixedAssetsManage);var user=RequireUser();RequireOperation(operationId);if(string.IsNullOrWhiteSpace(reason))throw new ArgumentException("A transfer reason is required.",nameof(reason));
		return await _transactions.ExecuteAsync(async(tx,ct)=>{var existing=await _assets.FindTransactionByOperationAsync(tx,operationId,ct);if(existing is not null)return existing;var before=await _assets.GetAssetAsync(tx,assetId,ct)??throw new InvalidOperationException("Asset was not found.");var after=before with{Location=Trim(location,200),Custodian=Trim(custodian,200)};if(await _assets.UpdateAssetAsync(tx,after,before.Version,ct)!=1)throw new ConcurrencyConflictException("fixed asset");var tr=NewTransaction(assetId,FinanceAssetTransactionKind.Transfer,operationId,date,0m,null,Required(reason,500),$"{before.Location}/{before.Custodian} -> {after.Location}/{after.Custodian}",user.Id);var id=await _assets.CreateTransactionAsync(tx,tr,ct);tr=tr with{Id=id};await _auditEntries.CreateAsync(tx,_audit.CreateActionEntry(assetId,"Transferred",before,after),ct);return tr;},token);
	}

	public async Task<FinanceAssetTransaction> DisposeAsync(long assetId,Guid operationId,Guid accountingPeriodId,DateOnly postingDate,decimal proceeds,string reason,CancellationToken token=default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceFixedAssetsManage);var user=RequireUser();RequireOperation(operationId);if(proceeds<0m)throw new ArgumentOutOfRangeException(nameof(proceeds));if(string.IsNullOrWhiteSpace(reason))throw new ArgumentException("A disposal reason is required.",nameof(reason));
		return await _transactions.ExecuteAsync(async(tx,ct)=>{var existing=await _assets.FindTransactionByOperationAsync(tx,operationId,ct);if(existing is not null)return existing;var asset=await _assets.GetAssetAsync(tx,assetId,ct)??throw new InvalidOperationException("Asset was not found.");if(asset.Status!=FinanceAssetStatus.Active)throw new InvalidOperationException("Only active assets can be disposed.");var cls=await _assets.GetClassAsync(tx,asset.AssetClassId,ct)??throw new InvalidOperationException("Asset class was not found.");await RequireProfileAsync(tx,cls.DisposalPostingProfileId,cls.LegalEntityId,DisposalEvent,ct);var depreciation=await _assets.SumPostedAsync(tx,asset.Id,FinanceAssetTransactionKind.Depreciation,ct);var impairment=await _assets.SumPostedAsync(tx,asset.Id,FinanceAssetTransactionKind.Impairment,ct);var impairmentCorrections=await _assets.SumPostedAsync(tx,asset.Id,FinanceAssetTransactionKind.Correction,ct);var netImpairment=Math.Max(0m,impairment-impairmentCorrections);var carrying=Math.Max(0m,asset.OriginalCost-depreciation-netImpairment);var exchange=await _ledger.ResolveExchangeRateIdForProfileAsync(tx,cls.DisposalPostingProfileId,asset.Currency,postingDate,ct);var journal=await _ledger.PostFromProfileInTransactionAsync(tx,new FinanceProfilePostingRequest{OperationId=operationId,PostingProfileId=cls.DisposalPostingProfileId,AccountingPeriodId=accountingPeriodId,PostingDate=postingDate,Description=$"Dispose asset {asset.AssetNumber}",SourceId=asset.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),SourceReference=asset.AssetNumber,TransactionCurrency=asset.Currency,ExchangeRateId=exchange,Amounts=new Dictionary<string,decimal>(StringComparer.Ordinal){{"AssetCost",asset.OriginalCost},{"AccumulatedDepreciation",depreciation},{"Impairment",netImpairment},{"Proceeds",proceeds},{"CarryingValue",carrying},{"Gain",Math.Max(0m,proceeds-carrying)},{"Loss",Math.Max(0m,carrying-proceeds)}}},user.Id,ct);var after=asset with{Status=FinanceAssetStatus.Disposed};if(await _assets.UpdateAssetAsync(tx,after,asset.Version,ct)!=1)throw new ConcurrencyConflictException("fixed asset");var tr=NewTransaction(asset.Id,FinanceAssetTransactionKind.Disposal,operationId,postingDate,carrying,journal.Id,Required(reason,500),$"Proceeds {proceeds}; GL {journal.EntryNumber}",user.Id);var id=await _assets.CreateTransactionAsync(tx,tr,ct);tr=tr with{Id=id};await _auditEntries.CreateAsync(tx,_audit.CreateActionEntry(asset.Id,"Disposed",asset,after),ct);return tr;},token);
	}

	private async Task<FinanceAssetTransaction> PostAdjustmentAsync(long assetId,Guid operationId,Guid accountingPeriodId,DateOnly postingDate,decimal amount,string reason,FinanceAssetTransactionKind kind,string expectedEvent,ApplicationPermission permission,CancellationToken token)
	{
		_authorization.RequirePermission(permission);var user=RequireUser();RequireOperation(operationId);if(amount<=0m)throw new ArgumentOutOfRangeException(nameof(amount));if(string.IsNullOrWhiteSpace(reason))throw new ArgumentException("A reason is required.",nameof(reason));
		return await _transactions.ExecuteAsync(async(tx,ct)=>{var existing=await _assets.FindTransactionByOperationAsync(tx,operationId,ct);if(existing is not null)return existing;var asset=await _assets.GetAssetAsync(tx,assetId,ct)??throw new InvalidOperationException("Asset was not found.");if(asset.Status!=FinanceAssetStatus.Active)throw new InvalidOperationException("Only active assets can be adjusted.");var cls=await _assets.GetClassAsync(tx,asset.AssetClassId,ct)??throw new InvalidOperationException("Asset class was not found.");var profileId=cls.ImpairmentPostingProfileId;await RequireProfileAsync(tx,profileId,cls.LegalEntityId,expectedEvent,ct);var exchange=await _ledger.ResolveExchangeRateIdForProfileAsync(tx,profileId,asset.Currency,postingDate,ct);var journal=await _ledger.PostFromProfileInTransactionAsync(tx,new FinanceProfilePostingRequest{OperationId=operationId,PostingProfileId=profileId,AccountingPeriodId=accountingPeriodId,PostingDate=postingDate,Description=$"{kind} {asset.AssetNumber}: {reason.Trim()}",SourceId=asset.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),SourceReference=asset.AssetNumber,TransactionCurrency=asset.Currency,ExchangeRateId=exchange,Amounts=new Dictionary<string,decimal>(StringComparer.Ordinal){{"Impairment",amount}}},user.Id,ct);var tr=NewTransaction(asset.Id,kind,operationId,postingDate,amount,journal.Id,Required(reason,500),$"GL {journal.EntryNumber}",user.Id);var id=await _assets.CreateTransactionAsync(tx,tr,ct);tr=tr with{Id=id};await _auditEntries.CreateAsync(tx,_audit.CreateActionEntry(asset.Id,kind.ToString(),asset,asset),ct);return tr;},token);
	}

	private async Task<List<FinanceAssetDepreciationPeriod>> CalculateScheduleAsync(DatabaseTransactionContext tx,FinanceFixedAsset asset,FinanceAssetClass cls,CancellationToken ct)
	{
		if(asset.DepreciationMethod==FinanceDepreciationMethod.NoDepreciation)return [];
		var periods=await _assets.GetPeriodsAsync(tx,cls.FiscalCalendarId,asset.DepreciationStartDate,ct);var selected=periods.Where(p=>p.EndDate>=asset.DepreciationStartDate).Take(asset.UsefulLifeMonths).ToArray();if(selected.Length<asset.UsefulLifeMonths)throw new InvalidOperationException("The fiscal calendar does not contain enough future periods for the asset useful life.");
		var total=asset.OriginalCost-asset.SalvageValue;var baseAmount=Math.Round(total/asset.UsefulLifeMonths,2,MidpointRounding.AwayFromZero);var result=new List<FinanceAssetDepreciationPeriod>(selected.Length);decimal assigned=0;
		for(var i=0;i<selected.Length;i++){var amount=i==selected.Length-1?total-assigned:baseAmount;assigned+=amount;result.Add(new FinanceAssetDepreciationPeriod{AssetId=asset.Id,AccountingPeriodId=selected[i].Id,PeriodStart=selected[i].StartDate,PeriodEnd=selected[i].EndDate,PlannedAmount=amount});}return result;
	}

	private async Task<(Guid PeriodId,DateOnly PostingDate)> ResolvePostingPeriodAsync(DatabaseTransactionContext tx,Guid calendarId,Guid requestedPeriodId,DateOnly requestedDate,FinanceClosedPeriodPolicy policy,CancellationToken ct)
	{
		var periods=await _assets.GetPeriodsAsync(tx,calendarId,requestedDate,ct);var requested=periods.FirstOrDefault(p=>p.Id==requestedPeriodId);if(requested is not null&&requested.Status==AccountingPeriodStatus.Open)return(requested.Id,requestedDate<=requested.EndDate?requestedDate:requested.EndDate);if(policy==FinanceClosedPeriodPolicy.Fail)throw new InvalidOperationException("The depreciation accounting period is closed.");
		var next=periods.FirstOrDefault(p=>p.Status==AccountingPeriodStatus.Open&&p.StartDate>requestedDate);return next is null?throw new InvalidOperationException("No next open accounting period is available."):(next.Id,next.StartDate);
	}

	private async Task ValidateProfilesAsync(DatabaseTransactionContext tx,FinanceAssetClass cls,CancellationToken ct)
	{
		await RequireProfileAsync(tx,cls.CapitalizationPostingProfileId,cls.LegalEntityId,CapitalizationEvent,ct);await RequireProfileAsync(tx,cls.DepreciationPostingProfileId,cls.LegalEntityId,DepreciationEvent,ct);await RequireProfileAsync(tx,cls.ImpairmentPostingProfileId,cls.LegalEntityId,ImpairmentEvent,ct);await RequireProfileAsync(tx,cls.DisposalPostingProfileId,cls.LegalEntityId,DisposalEvent,ct);
	}
	private async Task<FinancePostingProfile> RequireProfileAsync(DatabaseTransactionContext tx,long profileId,Guid entity,string eventName,CancellationToken ct){if(profileId<=0)throw new InvalidOperationException($"A {eventName} posting profile is required.");var p=await _ledger.GetPostingProfileInTransactionAsync(tx,profileId,ct)??throw new InvalidOperationException($"{eventName} posting profile was not found.");if(!p.IsActive||p.LegalEntityId!=entity||!string.Equals(p.SourceType,SourceType,StringComparison.Ordinal)||!string.Equals(p.SourceEvent,eventName,StringComparison.Ordinal))throw new InvalidOperationException($"{eventName} posting profile must be active and use source {SourceType}/{eventName} for the asset legal entity.");return p;}
	private static FinanceAssetClass NormalizeClass(FinanceAssetClass v){if(v.LegalEntityId==Guid.Empty||v.FiscalCalendarId==Guid.Empty)throw new ArgumentException("Legal entity and fiscal calendar are required.");if(v.DefaultUsefulLifeMonths<1||v.DefaultUsefulLifeMonths>1200)throw new ArgumentOutOfRangeException(nameof(v.DefaultUsefulLifeMonths));if(!Enum.IsDefined(v.DefaultMethod))throw new ArgumentOutOfRangeException(nameof(v.DefaultMethod));return v with{Code=Required(v.Code,50).ToUpperInvariant(),Name=Required(v.Name,200)};}
	private static FinanceFixedAsset NormalizeAsset(FinanceFixedAsset v){if(v.LegalEntityId==Guid.Empty)throw new ArgumentException("Legal entity is required.");if(v.AssetClassId<=0)throw new ArgumentException("Asset class is required.");if(v.OriginalCost<0m||v.SalvageValue<0m||v.SalvageValue>v.OriginalCost)throw new ArgumentException("Cost and salvage value are invalid.");if(v.DepreciationMethod==FinanceDepreciationMethod.StraightLine&&v.UsefulLifeMonths<1)throw new ArgumentException("Straight-line depreciation requires a positive useful life.");if(!Enum.IsDefined(v.DepreciationMethod)||!Enum.IsDefined(v.Status))throw new ArgumentOutOfRangeException(nameof(v));return v with{AssetNumber=Required(v.AssetNumber,50).ToUpperInvariant(),Description=Required(v.Description,500),Location=Trim(v.Location,200),Custodian=Trim(v.Custodian,200)};}
	private static FinanceAssetTransaction NewTransaction(long assetId,FinanceAssetTransactionKind kind,Guid op,DateOnly date,decimal amount,long? journal,string? reason,string? evidence,long user)=>new(){AssetId=assetId,Kind=kind,OperationId=op,TransactionDate=date,Amount=amount,JournalEntryId=journal,Reason=reason,Evidence=evidence,CreatedAtUtc=DateTime.UtcNow,CreatedByUserId=user};
	private static Guid DeriveOperationId(Guid runOperationId,long schedulePeriodId)
	{
		Span<byte> input=stackalloc byte[24];runOperationId.TryWriteBytes(input[..16]);BinaryPrimitives.WriteInt64LittleEndian(input[16..],schedulePeriodId);Span<byte> hash=stackalloc byte[32];SHA256.HashData(input,hash);return new Guid(hash[..16]);
	}
	private User RequireUser()=>_authorization.CurrentUser??throw new UnauthorizedAccessException("An authenticated user is required.");
	private static void RequireOperation(Guid operationId){if(operationId==Guid.Empty)throw new ArgumentException("An operation ID is required.",nameof(operationId));}
	private static string Required(string? value,int max){var v=value?.Trim();if(string.IsNullOrWhiteSpace(v))throw new ArgumentException("A value is required.");if(v.Length>max)throw new ArgumentException($"Value cannot exceed {max} characters.");return v;}
	private static string? Trim(string? value,int max){var v=value?.Trim();if(string.IsNullOrEmpty(v))return null;if(v.Length>max)throw new ArgumentException($"Value cannot exceed {max} characters.");return v;}
}
