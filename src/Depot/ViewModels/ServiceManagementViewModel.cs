// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;
using System.Globalization;

using Depot.Commands;
using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels;

public sealed class ServiceManagementViewModel : BaseViewModel, IDisposable
{
	private readonly ServiceManagementService _service;
	private readonly CustomerService _customers;
	private ServiceCase? _selectedCase;
	private ServiceOrder? _selectedOrder;
	private Customer? _selectedCustomer;
	private CustomerContact? _selectedContact;
	private ServiceWorkLine? _selectedWorkLine;
	private InventoryLookupItem? _selectedPartInventory;
	private string _subject=string.Empty;
	private string _description=string.Empty;
	private string _category=string.Empty;
	private string _ownerUserId=string.Empty;
	private DateTime? _dueDate;
	private ServiceCasePriority _priority=ServiceCasePriority.Normal;
	private string _transitionNote=string.Empty;
	private string _orderOwnerUserId=string.Empty;
	private string _servicedItemId=string.Empty;
	private InventoryLookupItem? _servicedInventory;
	private string _serialLotReference=string.Empty;
	private DateTime? _plannedStartDate;
	private DateTime? _plannedEndDate;
	private string _completionNotes=string.Empty;
	private string _workDescription=string.Empty;
	private decimal _workQuantity=1m;
	private decimal _workUnitPrice;
	private bool _workBillable=true;
	private decimal _workTaxRate=19m;
	private int _partQuantity=1;
	private string _partReasonCodeId=string.Empty;
	private decimal _partUnitPrice;
	private bool _partBillable=true;
	private decimal _partTaxRate=19m;
	private string _trackingText=string.Empty;
	private bool _disposed;

	public ServiceManagementViewModel(ServiceManagementService service,CustomerService customers,BusinessAttachmentService attachments,IFileDialogService fileDialogs)
	{
		_service=service??throw new ArgumentNullException(nameof(service));
		_customers=customers??throw new ArgumentNullException(nameof(customers));
		CaseAttachments=new BusinessAttachmentPanelViewModel(attachments??throw new ArgumentNullException(nameof(attachments)),fileDialogs??throw new ArgumentNullException(nameof(fileDialogs)));
		OrderAttachments=new BusinessAttachmentPanelViewModel(attachments,fileDialogs);
		RefreshCommand=new AsyncRelayCommand(LoadAsync);
		NewCaseCommand=new RelayCommand(NewCase,()=>CanManageCases);
		SaveCaseCommand=new AsyncRelayCommand(SaveCaseAsync,()=>CanManageCases&&SelectedCustomer is not null&&!string.IsNullOrWhiteSpace(Subject));
		OpenCaseCommand=new AsyncRelayCommand(token=>TransitionCaseAsync((c,ct)=>_service.OpenCaseAsync(c.Id,c.Version,ct),token),()=>CanManageCases&&SelectedCase?.Status==ServiceCaseStatus.New);
		StartCaseCommand=new AsyncRelayCommand(token=>TransitionCaseAsync((c,ct)=>_service.StartCaseAsync(c.Id,c.Version,ct),token),()=>CanManageCases&&SelectedCase?.Status is ServiceCaseStatus.Open or ServiceCaseStatus.Waiting);
		WaitCaseCommand=new AsyncRelayCommand(token=>TransitionCaseAsync((c,ct)=>_service.WaitCaseAsync(c.Id,c.Version,TransitionNote,ct),token),()=>CanManageCases&&SelectedCase?.Status is ServiceCaseStatus.Open or ServiceCaseStatus.InProgress);
		ResolveCaseCommand=new AsyncRelayCommand(token=>TransitionCaseAsync((c,ct)=>_service.ResolveCaseAsync(c.Id,c.Version,TransitionNote,ct),token),()=>CanManageCases&&SelectedCase?.Status is (ServiceCaseStatus.Open or ServiceCaseStatus.InProgress or ServiceCaseStatus.Waiting)&&!string.IsNullOrWhiteSpace(TransitionNote));
		CloseCaseCommand=new AsyncRelayCommand(token=>TransitionCaseAsync((c,ct)=>_service.CloseCaseAsync(c.Id,c.Version,TransitionNote,ct),token),()=>CanManageCases&&SelectedCase?.Status==ServiceCaseStatus.Resolved);
		CancelCaseCommand=new AsyncRelayCommand(token=>TransitionCaseAsync((c,ct)=>_service.CancelCaseAsync(c.Id,c.Version,TransitionNote,ct),token),()=>CanManageCases&&SelectedCase is{Status:not ServiceCaseStatus.Closed and not ServiceCaseStatus.Cancelled}&&!string.IsNullOrWhiteSpace(TransitionNote));
		ReopenCaseCommand=new AsyncRelayCommand(token=>TransitionCaseAsync((c,ct)=>_service.ReopenCaseAsync(c.Id,c.Version,TransitionNote,ct),token),()=>CanManageCases&&SelectedCase?.Status is (ServiceCaseStatus.Resolved or ServiceCaseStatus.Closed)&&!string.IsNullOrWhiteSpace(TransitionNote));
		CreateOrderCommand=new AsyncRelayCommand(CreateOrderAsync,()=>CanManageOrders&&SelectedCase is not null&&SelectedOrder is null);
		SaveOrderCommand=new AsyncRelayCommand(SaveOrderAsync,()=>CanManageOrders&&SelectedOrder is{Status:not ServiceOrderStatus.Completed and not ServiceOrderStatus.Cancelled});
		StartOrderCommand=new AsyncRelayCommand(token=>RunOrderMutationAsync(o=>_service.StartServiceOrderAsync(o.Id,o.Version,token),"Starting service order...",token),()=>CanManageOrders&&SelectedOrder?.Status==ServiceOrderStatus.Planned);
		CancelOrderCommand=new AsyncRelayCommand(token=>RunOrderMutationAsync(o=>_service.CancelServiceOrderAsync(o.Id,o.Version,TransitionNote,token),"Cancelling service order...",token),()=>CanManageOrders&&SelectedOrder is{Status:not ServiceOrderStatus.Completed and not ServiceOrderStatus.Cancelled}&&!string.IsNullOrWhiteSpace(TransitionNote));
		CompleteOrderCommand=new AsyncRelayCommand(CompleteOrderAsync,()=>CanCompleteOrders&&SelectedOrder is{Status:not ServiceOrderStatus.Completed and not ServiceOrderStatus.Cancelled}&&!string.IsNullOrWhiteSpace(CompletionNotes));
		AddWorkLineCommand=new AsyncRelayCommand(AddWorkLineAsync,()=>CanManageOrders&&SelectedOrder is{Status:not ServiceOrderStatus.Completed and not ServiceOrderStatus.Cancelled}&&!string.IsNullOrWhiteSpace(WorkDescription));
		RemoveWorkLineCommand=new AsyncRelayCommand(RemoveWorkLineAsync,()=>CanManageOrders&&SelectedOrder is{Status:not ServiceOrderStatus.Completed and not ServiceOrderStatus.Cancelled}&&SelectedWorkLine is not null);
		ConsumePartCommand=new AsyncRelayCommand(token=>PostPartAsync(false,token),()=>CanManageOrders&&SelectedOrder is{Status:not ServiceOrderStatus.Completed and not ServiceOrderStatus.Cancelled}&&SelectedPartInventory is not null&&PartQuantity>0);
		ReturnPartCommand=new AsyncRelayCommand(token=>PostPartAsync(true,token),()=>CanManageOrders&&SelectedOrder is{Status:not ServiceOrderStatus.Completed and not ServiceOrderStatus.Cancelled}&&SelectedPartInventory is not null&&PartQuantity>0);
		GenerateInvoiceCommand=new AsyncRelayCommand(GenerateInvoiceAsync,()=>CanBill&&SelectedOrder?.Status==ServiceOrderStatus.Completed);
	}

	public ObservableCollection<ServiceCase> Cases{get;}=[];
	public ObservableCollection<Customer> Customers{get;}=[];
	public ObservableCollection<CustomerContact> Contacts{get;}=[];
	public ObservableCollection<ServiceCaseHistory> History{get;}=[];
	public ObservableCollection<ServiceWorkLine> WorkLines{get;}=[];
	public ObservableCollection<ServicePartEvidence> Parts{get;}=[];
	public ObservableCollection<InventoryLookupItem> PartInventory{get;}=[];
	public IReadOnlyList<ServiceCasePriority> Priorities{get;}=Enum.GetValues<ServiceCasePriority>();
	public BusinessAttachmentPanelViewModel CaseAttachments{get;}
	public BusinessAttachmentPanelViewModel OrderAttachments{get;}

	public AsyncRelayCommand RefreshCommand{get;} public RelayCommand NewCaseCommand{get;} public AsyncRelayCommand SaveCaseCommand{get;}
	public AsyncRelayCommand OpenCaseCommand{get;} public AsyncRelayCommand StartCaseCommand{get;} public AsyncRelayCommand WaitCaseCommand{get;} public AsyncRelayCommand ResolveCaseCommand{get;} public AsyncRelayCommand CloseCaseCommand{get;} public AsyncRelayCommand CancelCaseCommand{get;} public AsyncRelayCommand ReopenCaseCommand{get;}
	public AsyncRelayCommand CreateOrderCommand{get;} public AsyncRelayCommand SaveOrderCommand{get;} public AsyncRelayCommand StartOrderCommand{get;} public AsyncRelayCommand CancelOrderCommand{get;} public AsyncRelayCommand CompleteOrderCommand{get;}
	public AsyncRelayCommand AddWorkLineCommand{get;} public AsyncRelayCommand RemoveWorkLineCommand{get;} public AsyncRelayCommand ConsumePartCommand{get;} public AsyncRelayCommand ReturnPartCommand{get;} public AsyncRelayCommand GenerateInvoiceCommand{get;}

	public bool CanManageCases=>_service.CanManageCases; public bool CanManageOrders=>_service.CanManageOrders; public bool CanCompleteOrders=>_service.CanCompleteOrders; public bool CanBill=>_service.CanBill;
	public string TrackingHint=>TrackingAllocationTextParser.GenericFormatHint;

	public ServiceCase? SelectedCase
	{
		get=>_selectedCase;
		set
		{
			if(ReferenceEquals(_selectedCase,value))return;_selectedCase=value;OnPropertyChanged();LoadCaseEditor(value);RaiseCommands();
			_=CaseAttachments.SetTargetAsync(BusinessAttachmentEntityKind.ServiceCase,value?.Id);
			_=LoadSelectedCaseAsync(value?.Id,CancellationToken.None);
		}
	}
	public ServiceOrder? SelectedOrder{get=>_selectedOrder;private set{if(ReferenceEquals(_selectedOrder,value))return;_selectedOrder=value;OnPropertyChanged();LoadOrderEditor(value);RaiseCommands();_=OrderAttachments.SetTargetAsync(BusinessAttachmentEntityKind.ServiceOrder,value?.Id);}}
	public Customer? SelectedCustomer
	{
		get=>_selectedCustomer;
		set{if(ReferenceEquals(_selectedCustomer,value))return;_selectedCustomer=value;OnPropertyChanged();SelectedContact=null;_=LoadContactsAsync(value?.Id,CancellationToken.None);SaveCaseCommand.RaiseCanExecuteChanged();}
	}
	public CustomerContact? SelectedContact{get=>_selectedContact;set=>Set(ref _selectedContact,value);}
	public ServiceWorkLine? SelectedWorkLine{get=>_selectedWorkLine;set{if(Set(ref _selectedWorkLine,value))RemoveWorkLineCommand.RaiseCanExecuteChanged();}}
	public InventoryLookupItem? SelectedPartInventory{get=>_selectedPartInventory;set{if(Set(ref _selectedPartInventory,value)){ConsumePartCommand.RaiseCanExecuteChanged();ReturnPartCommand.RaiseCanExecuteChanged();}}}
	public InventoryLookupItem? ServicedInventory{get=>_servicedInventory;set=>Set(ref _servicedInventory,value);}
	public string Subject{get=>_subject;set{if(Set(ref _subject,value))SaveCaseCommand.RaiseCanExecuteChanged();}} public string Description{get=>_description;set=>Set(ref _description,value);} public string Category{get=>_category;set=>Set(ref _category,value);}
	public string OwnerUserId{get=>_ownerUserId;set=>Set(ref _ownerUserId,value);} public DateTime? DueDate{get=>_dueDate;set=>Set(ref _dueDate,value);} public ServiceCasePriority Priority{get=>_priority;set=>Set(ref _priority,value);}
	public string TransitionNote{get=>_transitionNote;set{if(Set(ref _transitionNote,value))RaiseCommands();}}
	public string OrderOwnerUserId{get=>_orderOwnerUserId;set=>Set(ref _orderOwnerUserId,value);} public string ServicedItemId{get=>_servicedItemId;set=>Set(ref _servicedItemId,value);} public string SerialLotReference{get=>_serialLotReference;set=>Set(ref _serialLotReference,value);}
	public DateTime? PlannedStartDate{get=>_plannedStartDate;set=>Set(ref _plannedStartDate,value);} public DateTime? PlannedEndDate{get=>_plannedEndDate;set=>Set(ref _plannedEndDate,value);}
	public string CompletionNotes{get=>_completionNotes;set{if(Set(ref _completionNotes,value))CompleteOrderCommand.RaiseCanExecuteChanged();}}
	public string WorkDescription{get=>_workDescription;set{if(Set(ref _workDescription,value))AddWorkLineCommand.RaiseCanExecuteChanged();}} public decimal WorkQuantity{get=>_workQuantity;set=>Set(ref _workQuantity,value);} public decimal WorkUnitPrice{get=>_workUnitPrice;set=>Set(ref _workUnitPrice,value);} public bool WorkBillable{get=>_workBillable;set=>Set(ref _workBillable,value);} public decimal WorkTaxRate{get=>_workTaxRate;set=>Set(ref _workTaxRate,value);}
	public int PartQuantity{get=>_partQuantity;set{if(Set(ref _partQuantity,value)){ConsumePartCommand.RaiseCanExecuteChanged();ReturnPartCommand.RaiseCanExecuteChanged();}}} public string PartReasonCodeId{get=>_partReasonCodeId;set=>Set(ref _partReasonCodeId,value);} public decimal PartUnitPrice{get=>_partUnitPrice;set=>Set(ref _partUnitPrice,value);} public bool PartBillable{get=>_partBillable;set=>Set(ref _partBillable,value);} public decimal PartTaxRate{get=>_partTaxRate;set=>Set(ref _partTaxRate,value);} public string TrackingText{get=>_trackingText;set=>Set(ref _trackingText,value);}

	public async Task LoadAsync(CancellationToken token=default)
	{
		BeginOperation("Loading service workspace...");
		try
		{
			var selected=SelectedCase?.Id;
			var casesTask=_service.SearchCasesAsync(new ServiceCaseFilter(),1,250,token);
			var customersTask=_customers.ListActiveAsync(token);
			var inventoryTask=_service.SearchPartInventoryAsync(null,150,token);
			await Task.WhenAll(casesTask,customersTask,inventoryTask);
			Replace(Cases,casesTask.Result.Items);Replace(Customers,customersTask.Result);Replace(PartInventory,inventoryTask.Result);
			SelectedCase=selected is long id?Cases.FirstOrDefault(x=>x.Id==id)??Cases.FirstOrDefault():Cases.FirstOrDefault();
			CompleteOperation(Cases.Count==0,Cases.Count==0?"No service cases are available.":$"{Cases.Count:N0} service case(s) loaded.");
		}
		catch(OperationCanceledException)when(token.IsCancellationRequested){}
		catch(Exception ex){FailOperation(ex,"Service workspace could not be loaded.");}
	}

	public async Task OpenCaseAsync(long id,CancellationToken token=default)
	{
		if(Cases.Count==0)await LoadAsync(token);
		var value=Cases.FirstOrDefault(x=>x.Id==id)??await _service.GetCaseAsync(id,token)??throw new InvalidOperationException("Service case was not found.");
		if(Cases.All(x=>x.Id!=id))Cases.Add(value);SelectedCase=value;await LoadSelectedCaseAsync(id,token);
	}
	public async Task OpenOrderAsync(long id,CancellationToken token=default)
	{
		var order=await _service.GetOrderAsync(id,token)??throw new InvalidOperationException("Service order was not found.");
		await OpenCaseAsync(order.ServiceCaseId,token);SelectedOrder=order;await LoadOrderEvidenceAsync(order.Id,token);
	}

	private void NewCase(){SelectedCase=null;SelectedCustomer=null;Subject=Description=Category=OwnerUserId=TransitionNote=string.Empty;DueDate=null;Priority=ServiceCasePriority.Normal;SelectedOrder=null;History.Clear();WorkLines.Clear();Parts.Clear();}
	private async Task SaveCaseAsync(CancellationToken token)
	{
		var customer=SelectedCustomer??throw new InvalidOperationException("Select a customer.");
		BeginOperation("Saving service case...");
		try
		{
			var before=SelectedCase;var saved=await _service.SaveCaseAsync(new ServiceCase{Id=before?.Id??0,Version=before?.Version??1,CaseNumber=before?.CaseNumber??string.Empty,CustomerId=customer.Id,CustomerContactId=SelectedContact?.Id,Subject=Subject,Description=Description,Category=Category,Priority=Priority,Status=before?.Status??ServiceCaseStatus.New,OwnerUserId=ParseOptionalId(OwnerUserId,"Owner user ID"),DueAtUtc=ToUtcEndOfDay(DueDate)},token);
			await ReloadAndSelectCaseAsync(saved.Id,token);CompleteOperation(false,$"Service case {saved.CaseNumber} saved.");
		}
		catch(Exception ex){FailOperation(ex,"Service case could not be saved.");}
	}
	private async Task TransitionCaseAsync(Func<ServiceCase,CancellationToken,Task<ServiceCase>> mutation,CancellationToken token)
	{
		var current=SelectedCase;if(current is null)return;BeginOperation("Updating service case...");
		try{var saved=await mutation(current,token);TransitionNote=string.Empty;await ReloadAndSelectCaseAsync(saved.Id,token);CompleteOperation(false,$"Service case {saved.CaseNumber} is {saved.Status}.");}
		catch(Exception ex){FailOperation(ex,"Service case could not be updated.");}
	}
	private async Task CreateOrderAsync(CancellationToken token)
	{
		var current=SelectedCase??throw new InvalidOperationException("Select a service case.");BeginOperation("Creating service order...");
		try
		{
			var itemId=ParseOptionalId(ServicedItemId,"Serviced item ID")??ServicedInventory?.ItemId;
			var created=await _service.CreateServiceOrderAsync(current.Id,ParseOptionalId(OrderOwnerUserId,"Assigned owner ID"),itemId,ServicedInventory?.Id,SerialLotReference,ToUtcStart(PlannedStartDate),ToUtcEndOfDay(PlannedEndDate),token);
			SelectedOrder=created;await LoadOrderEvidenceAsync(created.Id,token);CompleteOperation(false,$"Service order {created.OrderNumber} created.");
		}
		catch(Exception ex){FailOperation(ex,"Service order could not be created.");}
	}
	private async Task SaveOrderAsync(CancellationToken token)
	{
		var current=SelectedOrder;if(current is null)return;BeginOperation("Saving service order...");
		try
		{
			var itemId=ParseOptionalId(ServicedItemId,"Serviced item ID")??ServicedInventory?.ItemId;
			var saved=await _service.UpdateServiceOrderAsync(current with{AssignedOwnerUserId=ParseOptionalId(OrderOwnerUserId,"Assigned owner ID"),ServicedItemId=itemId,ServicedInventoryId=ServicedInventory?.Id,SerialLotReference=SerialLotReference,PlannedStartAtUtc=ToUtcStart(PlannedStartDate),PlannedEndAtUtc=ToUtcEndOfDay(PlannedEndDate)},token);
			SelectedOrder=saved;CompleteOperation(false,$"Service order {saved.OrderNumber} saved.");
		}
		catch(Exception ex){FailOperation(ex,"Service order could not be saved.");}
	}
	private async Task RunOrderMutationAsync(Func<ServiceOrder,Task<ServiceOrder>> mutation,string status,CancellationToken token)
	{
		var current=SelectedOrder;if(current is null)return;BeginOperation(status);
		try{SelectedOrder=await mutation(current);TransitionNote=string.Empty;await LoadOrderEvidenceAsync(SelectedOrder.Id,token);CompleteOperation(false,$"Service order {SelectedOrder.OrderNumber} is {SelectedOrder.Status}.");}
		catch(Exception ex){FailOperation(ex,"Service order could not be updated.");}
	}
	private async Task CompleteOrderAsync(CancellationToken token)
	{
		var current=SelectedOrder;if(current is null)return;BeginOperation("Completing service order...");
		try{SelectedOrder=await _service.CompleteServiceOrderAsync(current.Id,current.Version,CompletionNotes,token);await ReloadAndSelectCaseAsync(current.ServiceCaseId,token);CompleteOperation(false,$"Service order {SelectedOrder?.OrderNumber??current.OrderNumber} completed.");}
		catch(Exception ex){FailOperation(ex,"Service order could not be completed.");}
	}
	private async Task AddWorkLineAsync(CancellationToken token)
	{
		var order=SelectedOrder;if(order is null)return;BeginOperation("Adding work evidence...");
		try{await _service.AddWorkLineAsync(order.Id,WorkDescription,WorkQuantity,WorkUnitPrice,WorkBillable,WorkTaxRate,token);WorkDescription=string.Empty;WorkQuantity=1m;await LoadOrderEvidenceAsync(order.Id,token);CompleteOperation(false,"Work evidence added.");}
		catch(Exception ex){FailOperation(ex,"Work evidence could not be added.");}
	}
	private async Task RemoveWorkLineAsync(CancellationToken token)
	{
		var order=SelectedOrder;var line=SelectedWorkLine;if(order is null||line is null)return;BeginOperation("Removing work evidence...");
		try{await _service.RemoveWorkLineAsync(order.Id,line.Id,line.Version,token);await LoadOrderEvidenceAsync(order.Id,token);CompleteOperation(false,"Work evidence removed.");}
		catch(Exception ex){FailOperation(ex,"Work evidence could not be removed.");}
	}
	private async Task PostPartAsync(bool isReturn,CancellationToken token)
	{
		var order=SelectedOrder;var inventory=SelectedPartInventory;if(order is null||inventory is null)return;BeginOperation(isReturn?"Returning service part...":"Consuming service part...");
		try
		{
			var reason=ParseOptionalId(PartReasonCodeId,"Reason code ID");var tracking=TrackingAllocationTextParser.ParseUnspecified(TrackingText);
			if(isReturn)await _service.ReturnPartAsync(order.Id,inventory.Id,PartQuantity,reason,PartUnitPrice,PartTaxRate,tracking,token);
			else await _service.ConsumePartAsync(order.Id,inventory.Id,PartQuantity,reason,PartUnitPrice,PartBillable,PartTaxRate,tracking,token);
			TrackingText=string.Empty;await LoadOrderEvidenceAsync(order.Id,token);SelectedOrder=await _service.GetOrderAsync(order.Id,token);CompleteOperation(false,isReturn?"Part return posted.":"Part consumption posted.");
		}
		catch(Exception ex){FailOperation(ex,isReturn?"Part return could not be posted.":"Part consumption could not be posted.");}
	}
	private async Task GenerateInvoiceAsync(CancellationToken token)
	{
		var order=SelectedOrder;if(order is null)return;BeginOperation("Creating service invoice draft...");
		try{var result=await _service.GenerateInvoiceDraftAsync(order.Id,token);SelectedOrder=result.Order;CompleteOperation(false,result.Invoice is null?"The service order is already linked to an invoice.":$"Draft invoice {result.Invoice.InvoiceNumber} created.");}
		catch(Exception ex){FailOperation(ex,"Service invoice draft could not be created.");}
	}
	private async Task LoadSelectedCaseAsync(long? id,CancellationToken token)
	{
		History.Clear();WorkLines.Clear();Parts.Clear();SelectedOrder=null;if(id is null)return;
		try
		{
			var current=SelectedCase;
			if(current is not null)
			{
				await LoadContactsAsync(current.CustomerId,token);
				SelectedContact=current.CustomerContactId is long contactId?Contacts.FirstOrDefault(x=>x.Id==contactId):null;
			}
			var historyTask=_service.ListCaseHistoryAsync(id.Value,token);var orderTask=_service.GetOrderForCaseAsync(id.Value,token);await Task.WhenAll(historyTask,orderTask);Replace(History,historyTask.Result);SelectedOrder=orderTask.Result;if(SelectedOrder is not null)await LoadOrderEvidenceAsync(SelectedOrder.Id,token);
		}
		catch(OperationCanceledException)when(token.IsCancellationRequested){}
		catch(Exception ex){FailOperation(ex,"Service-case details could not be loaded.");}
	}
	private async Task LoadOrderEvidenceAsync(long orderId,CancellationToken token)
	{
		var workTask=_service.ListWorkLinesAsync(orderId,token);var partsTask=_service.ListPartEvidenceAsync(orderId,token);await Task.WhenAll(workTask,partsTask);Replace(WorkLines,workTask.Result);Replace(Parts,partsTask.Result);
	}
	private async Task LoadContactsAsync(long? customerId,CancellationToken token)
	{
		Contacts.Clear();if(customerId is null)return;try{Replace(Contacts,await _customers.ListContactsAsync(customerId.Value,token));}catch(OperationCanceledException)when(token.IsCancellationRequested){}
	}
	private async Task ReloadAndSelectCaseAsync(long id,CancellationToken token)
	{
		var page=await _service.SearchCasesAsync(new ServiceCaseFilter(),1,250,token);Replace(Cases,page.Items);SelectedCase=Cases.FirstOrDefault(x=>x.Id==id)??await _service.GetCaseAsync(id,token);if(SelectedCase is not null&&Cases.All(x=>x.Id!=id))Cases.Add(SelectedCase);
	}
	private void LoadCaseEditor(ServiceCase? value)
	{
		SelectedCustomer=value is null?null:Customers.FirstOrDefault(x=>x.Id==value.CustomerId);Subject=value?.Subject??string.Empty;Description=value?.Description??string.Empty;Category=value?.Category??string.Empty;Priority=value?.Priority??ServiceCasePriority.Normal;OwnerUserId=value?.OwnerUserId?.ToString(CultureInfo.InvariantCulture)??string.Empty;DueDate=value?.DueAtUtc?.ToLocalTime().Date;TransitionNote=string.Empty;
		if(value?.CustomerContactId is long contactId)SelectedContact=Contacts.FirstOrDefault(x=>x.Id==contactId);
	}
	private void LoadOrderEditor(ServiceOrder? value)
	{
		OrderOwnerUserId=value?.AssignedOwnerUserId?.ToString(CultureInfo.InvariantCulture)??string.Empty;ServicedItemId=value?.ServicedItemId?.ToString(CultureInfo.InvariantCulture)??string.Empty;ServicedInventory=value?.ServicedInventoryId is long id?PartInventory.FirstOrDefault(x=>x.Id==id):null;SerialLotReference=value?.SerialLotReference??string.Empty;PlannedStartDate=value?.PlannedStartAtUtc?.ToLocalTime().Date;PlannedEndDate=value?.PlannedEndAtUtc?.ToLocalTime().Date;CompletionNotes=value?.CompletionNotes??string.Empty;
	}
	private void RaiseCommands(){SaveCaseCommand.RaiseCanExecuteChanged();OpenCaseCommand.RaiseCanExecuteChanged();StartCaseCommand.RaiseCanExecuteChanged();WaitCaseCommand.RaiseCanExecuteChanged();ResolveCaseCommand.RaiseCanExecuteChanged();CloseCaseCommand.RaiseCanExecuteChanged();CancelCaseCommand.RaiseCanExecuteChanged();ReopenCaseCommand.RaiseCanExecuteChanged();CreateOrderCommand.RaiseCanExecuteChanged();SaveOrderCommand.RaiseCanExecuteChanged();StartOrderCommand.RaiseCanExecuteChanged();CancelOrderCommand.RaiseCanExecuteChanged();CompleteOrderCommand.RaiseCanExecuteChanged();AddWorkLineCommand.RaiseCanExecuteChanged();RemoveWorkLineCommand.RaiseCanExecuteChanged();ConsumePartCommand.RaiseCanExecuteChanged();ReturnPartCommand.RaiseCanExecuteChanged();GenerateInvoiceCommand.RaiseCanExecuteChanged();}
	private static long? ParseOptionalId(string? text,string label){if(string.IsNullOrWhiteSpace(text))return null;if(!long.TryParse(text,NumberStyles.Integer,CultureInfo.InvariantCulture,out var value)||value<=0)throw new InvalidOperationException($"{label} must be a positive number.");return value;}
	private static DateTime? ToUtcStart(DateTime? value)=>value is null?null:DateTime.SpecifyKind(value.Value.Date,DateTimeKind.Local).ToUniversalTime();
	private static DateTime? ToUtcEndOfDay(DateTime? value)=>value is null?null:DateTime.SpecifyKind(value.Value.Date.AddDays(1).AddTicks(-1),DateTimeKind.Local).ToUniversalTime();
	private static void Replace<T>(ObservableCollection<T> target,IEnumerable<T> source){target.Clear();foreach(var item in source)target.Add(item);}
	public void Dispose(){if(_disposed)return;_disposed=true;CaseAttachments.Dispose();OrderAttachments.Dispose();foreach(var command in new IDisposable[]{RefreshCommand,SaveCaseCommand,OpenCaseCommand,StartCaseCommand,WaitCaseCommand,ResolveCaseCommand,CloseCaseCommand,CancelCaseCommand,ReopenCaseCommand,CreateOrderCommand,SaveOrderCommand,StartOrderCommand,CancelOrderCommand,CompleteOrderCommand,AddWorkLineCommand,RemoveWorkLineCommand,ConsumePartCommand,ReturnPartCommand,GenerateInvoiceCommand})command.Dispose();}
}
