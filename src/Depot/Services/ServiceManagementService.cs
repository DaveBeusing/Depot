// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class ServiceManagementService
{
	private readonly IDatabaseTransactionRunner _transactions;
	private readonly ServiceManagementRepository _service;
	private readonly CustomerRepository _customers;
	private readonly AuditRepository _auditEntries;
	private readonly AuditService _audit;
	private readonly IAuthorizationService _authorization;
	private readonly NotificationService _notifications;
	private readonly MovementService _movements;
	private readonly SalesInvoiceService _invoices;

	public ServiceManagementService(
		IDatabaseTransactionRunner transactions,
		ServiceManagementRepository service,
		CustomerRepository customers,
		AuditRepository auditEntries,
		AuditService audit,
		IAuthorizationService authorization,
		NotificationService notifications,
		MovementService movements,
		SalesInvoiceService invoices)
	{
		_transactions=transactions;_service=service;_customers=customers;_auditEntries=auditEntries;_audit=audit;_authorization=authorization;_notifications=notifications;_movements=movements;_invoices=invoices;
	}

	public bool CanView=>_authorization.HasPermission(ApplicationPermission.ServiceManagementView);
	public bool CanManageCases=>_authorization.HasPermission(ApplicationPermission.ServiceCasesManage);
	public bool CanManageOrders=>_authorization.HasPermission(ApplicationPermission.ServiceOrdersManage);
	public bool CanCompleteOrders=>_authorization.HasPermission(ApplicationPermission.ServiceOrdersClose);
	public bool CanBill=>_authorization.HasPermission(ApplicationPermission.ServiceBillingGenerate)&&_authorization.HasPermission(ApplicationPermission.SalesInvoicesCreate);

	public Task<PageResult<ServiceCase>> SearchCasesAsync(ServiceCaseFilter filter,int pageNumber=1,int pageSize=100,CancellationToken token=default){_authorization.RequirePermission(ApplicationPermission.ServiceManagementView);return _service.SearchCasesAsync(filter,pageNumber,pageSize,token);}
	public Task<ServiceCase?> GetCaseAsync(long id,CancellationToken token=default){_authorization.RequirePermission(ApplicationPermission.ServiceManagementView);return _service.GetCaseAsync(id,token);}
	public Task<IReadOnlyList<ServiceCaseHistory>> ListCaseHistoryAsync(long caseId,CancellationToken token=default){_authorization.RequirePermission(ApplicationPermission.ServiceManagementView);return _service.ListCaseHistoryAsync(caseId,token);}
	public Task<ServiceOrder?> GetOrderAsync(long id,CancellationToken token=default){_authorization.RequirePermission(ApplicationPermission.ServiceManagementView);return _service.GetOrderAsync(id,token);}
	public Task<ServiceOrder?> GetOrderForCaseAsync(long caseId,CancellationToken token=default){_authorization.RequirePermission(ApplicationPermission.ServiceManagementView);return _service.GetOrderForCaseAsync(caseId,token);}
	public Task<IReadOnlyList<ServiceWorkLine>> ListWorkLinesAsync(long orderId,CancellationToken token=default){_authorization.RequirePermission(ApplicationPermission.ServiceManagementView);return _service.ListWorkLinesAsync(orderId,token);}
	public Task<IReadOnlyList<ServicePartEvidence>> ListPartEvidenceAsync(long orderId,CancellationToken token=default){_authorization.RequirePermission(ApplicationPermission.ServiceManagementView);return _service.ListPartEvidenceAsync(orderId,token);}
	public Task<IReadOnlyList<ServiceCase>> GetOwnedOpenCasesAsync(long userId,int count,CancellationToken token=default){_authorization.RequirePermission(ApplicationPermission.ServiceManagementView);return _service.GetOwnedOpenCasesAsync(userId,count,token);}
	public Task<IReadOnlyList<ServiceOrder>> GetOwnedOpenOrdersAsync(long userId,int count,CancellationToken token=default){_authorization.RequirePermission(ApplicationPermission.ServiceManagementView);return _service.GetOwnedOpenOrdersAsync(userId,count,token);}
	public Task<IReadOnlyList<InventoryLookupItem>> SearchPartInventoryAsync(string? search,int count,CancellationToken token=default){_authorization.RequirePermission(ApplicationPermission.ServiceManagementView);return _movements.SearchAvailableInventoriesAsync(search,count,token);}

	public async Task<ServiceCase> SaveCaseAsync(ServiceCase input,CancellationToken token=default)
	{
		_authorization.RequirePermission(ApplicationPermission.ServiceCasesManage);
		var user=RequireUser();
		ValidateCaseInput(input);
		long? notifyOwner=null;
		var result=await _transactions.ExecuteAsync(async(tx,ct)=>
		{
			await ValidateCaseReferencesAsync(tx,input,ct);
			var now=DateTime.UtcNow;
			if(input.Id==0)
			{
				var value=input with{Id=0,Version=1,CaseNumber=string.Empty,Status=ServiceCaseStatus.New,CreatedAtUtc=now,CreatedByUserId=user.Id,UpdatedAtUtc=now,UpdatedByUserId=user.Id,ResolvedAtUtc=null,ResolvedByUserId=null,ClosedAtUtc=null,ClosedByUserId=null,CancelledAtUtc=null,CancelledByUserId=null};
				var created=await _service.CreateCaseAsync(tx,value,ct);
				await _service.AppendCaseHistoryAsync(tx,created.Id,null,created.Status,"Case created",user.Id,now,ct);
				await _auditEntries.CreateAsync(tx,_audit.CreateCreatedEntry(created.Id,created),ct);
				notifyOwner=created.OwnerUserId is{} owner&&owner!=user.Id?owner:null;
				return await _service.GetCaseAsync(tx,created.Id,ct)??created;
			}
			var before=await _service.GetCaseAsync(tx,input.Id,ct)??throw new InvalidOperationException("Service case was not found.");
			if(before.Version!=input.Version)throw new ConcurrencyConflictException("service case");
			if(before.Status is ServiceCaseStatus.Closed or ServiceCaseStatus.Cancelled)throw new InvalidOperationException("Closed or cancelled service cases cannot be edited. Reopen the case through its controlled lifecycle first.");
			if(input.Status!=before.Status)throw new InvalidOperationException("Service-case status must be changed through the lifecycle commands.");
			var changed=input with{CaseNumber=before.CaseNumber,CreatedAtUtc=before.CreatedAtUtc,CreatedByUserId=before.CreatedByUserId,UpdatedAtUtc=now,UpdatedByUserId=user.Id,ResolvedAtUtc=before.ResolvedAtUtc,ResolvedByUserId=before.ResolvedByUserId,ClosedAtUtc=before.ClosedAtUtc,ClosedByUserId=before.ClosedByUserId,CancelledAtUtc=before.CancelledAtUtc,CancelledByUserId=before.CancelledByUserId};
			if(await _service.UpdateCaseAsync(tx,changed,before.Version,ct)!=1)throw new ConcurrencyConflictException("service case");
			var after=await _service.GetCaseAsync(tx,before.Id,ct)??throw new InvalidOperationException("Service case could not be reloaded.");
			await _auditEntries.CreateAsync(tx,_audit.CreateUpdatedEntry(after.Id,before,after),ct);
			if(before.OwnerUserId!=after.OwnerUserId&&after.OwnerUserId is{} owner&&owner!=user.Id)notifyOwner=owner;
			return after;
		},token);
		if(notifyOwner is{} recipient)await NotifyAssignmentAsync(result,recipient,user.Id,token);
		return result;
	}

	public Task<ServiceCase> OpenCaseAsync(long id,long version,CancellationToken token=default)=>TransitionCaseAsync(id,version,ServiceCaseStatus.Open,null,token);
	public Task<ServiceCase> StartCaseAsync(long id,long version,CancellationToken token=default)=>TransitionCaseAsync(id,version,ServiceCaseStatus.InProgress,null,token);
	public Task<ServiceCase> WaitCaseAsync(long id,long version,string? note,CancellationToken token=default)=>TransitionCaseAsync(id,version,ServiceCaseStatus.Waiting,note,token);
	public Task<ServiceCase> ResolveCaseAsync(long id,long version,string resolution,CancellationToken token=default)
	{
		if(string.IsNullOrWhiteSpace(resolution))throw new ArgumentException("Resolution evidence is required.",nameof(resolution));
		return TransitionCaseAsync(id,version,ServiceCaseStatus.Resolved,resolution,token);
	}
	public Task<ServiceCase> CloseCaseAsync(long id,long version,string? note,CancellationToken token=default)=>TransitionCaseAsync(id,version,ServiceCaseStatus.Closed,note,token);
	public Task<ServiceCase> CancelCaseAsync(long id,long version,string reason,CancellationToken token=default)
	{
		if(string.IsNullOrWhiteSpace(reason))throw new ArgumentException("A cancellation reason is required.",nameof(reason));
		return TransitionCaseAsync(id,version,ServiceCaseStatus.Cancelled,reason,token);
	}
	public Task<ServiceCase> ReopenCaseAsync(long id,long version,string reason,CancellationToken token=default)
	{
		if(string.IsNullOrWhiteSpace(reason))throw new ArgumentException("A reopen reason is required.",nameof(reason));
		return TransitionCaseAsync(id,version,ServiceCaseStatus.Open,reason,token);
	}

	public async Task<ServiceOrder> CreateServiceOrderAsync(long caseId,long? assignedOwnerUserId,long? servicedItemId,long? servicedInventoryId,string? serialLotReference,DateTime? plannedStartAtUtc,DateTime? plannedEndAtUtc,CancellationToken token=default)
	{
		_authorization.RequirePermission(ApplicationPermission.ServiceOrdersManage);var user=RequireUser();
		return await _transactions.ExecuteAsync(async(tx,ct)=>
		{
			var serviceCase=await _service.GetCaseAsync(tx,caseId,ct)??throw new InvalidOperationException("Service case was not found.");
			if(serviceCase.Status is ServiceCaseStatus.Closed or ServiceCaseStatus.Cancelled)throw new InvalidOperationException("A service order cannot be created for a closed or cancelled case.");
			if(await _service.GetOrderForCaseAsync(tx,caseId,ct)is not null)throw new InvalidOperationException("This service case already has an execution order.");
			var owner=assignedOwnerUserId??serviceCase.OwnerUserId;
			if(owner is{} ownerId&&!await _service.UserExistsAsync(tx,ownerId,ct))throw new InvalidOperationException("The assigned service owner is not an active user.");
			if(servicedItemId is{} itemId&&!await _service.ItemExistsAsync(tx,itemId,ct))throw new InvalidOperationException("The serviced item is not active or does not exist.");
			if(servicedInventoryId is{} inventoryId)
			{
				var inventoryItem=await _service.InventoryItemIdAsync(tx,inventoryId,ct)??throw new InvalidOperationException("The serviced inventory record was not found.");
				if(servicedItemId is{} selected&&selected!=inventoryItem)throw new InvalidOperationException("The serviced inventory record does not belong to the selected item.");
				servicedItemId??=inventoryItem;
			}
			if(plannedStartAtUtc is{} start&&plannedEndAtUtc is{} end&&end<start)throw new ArgumentException("Planned end cannot precede planned start.");
			var now=DateTime.UtcNow;
			var order=await _service.CreateOrderAsync(tx,new ServiceOrder{ServiceCaseId=serviceCase.Id,CustomerId=serviceCase.CustomerId,AssignedOwnerUserId=owner,ServicedItemId=servicedItemId,ServicedInventoryId=servicedInventoryId,SerialLotReference=Normalize(serialLotReference),Status=ServiceOrderStatus.Planned,PlannedStartAtUtc=plannedStartAtUtc?.ToUniversalTime(),PlannedEndAtUtc=plannedEndAtUtc?.ToUniversalTime(),CreatedAtUtc=now,CreatedByUserId=user.Id,UpdatedAtUtc=now,UpdatedByUserId=user.Id},ct);
			await _auditEntries.CreateAsync(tx,_audit.CreateCreatedEntry(order.Id,order),ct);
			if(serviceCase.Status==ServiceCaseStatus.New)
			{
				var opened=TransitionCaseCopy(serviceCase,ServiceCaseStatus.Open,user.Id,now);
				if(await _service.UpdateCaseAsync(tx,opened,serviceCase.Version,ct)!=1)throw new ConcurrencyConflictException("service case");
				await _service.AppendCaseHistoryAsync(tx,serviceCase.Id,serviceCase.Status,opened.Status,"Service order created",user.Id,now,ct);
			}
			return await _service.GetOrderAsync(tx,order.Id,ct)??order;
		},token);
	}

	public async Task<ServiceOrder> UpdateServiceOrderAsync(ServiceOrder input,CancellationToken token=default)
	{
		_authorization.RequirePermission(ApplicationPermission.ServiceOrdersManage);var user=RequireUser();
		return await _transactions.ExecuteAsync(async(tx,ct)=>
		{
			var before=await _service.GetOrderAsync(tx,input.Id,ct)??throw new InvalidOperationException("Service order was not found.");
			if(before.Version!=input.Version)throw new ConcurrencyConflictException("service order");
			if(before.Status is ServiceOrderStatus.Completed or ServiceOrderStatus.Cancelled)throw new InvalidOperationException("Completed or cancelled service orders are immutable.");
			if(input.Status!=before.Status)throw new InvalidOperationException("Service-order status must be changed through lifecycle commands.");
			if(input.AssignedOwnerUserId is{} owner&&!await _service.UserExistsAsync(tx,owner,ct))throw new InvalidOperationException("The assigned service owner is not active.");
			if(input.ServicedItemId is{} item&&!await _service.ItemExistsAsync(tx,item,ct))throw new InvalidOperationException("The serviced item is not active.");
			if(input.ServicedInventoryId is{} inventory)
			{
				var inventoryItem=await _service.InventoryItemIdAsync(tx,inventory,ct)??throw new InvalidOperationException("The serviced inventory record was not found.");
				if(input.ServicedItemId is{} itemId&&itemId!=inventoryItem)throw new InvalidOperationException("The serviced inventory record does not belong to the selected item.");
			}
			if(input.PlannedStartAtUtc is{} start&&input.PlannedEndAtUtc is{} end&&end<start)throw new ArgumentException("Planned end cannot precede planned start.");
			var after=input with{OrderNumber=before.OrderNumber,ServiceCaseId=before.ServiceCaseId,CaseNumber=before.CaseNumber,CustomerId=before.CustomerId,CustomerName=before.CustomerName,CreatedAtUtc=before.CreatedAtUtc,CreatedByUserId=before.CreatedByUserId,CompletedAtUtc=before.CompletedAtUtc,CompletedByUserId=before.CompletedByUserId,CompletionNotes=before.CompletionNotes,SalesInvoiceId=before.SalesInvoiceId,SalesInvoiceNumber=before.SalesInvoiceNumber,UpdatedAtUtc=DateTime.UtcNow,UpdatedByUserId=user.Id};
			if(await _service.UpdateOrderAsync(tx,after,before.Version,ct)!=1)throw new ConcurrencyConflictException("service order");
			var reloaded=await _service.GetOrderAsync(tx,before.Id,ct)??throw new InvalidOperationException("Service order could not be reloaded.");
			await _auditEntries.CreateAsync(tx,_audit.CreateUpdatedEntry(reloaded.Id,before,reloaded),ct);
			return reloaded;
		},token);
	}

	public Task<ServiceOrder> StartServiceOrderAsync(long id,long version,CancellationToken token=default)=>TransitionOrderAsync(id,version,ServiceOrderStatus.InProgress,null,token);
	public Task<ServiceOrder> CancelServiceOrderAsync(long id,long version,string reason,CancellationToken token=default)
	{
		if(string.IsNullOrWhiteSpace(reason))throw new ArgumentException("A cancellation reason is required.",nameof(reason));
		return TransitionOrderAsync(id,version,ServiceOrderStatus.Cancelled,reason,token);
	}

	public async Task<ServiceWorkLine> AddWorkLineAsync(long orderId,string description,decimal quantity,decimal unitPrice,bool billable,decimal taxRate,CancellationToken token=default)
	{
		_authorization.RequirePermission(ApplicationPermission.ServiceOrdersManage);var user=RequireUser();
		if(string.IsNullOrWhiteSpace(description)||quantity<=0||unitPrice<0||taxRate<0)throw new ArgumentException("A valid description, positive quantity and non-negative commercial values are required.");
		return await _transactions.ExecuteAsync(async(tx,ct)=>
		{
			var order=await _service.GetOrderAsync(tx,orderId,ct)??throw new InvalidOperationException("Service order was not found.");
			EnsureOrderMutable(order);
			var line=await _service.CreateWorkLineAsync(tx,new ServiceWorkLine{ServiceOrderId=orderId,Description=description.Trim(),Quantity=quantity,UnitPrice=unitPrice,Billable=billable,TaxRate=taxRate,CreatedAtUtc=DateTime.UtcNow,CreatedByUserId=user.Id},ct);
			await _auditEntries.CreateAsync(tx,_audit.CreateCreatedEntry(line.Id,line),ct);return line;
		},token);
	}

	public async Task RemoveWorkLineAsync(long orderId,long lineId,long version,CancellationToken token=default)
	{
		_authorization.RequirePermission(ApplicationPermission.ServiceOrdersManage);var user=RequireUser();
		await _transactions.ExecuteAsync(async(tx,ct)=>
		{
			var order=await _service.GetOrderAsync(tx,orderId,ct)??throw new InvalidOperationException("Service order was not found.");EnsureOrderMutable(order);
			if(await _service.DeleteWorkLineAsync(tx,lineId,orderId,version,ct)!=1)throw new ConcurrencyConflictException("service work line");
			await _auditEntries.CreateAsync(tx,_audit.CreateActionEntry(lineId,"Deleted",new{ServiceOrderId=orderId,Version=version},new{ServiceOrderId=orderId,DeletedBy=user.Id}),ct);
		},token);
	}

	public Task<ServicePartEvidence> ConsumePartAsync(long orderId,long inventoryId,int quantity,long? reasonCodeId,decimal unitPrice,bool billable,decimal taxRate,IReadOnlyList<TrackingAllocationInput>? tracking=null,CancellationToken token=default)=>
		PostPartMovementAsync(orderId,inventoryId,quantity,reasonCodeId,unitPrice,billable,taxRate,ServicePartMovementKind.Consumption,tracking??[],token);

	public Task<ServicePartEvidence> ReturnPartAsync(long orderId,long inventoryId,int quantity,long? reasonCodeId,decimal unitPrice,decimal taxRate,IReadOnlyList<TrackingAllocationInput>? tracking=null,CancellationToken token=default)=>
		PostPartMovementAsync(orderId,inventoryId,quantity,reasonCodeId,unitPrice,false,taxRate,ServicePartMovementKind.Return,tracking??[],token);

	public async Task<ServiceOrder> CompleteServiceOrderAsync(long id,long version,string completionNotes,CancellationToken token=default)
	{
		_authorization.RequirePermission(ApplicationPermission.ServiceOrdersClose);var user=RequireUser();
		if(string.IsNullOrWhiteSpace(completionNotes))throw new ArgumentException("Completion notes are required.",nameof(completionNotes));
		return await _transactions.ExecuteAsync(async(tx,ct)=>
		{
			var before=await _service.GetOrderAsync(tx,id,ct)??throw new InvalidOperationException("Service order was not found.");
			if(before.Version!=version)throw new ConcurrencyConflictException("service order");
			if(before.Status is ServiceOrderStatus.Completed or ServiceOrderStatus.Cancelled)throw new InvalidOperationException("The service order is already final.");
			var work=await _service.ListWorkLinesAsync(id,ct);var parts=await _service.ListPartEvidenceAsync(id,ct);
			if(work.Count==0&&parts.Count==0)throw new InvalidOperationException("Completion requires work or parts evidence in addition to completion notes.");
			var now=DateTime.UtcNow;var completed=before with{Status=ServiceOrderStatus.Completed,CompletionNotes=completionNotes.Trim(),CompletedAtUtc=now,CompletedByUserId=user.Id,UpdatedAtUtc=now,UpdatedByUserId=user.Id};
			if(await _service.UpdateOrderAsync(tx,completed,before.Version,ct)!=1)throw new ConcurrencyConflictException("service order");
			var serviceCase=await _service.GetCaseAsync(tx,before.ServiceCaseId,ct)??throw new InvalidOperationException("Service case was not found.");
			if(serviceCase.Status is not ServiceCaseStatus.Closed and not ServiceCaseStatus.Cancelled and not ServiceCaseStatus.Resolved)
			{
				var resolved=TransitionCaseCopy(serviceCase,ServiceCaseStatus.Resolved,user.Id,now);
				if(await _service.UpdateCaseAsync(tx,resolved,serviceCase.Version,ct)!=1)throw new ConcurrencyConflictException("service case");
				await _service.AppendCaseHistoryAsync(tx,serviceCase.Id,serviceCase.Status,ServiceCaseStatus.Resolved,$"Service order {before.OrderNumber} completed",user.Id,now,ct);
				await _auditEntries.CreateAsync(tx,_audit.CreateActionEntry(serviceCase.Id,"ResolvedByServiceOrder",serviceCase,resolved),ct);
			}
			var after=await _service.GetOrderAsync(tx,id,ct)??throw new InvalidOperationException("Service order could not be reloaded.");
			await _auditEntries.CreateAsync(tx,_audit.CreateActionEntry(id,"Completed",before,after),ct);return after;
		},token);
	}

	public async Task<ServiceInvoiceGenerationResult> GenerateInvoiceDraftAsync(long orderId,CancellationToken token=default)
	{
		_authorization.RequirePermission(ApplicationPermission.ServiceBillingGenerate);_authorization.RequirePermission(ApplicationPermission.SalesInvoicesCreate);var user=RequireUser();
		var initial=await _service.GetOrderAsync(orderId,token)??throw new InvalidOperationException("Service order was not found.");
		if(initial.SalesInvoiceId is long existing)
		{
			_authorization.RequirePermission(ApplicationPermission.SalesInvoicesView);
			return new(initial,await _invoices.GetByIdAsync(existing,token));
		}
		if(initial.Status!=ServiceOrderStatus.Completed)throw new InvalidOperationException("Only completed service orders can be billed.");
		var work=await _service.ListWorkLinesAsync(orderId,token);var parts=await _service.ListPartEvidenceAsync(orderId,token);
		var billableWork=work.Where(x=>x.Billable).ToArray();var billableParts=parts.Where(x=>x.Billable&&x.MovementKind==ServicePartMovementKind.Consumption).ToArray();
		if(billableWork.Length+billableParts.Length==0)throw new InvalidOperationException("The completed service order has no billable evidence.");
		var draft=await _transactions.ExecuteAsync(async(tx,ct)=>
		{
			var current=await _service.GetOrderAsync(tx,orderId,ct)??throw new InvalidOperationException("Service order was not found.");
			if(current.SalesInvoiceId is not null)throw new ConcurrencyConflictException("service billing");
			if(current.Status!=ServiceOrderStatus.Completed)throw new InvalidOperationException("Only completed service orders can be billed.");
			var customer=await _customers.GetByIdAsync(tx,current.CustomerId,ct)??throw new InvalidOperationException("Customer was not found.");
			if(!customer.IsActive)throw new InvalidOperationException("Inactive customers cannot receive service invoice drafts.");
			var lines=new List<SalesInvoiceLine>();var lineNumber=1;
			foreach(var line in billableWork)lines.Add(new SalesInvoiceLine{LineNumber=lineNumber++,PartNumber="SERVICE",Description=line.Description,Quantity=line.Quantity,UnitPrice=line.UnitPrice,DiscountPercent=0m,TaxRate=line.TaxRate,TaxCategoryCode=line.TaxRate==0m?ElectronicInvoiceTaxCategories.ZeroRated:ElectronicInvoiceTaxCategories.StandardRated});
			foreach(var part in billableParts)lines.Add(new SalesInvoiceLine{LineNumber=lineNumber++,PartNumber=part.PartNumber,Description=part.Description,Quantity=part.Quantity,UnitPrice=part.UnitPrice,DiscountPercent=0m,TaxRate=part.TaxRate,TaxCategoryCode=part.TaxRate==0m?ElectronicInvoiceTaxCategories.ZeroRated:ElectronicInvoiceTaxCategories.StandardRated});
			var invoice=await _invoices.CreateDirectDraftAsync(tx,new SalesInvoice{CustomerId=current.CustomerId,InvoiceDate=DateTime.Today,DueDate=DateTime.Today.AddDays(customer.PaymentTermsDays),Currency=customer.Currency,CustomerReference=current.OrderNumber,BillingAddress=customer.BillingAddress,Notes=$"Service order {current.OrderNumber} / case {current.CaseNumber}.",Lines=lines},ct);
			var linked=current with{SalesInvoiceId=invoice.Id,UpdatedAtUtc=DateTime.UtcNow,UpdatedByUserId=user.Id};
			if(await _service.UpdateOrderAsync(tx,linked,current.Version,ct)!=1)throw new ConcurrencyConflictException("service order");
			await _auditEntries.CreateAsync(tx,_audit.CreateActionEntry(current.Id,"InvoiceDraftGenerated",current,linked),ct);
			return invoice;
		},token);
		var order=await _service.GetOrderAsync(orderId,token)??initial;
		await _notifications.NotifyUsersAsync(new(NotificationType.Workflow,NotificationSeverity.Success,$"Service invoice {draft.InvoiceNumber} created",$"A draft invoice was created for service order {order.OrderNumber}.",NotificationSourceTypes.ServiceOrder,order.Id,order.OrderNumber,user.Id),[user.Id],token);
		return new(order,draft);
	}

	private async Task<ServiceCase> TransitionCaseAsync(long id,long version,ServiceCaseStatus target,string? note,CancellationToken token)
	{
		_authorization.RequirePermission(ApplicationPermission.ServiceCasesManage);var user=RequireUser();
		return await _transactions.ExecuteAsync(async(tx,ct)=>
		{
			var before=await _service.GetCaseAsync(tx,id,ct)??throw new InvalidOperationException("Service case was not found.");
			if(before.Version!=version)throw new ConcurrencyConflictException("service case");
			if(!IsAllowed(before.Status,target))throw new InvalidOperationException($"Transition from {before.Status} to {target} is not allowed.");
			if(target==ServiceCaseStatus.Closed)
			{
				var order=await _service.GetOrderForCaseAsync(tx,id,ct);
				if(order is not null&&order.Status is not ServiceOrderStatus.Completed and not ServiceOrderStatus.Cancelled)throw new InvalidOperationException("The service case cannot be closed while its service order is still active.");
			}
			var now=DateTime.UtcNow;var changed=TransitionCaseCopy(before,target,user.Id,now);
			if(await _service.UpdateCaseAsync(tx,changed,before.Version,ct)!=1)throw new ConcurrencyConflictException("service case");
			await _service.AppendCaseHistoryAsync(tx,id,before.Status,target,Normalize(note),user.Id,now,ct);
			var after=await _service.GetCaseAsync(tx,id,ct)??throw new InvalidOperationException("Service case could not be reloaded.");
			await _auditEntries.CreateAsync(tx,_audit.CreateActionEntry(id,target.ToString(),before,after),ct);return after;
		},token);
	}

	private async Task<ServiceOrder> TransitionOrderAsync(long id,long version,ServiceOrderStatus target,string? note,CancellationToken token)
	{
		_authorization.RequirePermission(ApplicationPermission.ServiceOrdersManage);var user=RequireUser();
		return await _transactions.ExecuteAsync(async(tx,ct)=>
		{
			var before=await _service.GetOrderAsync(tx,id,ct)??throw new InvalidOperationException("Service order was not found.");
			if(before.Version!=version)throw new ConcurrencyConflictException("service order");
			var allowed=(before.Status,target) switch{(ServiceOrderStatus.Planned,ServiceOrderStatus.InProgress)=>true,(ServiceOrderStatus.Planned or ServiceOrderStatus.InProgress,ServiceOrderStatus.Cancelled)=>true,_=>false};
			if(!allowed)throw new InvalidOperationException($"Transition from {before.Status} to {target} is not allowed.");
			var after=before with{Status=target,CompletionNotes=target==ServiceOrderStatus.Cancelled?Normalize(note):before.CompletionNotes,UpdatedAtUtc=DateTime.UtcNow,UpdatedByUserId=user.Id};
			if(await _service.UpdateOrderAsync(tx,after,before.Version,ct)!=1)throw new ConcurrencyConflictException("service order");
			var reloaded=await _service.GetOrderAsync(tx,id,ct)??throw new InvalidOperationException("Service order could not be reloaded.");
			await _auditEntries.CreateAsync(tx,_audit.CreateActionEntry(id,target.ToString(),before,reloaded),ct);return reloaded;
		},token);
	}

	private async Task<ServicePartEvidence> PostPartMovementAsync(long orderId,long inventoryId,int quantity,long? reasonCodeId,decimal unitPrice,bool billable,decimal taxRate,ServicePartMovementKind kind,IReadOnlyList<TrackingAllocationInput> tracking,CancellationToken token)
	{
		_authorization.RequirePermission(ApplicationPermission.ServiceOrdersManage);var user=RequireUser();
		if(quantity<=0||inventoryId<=0||unitPrice<0||taxRate<0)throw new ArgumentException("A valid inventory, positive quantity and non-negative commercial values are required.");
		var order=await _service.GetOrderAsync(orderId,token)??throw new InvalidOperationException("Service order was not found.");EnsureOrderMutable(order);
		var reference=$"SERVICE:{order.OrderNumber}";
		var movement=kind==ServicePartMovementKind.Consumption
			?await _movements.AddWithdrawalAsync(inventoryId,quantity,reasonCodeId,reference,$"Parts consumed for service case {order.CaseNumber}.",tracking,token)
			:await _movements.AddCorrectionAsync(inventoryId,quantity,reasonCodeId,reference,$"Parts returned from service case {order.CaseNumber}.",tracking,token);
		return await _transactions.ExecuteAsync(async(tx,ct)=>
		{
			var current=await _service.GetOrderAsync(tx,orderId,ct)??throw new InvalidOperationException("Service order was not found.");
			var evidence=await _service.CreatePartEvidenceAsync(tx,new ServicePartEvidence{ServiceOrderId=orderId,StockMovementId=movement.MovementId,MovementKind=kind,InventoryId=movement.InventoryId,ItemId=movement.ItemId,PartNumber=movement.PartNumber,Description=movement.Description,Quantity=Math.Abs(movement.Quantity),UnitPrice=unitPrice,Billable=billable&&kind==ServicePartMovementKind.Consumption,TaxRate=taxRate,CreatedAtUtc=DateTime.UtcNow,CreatedByUserId=user.Id},ct);
			await _auditEntries.CreateAsync(tx,_audit.CreateCreatedEntry(evidence.Id,evidence),ct);
			if(current.Status==ServiceOrderStatus.Planned)
			{
				var started=current with{Status=ServiceOrderStatus.InProgress,UpdatedAtUtc=DateTime.UtcNow,UpdatedByUserId=user.Id};
				if(await _service.UpdateOrderAsync(tx,started,current.Version,ct)!=1)throw new ConcurrencyConflictException("service order");
			}
			return evidence;
		},token);
	}

	private async Task ValidateCaseReferencesAsync(DatabaseTransactionContext tx,ServiceCase input,CancellationToken token)
	{
		if(!await _service.CustomerExistsAsync(tx,input.CustomerId,token))throw new InvalidOperationException("The customer is not active or does not exist.");
		if(input.CustomerContactId is{} contact&&!await _service.ContactBelongsToCustomerAsync(tx,contact,input.CustomerId,token))throw new InvalidOperationException("The selected contact does not belong to the customer.");
		if(input.OwnerUserId is{} owner&&!await _service.UserExistsAsync(tx,owner,token))throw new InvalidOperationException("The selected service owner is not active.");
		if(input.SalesOrderId is{} order&&!await _service.SalesOrderBelongsToCustomerAsync(tx,order,input.CustomerId,token))throw new InvalidOperationException("The related sales order does not belong to the customer.");
		if(input.ItemId is{} item&&!await _service.ItemExistsAsync(tx,item,token))throw new InvalidOperationException("The related item is not active.");
	}

	private static void ValidateCaseInput(ServiceCase input)
	{
		ArgumentNullException.ThrowIfNull(input);
		if(input.CustomerId<=0)throw new ArgumentException("A customer is required.",nameof(input));
		if(string.IsNullOrWhiteSpace(input.Subject))throw new ArgumentException("A service-case subject is required.",nameof(input));
		if(input.Subject.Trim().Length>200)throw new ArgumentException("The service-case subject cannot exceed 200 characters.",nameof(input));
		if(!Enum.IsDefined(input.Priority))throw new ArgumentException("The service-case priority is invalid.",nameof(input));
		if(input.DueAtUtc is{} due&&due.Kind==DateTimeKind.Unspecified)throw new ArgumentException("Service-case due timestamps must carry timezone information.",nameof(input));
	}

	private static bool IsAllowed(ServiceCaseStatus source,ServiceCaseStatus target)=>(source,target) switch
	{
		(ServiceCaseStatus.New,ServiceCaseStatus.Open or ServiceCaseStatus.Cancelled)=>true,
		(ServiceCaseStatus.Open,ServiceCaseStatus.InProgress or ServiceCaseStatus.Waiting or ServiceCaseStatus.Resolved or ServiceCaseStatus.Cancelled)=>true,
		(ServiceCaseStatus.InProgress,ServiceCaseStatus.Waiting or ServiceCaseStatus.Resolved or ServiceCaseStatus.Cancelled)=>true,
		(ServiceCaseStatus.Waiting,ServiceCaseStatus.InProgress or ServiceCaseStatus.Resolved or ServiceCaseStatus.Cancelled)=>true,
		(ServiceCaseStatus.Resolved,ServiceCaseStatus.Closed or ServiceCaseStatus.Open)=>true,
		(ServiceCaseStatus.Closed,ServiceCaseStatus.Open)=>true,
		_=>false
	};

	private static ServiceCase TransitionCaseCopy(ServiceCase before,ServiceCaseStatus target,long userId,DateTime now)=>before with
	{
		Status=target,UpdatedAtUtc=now,UpdatedByUserId=userId,
		ResolvedAtUtc=target==ServiceCaseStatus.Resolved?now:target==ServiceCaseStatus.Open?null:before.ResolvedAtUtc,
		ResolvedByUserId=target==ServiceCaseStatus.Resolved?userId:target==ServiceCaseStatus.Open?null:before.ResolvedByUserId,
		ClosedAtUtc=target==ServiceCaseStatus.Closed?now:target==ServiceCaseStatus.Open?null:before.ClosedAtUtc,
		ClosedByUserId=target==ServiceCaseStatus.Closed?userId:target==ServiceCaseStatus.Open?null:before.ClosedByUserId,
		CancelledAtUtc=target==ServiceCaseStatus.Cancelled?now:target==ServiceCaseStatus.Open?null:before.CancelledAtUtc,
		CancelledByUserId=target==ServiceCaseStatus.Cancelled?userId:target==ServiceCaseStatus.Open?null:before.CancelledByUserId
	};
	private static void EnsureOrderMutable(ServiceOrder order){if(order.Status is ServiceOrderStatus.Completed or ServiceOrderStatus.Cancelled)throw new InvalidOperationException("Completed or cancelled service orders are immutable.");}
	private async Task NotifyAssignmentAsync(ServiceCase value,long owner,long actor,CancellationToken token)=>await _notifications.NotifyUsersAsync(new(NotificationType.Workflow,NotificationSeverity.Information,$"Service case {value.CaseNumber} assigned",$"{value.CaseNumber}: {value.Subject}",NotificationSourceTypes.ServiceCase,value.Id,value.CaseNumber,actor),[owner],token);
	private User RequireUser()=>_authorization.CurrentUser is{IsActive:true} user?user:throw new UnauthorizedAccessException("An active signed-in user is required for service management.");
	private static string? Normalize(string? value)=>string.IsNullOrWhiteSpace(value)?null:value.Trim();
}
