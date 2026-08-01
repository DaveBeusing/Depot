// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Commands;
using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels;

public sealed class MaterialIssuesViewModel : BaseViewModel, IDisposable
{
	private const int PageSize = 50;
	private readonly MaterialIssueService _service;
	private readonly ReasonCodeService _reasonCodes;
	private readonly IFileDialogService _dialogs;
	private readonly AsyncDebouncer _search = new(TimeSpan.FromMilliseconds(300));
	private readonly AsyncDebouncer _inventorySearch = new(TimeSpan.FromMilliseconds(300));
	private CancellationTokenSource? _selectionCancellation;
	private MaterialIssueOverviewItem? _selectedIssue;
	private MaterialIssue _draft = NewDraft();
	private MaterialIssueLineEditor? _selectedLine;
	private InventoryOverviewItem? _selectedInventory;
	private ReasonCode? _selectedReasonCode;
	private string _searchText = string.Empty;
	private string _inventorySearchText = string.Empty;
	private MaterialIssueStatusFilter _selectedStatusFilter;
	private int _lineQuantity = 1;
	private string? _lineNotes;
	private int _pageNumber = 1;
	private long _totalCount;
	private ReasonCode? _selectedReversalReasonCode;
	private string _reversalReason = string.Empty;

	public MaterialIssuesViewModel(MaterialIssueService service, ReasonCodeService reasonCodes, IFileDialogService dialogs)
	{
		_service = service; _reasonCodes = reasonCodes; _dialogs = dialogs;
		StatusFilters = [new("All statuses", null), .. Enum.GetValues<MaterialIssueStatus>().Select(status => new MaterialIssueStatusFilter(status.ToString(), status))];
		_selectedStatusFilter = StatusFilters[0];
		NewIssueCommand = new RelayCommand(NewIssue, () => CanCreate);
		SaveCommand = new AsyncRelayCommand(SaveAsync, () => CanCreate && IsDraft && Lines.Count > 0);
		PostCommand = new AsyncRelayCommand(PostAsync, () => CanPost && Draft.Id > 0 && IsDraft);
		CancelCommand = new AsyncRelayCommand(CancelAsync, () => CanCreate && Draft.Id > 0 && IsDraft);
		ReverseCommand = new AsyncRelayCommand(ReverseAsync, () => CanReverse && Draft.Status == MaterialIssueStatus.Posted && SelectedReversalReasonCode is not null && !string.IsNullOrWhiteSpace(ReversalReason));
		AddLineCommand = new RelayCommand(AddOrUpdateLine, () => IsDraft && SelectedInventory is not null && SelectedReasonCode is not null && LineQuantity > 0);
		RemoveLineCommand = new RelayCommand(RemoveLine, () => IsDraft && SelectedLine is not null);
		PreviousPageCommand = new AsyncRelayCommand(PreviousPageAsync, () => PageNumber > 1);
		NextPageCommand = new AsyncRelayCommand(NextPageAsync, () => HasNextPage);
	}

	public ObservableCollection<MaterialIssueOverviewItem> Issues { get; } = new();
	public ObservableCollection<MaterialIssueLineEditor> Lines { get; } = new();
	public ObservableCollection<InventoryOverviewItem> InventoryOptions { get; } = new();
	public ObservableCollection<ReasonCode> ReasonCodes { get; } = new();
	public ObservableCollection<MovementOverviewItem> Movements { get; } = new();
	public IReadOnlyList<MaterialIssueStatusFilter> StatusFilters { get; }
	public RelayCommand NewIssueCommand { get; }
	public AsyncRelayCommand SaveCommand { get; }
	public AsyncRelayCommand PostCommand { get; }
	public AsyncRelayCommand CancelCommand { get; }
	public AsyncRelayCommand ReverseCommand { get; }
	public RelayCommand AddLineCommand { get; }
	public RelayCommand RemoveLineCommand { get; }
	public AsyncRelayCommand PreviousPageCommand { get; }
	public AsyncRelayCommand NextPageCommand { get; }

	public MaterialIssue Draft { get => _draft; private set { _draft = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDraft)); OnPropertyChanged(nameof(IsReadOnly)); OnPropertyChanged(nameof(CanReverse)); OnPropertyChanged(nameof(EditorTitle)); RaiseCommands(); } }
	public bool CanCreate => _service.CanCreate;
	public bool CanPost => _service.CanPost;
	public bool CanReverse => _service.CanReverse && Draft.Status == MaterialIssueStatus.Posted;
	public bool IsDraft => Draft.Status == MaterialIssueStatus.Draft && CanCreate;
	public bool IsReadOnly => !IsDraft;
	public bool HasMovements => Movements.Count > 0;
	public string EditorTitle => Draft.Id == 0 ? "New Material Issue" : Draft.IssueNumber;
	public bool HasNextPage => (long)PageNumber * PageSize < TotalCount;
	public string PageDisplay => $"Page {PageNumber} · {TotalCount:N0} issues";
	public string SaveLineText => SelectedLine is null ? "Add Line" : "Update Line";
	public MaterialIssueOverviewItem? SelectedIssue { get => _selectedIssue; set { if (_selectedIssue == value) return; _selectedIssue = value; OnPropertyChanged(); _selectionCancellation?.Cancel(); _selectionCancellation?.Dispose(); _selectionCancellation = new CancellationTokenSource(); _ = LoadSelectedAsync(value, _selectionCancellation.Token); } }
	public MaterialIssueLineEditor? SelectedLine { get => _selectedLine; set { if (_selectedLine == value) return; _selectedLine = value; OnPropertyChanged(); SelectedInventory = value is null ? null : InventoryOptions.FirstOrDefault(option => option.InventoryId == value.InventoryId); SelectedReasonCode = value is null ? null : ReasonCodes.FirstOrDefault(reason => reason.Id == value.ReasonCodeId); LineQuantity = value?.Quantity ?? 1; LineNotes = value?.Notes; OnPropertyChanged(nameof(SaveLineText)); RaiseCommands(); } }
	public InventoryOverviewItem? SelectedInventory { get => _selectedInventory; set { if (_selectedInventory == value) return; _selectedInventory = value; OnPropertyChanged(); AddLineCommand.RaiseCanExecuteChanged(); } }
	public ReasonCode? SelectedReasonCode { get => _selectedReasonCode; set { if (_selectedReasonCode == value) return; _selectedReasonCode = value; OnPropertyChanged(); AddLineCommand.RaiseCanExecuteChanged(); } }
	public ReasonCode? SelectedReversalReasonCode { get => _selectedReversalReasonCode; set { if (_selectedReversalReasonCode == value) return; _selectedReversalReasonCode = value; OnPropertyChanged(); ReverseCommand.RaiseCanExecuteChanged(); } }
	public string ReversalReason { get => _reversalReason; set { if (_reversalReason == value) return; _reversalReason = value; OnPropertyChanged(); ReverseCommand.RaiseCanExecuteChanged(); } }
	public int LineQuantity { get => _lineQuantity; set { if (_lineQuantity == value) return; _lineQuantity = value; OnPropertyChanged(); AddLineCommand.RaiseCanExecuteChanged(); } }
	public string? LineNotes { get => _lineNotes; set { if (_lineNotes == value) return; _lineNotes = value; OnPropertyChanged(); } }
	public string SearchText { get => _searchText; set { if (_searchText == value) return; _searchText = value; OnPropertyChanged(); PageNumber = 1; _ = _search.DebounceAsync(LoadPageAsync); } }
	public string InventorySearchText { get => _inventorySearchText; set { if (_inventorySearchText == value) return; _inventorySearchText = value; OnPropertyChanged(); _ = _inventorySearch.DebounceAsync(LoadInventoryOptionsAsync); } }
	public MaterialIssueStatusFilter SelectedStatusFilter { get => _selectedStatusFilter; set { if (_selectedStatusFilter == value) return; _selectedStatusFilter = value; OnPropertyChanged(); PageNumber = 1; _ = LoadPageAsync(); } }
	public int PageNumber { get => _pageNumber; private set { if (_pageNumber == value) return; _pageNumber = value; OnPropertyChanged(); OnPropertyChanged(nameof(PageDisplay)); OnPropertyChanged(nameof(HasNextPage)); RaiseCommands(); } }
	public long TotalCount { get => _totalCount; private set { if (_totalCount == value) return; _totalCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(PageDisplay)); OnPropertyChanged(nameof(HasNextPage)); RaiseCommands(); } }

	public async Task LoadAsync(CancellationToken cancellationToken = default)
	{
		BeginOperation("Material issues are loading");
		try
		{
			var reasonsTask = _reasonCodes.GetActiveAsync(cancellationToken); var inventoryTask = _service.SearchInventoryOptionsAsync(InventorySearchText, 1, 100, cancellationToken); var pageTask = _service.SearchAsync(SearchText, SelectedStatusFilter.Status, PageNumber, PageSize, cancellationToken);
			await Task.WhenAll(reasonsTask, inventoryTask, pageTask);
			Replace(ReasonCodes, await reasonsTask); Replace(InventoryOptions, (await inventoryTask).Items); ApplyPage(await pageTask);
			CompleteOperation(Issues.Count == 0, $"{TotalCount:N0} material issues");
		}
		catch (Exception exception) when (exception is not OperationCanceledException) { FailOperation(exception, "Material issues could not be loaded"); }
	}

	private async Task LoadPageAsync(CancellationToken cancellationToken = default) { try { ApplyPage(await _service.SearchAsync(SearchText, SelectedStatusFilter.Status, PageNumber, PageSize, cancellationToken)); } catch (Exception exception) when (exception is not OperationCanceledException) { FailOperation(exception, "Material issues could not be loaded"); } }
	private async Task LoadInventoryOptionsAsync(CancellationToken cancellationToken = default) { try { Replace(InventoryOptions, (await _service.SearchInventoryOptionsAsync(InventorySearchText, 1, 100, cancellationToken)).Items); } catch (Exception exception) when (exception is not OperationCanceledException) { FailOperation(exception, "Inventory options could not be loaded"); } }
	private async Task LoadSelectedAsync(MaterialIssueOverviewItem? overview, CancellationToken cancellationToken)
	{
		if (overview is null) { Draft = NewDraft(); Lines.Clear(); Movements.Clear(); OnPropertyChanged(nameof(HasMovements)); return; }
		BeginOperation("Material issue is loading");
		try
		{
			var details = await _service.GetByIdAsync(overview.Id, cancellationToken) ?? throw new InvalidOperationException("The material issue was not found.");
			Draft = Copy(details); Lines.Clear(); foreach (var line in details.Lines) Lines.Add(new MaterialIssueLineEditor(line));
			Replace(Movements, await _service.GetMovementsAsync(details.Id, cancellationToken)); OnPropertyChanged(nameof(HasMovements)); CompleteOperation(false, "Material issue loaded");
		}
		catch (Exception exception) when (exception is not OperationCanceledException) { FailOperation(exception, "Material issue could not be loaded"); }
	}

	private void NewIssue() { _selectedIssue = null; OnPropertyChanged(nameof(SelectedIssue)); Draft = NewDraft(); Lines.Clear(); Movements.Clear(); SelectedLine = null; OnPropertyChanged(nameof(HasMovements)); }
	private void AddOrUpdateLine()
	{
		if (SelectedInventory is null || SelectedReasonCode is null || LineQuantity <= 0) return;
		if (Lines.Any(line => line.InventoryId == SelectedInventory.InventoryId && line != SelectedLine)) { FailOperation(new InvalidOperationException("The inventory is already included."), "Line could not be added"); return; }
		var line = SelectedLine ?? new MaterialIssueLineEditor(); line.InventoryId = SelectedInventory.InventoryId; line.Quantity = LineQuantity; line.ReasonCodeId = SelectedReasonCode.Id; line.Notes = LineNotes; line.PartNumber = SelectedInventory.PartNumber; line.ItemDescription = SelectedInventory.Description; line.WarehouseName = SelectedInventory.WarehouseName; line.StorageLocationName = SelectedInventory.LocationName; line.PurposeName = SelectedInventory.PurposeName; line.ReasonCodeName = SelectedReasonCode.Name; line.CurrentStock = SelectedInventory.CurrentStock;
		if (SelectedLine is null) { line.LineNumber = Lines.Count + 1; Lines.Add(line); } SelectedLine = null; RaiseCommands();
	}
	private void RemoveLine() { if (SelectedLine is null) return; Lines.Remove(SelectedLine); for (var index = 0; index < Lines.Count; index++) Lines[index].LineNumber = index + 1; SelectedLine = null; RaiseCommands(); }
	private async Task SaveAsync(CancellationToken cancellationToken) => await ExecuteChangeAsync("Material issue is saving", "Material issue saved", () => _service.SaveDraftAsync(ToModel(), cancellationToken), cancellationToken);
	private async Task PostAsync(CancellationToken cancellationToken) { if (!_dialogs.Confirm(new ConfirmationDialogRequest("Post Material Issue", $"Post {Draft.IssueNumber} and withdraw all line quantities?", true))) return; await ExecuteChangeAsync("Stocks are being checked and the material issue is posting", "Material issue posted", () => _service.PostMaterialIssueAsync(Draft.Id, Draft.Version, cancellationToken), cancellationToken); }
	private async Task CancelAsync(CancellationToken cancellationToken) { if (!_dialogs.Confirm(new ConfirmationDialogRequest("Cancel Draft", $"Cancel draft {Draft.IssueNumber}?", true))) return; await ExecuteChangeAsync("Material issue is cancelling", "Draft cancelled", () => _service.CancelAsync(Draft.Id, Draft.Version, cancellationToken), cancellationToken); }
	private async Task ReverseAsync(CancellationToken cancellationToken) { if (SelectedReversalReasonCode is null || !_dialogs.Confirm(new ConfirmationDialogRequest("Reverse Material Issue", $"Create counter-movements for {Draft.IssueNumber}?", true))) return; await ExecuteChangeAsync("Material issue is reversing", "Material issue reversed", () => _service.ReverseAsync(Draft.Id, Draft.Version, SelectedReversalReasonCode.Id, ReversalReason, cancellationToken), cancellationToken); }
	private async Task ExecuteChangeAsync(string busy, string completed, Func<Task<MaterialIssue>> action, CancellationToken cancellationToken)
	{
		BeginOperation(busy);
		try { var changed = await action(); var details = await _service.GetByIdAsync(changed.Id, cancellationToken) ?? throw new InvalidOperationException("The material issue was not found after the operation."); var overview = await _service.GetOverviewByIdAsync(changed.Id, cancellationToken) ?? throw new InvalidOperationException("The material issue overview was not found."); ApplyOverview(overview); Draft = Copy(details); Lines.Clear(); foreach (var line in details.Lines) Lines.Add(new MaterialIssueLineEditor(line)); Replace(Movements, await _service.GetMovementsAsync(changed.Id, cancellationToken)); OnPropertyChanged(nameof(HasMovements)); ReversalReason = string.Empty; CompleteOperation(false, completed); }
		catch (ConcurrencyConflictException exception) { FailOperation(exception, "The material issue was changed by another user"); }
		catch (Exception exception) when (exception is not OperationCanceledException) { FailOperation(exception, "Material issue operation failed"); }
	}
	private MaterialIssue ToModel() { var result = Copy(Draft); result.Lines = Lines.Select(line => line.ToModel()).ToArray(); return result; }
	private void ApplyPage(PageResult<MaterialIssueOverviewItem> page) { var selectedId = SelectedIssue?.Id; Replace(Issues, page.Items); TotalCount = page.TotalCount; PageNumber = page.PageNumber; if (selectedId is not null) { _selectedIssue = Issues.FirstOrDefault(issue => issue.Id == selectedId); OnPropertyChanged(nameof(SelectedIssue)); } }
	private void ApplyOverview(MaterialIssueOverviewItem overview) { var existing = Issues.FirstOrDefault(issue => issue.Id == overview.Id); if (existing is null) Issues.Insert(0, overview); else Issues[Issues.IndexOf(existing)] = overview; _selectedIssue = overview; OnPropertyChanged(nameof(SelectedIssue)); }
	private async Task PreviousPageAsync(CancellationToken cancellationToken) { if (PageNumber <= 1) return; PageNumber--; await LoadPageAsync(cancellationToken); }
	private async Task NextPageAsync(CancellationToken cancellationToken) { if (!HasNextPage) return; PageNumber++; await LoadPageAsync(cancellationToken); }
	private void RaiseCommands() { SaveCommand.RaiseCanExecuteChanged(); PostCommand.RaiseCanExecuteChanged(); CancelCommand.RaiseCanExecuteChanged(); ReverseCommand.RaiseCanExecuteChanged(); AddLineCommand.RaiseCanExecuteChanged(); RemoveLineCommand.RaiseCanExecuteChanged(); PreviousPageCommand.RaiseCanExecuteChanged(); NextPageCommand.RaiseCanExecuteChanged(); }
	private static void Replace<T>(ObservableCollection<T> target, IReadOnlyList<T> values) { target.Clear(); foreach (var value in values) target.Add(value); }
	private static MaterialIssue NewDraft() => new() { IssueDate = DateTime.Today };
	private static MaterialIssue Copy(MaterialIssue value) => new() { Id = value.Id, IssueNumber = value.IssueNumber, IssueDate = value.IssueDate, Status = value.Status, Recipient = value.Recipient, Reference = value.Reference, Notes = value.Notes, CreatedByUserId = value.CreatedByUserId, PostedByUserId = value.PostedByUserId, PostedAtUtc = value.PostedAtUtc, ReversedByUserId = value.ReversedByUserId, ReversedAtUtc = value.ReversedAtUtc, ReversalReason = value.ReversalReason, Version = value.Version, Lines = value.Lines };
	public void Dispose() { _search.Dispose(); _inventorySearch.Dispose(); _selectionCancellation?.Cancel(); _selectionCancellation?.Dispose(); SaveCommand.Dispose(); PostCommand.Dispose(); CancelCommand.Dispose(); ReverseCommand.Dispose(); PreviousPageCommand.Dispose(); NextPageCommand.Dispose(); }
}

public sealed record MaterialIssueStatusFilter(string Name, MaterialIssueStatus? Status);

public sealed class MaterialIssueLineEditor
{
	public MaterialIssueLineEditor() { }
	public MaterialIssueLineEditor(MaterialIssueLine line) { Id = line.Id; LineNumber = line.LineNumber; InventoryId = line.InventoryId; Quantity = line.Quantity; ReasonCodeId = line.ReasonCodeId; Notes = line.Notes; Version = line.Version; PartNumber = line.PartNumber; ItemDescription = line.ItemDescription; WarehouseName = line.WarehouseName; StorageLocationName = line.StorageLocationName; PurposeName = line.PurposeName; ReasonCodeName = line.ReasonCodeName; CurrentStock = line.CurrentStock; }
	public long Id { get; set; }
	public int LineNumber { get; set; }
	public long InventoryId { get; set; }
	public int Quantity { get; set; }
	public long ReasonCodeId { get; set; }
	public string? Notes { get; set; }
	public long Version { get; set; } = 1;
	public string PartNumber { get; set; } = string.Empty;
	public string ItemDescription { get; set; } = string.Empty;
	public string WarehouseName { get; set; } = string.Empty;
	public string StorageLocationName { get; set; } = string.Empty;
	public string PurposeName { get; set; } = string.Empty;
	public string ReasonCodeName { get; set; } = string.Empty;
	public long CurrentStock { get; set; }
	public long RemainingStock => CurrentStock - Quantity;
	public MaterialIssueLine ToModel() => new() { Id = Id, LineNumber = LineNumber, InventoryId = InventoryId, Quantity = Quantity, ReasonCodeId = ReasonCodeId, Notes = Notes, Version = Version, PartNumber = PartNumber, ItemDescription = ItemDescription, WarehouseName = WarehouseName, StorageLocationName = StorageLocationName, PurposeName = PurposeName, ReasonCodeName = ReasonCodeName };
}
