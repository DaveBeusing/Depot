// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Commands;
using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels;

public sealed class ReplenishmentViewModel : BaseViewModel, IDisposable
{
	private const int PageSize = 200;
	private readonly ReplenishmentService _service;
	private ReplenishmentPolicy? _selectedPolicy;
	private ReplenishmentPolicy _policyDraft = NewPolicyDraft();
	private string _policySearchText = string.Empty;
	private string _lastBatchStatus = string.Empty;
	private string _lastConversionStatus = string.Empty;
	private bool _disposed;

	public ReplenishmentViewModel(ReplenishmentService service)
	{
		_service = service ?? throw new ArgumentNullException(nameof(service));
		RefreshCommand = new AsyncRelayCommand(LoadAsync);
		NewPolicyCommand = new RelayCommand(NewPolicy, () => _service.CanManagePolicies);
		SavePolicyCommand = new AsyncRelayCommand(SavePolicyAsync, () => _service.CanManagePolicies);
		RecalculateBatchCommand = new AsyncRelayCommand(RecalculateBatchAsync, () => _service.CanReview);
		DismissSelectedCommand = new AsyncRelayCommand(DismissSelectedAsync, CanDismissSelected);
		ConvertSelectedCommand = new AsyncRelayCommand(ConvertSelectedAsync, CanConvertSelected);
	}

	public ObservableCollection<ReplenishmentPolicy> Policies { get; } = [];
	public ObservableCollection<ReplenishmentSuggestionSelectionViewModel> Suggestions { get; } = [];
	public ObservableCollection<Item> Items { get; } = [];
	public ObservableCollection<Warehouse> Warehouses { get; } = [];
	public ObservableCollection<Supplier> Suppliers { get; } = [];

	public AsyncRelayCommand RefreshCommand { get; }
	public RelayCommand NewPolicyCommand { get; }
	public AsyncRelayCommand SavePolicyCommand { get; }
	public AsyncRelayCommand RecalculateBatchCommand { get; }
	public AsyncRelayCommand DismissSelectedCommand { get; }
	public AsyncRelayCommand ConvertSelectedCommand { get; }

	public bool CanManagePolicies => _service.CanManagePolicies;
	public bool CanReview => _service.CanReview;
	public bool HasSelectedSuggestions => Suggestions.Any(value => value.IsSelected);

	public string PolicySearchText
	{
		get => _policySearchText;
		set
		{
			if (_policySearchText == value) return;
			_policySearchText = value ?? string.Empty;
			OnPropertyChanged();
		}
	}

	public ReplenishmentPolicy? SelectedPolicy
	{
		get => _selectedPolicy;
		set
		{
			if (ReferenceEquals(_selectedPolicy, value)) return;
			_selectedPolicy = value;
			OnPropertyChanged();
			if (value is not null) PolicyDraft = Copy(value);
		}
	}

	public ReplenishmentPolicy PolicyDraft
	{
		get => _policyDraft;
		private set
		{
			_policyDraft = value;
			OnPropertyChanged();
		}
	}

	public string LastBatchStatus
	{
		get => _lastBatchStatus;
		private set
		{
			if (_lastBatchStatus == value) return;
			_lastBatchStatus = value;
			OnPropertyChanged();
		}
	}

	public string LastConversionStatus
	{
		get => _lastConversionStatus;
		private set
		{
			if (_lastConversionStatus == value) return;
			_lastConversionStatus = value;
			OnPropertyChanged();
		}
	}

	public async Task LoadAsync(CancellationToken cancellationToken = default)
	{
		BeginOperation("Loading inventory replenishment");
		try
		{
			var policiesTask = _service.SearchPoliciesAsync(PolicySearchText,true,1,PageSize,cancellationToken);
			var suggestionsTask = _service.SearchSuggestionsAsync(null,1,PageSize,cancellationToken);
			Task<IReadOnlyList<Item>> itemsTask = _service.CanManagePolicies ? _service.ListPolicyItemsAsync(cancellationToken:cancellationToken) : Task.FromResult<IReadOnlyList<Item>>([]);
			Task<IReadOnlyList<Warehouse>> warehousesTask = _service.CanManagePolicies ? _service.ListPolicyWarehousesAsync(cancellationToken:cancellationToken) : Task.FromResult<IReadOnlyList<Warehouse>>([]);
			Task<IReadOnlyList<Supplier>> suppliersTask = _service.CanManagePolicies ? _service.ListPolicySuppliersAsync(cancellationToken:cancellationToken) : Task.FromResult<IReadOnlyList<Supplier>>([]);
			await Task.WhenAll(policiesTask,suggestionsTask,itemsTask,warehousesTask,suppliersTask);
			Replace(Policies,(await policiesTask).Items);
			ReplaceSuggestions((await suggestionsTask).Items);
			Replace(Items,await itemsTask);
			Replace(Warehouses,await warehousesTask);
			Replace(Suppliers,await suppliersTask);
			if(SelectedPolicy is not null) SelectedPolicy=Policies.FirstOrDefault(value=>value.Id==SelectedPolicy.Id);
			CompleteOperation(Policies.Count==0 && Suggestions.Count==0,"Inventory replenishment loaded");
		}
		catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested) { }
		catch(Exception exception) { FailOperation(exception,"Inventory replenishment could not be loaded"); }
		RaiseState();
	}

	private void NewPolicy()
	{
		SelectedPolicy=null;
		PolicyDraft=NewPolicyDraft();
		RequestEditorFocus();
		RaiseState();
	}

	private async Task SavePolicyAsync(CancellationToken cancellationToken)
	{
		BeginOperation("Saving replenishment policy");
		try
		{
			var saved=await _service.SavePolicyAsync(PolicyDraft,cancellationToken);
			await LoadAsync(cancellationToken);
			SelectedPolicy=Policies.FirstOrDefault(value=>value.Id==saved.Id);
			CompleteOperation(false,"Replenishment policy saved");
		}
		catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested) { }
		catch(Exception exception) { FailOperation(exception,"Replenishment policy could not be saved"); }
		RaiseState();
	}

	private async Task RecalculateBatchAsync(CancellationToken cancellationToken)
	{
		BeginOperation("Recalculating replenishment suggestions");
		try
		{
			var result=await _service.RecalculateBatchAsync(0,500,cancellationToken);
			LastBatchStatus=$"Evaluated {result.PoliciesEvaluated:N0} policies in {result.Elapsed.TotalMilliseconds:N0} ms · open {result.SuggestionsOpen:N0} · blocked {result.SuggestionsBlocked:N0} · superseded {result.SuggestionsSuperseded:N0}.";
			await ReloadPlanningAsync(cancellationToken);
			CompleteOperation(Suggestions.Count==0,"Replenishment recalculation completed");
		}
		catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested) { }
		catch(Exception exception) { FailOperation(exception,"Replenishment recalculation failed"); }
		RaiseState();
	}

	private async Task DismissSelectedAsync(CancellationToken cancellationToken)
	{
		var selected=Suggestions.Where(value=>value.IsSelected && value.Source.Status is ReplenishmentSuggestionStatus.Open or ReplenishmentSuggestionStatus.Blocked).Select(value=>value.Source).ToArray();
		if(selected.Length==0) return;
		BeginOperation("Dismissing replenishment suggestions");
		try
		{
			foreach(var suggestion in selected) await _service.DismissAsync(suggestion.Id,suggestion.Version,cancellationToken);
			await ReloadPlanningAsync(cancellationToken);
			CompleteOperation(Suggestions.Count==0,$"Dismissed {selected.Length:N0} suggestion(s)");
		}
		catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested) { }
		catch(Exception exception) { FailOperation(exception,"Replenishment suggestions could not be dismissed"); }
		RaiseState();
	}

	private async Task ConvertSelectedAsync(CancellationToken cancellationToken)
	{
		var selected=Suggestions.Where(value=>value.IsSelected && value.Source.Status==ReplenishmentSuggestionStatus.Open).Select(value=>value.Source.Id).ToArray();
		if(selected.Length==0) return;
		BeginOperation("Creating purchase requisition");
		try
		{
			var result=await _service.ConvertToPurchaseRequisitionAsync(selected,cancellationToken);
			LastConversionStatus=$"Purchase requisition #{result.PurchaseRequisitionId:N0} created from {result.SuggestionIds.Count:N0} reviewed suggestion(s). No purchase order was created.";
			await ReloadPlanningAsync(cancellationToken);
			CompleteOperation(Suggestions.Count==0,"Purchase requisition created");
		}
		catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested) { }
		catch(Exception exception) { FailOperation(exception,"Purchase requisition could not be created"); }
		RaiseState();
	}

	private async Task ReloadPlanningAsync(CancellationToken cancellationToken)
	{
		var suggestions=await _service.SearchSuggestionsAsync(null,1,PageSize,cancellationToken);
		ReplaceSuggestions(suggestions.Items);
	}

	private void ReplaceSuggestions(IEnumerable<ReplenishmentSuggestion> values)
	{
		Suggestions.Clear();
		foreach(var value in values) Suggestions.Add(new ReplenishmentSuggestionSelectionViewModel(value,RaiseState));
		RaiseState();
	}

	private static void Replace<T>(ObservableCollection<T> target,IEnumerable<T> source)
	{
		target.Clear();
		foreach(var value in source) target.Add(value);
	}

	private bool CanDismissSelected() =>
		_service.CanReview && Suggestions.Any(value=>value.IsSelected && value.Source.Status is ReplenishmentSuggestionStatus.Open or ReplenishmentSuggestionStatus.Blocked);

	private bool CanConvertSelected()
	{
		if(!_service.CanReview) return false;
		var selected=Suggestions.Where(value=>value.IsSelected).Select(value=>value.Source).ToArray();
		return selected.Length>0 && selected.All(value=>value.Status==ReplenishmentSuggestionStatus.Open);
	}

	private void RaiseState()
	{
		OnPropertyChanged(nameof(HasSelectedSuggestions));
		DismissSelectedCommand.RaiseCanExecuteChanged();
		ConvertSelectedCommand.RaiseCanExecuteChanged();
		SavePolicyCommand.RaiseCanExecuteChanged();
		RecalculateBatchCommand.RaiseCanExecuteChanged();
	}

	private static ReplenishmentPolicy NewPolicyDraft()=>new(){IsActive=true};
	private static ReplenishmentPolicy Copy(ReplenishmentPolicy source)=>new()
	{
		Id=source.Id,ItemId=source.ItemId,ItemPartNumber=source.ItemPartNumber,ItemDescription=source.ItemDescription,
		WarehouseId=source.WarehouseId,WarehouseName=source.WarehouseName,IsActive=source.IsActive,
		ReorderPoint=source.ReorderPoint,SafetyStock=source.SafetyStock,TargetStock=source.TargetStock,
		PreferredSupplierId=source.PreferredSupplierId,PreferredSupplierName=source.PreferredSupplierName,Version=source.Version
	};

	public void Dispose()
	{
		if(_disposed) return;
		_disposed=true;
		RefreshCommand.Dispose();
		SavePolicyCommand.Dispose();
		RecalculateBatchCommand.Dispose();
		DismissSelectedCommand.Dispose();
		ConvertSelectedCommand.Dispose();
	}
}

public sealed class ReplenishmentSuggestionSelectionViewModel : BaseViewModel
{
	private readonly Action _changed;
	private bool _isSelected;

	public ReplenishmentSuggestionSelectionViewModel(ReplenishmentSuggestion source,Action changed)
	{
		Source=source ?? throw new ArgumentNullException(nameof(source));
		_changed=changed ?? throw new ArgumentNullException(nameof(changed));
	}

	public ReplenishmentSuggestion Source { get; }
	public bool IsSelected
	{
		get=>_isSelected;
		set
		{
			if(_isSelected==value) return;
			_isSelected=value;
			OnPropertyChanged();
			_changed();
		}
	}
}
