// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Depot.Data;
using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class ReplenishmentService
{
	private const int MaximumBatchSize = 500;
	private const int MaximumConversionSuggestions = 100;

	private readonly IDatabaseTransactionRunner _transactions;
	private readonly ReplenishmentRepository _repository;
	private readonly ItemRepository _items;
	private readonly WarehouseRepository _warehouses;
	private readonly SupplierRepository _suppliers;
	private readonly ProcurementSourcingService _sourcing;
	private readonly AuditRepository _auditRepository;
	private readonly AuditService _audit;
	private readonly IAuthorizationService _authorization;

	public ReplenishmentService(
		IDatabaseTransactionRunner transactions,
		ReplenishmentRepository repository,
		ItemRepository items,
		WarehouseRepository warehouses,
		SupplierRepository suppliers,
		ProcurementSourcingService sourcing,
		AuditRepository auditRepository,
		AuditService audit,
		IAuthorizationService authorization)
	{
		_transactions=transactions;
		_repository=repository;
		_items=items;
		_warehouses=warehouses;
		_suppliers=suppliers;
		_sourcing=sourcing;
		_auditRepository=auditRepository;
		_audit=audit;
		_authorization=authorization;
	}

	public bool CanView => _authorization.HasPermission(ApplicationPermission.ReplenishmentView);
	public bool CanReview => _authorization.HasPermission(ApplicationPermission.ReplenishmentSuggestionsManage);
	public bool CanManagePolicies => _authorization.HasPermission(ApplicationPermission.ReplenishmentPoliciesManage);

	public Task<PageResult<ReplenishmentPolicy>> SearchPoliciesAsync(string? searchText=null,bool includeInactive=true,int pageNumber=1,int pageSize=100,CancellationToken cancellationToken=default)
	{
		_authorization.RequirePermission(ApplicationPermission.ReplenishmentView);
		return _repository.SearchPoliciesAsync(searchText,includeInactive,pageNumber,pageSize,cancellationToken);
	}

	public Task<PageResult<ReplenishmentSuggestion>> SearchSuggestionsAsync(ReplenishmentSuggestionStatus? status=null,int pageNumber=1,int pageSize=100,CancellationToken cancellationToken=default)
	{
		_authorization.RequirePermission(ApplicationPermission.ReplenishmentView);
		return _repository.SearchSuggestionsAsync(status,pageNumber,pageSize,cancellationToken);
	}

	public Task<IReadOnlyList<ReplenishmentSuggestion>> ListActionableAsync(int count=50,CancellationToken cancellationToken=default)
	{
		_authorization.RequirePermission(ApplicationPermission.ReplenishmentView);
		if(count is <1 or >200) throw new ArgumentOutOfRangeException(nameof(count));
		return _repository.ListActionableAsync(count,cancellationToken);
	}

	public async Task<ReplenishmentPolicy> SavePolicyAsync(ReplenishmentPolicy value,CancellationToken cancellationToken=default)
	{
		_authorization.RequirePermission(ApplicationPermission.ReplenishmentPoliciesManage);
		ArgumentNullException.ThrowIfNull(value);
		ValidateThresholds(value);
		var item=await _items.GetByIdAsync(value.ItemId,cancellationToken) ?? throw new InvalidOperationException("Replenishment item was not found.");
		if(!item.IsActive) throw new InvalidOperationException("Replenishment item is inactive.");
		var warehouse=await _warehouses.GetByIdAsync(value.WarehouseId,cancellationToken) ?? throw new InvalidOperationException("Replenishment warehouse was not found.");
		if(!warehouse.IsActive) throw new InvalidOperationException("Replenishment warehouse is inactive.");
		if(value.PreferredSupplierId is not null)
		{
			var supplier=await _suppliers.GetByIdAsync(value.PreferredSupplierId.Value,cancellationToken) ?? throw new InvalidOperationException("Preferred planning supplier was not found.");
			if(!supplier.IsActive) throw new InvalidOperationException("Preferred planning supplier is inactive.");
		}
		var before=value.Id==0?null:await _repository.GetPolicyAsync(value.Id,cancellationToken);
		return await _transactions.ExecuteAsync(async(transaction,token)=>
		{
			var saved=await _repository.SavePolicyAsync(transaction,value,token);
			await _auditRepository.CreateAsync(transaction,before is null?_audit.CreateCreatedEntry(saved.Id,saved):_audit.CreateUpdatedEntry(saved.Id,before,saved),token);
			return saved;
		},cancellationToken);
	}

	public async Task<ReplenishmentSuggestion?> RecalculatePolicyAsync(long policyId,CancellationToken cancellationToken=default)
	{
		_authorization.RequirePermission(ApplicationPermission.ReplenishmentSuggestionsManage);
		var input=await _repository.LoadRequirementInputAsync(policyId,cancellationToken) ?? throw new InvalidOperationException("Active replenishment policy was not found.");
		return (await ApplyInputAsync(input,cancellationToken)).Suggestion;
	}

	public async Task<ReplenishmentBatchResult> RecalculateBatchAsync(int offset=0,int count=200,CancellationToken cancellationToken=default)
	{
		_authorization.RequirePermission(ApplicationPermission.ReplenishmentSuggestionsManage);
		if(offset<0) throw new ArgumentOutOfRangeException(nameof(offset));
		if(count is <1 or >MaximumBatchSize) throw new ArgumentOutOfRangeException(nameof(count));
		var stopwatch=Stopwatch.StartNew();
		var inputs=await _repository.LoadRequirementInputsSliceAsync(offset,count,cancellationToken);
		var open=0;
		var blocked=0;
		var superseded=0;
		foreach(var input in inputs)
		{
			var result=await ApplyInputAsync(input,cancellationToken);
			superseded+=result.Superseded;
			if(result.Suggestion?.Status==ReplenishmentSuggestionStatus.Open) open++;
			if(result.Suggestion?.Status==ReplenishmentSuggestionStatus.Blocked) blocked++;
		}
		stopwatch.Stop();
		return new ReplenishmentBatchResult(inputs.Count,open,blocked,superseded,stopwatch.Elapsed);
	}

	public async Task<ReplenishmentSuggestion> DismissAsync(long suggestionId,long version,CancellationToken cancellationToken=default)
	{
		_authorization.RequirePermission(ApplicationPermission.ReplenishmentSuggestionsManage);
		var before=await _repository.GetSuggestionAsync(suggestionId,cancellationToken) ?? throw new InvalidOperationException("Replenishment suggestion was not found.");
		if(before.Version!=version) throw new ConcurrencyConflictException("replenishment suggestion");
		var user=CurrentUser();
		return await _transactions.ExecuteAsync(async(transaction,token)=>
		{
			if(!await _repository.DismissAsync(transaction,suggestionId,version,user.Id,DateTime.UtcNow,token)) throw new ConcurrencyConflictException("replenishment suggestion");
			var after=await _repository.GetSuggestionsByIdsAsync(transaction,[suggestionId],token);
			var value=after.Single();
			await _auditRepository.CreateAsync(transaction,_audit.CreateActionEntry(suggestionId,"Dismissed",before,value),token);
			return value;
		},cancellationToken);
	}

	public async Task<ReplenishmentConversionResult> ConvertToPurchaseRequisitionAsync(IReadOnlyCollection<long> suggestionIds,CancellationToken cancellationToken=default)
	{
		_authorization.RequirePermission(ApplicationPermission.ReplenishmentSuggestionsManage);
		if(suggestionIds.Count is <1 or >MaximumConversionSuggestions) throw new ArgumentOutOfRangeException(nameof(suggestionIds));
		var ids=suggestionIds.Distinct().Order().ToArray();
		if(ids.Length!=suggestionIds.Count) throw new InvalidOperationException("Duplicate replenishment suggestion IDs are not allowed.");
		var visible=new List<ReplenishmentSuggestion>(ids.Length);
		foreach(var id in ids)
		{
			var suggestion=await _repository.GetSuggestionAsync(id,cancellationToken) ?? throw new InvalidOperationException($"Replenishment suggestion '{id}' was not found.");
			visible.Add(suggestion);
		}
		var convertedIds=visible.Where(x=>x.ConvertedPurchaseRequisitionId is not null).Select(x=>x.ConvertedPurchaseRequisitionId!.Value).Distinct().ToArray();
		if(convertedIds.Length==1 && visible.All(x=>x.Status==ReplenishmentSuggestionStatus.Accepted && x.ConvertedPurchaseRequisitionId==convertedIds[0]))
			return new ReplenishmentConversionResult(convertedIds[0],ids);
		if(convertedIds.Length>0 || visible.Any(x=>x.Status!=ReplenishmentSuggestionStatus.Open))
			throw new InvalidOperationException("Only open, not-yet-converted replenishment suggestions can be converted.");

		var requisition=BuildRequisition(visible);
		await _sourcing.PrepareRequisitionAsync(requisition,cancellationToken);
		var user=CurrentUser();
		return await _transactions.ExecuteAsync(async(transaction,token)=>
		{
			var current=await _repository.GetSuggestionsByIdsAsync(transaction,ids,token);
			if(current.Count!=ids.Length) throw new ConcurrencyConflictException("replenishment suggestion");
			var currentConverted=current.Where(x=>x.ConvertedPurchaseRequisitionId is not null).Select(x=>x.ConvertedPurchaseRequisitionId!.Value).Distinct().ToArray();
			if(currentConverted.Length==1 && current.All(x=>x.Status==ReplenishmentSuggestionStatus.Accepted && x.ConvertedPurchaseRequisitionId==currentConverted[0]))
				return new ReplenishmentConversionResult(currentConverted[0],ids);
			if(current.Any(x=>x.Status!=ReplenishmentSuggestionStatus.Open || x.ConvertedPurchaseRequisitionId is not null))
				throw new ConcurrencyConflictException("replenishment suggestion");

			var saved=await _sourcing.SavePreparedRequisitionAsync(transaction,requisition,token);
			var reviewedAt=DateTime.UtcNow;
			foreach(var suggestion in current)
			{
				if(!await _repository.AcceptAsync(transaction,suggestion.Id,suggestion.Version,saved.Id,user.Id,reviewedAt,token))
					throw new ConcurrencyConflictException("replenishment suggestion");
				await _auditRepository.CreateAsync(transaction,_audit.CreateActionEntry(suggestion.Id,"ConvertedToPurchaseRequisition",suggestion,new { PurchaseRequisitionId=saved.Id }),token);
			}
			return new ReplenishmentConversionResult(saved.Id,ids);
		},cancellationToken);
	}

	private async Task<(ReplenishmentSuggestion? Suggestion,int Superseded)> ApplyInputAsync(ReplenishmentRequirementInput input,CancellationToken cancellationToken)
	{
		var snapshot=Calculate(input);
		var createSuggestion=snapshot.IsBlocked || snapshot.RequiredReplenishmentQuantity>0;
		return await _transactions.ExecuteAsync(
			(transaction,token)=>_repository.ApplySnapshotAsync(transaction,snapshot,createSuggestion,token),
			cancellationToken);
	}

	internal static ReplenishmentRequirementSnapshot Calculate(ReplenishmentRequirementInput input)
	{
		ArgumentNullException.ThrowIfNull(input);
		ValidateThresholds(input.Policy);
		var projected=checked(input.OnHandQuantity-input.ReservedQuantity-input.BackorderedQuantity+input.EligibleInboundQuantity);
		var required=projected<=input.Policy.ReorderPoint ? Math.Max(0L,checked((long)input.Policy.TargetStock-projected)) : 0L;
		var allocationAmbiguous=input.ActivePolicyCountForItem>1 && (input.BackorderedQuantity>0 || input.EligibleInboundQuantity>0);
		string? blockReason=null;
		if(allocationAmbiguous)
			blockReason="Sales backorders or open purchase-order supply are item-level in the current data model and cannot be attributed safely across multiple warehouse policies.";
		else if(required>0 && input.SupplierEvidenceCount==0)
			blockReason="No active supplier-item evidence matches the replenishment policy.";
		else if(required>0 && input.SupplierEvidenceCount>1)
			blockReason="Multiple active supplier-item records match the replenishment policy; supplier planning evidence is ambiguous.";

		var suggested=0;
		if(required>0 && blockReason is null)
		{
			var moq=input.SupplierMinimumOrderQuantity ?? 1m;
			if(moq<=0m) blockReason="Supplier minimum-order quantity is invalid.";
			else
			{
				var rounded=Math.Ceiling(Math.Ceiling(required/moq)*moq);
				if(rounded>int.MaxValue) blockReason="The MOQ-rounded replenishment quantity exceeds the supported requisition quantity range.";
				else suggested=checked((int)rounded);
			}
		}

		var explanation=BuildExplanation(input,projected,required,suggested,blockReason);
		var key=SnapshotKey(input,projected,required,suggested,blockReason);
		return new ReplenishmentRequirementSnapshot
		{
			PolicyId=input.Policy.Id,SnapshotKey=key,CalculatedAtUtc=DateTime.UtcNow,
			OnHandQuantity=input.OnHandQuantity,ReservedQuantity=input.ReservedQuantity,BackorderedQuantity=input.BackorderedQuantity,
			EligibleInboundQuantity=input.EligibleInboundQuantity,ProjectedAvailableQuantity=projected,
			ReorderPoint=input.Policy.ReorderPoint,SafetyStock=input.Policy.SafetyStock,TargetStock=input.Policy.TargetStock,
			RequiredReplenishmentQuantity=required,SuggestedPurchaseQuantity=suggested,
			PlanningSupplierId=input.SupplierEvidenceCount==1?input.PlanningSupplierId:null,
			SupplierItemId=input.SupplierEvidenceCount==1?input.SupplierItemId:null,
			SupplierLeadTimeDays=input.SupplierEvidenceCount==1?input.SupplierLeadTimeDays:null,
			SupplierMinimumOrderQuantity=input.SupplierEvidenceCount==1?input.SupplierMinimumOrderQuantity:null,
			IsBlocked=blockReason is not null,BlockReason=blockReason,Explanation=explanation
		};
	}

	private static PurchaseRequisition BuildRequisition(IReadOnlyList<ReplenishmentSuggestion> suggestions)
	{
		var commonSupplier=suggestions.Select(x=>x.Snapshot.PlanningSupplierId).Distinct().ToArray();
		var sourceList=string.Join(", ",suggestions.Select(x=>$"#{x.Id}"));
		var lines=suggestions.GroupBy(x=>x.ItemId).OrderBy(x=>x.Key).Select(group=>
		{
			var quantity=group.Aggregate(0,(total,x)=>checked(total+x.SuggestedQuantity));
			var evidence=string.Join("; ",group.Select(x=>$"Suggestion #{x.Id} / {x.WarehouseName}"));
			if(evidence.Length>2000) evidence=evidence[..2000];
			return new PurchaseRequisitionLine { ItemId=group.Key,Quantity=quantity,Notes=evidence };
		}).ToArray();
		return new PurchaseRequisition
		{
			PreferredSupplierId=commonSupplier.Length==1?commonSupplier[0]:null,
			BusinessJustification=$"Inventory replenishment review from suggestions {sourceList}.",
			Lines=lines
		};
	}

	private static string BuildExplanation(ReplenishmentRequirementInput input,long projected,long required,int suggested,string? blockReason)
	{
		var supplier=input.SupplierEvidenceCount==1
			?$" Supplier evidence: lead time {input.SupplierLeadTimeDays ?? 0} day(s), MOQ {(input.SupplierMinimumOrderQuantity ?? 1m).ToString(CultureInfo.InvariantCulture)}."
			:string.Empty;
		var result=$"On hand {input.OnHandQuantity}; reserved {input.ReservedQuantity}; backordered {input.BackorderedQuantity}; eligible inbound {input.EligibleInboundQuantity}; projected available {projected}; reorder point {input.Policy.ReorderPoint}; safety stock {input.Policy.SafetyStock}; target {input.Policy.TargetStock}; required {required}; suggested {suggested}.{supplier}";
		return blockReason is null?result:$"{result} Blocked: {blockReason}";
	}

	private static string SnapshotKey(ReplenishmentRequirementInput input,long projected,long required,int suggested,string? blockReason)
	{
		var canonical=string.Join("|",
			input.Policy.Id,input.Policy.Version,input.OnHandQuantity,input.ReservedQuantity,input.BackorderedQuantity,input.EligibleInboundQuantity,
			input.ActivePolicyCountForItem,input.Policy.ReorderPoint,input.Policy.SafetyStock,input.Policy.TargetStock,input.Policy.PreferredSupplierId,
			input.SupplierEvidenceCount,input.SupplierItemId,input.PlanningSupplierId,input.SupplierLeadTimeDays,
			input.SupplierMinimumOrderQuantity?.ToString(CultureInfo.InvariantCulture),projected,required,suggested,blockReason);
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
	}

	private static void ValidateThresholds(ReplenishmentPolicy value)
	{
		if(value.ItemId<=0) throw new InvalidOperationException("A replenishment item is required.");
		if(value.WarehouseId<=0) throw new InvalidOperationException("A replenishment warehouse is required.");
		if(value.ReorderPoint<0 || value.SafetyStock<0 || value.TargetStock<0) throw new InvalidOperationException("Replenishment thresholds cannot be negative.");
		if(value.ReorderPoint<value.SafetyStock) throw new InvalidOperationException("Reorder point cannot be below safety stock.");
		if(value.TargetStock<value.ReorderPoint) throw new InvalidOperationException("Target stock cannot be below the reorder point.");
	}

	private User CurrentUser()=>_authorization.CurrentUser is { IsActive:true } user?user:throw new UnauthorizedAccessException("An active signed-in user is required.");
}
