// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Commands;
using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels;

public sealed class ProductionViewModel : BaseViewModel, IDisposable
{
	private const int PageSize=200;
	private readonly ProductionService _service;
	private readonly ReasonCodeService _reasonCodes;
	private BillOfMaterial? _selectedBom;
	private BillOfMaterial _bomDraft=NewBomDraft();
	private Item? _selectedBomFinishedItem;
	private ProductionBomLineEditor? _selectedBomLine;
	private BillOfMaterial? _selectedOrderBom;
	private Warehouse? _selectedWarehouse;
	private InventoryLookupItem? _selectedFinishedInventory;
	private ProductionOrder? _selectedOrder;
	private ProductionOrder? _orderDetail;
	private ProductionOrderRequirement? _selectedRequirement;
	private InventoryLookupItem? _selectedIssueInventory;
	private ReasonCode? _selectedReversalReasonCode;
	private int _orderPlannedQuantity=1;
	private int _issueQuantity=1;
	private string _issueTrackingCodes=string.Empty;
	private string _finishedTrackingCodes=string.Empty;
	private string _reversalReason=string.Empty;
	private string _handoffStatus=string.Empty;
	private bool _disposed;

	public ProductionViewModel(ProductionService service,ReasonCodeService reasonCodes)
	{
		_service=service??throw new ArgumentNullException(nameof(service));
		_reasonCodes=reasonCodes??throw new ArgumentNullException(nameof(reasonCodes));
		RefreshCommand=new AsyncRelayCommand(LoadAsync);
		NewBomCommand=new RelayCommand(NewBom,()=>_service.CanManageBoms);
		AddBomLineCommand=new RelayCommand(AddBomLine,()=>_service.CanManageBoms && BomDraft.Status==BillOfMaterialStatus.Draft);
		RemoveBomLineCommand=new RelayCommand(RemoveBomLine,()=>_service.CanManageBoms && SelectedBomLine is not null && BomDraft.Status==BillOfMaterialStatus.Draft);
		SaveBomCommand=new AsyncRelayCommand(SaveBomAsync,CanSaveBom);
		ActivateBomCommand=new AsyncRelayCommand(ActivateBomAsync,CanActivateBom);
		CreateOrderCommand=new AsyncRelayCommand(CreateOrderAsync,CanCreateOrder);
		ReleaseOrderCommand=new AsyncRelayCommand(ReleaseOrderAsync,CanReleaseOrder);
		HandoffShortagesCommand=new AsyncRelayCommand(HandoffShortagesAsync,CanHandoffShortages);
		IssueComponentCommand=new AsyncRelayCommand(IssueComponentAsync,CanIssueComponent);
		CompleteOrderCommand=new AsyncRelayCommand(CompleteOrderAsync,CanCompleteOrder);
		ReverseOrderCommand=new AsyncRelayCommand(ReverseOrderAsync,CanReverseOrder);
	}

	public ObservableCollection<BillOfMaterial> Boms { get; }=[];
	public ObservableCollection<ProductionOrder> Orders { get; }=[];
	public ObservableCollection<Item> StockItems { get; }=[];
	public ObservableCollection<Warehouse> Warehouses { get; }=[];
	public ObservableCollection<InventoryLookupItem> Inventories { get; }=[];
	public ObservableCollection<ReasonCode> ReversalReasonCodes { get; }=[];
	public ObservableCollection<ProductionBomLineEditor> BomLines { get; }=[];
	public ObservableCollection<ProductionRequirementAvailability> Availability { get; }=[];
	public ObservableCollection<ProductionAssemblyCostEvidence> CostEvidence { get; }=[];

	public AsyncRelayCommand RefreshCommand { get; }
	public RelayCommand NewBomCommand { get; }
	public RelayCommand AddBomLineCommand { get; }
	public RelayCommand RemoveBomLineCommand { get; }
	public AsyncRelayCommand SaveBomCommand { get; }
	public AsyncRelayCommand ActivateBomCommand { get; }
	public AsyncRelayCommand CreateOrderCommand { get; }
	public AsyncRelayCommand ReleaseOrderCommand { get; }
	public AsyncRelayCommand HandoffShortagesCommand { get; }
	public AsyncRelayCommand IssueComponentCommand { get; }
	public AsyncRelayCommand CompleteOrderCommand { get; }
	public AsyncRelayCommand ReverseOrderCommand { get; }

	public bool CanManageBoms=>_service.CanManageBoms;
	public bool CanManageOrders=>_service.CanManageOrders;
	public bool CanIssue=>_service.CanIssue;
	public bool CanComplete=>_service.CanComplete;
	public bool CanReverse=>_service.CanReverse;
	public bool CanHandoff=>_service.CanHandoffShortages;
	public IEnumerable<BillOfMaterial> ActiveBoms=>Boms.Where(value=>value.Status==BillOfMaterialStatus.Active);
	public IEnumerable<InventoryLookupItem> FinishedInventoryOptions=>Inventories.Where(value=>SelectedOrderBom is not null && SelectedWarehouse is not null && value.ItemId==SelectedOrderBom.FinishedItemId && string.Equals(value.WarehouseName,SelectedWarehouse.Name,StringComparison.CurrentCultureIgnoreCase));
	public IEnumerable<InventoryLookupItem> IssueInventoryOptions=>Inventories.Where(value=>SelectedRequirement is not null && OrderDetail is not null && value.ItemId==SelectedRequirement.ComponentItemId && string.Equals(value.WarehouseName,OrderDetail.WarehouseName,StringComparison.CurrentCultureIgnoreCase));

	public BillOfMaterial? SelectedBom
	{
		get=>_selectedBom;
		set
		{
			if(ReferenceEquals(_selectedBom,value))return;
			_selectedBom=value;
			OnPropertyChanged();
			if(value is not null) LoadBomEditor(value);
			RaiseState();
		}
	}

	public BillOfMaterial BomDraft
	{
		get=>_bomDraft;
		private set{_bomDraft=value;OnPropertyChanged();RaiseState();}
	}

	public Item? SelectedBomFinishedItem
	{
		get=>_selectedBomFinishedItem;
		set
		{
			if(ReferenceEquals(_selectedBomFinishedItem,value))return;
			_selectedBomFinishedItem=value;
			BomDraft.FinishedItemId=value?.Id??0;
			OnPropertyChanged();
			RaiseState();
		}
	}

	public ProductionBomLineEditor? SelectedBomLine
	{
		get=>_selectedBomLine;
		set{if(ReferenceEquals(_selectedBomLine,value))return;_selectedBomLine=value;OnPropertyChanged();RaiseState();}
	}

	public BillOfMaterial? SelectedOrderBom
	{
		get=>_selectedOrderBom;
		set
		{
			if(ReferenceEquals(_selectedOrderBom,value))return;
			_selectedOrderBom=value;
			_selectedFinishedInventory=null;
			OnPropertyChanged();
			OnPropertyChanged(nameof(SelectedFinishedInventory));
			OnPropertyChanged(nameof(FinishedInventoryOptions));
			RaiseState();
		}
	}

	public Warehouse? SelectedWarehouse
	{
		get=>_selectedWarehouse;
		set
		{
			if(ReferenceEquals(_selectedWarehouse,value))return;
			_selectedWarehouse=value;
			_selectedFinishedInventory=null;
			OnPropertyChanged();
			OnPropertyChanged(nameof(SelectedFinishedInventory));
			OnPropertyChanged(nameof(FinishedInventoryOptions));
			RaiseState();
		}
	}

	public InventoryLookupItem? SelectedFinishedInventory
	{
		get=>_selectedFinishedInventory;
		set{if(ReferenceEquals(_selectedFinishedInventory,value))return;_selectedFinishedInventory=value;OnPropertyChanged();RaiseState();}
	}

	public int OrderPlannedQuantity
	{
		get=>_orderPlannedQuantity;
		set{if(_orderPlannedQuantity==value)return;_orderPlannedQuantity=value;OnPropertyChanged();RaiseState();}
	}

	public ProductionOrder? SelectedOrder
	{
		get=>_selectedOrder;
		set
		{
			if(ReferenceEquals(_selectedOrder,value))return;
			_selectedOrder=value;
			OnPropertyChanged();
			if(value is null) ClearOrderDetail();
			else _=LoadOrderDetailsSafeAsync(value.Id);
			RaiseState();
		}
	}

	public ProductionOrder? OrderDetail
	{
		get=>_orderDetail;
		private set
		{
			_orderDetail=value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(IssueInventoryOptions));
			RaiseState();
		}
	}

	public ProductionOrderRequirement? SelectedRequirement
	{
		get=>_selectedRequirement;
		set
		{
			if(ReferenceEquals(_selectedRequirement,value))return;
			_selectedRequirement=value;
			_selectedIssueInventory=null;
			_issueQuantity=Math.Max(1,value?.RemainingQuantity??1);
			OnPropertyChanged();
			OnPropertyChanged(nameof(SelectedIssueInventory));
			OnPropertyChanged(nameof(IssueQuantity));
			OnPropertyChanged(nameof(IssueInventoryOptions));
			RaiseState();
		}
	}

	public InventoryLookupItem? SelectedIssueInventory
	{
		get=>_selectedIssueInventory;
		set{if(ReferenceEquals(_selectedIssueInventory,value))return;_selectedIssueInventory=value;OnPropertyChanged();RaiseState();}
	}

	public int IssueQuantity
	{
		get=>_issueQuantity;
		set{if(_issueQuantity==value)return;_issueQuantity=value;OnPropertyChanged();RaiseState();}
	}

	public string IssueTrackingCodes
	{
		get=>_issueTrackingCodes;
		set{if(_issueTrackingCodes==value)return;_issueTrackingCodes=value??string.Empty;OnPropertyChanged();}
	}

	public string FinishedTrackingCodes
	{
		get=>_finishedTrackingCodes;
		set{if(_finishedTrackingCodes==value)return;_finishedTrackingCodes=value??string.Empty;OnPropertyChanged();}
	}

	public ReasonCode? SelectedReversalReasonCode
	{
		get=>_selectedReversalReasonCode;
		set{if(ReferenceEquals(_selectedReversalReasonCode,value))return;_selectedReversalReasonCode=value;OnPropertyChanged();RaiseState();}
	}

	public string ReversalReason
	{
		get=>_reversalReason;
		set{if(_reversalReason==value)return;_reversalReason=value??string.Empty;OnPropertyChanged();RaiseState();}
	}

	public string HandoffStatus
	{
		get=>_handoffStatus;
		private set{if(_handoffStatus==value)return;_handoffStatus=value;OnPropertyChanged();}
	}

	public async Task LoadAsync(CancellationToken cancellationToken=default)
	{
		BeginOperation("Loading production workspace");
		try
		{
			var bomsTask=_service.SearchBomsAsync(null,1,PageSize,cancellationToken);
			var ordersTask=_service.SearchOrdersAsync(null,1,PageSize,cancellationToken);
			var itemsTask=_service.ListStockItemsAsync(500,cancellationToken);
			var warehousesTask=_service.ListWarehousesAsync(200,cancellationToken);
			var inventoriesTask=_service.SearchInventoriesAsync(null,1000,cancellationToken);
			var reasonsTask=_reasonCodes.GetActiveAsync(cancellationToken);
			await Task.WhenAll(bomsTask,ordersTask,itemsTask,warehousesTask,inventoriesTask,reasonsTask);
			var selectedBomId=SelectedBom?.Id;
			var selectedOrderId=SelectedOrder?.Id;
			Replace(Boms,(await bomsTask).Items);
			Replace(Orders,(await ordersTask).Items);
			Replace(StockItems,await itemsTask);
			Replace(Warehouses,await warehousesTask);
			Replace(Inventories,await inventoriesTask);
			Replace(ReversalReasonCodes,await reasonsTask);
			OnPropertyChanged(nameof(ActiveBoms));
			OnPropertyChanged(nameof(FinishedInventoryOptions));
			SelectedBom=selectedBomId is long bomId?Boms.FirstOrDefault(value=>value.Id==bomId):Boms.FirstOrDefault();
			SelectedOrderBom=ActiveBoms.FirstOrDefault();
			SelectedWarehouse??=Warehouses.FirstOrDefault();
			SelectedOrder=selectedOrderId is long orderId?Orders.FirstOrDefault(value=>value.Id==orderId):Orders.FirstOrDefault();
			SelectedReversalReasonCode??=ReversalReasonCodes.FirstOrDefault();
			CompleteOperation(Boms.Count==0 && Orders.Count==0,"Production workspace loaded");
		}
		catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested){}
		catch(Exception exception){FailOperation(exception,"Production workspace could not be loaded");}
		RaiseState();
	}

	public async Task OpenOrderAsync(long orderId,CancellationToken cancellationToken=default)
	{
		var order=Orders.FirstOrDefault(value=>value.Id==orderId)??await _service.GetOrderAsync(orderId,cancellationToken);
		if(order is null)return;
		if(Orders.All(value=>value.Id!=order.Id))Orders.Insert(0,order);
		_selectedOrder=order;
		OnPropertyChanged(nameof(SelectedOrder));
		await LoadOrderDetailsAsync(order.Id,cancellationToken);
	}

	private void NewBom()
	{
		_selectedBom=null;
		OnPropertyChanged(nameof(SelectedBom));
		BomDraft=NewBomDraft();
		BomLines.Clear();
		SelectedBomFinishedItem=null;
		SelectedBomLine=null;
		AddBomLine();
		RequestEditorFocus();
	}

	private void AddBomLine()
	{
		var next=BomLines.Count==0?10:BomLines.Max(value=>value.Sequence)+10;
		var line=new ProductionBomLineEditor{Quantity=1m,Sequence=next};
		BomLines.Add(line);
		SelectedBomLine=line;
		RaiseState();
	}

	private void RemoveBomLine()
	{
		if(SelectedBomLine is null)return;
		BomLines.Remove(SelectedBomLine);
		SelectedBomLine=BomLines.LastOrDefault();
		RaiseState();
	}

	private async Task SaveBomAsync(CancellationToken token)
	{
		BeginOperation("Saving bill of material");
		try
		{
			var draft=CopyBom(BomDraft);
			draft.FinishedItemId=SelectedBomFinishedItem?.Id??0;
			draft.Lines=BomLines.Select(value=>new BillOfMaterialLine{Id=value.Id,BillOfMaterialId=draft.Id,ComponentItemId=value.ComponentItemId,Quantity=value.Quantity,Sequence=value.Sequence}).ToArray();
			var saved=await _service.SaveBomAsync(draft,token);
			await ReloadBomsAsync(saved.Id,token);
			CompleteOperation(false,"Bill of material saved");
		}
		catch(OperationCanceledException) when(token.IsCancellationRequested){}
		catch(Exception exception){FailOperation(exception,"Bill of material could not be saved");}
		RaiseState();
	}

	private async Task ActivateBomAsync(CancellationToken token)
	{
		if(BomDraft.Id<=0)return;
		BeginOperation("Activating bill of material");
		try
		{
			var saved=await _service.ActivateBomAsync(BomDraft.Id,BomDraft.Version,token);
			await ReloadBomsAsync(saved.Id,token);
			CompleteOperation(false,"Bill of material activated");
		}
		catch(OperationCanceledException) when(token.IsCancellationRequested){}
		catch(Exception exception){FailOperation(exception,"Bill of material could not be activated");}
		RaiseState();
	}

	private async Task CreateOrderAsync(CancellationToken token)
	{
		BeginOperation("Creating production order");
		try
		{
			var bom=SelectedOrderBom??throw new InvalidOperationException("Select an active bill of material.");
			var warehouse=SelectedWarehouse??throw new InvalidOperationException("Select a production warehouse.");
			var inventory=SelectedFinishedInventory??throw new InvalidOperationException("Select a finished-goods inventory location.");
			var saved=await _service.CreateOrderAsync(bom.FinishedItemId,bom.Id,OrderPlannedQuantity,warehouse.Id,inventory.Id,null,token);
			await ReloadOrdersAsync(saved.Id,token);
			CompleteOperation(false,$"Production order {saved.OrderNumber} created");
		}
		catch(OperationCanceledException) when(token.IsCancellationRequested){}
		catch(Exception exception){FailOperation(exception,"Production order could not be created");}
		RaiseState();
	}

	private async Task ReleaseOrderAsync(CancellationToken token)
	{
		if(OrderDetail is null)return;
		BeginOperation("Releasing production order");
		try
		{
			var saved=await _service.ReleaseOrderAsync(OrderDetail.Id,OrderDetail.Version,token);
			await ReloadOrdersAsync(saved.Id,token);
			CompleteOperation(false,$"{saved.OrderNumber} released");
		}
		catch(OperationCanceledException) when(token.IsCancellationRequested){}
		catch(Exception exception){FailOperation(exception,"Production order could not be released");}
		RaiseState();
	}

	private async Task HandoffShortagesAsync(CancellationToken token)
	{
		if(OrderDetail is null)return;
		BeginOperation("Handing shortages to replenishment");
		try
		{
			var result=await _service.HandoffShortagesToReplenishmentAsync(OrderDetail.Id,token);
			HandoffStatus=result.ShortagesEvaluated==0?"No current shortages require replenishment.":$"Evaluated {result.ShortagesEvaluated:N0} shortage(s); updated {result.ReplenishmentSuggestionIds.Count:N0} replenishment suggestion(s).";
			await LoadOrderDetailsAsync(OrderDetail.Id,token);
			CompleteOperation(false,"Shortage handoff completed");
		}
		catch(OperationCanceledException) when(token.IsCancellationRequested){}
		catch(Exception exception){FailOperation(exception,"Shortages could not be handed to replenishment");}
		RaiseState();
	}

	private async Task IssueComponentAsync(CancellationToken token)
	{
		if(OrderDetail is null || SelectedRequirement is null || SelectedIssueInventory is null)return;
		BeginOperation("Issuing production component");
		try
		{
			var allocations=BuildTrackingAllocations(IssueTrackingCodes,IssueQuantity);
			var saved=await _service.IssueComponentAsync(OrderDetail.Id,SelectedRequirement.Id,SelectedIssueInventory.Id,IssueQuantity,allocations,null,token);
			IssueTrackingCodes=string.Empty;
			await ReloadOrdersAsync(saved.Id,token);
			CompleteOperation(false,"Component issue posted");
		}
		catch(OperationCanceledException) when(token.IsCancellationRequested){}
		catch(Exception exception){FailOperation(exception,"Component issue could not be posted");}
		RaiseState();
	}

	private async Task CompleteOrderAsync(CancellationToken token)
	{
		if(OrderDetail is null)return;
		BeginOperation("Completing production order");
		try
		{
			var allocations=BuildTrackingAllocations(FinishedTrackingCodes,OrderDetail.PlannedQuantity);
			var result=await _service.CompleteAsync(OrderDetail.Id,allocations,null,token);
			FinishedTrackingCodes=string.Empty;
			await ReloadOrdersAsync(result.Order.Id,token);
			CompleteOperation(false,$"{result.Order.OrderNumber} completed · component cost {result.Cost.TotalComponentCost:N2} {result.Cost.Currency}");
		}
		catch(OperationCanceledException) when(token.IsCancellationRequested){}
		catch(Exception exception){FailOperation(exception,"Production order could not be completed");}
		RaiseState();
	}

	private async Task ReverseOrderAsync(CancellationToken token)
	{
		if(OrderDetail is null || SelectedReversalReasonCode is null)return;
		BeginOperation("Reversing production order");
		try
		{
			var saved=await _service.ReverseAsync(OrderDetail.Id,SelectedReversalReasonCode.Id,ReversalReason,null,token);
			ReversalReason=string.Empty;
			await ReloadOrdersAsync(saved.Id,token);
			CompleteOperation(false,$"{saved.OrderNumber} reversed with compensating stock movements");
		}
		catch(OperationCanceledException) when(token.IsCancellationRequested){}
		catch(Exception exception){FailOperation(exception,"Production order could not be reversed");}
		RaiseState();
	}

	private async Task ReloadBomsAsync(long selectedId,CancellationToken token)
	{
		var page=await _service.SearchBomsAsync(null,1,PageSize,token);
		Replace(Boms,page.Items);
		OnPropertyChanged(nameof(ActiveBoms));
		SelectedBom=Boms.FirstOrDefault(value=>value.Id==selectedId);
		if(SelectedOrderBom is not null)SelectedOrderBom=Boms.FirstOrDefault(value=>value.Id==SelectedOrderBom.Id && value.Status==BillOfMaterialStatus.Active)??ActiveBoms.FirstOrDefault();
	}

	private async Task ReloadOrdersAsync(long selectedId,CancellationToken token)
	{
		var page=await _service.SearchOrdersAsync(null,1,PageSize,token);
		Replace(Orders,page.Items);
		SelectedOrder=Orders.FirstOrDefault(value=>value.Id==selectedId);
		await LoadOrderDetailsAsync(selectedId,token);
	}

	private async Task LoadOrderDetailsSafeAsync(long orderId)
	{
		try{await LoadOrderDetailsAsync(orderId,CancellationToken.None);}
		catch(Exception exception){FailOperation(exception,"Production order details could not be loaded");RaiseState();}
	}

	private async Task LoadOrderDetailsAsync(long orderId,CancellationToken token)
	{
		var detail=await _service.GetOrderAsync(orderId,token);
		if(detail is null || SelectedOrder?.Id!=orderId)return;
		var availabilityTask=_service.GetAvailabilityAsync(orderId,token);
		var costTask=_service.GetCostEvidenceAsync(orderId,token);
		await Task.WhenAll(availabilityTask,costTask);
		OrderDetail=detail;
		Replace(Availability,await availabilityTask);
		Replace(CostEvidence,await costTask);
		SelectedRequirement=detail.Requirements.FirstOrDefault(value=>value.RemainingQuantity>0)??detail.Requirements.FirstOrDefault();
		SelectedIssueInventory=IssueInventoryOptions.FirstOrDefault();
		RaiseState();
	}

	private void ClearOrderDetail()
	{
		OrderDetail=null;
		Availability.Clear();
		CostEvidence.Clear();
		SelectedRequirement=null;
		SelectedIssueInventory=null;
	}

	private void LoadBomEditor(BillOfMaterial value)
	{
		BomDraft=CopyBom(value);
		SelectedBomFinishedItem=StockItems.FirstOrDefault(item=>item.Id==value.FinishedItemId);
		BomLines.Clear();
		foreach(var line in value.Lines.OrderBy(line=>line.Sequence))BomLines.Add(new ProductionBomLineEditor{Id=line.Id,ComponentItemId=line.ComponentItemId,Quantity=line.Quantity,Sequence=line.Sequence});
		SelectedBomLine=BomLines.FirstOrDefault();
	}

	private bool CanSaveBom()=>_service.CanManageBoms && SelectedBomFinishedItem is not null && BomLines.Count>0 && (BomDraft.Id==0 || BomDraft.Status==BillOfMaterialStatus.Draft);
	private bool CanActivateBom()=>_service.CanManageBoms && BomDraft.Id>0 && BomDraft.Status==BillOfMaterialStatus.Draft;
	private bool CanCreateOrder()=>_service.CanManageOrders && SelectedOrderBom?.Status==BillOfMaterialStatus.Active && SelectedWarehouse is not null && SelectedFinishedInventory is not null && OrderPlannedQuantity>0;
	private bool CanReleaseOrder()=>_service.CanManageOrders && OrderDetail?.Status==ProductionOrderStatus.Draft;
	private bool CanHandoffShortages()=>_service.CanHandoffShortages && OrderDetail?.Status is ProductionOrderStatus.Released or ProductionOrderStatus.InProgress && Availability.Any(value=>value.ShortageQuantity>0);
	private bool CanIssueComponent()=>_service.CanIssue && OrderDetail?.Status is ProductionOrderStatus.Released or ProductionOrderStatus.InProgress && SelectedRequirement is {RemainingQuantity:>0} && SelectedIssueInventory is not null && IssueQuantity>0 && IssueQuantity<=SelectedRequirement.RemainingQuantity;
	private bool CanCompleteOrder()=>_service.CanComplete && OrderDetail?.Status is ProductionOrderStatus.Released or ProductionOrderStatus.InProgress && OrderDetail.Requirements.Count>0 && OrderDetail.Requirements.All(value=>value.IssuedQuantity==value.RequiredQuantity);
	private bool CanReverseOrder()=>_service.CanReverse && OrderDetail?.Status==ProductionOrderStatus.Completed && SelectedReversalReasonCode is not null && !string.IsNullOrWhiteSpace(ReversalReason);

	private void RaiseState()
	{
		OnPropertyChanged(nameof(CanManageBoms));OnPropertyChanged(nameof(CanManageOrders));OnPropertyChanged(nameof(CanIssue));OnPropertyChanged(nameof(CanComplete));OnPropertyChanged(nameof(CanReverse));OnPropertyChanged(nameof(CanHandoff));
		SaveBomCommand.RaiseCanExecuteChanged();ActivateBomCommand.RaiseCanExecuteChanged();CreateOrderCommand.RaiseCanExecuteChanged();ReleaseOrderCommand.RaiseCanExecuteChanged();HandoffShortagesCommand.RaiseCanExecuteChanged();IssueComponentCommand.RaiseCanExecuteChanged();CompleteOrderCommand.RaiseCanExecuteChanged();ReverseOrderCommand.RaiseCanExecuteChanged();RemoveBomLineCommand.RaiseCanExecuteChanged();AddBomLineCommand.RaiseCanExecuteChanged();
	}

	private static IReadOnlyList<TrackingAllocationInput> BuildTrackingAllocations(string raw,int quantity)
	{
		if(string.IsNullOrWhiteSpace(raw))return [];
		var codes=raw.Split(['\r','\n',',',';'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
		if(codes.Length==1)return [new TrackingAllocationInput{Code=codes[0],Quantity=quantity}];
		if(codes.Length!=quantity)throw new InvalidOperationException("For serial tracking, enter one unique tracking code per moved unit. For lot tracking, enter a single lot code.");
		return codes.Select(code=>new TrackingAllocationInput{Code=code,Quantity=1}).ToArray();
	}

	private static BillOfMaterial NewBomDraft()=>new(){Status=BillOfMaterialStatus.Draft,Revision=string.Empty,Lines=[]};
	private static BillOfMaterial CopyBom(BillOfMaterial source)=>new(){Id=source.Id,FinishedItemId=source.FinishedItemId,FinishedPartNumber=source.FinishedPartNumber,FinishedDescription=source.FinishedDescription,Revision=source.Revision,Status=source.Status,EffectiveFromUtc=source.EffectiveFromUtc,EffectiveUntilUtc=source.EffectiveUntilUtc,Version=source.Version,CreatedAtUtc=source.CreatedAtUtc,CreatedByUserId=source.CreatedByUserId,Lines=source.Lines.ToArray()};
	private static void Replace<T>(ObservableCollection<T> target,IEnumerable<T> source){target.Clear();foreach(var value in source)target.Add(value);}

	public void Dispose()
	{
		if(_disposed)return;_disposed=true;
		RefreshCommand.Dispose();SaveBomCommand.Dispose();ActivateBomCommand.Dispose();CreateOrderCommand.Dispose();ReleaseOrderCommand.Dispose();HandoffShortagesCommand.Dispose();IssueComponentCommand.Dispose();CompleteOrderCommand.Dispose();ReverseOrderCommand.Dispose();
	}
}

public sealed class ProductionBomLineEditor
{
	public long Id { get; set; }
	public long ComponentItemId { get; set; }
	public decimal Quantity { get; set; }=1m;
	public int Sequence { get; set; }
}
