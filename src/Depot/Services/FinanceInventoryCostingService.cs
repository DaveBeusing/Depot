// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Depot.Data;
using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class FinanceInventoryCostingService
{
	private readonly IDatabaseTransactionRunner _transactions;
	private readonly FinanceInventoryAccountingRepository _accounting;
	private readonly FinanceInventoryCostingRepository _costing;
	private readonly FinanceGeneralLedgerService _generalLedger;
	private readonly AuditRepository _auditEntries;
	private readonly AuditService _audit;
	private readonly IAuthorizationService _authorization;

	public FinanceInventoryCostingService(IDatabaseTransactionRunner transactions, FinanceInventoryAccountingRepository accounting, FinanceInventoryCostingRepository costing, FinanceGeneralLedgerService generalLedger, AuditRepository auditEntries, AuditService audit, IAuthorizationService authorization)
	{
		_transactions=transactions; _accounting=accounting; _costing=costing; _generalLedger=generalLedger; _auditEntries=auditEntries; _audit=audit; _authorization=authorization;
	}

	public Task<FinanceInventoryAccountingPolicy?> GetPolicyAsync(CancellationToken token=default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceInventoryAccountingView);
		return _costing.GetPolicyAsync(token);
	}

	public Task<IReadOnlyList<FinanceInventoryValuationSummary>> GetValuationSummaryAsync(CancellationToken token=default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceInventoryAccountingView);
		return _costing.GetValuationSummaryAsync(token);
	}

	public Task<IReadOnlyList<FinanceInventoryReconciliationRun>> GetRecentReconciliationsAsync(CancellationToken token=default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceInventoryAccountingView);
		return _costing.GetRecentReconciliationsAsync(20,token);
	}

	public async Task<FinanceInventoryAccountingPolicy> SavePolicyAsync(FinanceInventoryAccountingPolicy policy,CancellationToken token=default)
	{
		ArgumentNullException.ThrowIfNull(policy);
		_authorization.RequirePermission(ApplicationPermission.FinanceInventoryAccountingManage);
		var user=RequireUser();
		if(policy.InventoryControlAccountId==Guid.Empty || policy.InventoryAdjustmentPostingProfileId<=0 || policy.PurchaseVariancePostingProfileId<=0 || policy.LandedCostPostingProfileId<=0) throw new ArgumentException("Inventory control account and all F4 posting profiles are required.",nameof(policy));
		return await _transactions.ExecuteAsync(async(tx,ct)=>
		{
			var config=await RequireConfigurationAsync(tx,ct);
			var receipt=await RequireProfileAsync(tx,config.GoodsReceiptPostingProfileId,config,FinanceInventoryAccountingEvents.GoodsReceipt,FinanceInventoryAccountingAmountKeys.Cost,ct);
			await RequireProfileAsync(tx,policy.InventoryAdjustmentPostingProfileId,config,FinanceInventoryAccountingEvents.InventoryAdjustment,FinanceInventoryAccountingAmountKeys.AdjustmentDebit,ct);
			await RequireProfileAsync(tx,policy.PurchaseVariancePostingProfileId,config,FinanceInventoryAccountingEvents.PurchaseVariance,FinanceInventoryAccountingAmountKeys.VarianceDebit,ct);
			var landed=await RequireProfileAsync(tx,policy.LandedCostPostingProfileId,config,FinanceInventoryAccountingEvents.LandedCost,FinanceInventoryAccountingAmountKeys.Cost,ct);
			if(landed.AccountingBookId!=receipt.AccountingBookId) throw new InvalidOperationException("All Inventory Accounting profiles must use the same accounting book.");
			if(!await _costing.AccountBelongsToBookAsync(tx,receipt.AccountingBookId,policy.InventoryControlAccountId,ct)) throw new InvalidOperationException("Inventory control account does not belong to the configured accounting book.");
			var before=await _costing.GetPolicyAsync(tx,ct);
			if(policy.Id==0)
			{
				if(before is not null) throw new InvalidOperationException("Inventory Accounting policy already exists.");
				var id=await _costing.CreatePolicyAsync(tx,policy,ct); var created=policy with{Id=id,Version=1};
				await _auditEntries.CreateAsync(tx,_audit.CreateCreatedEntry(id,created),ct); return created;
			}
			if(before is null || before.Id!=policy.Id || before.Version!=policy.Version) throw new ConcurrencyConflictException("inventory accounting policy");
			if(await _costing.UpdatePolicyAsync(tx,policy,before.Version,ct)!=1) throw new ConcurrencyConflictException("inventory accounting policy");
			var after=policy with{Version=before.Version+1}; await _auditEntries.CreateAsync(tx,_audit.CreateUpdatedEntry(after.Id,before,after),ct); return after;
		},token);
	}

	public async Task<FinanceInventoryLandedCostOperation> AllocateLandedCostAsync(FinanceInventoryLandedCostRequest request,CancellationToken token=default)
	{
		ArgumentNullException.ThrowIfNull(request); _authorization.RequirePermission(ApplicationPermission.FinanceInventoryAccountingManage); var user=RequireUser();
		if(request.OperationId==Guid.Empty || request.Amount<=0m || request.LayerIds.Count==0) throw new ArgumentException("Operation ID, positive amount and valuation layers are required.",nameof(request));
		var normalized=request with{LayerIds=request.LayerIds.Distinct().OrderBy(x=>x).ToArray(),Reference=Normalize(request.Reference)}; var hash=HashLandedCost(normalized);
		return await _transactions.ExecuteAsync(async(tx,ct)=>
		{
			var existing=await _costing.FindLandedCostByOperationAsync(tx,normalized.OperationId,ct); if(existing is not null){if(existing.RequestHash!=hash) throw new InvalidOperationException("Landed-cost operation ID is already assigned to different content."); return existing with{Allocations=await _costing.GetLandedCostAllocationsAsync(tx,existing.Id,ct)};}
			var config=await RequireConfigurationAsync(tx,ct); var policy=await RequirePolicyAsync(tx,ct); var profile=await RequireProfileAsync(tx,policy.LandedCostPostingProfileId,config,FinanceInventoryAccountingEvents.LandedCost,FinanceInventoryAccountingAmountKeys.Cost,ct);
			var layers=await _costing.LockLayersAsync(tx,normalized.LayerIds,ct); if(layers.Any(x=>x.ReversedAtUtc is not null || x.RemainingQuantity!=x.OriginalQuantity)) throw new InvalidOperationException("Landed cost can only be allocated to fully unconsumed valuation layers.");
			if(layers.Any(x=>x.AccountingBookId!=profile.AccountingBookId)) throw new InvalidOperationException("Selected valuation layers belong to another accounting book.");
			if(layers.Any(x=>x.Currency!=normalized.Currency)) throw new InvalidOperationException("Cross-currency landed-cost allocation is not permitted. Convert the landed cost to the valuation-layer currency first.");
			var weights=layers.Select(x=>normalized.AllocationMethod==FinanceLandedCostAllocationMethod.Quantity?(decimal)x.OriginalQuantity:x.OriginalQuantity*x.UnitCost).ToArray(); var totalWeight=weights.Sum(); if(totalWeight<=0m) throw new InvalidOperationException("Selected layers do not provide a positive allocation basis.");
			var allocations=new List<FinanceInventoryLandedCostAllocation>(); decimal allocated=0m;
			for(var i=0;i<layers.Count;i++){var amount=i==layers.Count-1?normalized.Amount-allocated:decimal.Round(normalized.Amount*weights[i]/totalWeight,9,MidpointRounding.ToEven); allocated+=amount; var increase=amount/layers[i].OriginalQuantity; if(await _costing.UpdateLayerUnitCostAsync(tx,layers[i].Id,layers[i].UnitCost,layers[i].UnitCost+increase,ct)!=1) throw new ConcurrencyConflictException("inventory valuation landed cost"); allocations.Add(new FinanceInventoryLandedCostAllocation{LayerId=layers[i].Id,Amount=amount,UnitCostIncrease=increase});}
			var period=await ResolvePeriodAsync(tx,config.FiscalCalendarId,normalized.PostingDate,ct); var rate=normalized.ExchangeRateId??await _generalLedger.ResolveExchangeRateIdForProfileAsync(tx,profile.Id,normalized.Currency,normalized.PostingDate,ct);
			var journal=await _generalLedger.PostFromProfileInTransactionAsync(tx,new FinanceProfilePostingRequest{OperationId=normalized.OperationId,PostingProfileId=profile.Id,AccountingPeriodId=period,PostingDate=normalized.PostingDate,Description="Inventory landed cost",SourceId=normalized.OperationId.ToString("D"),SourceReference=normalized.Reference,TransactionCurrency=normalized.Currency,ExchangeRateId=rate,Amounts=new Dictionary<string,decimal>(StringComparer.Ordinal){{FinanceInventoryAccountingAmountKeys.Cost,normalized.Amount}}},user.Id,ct);
			var value=new FinanceInventoryLandedCostOperation{OperationId=normalized.OperationId,RequestHash=hash,PostingDate=normalized.PostingDate,Currency=normalized.Currency,Amount=normalized.Amount,AllocationMethod=normalized.AllocationMethod,Reference=normalized.Reference,JournalEntryId=journal.Id,CreatedAtUtc=DateTime.UtcNow,CreatedByUserId=user.Id}; var id=await _costing.CreateLandedCostAsync(tx,value,ct);
			foreach(var a in allocations) await _costing.CreateLandedCostAllocationAsync(tx,a with{OperationId=id},ct); var created=value with{Id=id,Allocations=allocations.Select(a=>a with{OperationId=id}).ToArray()}; await _auditEntries.CreateAsync(tx,_audit.CreateCreatedEntry(id,created),ct); return created;
		},token);
	}

	public async Task<FinanceInventoryLandedCostOperation> ReverseLandedCostAsync(long id,Guid operationId,DateOnly postingDate,string reason,CancellationToken token=default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceInventoryAccountingManage); var user=RequireUser(); if(id<=0||operationId==Guid.Empty||string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Landed cost, operation ID and reversal reason are required.");
		return await _transactions.ExecuteAsync(async(tx,ct)=>
		{
			var value=await _costing.GetLandedCostAsync(tx,id,ct)??throw new InvalidOperationException("Landed-cost operation was not found."); if(value.ReversedAtUtc is not null){if(value.ReversalOperationId==operationId)return value;throw new InvalidOperationException("Landed-cost operation has already been reversed.");}
			var config=await RequireConfigurationAsync(tx,ct); var policy=await RequirePolicyAsync(tx,ct); var profile=await RequireProfileAsync(tx,policy.LandedCostPostingProfileId,config,FinanceInventoryAccountingEvents.LandedCost,FinanceInventoryAccountingAmountKeys.Cost,ct); var allocations=await _costing.GetLandedCostAllocationsAsync(tx,id,ct); var layers=await _costing.LockLayersAsync(tx,allocations.Select(x=>x.LayerId).ToArray(),ct);
			foreach(var a in allocations){var l=layers.Single(x=>x.Id==a.LayerId); if(l.RemainingQuantity!=l.OriginalQuantity||l.ReversedAtUtc is not null) throw new InvalidOperationException("Landed cost cannot be reversed after a selected layer has been consumed or reversed."); if(await _costing.UpdateLayerUnitCostAsync(tx,l.Id,l.UnitCost,l.UnitCost-a.UnitCostIncrease,ct)!=1) throw new ConcurrencyConflictException("landed-cost reversal");}
			var period=await ResolvePeriodAsync(tx,config.FiscalCalendarId,postingDate,ct); var journal=await _generalLedger.ReverseInTransactionAsync(tx,value.JournalEntryId,operationId,period,postingDate,profile.NumberSequenceCode,reason.Trim(),user.Id,ct); var now=DateTime.UtcNow; if(await _costing.MarkLandedCostReversedAsync(tx,id,operationId,journal.Id,now,user.Id,ct)!=1) throw new ConcurrencyConflictException("landed-cost reversal"); var after=value with{ReversalOperationId=operationId,ReversalJournalEntryId=journal.Id,ReversedAtUtc=now,ReversedByUserId=user.Id,Allocations=allocations}; await _auditEntries.CreateAsync(tx,_audit.CreateActionEntry(id,"Reversed",value,after),ct); return after;
		},token);
	}

	public async Task<FinanceInventoryReconciliationRun> ReconcileAsync(FinanceInventoryReconciliationRequest request,CancellationToken token=default)
	{
		ArgumentNullException.ThrowIfNull(request); _authorization.RequirePermission(ApplicationPermission.FinanceInventoryAccountingView); var user=RequireUser(); if(request.OperationId==Guid.Empty)throw new ArgumentException("Operation ID is required.",nameof(request));
		return await _transactions.ExecuteAsync(async(tx,ct)=>
		{
			var existing=await _costing.FindRunAsync(tx,request.OperationId,ct); if(existing is not null)return existing;
			var config=await RequireConfigurationAsync(tx,ct); var policy=await RequirePolicyAsync(tx,ct); var profile=await RequireProfileAsync(tx,config.GoodsReceiptPostingProfileId,config,FinanceInventoryAccountingEvents.GoodsReceipt,FinanceInventoryAccountingAmountKeys.Cost,ct); var reportingCurrency=await _costing.GetBookReportingCurrencyAsync(tx,profile.AccountingBookId,ct);
			var rows=await _costing.GetValuationReportingRowsAsync(tx,profile.AccountingBookId,request.AsOfDate,ct); var landed=await _costing.GetLandedReportingRowsAsync(tx,profile.AccountingBookId,request.AsOfDate,ct); var landedByLayer=landed.GroupBy(x=>x.LayerId).ToDictionary(g=>g.Key,g=>g.ToArray()); var lines=new List<FinanceInventoryReconciliationLine>();
			foreach(var group in rows.GroupBy(x=>x.ItemId)){decimal itemValue=0m;var qty=0;foreach(var row in group){var additions=landedByLayer.GetValueOrDefault(row.LayerId)??[];var currentIncrease=additions.Sum(x=>x.UnitCostIncrease);var baseCost=row.CurrentUnitCost-currentIncrease;var reportingUnit=baseCost*row.ReceiptExchangeRate+additions.Sum(x=>x.UnitCostIncrease*x.ExchangeRate);itemValue+=row.Quantity*reportingUnit;qty+=row.Quantity;}lines.Add(new FinanceInventoryReconciliationLine{ItemId=group.Key,Quantity=qty,ReportingValue=decimal.Round(itemValue,9,MidpointRounding.ToEven)});}
			var valuation=lines.Sum(x=>x.ReportingValue); var gl=await _costing.GetGlBalanceAsync(tx,profile.AccountingBookId,policy.InventoryControlAccountId,request.AsOfDate,ct); var value=new FinanceInventoryReconciliationRun{OperationId=request.OperationId,AccountingBookId=profile.AccountingBookId,InventoryControlAccountId=policy.InventoryControlAccountId,AsOfDate=request.AsOfDate,ReportingCurrency=reportingCurrency,ValuationAmount=valuation,GeneralLedgerAmount=gl,Difference=valuation-gl,CreatedAtUtc=DateTime.UtcNow,CreatedByUserId=user.Id,Lines=lines}; var id=await _costing.CreateRunAsync(tx,value,ct);foreach(var line in lines)await _costing.CreateRunLineAsync(tx,id,line,ct);var created=value with{Id=id};await _auditEntries.CreateAsync(tx,_audit.CreateCreatedEntry(id,created),ct);return created;
		},token);
	}

	internal async Task<FinanceInventoryPurchaseVariance?> RecordPurchaseVarianceAsync(DatabaseTransactionContext tx,FinanceSupplierDocument document,long userId,CancellationToken token)
	{
		var config=await GetActiveConfigurationAsync(tx,token); var policy=await GetActivePolicyAsync(tx,token); if(config is null||policy is null)return null; var linked=document.Lines.Where(x=>x.PurchaseOrderLineId.HasValue&&x.OrderedUnitPrice.HasValue).ToArray(); if(linked.Length==0)return null; if(document.Currency!=config.PurchaseOrderPriceCurrency)throw new InvalidOperationException("Purchase variance requires supplier-document currency to match the configured purchase-order price currency."); var existing=await _costing.GetPurchaseVarianceAsync(tx,document.Id,token);if(existing is not null)return existing;
		var expected=linked.Sum(x=>x.Quantity*x.OrderedUnitPrice!.Value);var actual=linked.Sum(x=>x.NetAmount);var signed=(actual-expected)*(document.Kind==FinancePayableDocumentKind.Invoice?1m:-1m);if(signed==0m)return null;var profile=await RequireProfileAsync(tx,policy.PurchaseVariancePostingProfileId,config,FinanceInventoryAccountingEvents.PurchaseVariance,FinanceInventoryAccountingAmountKeys.VarianceDebit,token);var period=await ResolvePeriodAsync(tx,config.FiscalCalendarId,document.DocumentDate,token);var rate=await _generalLedger.ResolveExchangeRateIdForProfileAsync(tx,profile.Id,document.Currency,document.DocumentDate,token);var operation=DeterministicGuid($"SupplierDocument:{document.Id}:PurchaseVariance");var journal=await _generalLedger.PostFromProfileInTransactionAsync(tx,new FinanceProfilePostingRequest{OperationId=operation,PostingProfileId=profile.Id,AccountingPeriodId=period,PostingDate=document.DocumentDate,Description=$"Purchase variance {document.SupplierDocumentNumber}",SourceId=document.Id.ToString(CultureInfo.InvariantCulture),SourceReference=document.SupplierDocumentNumber,TransactionCurrency=document.Currency,ExchangeRateId=rate,Amounts=new Dictionary<string,decimal>(StringComparer.Ordinal){{FinanceInventoryAccountingAmountKeys.VarianceDebit,Math.Max(signed,0m)},{FinanceInventoryAccountingAmountKeys.VarianceCredit,Math.Max(-signed,0m)}}},userId,token);var value=new FinanceInventoryPurchaseVariance{SupplierDocumentId=document.Id,OperationId=operation,Currency=document.Currency,ExpectedNetAmount=expected,ActualNetAmount=actual,SignedVarianceAmount=signed,JournalEntryId=journal.Id,CreatedAtUtc=DateTime.UtcNow,CreatedByUserId=userId};var id=await _costing.CreatePurchaseVarianceAsync(tx,value,token);var created=value with{Id=id};await _auditEntries.CreateAsync(tx,_audit.CreateCreatedEntry(id,created),token);return created;
	}

	internal async Task ReversePurchaseVarianceAsync(DatabaseTransactionContext tx,long supplierDocumentId,Guid parentOperation,DateOnly postingDate,string reason,long userId,CancellationToken token)
	{
		var value=await _costing.GetPurchaseVarianceAsync(tx,supplierDocumentId,token);if(value is null||value.ReversedAtUtc is not null)return;var config=await RequireConfigurationAsync(tx,token);var policy=await RequirePolicyAsync(tx,token);var profile=await RequireProfileAsync(tx,policy.PurchaseVariancePostingProfileId,config,FinanceInventoryAccountingEvents.PurchaseVariance,FinanceInventoryAccountingAmountKeys.VarianceDebit,token);var period=await ResolvePeriodAsync(tx,config.FiscalCalendarId,postingDate,token);var operation=DeterministicGuid($"{parentOperation:D}:PurchaseVarianceReversal");var journal=await _generalLedger.ReverseInTransactionAsync(tx,value.JournalEntryId,operation,period,postingDate,profile.NumberSequenceCode,reason,userId,token);var now=DateTime.UtcNow;if(await _costing.MarkPurchaseVarianceReversedAsync(tx,value.Id,operation,journal.Id,now,userId,token)!=1)throw new ConcurrencyConflictException("purchase variance reversal");
	}

	private async Task<FinanceInventoryAccountingConfiguration> RequireConfigurationAsync(DatabaseTransactionContext tx,CancellationToken token)=>await GetActiveConfigurationAsync(tx,token)??throw new InvalidOperationException("Active Inventory Accounting configuration is required.");
	private async Task<FinanceInventoryAccountingConfiguration?> GetActiveConfigurationAsync(DatabaseTransactionContext tx,CancellationToken token){var v=await _accounting.GetConfigurationAsync(tx,token);return v is{IsActive:true}?v:null;}
	private async Task<FinanceInventoryAccountingPolicy> RequirePolicyAsync(DatabaseTransactionContext tx,CancellationToken token)=>await GetActivePolicyAsync(tx,token)??throw new InvalidOperationException("Active Inventory Accounting policy is required.");
	private async Task<FinanceInventoryAccountingPolicy?> GetActivePolicyAsync(DatabaseTransactionContext tx,CancellationToken token){var v=await _costing.GetPolicyAsync(tx,token);return v is{IsActive:true}?v:null;}
	private async Task<FinancePostingProfile> RequireProfileAsync(DatabaseTransactionContext tx,long id,FinanceInventoryAccountingConfiguration config,string eventName,string requiredKey,CancellationToken token){var p=await _generalLedger.GetPostingProfileInTransactionAsync(tx,id,token)??throw new InvalidOperationException("Inventory Accounting posting profile was not found.");if(!p.IsActive||p.LegalEntityId!=config.LegalEntityId||p.SourceType!=FinanceInventoryAccountingEvents.SourceType||p.SourceEvent!=eventName)throw new InvalidOperationException($"Posting profile must use source InventoryAccounting/{eventName} for the active legal entity.");if(!p.Lines.Any(x=>x.AmountKey==requiredKey))throw new InvalidOperationException($"Posting profile '{p.Code}' must consume '{requiredKey}'.");return p;}
	private async Task<Guid> ResolvePeriodAsync(DatabaseTransactionContext tx,Guid calendar,DateOnly date,CancellationToken token){var periods=await _accounting.FindOpenPeriodsAsync(tx,calendar,date,token);if(periods.Count!=1)throw new InvalidOperationException("Posting date must resolve to exactly one open accounting period.");return periods[0].Id;}
	private User RequireUser()=>_authorization.CurrentUser is{IsActive:true}u?u:throw new UnauthorizedAccessException("An active signed-in user is required for Inventory Accounting.");
	private static string? Normalize(string? v)=>string.IsNullOrWhiteSpace(v)?null:v.Trim();
	private static string HashLandedCost(FinanceInventoryLandedCostRequest v)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|",v.PostingDate.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),v.Currency.Value,v.Amount.ToString("G29",CultureInfo.InvariantCulture),(int)v.AllocationMethod,string.Join(",",v.LayerIds),v.Reference??string.Empty))));
	private static Guid DeterministicGuid(string value){var hash=SHA256.HashData(Encoding.UTF8.GetBytes(value));Span<byte> bytes=stackalloc byte[16];hash.AsSpan(0,16).CopyTo(bytes);return new Guid(bytes);}
}
