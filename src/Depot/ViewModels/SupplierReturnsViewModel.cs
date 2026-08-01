// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Commands;
using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels;

public sealed class SupplierReturnsViewModel : BaseViewModel, IDisposable
{
    private const int PageSize = 50;
    private readonly SupplierReturnService _service;
    private readonly SupplierService _suppliers;
    private readonly ReasonCodeService _reasonCodes;
    private readonly IFileDialogService _dialogs;
    private readonly AsyncDebouncer _search = new(TimeSpan.FromMilliseconds(300));
    private readonly AsyncDebouncer _receiptSearch = new(TimeSpan.FromMilliseconds(300));
    private CancellationTokenSource? _selectionCancellation;
    private SupplierReturnOverviewItem? _selectedReturn;
    private SupplierReturn _draft = NewDraft();
    private SupplierReturnReceiptOption? _selectedReceipt;
    private SupplierReturnableLine? _selectedAvailableLine;
    private SupplierReturnLineEditor? _selectedLine;
    private ReasonCode? _selectedReasonCode;
    private ReasonCode? _selectedReversalReasonCode;
    private SupplierReturnSupplierFilter _selectedSupplierFilter;
    private SupplierReturnStatusFilter _selectedStatusFilter;
    private string _searchText = string.Empty;
    private string _receiptSearchText = string.Empty;
    private string _reversalReason = string.Empty;
    private int _lineQuantity = 1;
    private int _pageNumber = 1;
    private long _totalCount;

    public SupplierReturnsViewModel(SupplierReturnService service, SupplierService suppliers, ReasonCodeService reasonCodes, IFileDialogService dialogs)
    {
        _service = service;
        _suppliers = suppliers;
        _reasonCodes = reasonCodes;
        _dialogs = dialogs;
        SupplierFilters = [new("All suppliers", null)];
        _selectedSupplierFilter = SupplierFilters[0];
        StatusFilters = [new("All statuses", null), .. Enum.GetValues<SupplierReturnStatus>().Select(status => new SupplierReturnStatusFilter(status.ToString(), status))];
        _selectedStatusFilter = StatusFilters[0];
        NewReturnCommand = new RelayCommand(NewReturn);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => IsDraft && Lines.Count > 0);
        PostCommand = new AsyncRelayCommand(PostAsync, () => Draft.Id > 0 && IsDraft);
        CancelCommand = new AsyncRelayCommand(CancelAsync, () => Draft.Id > 0 && IsDraft);
        ReverseCommand = new AsyncRelayCommand(ReverseAsync, () => CanReverse && SelectedReversalReasonCode is not null && !string.IsNullOrWhiteSpace(ReversalReason));
        AddLineCommand = new RelayCommand(AddOrUpdateLine, () => IsDraft && SelectedAvailableLine is not null && SelectedReasonCode is not null && LineQuantity > 0);
        RemoveLineCommand = new RelayCommand(RemoveLine, () => IsDraft && SelectedLine is not null);
        PreviousPageCommand = new AsyncRelayCommand(PreviousPageAsync, () => PageNumber > 1);
        NextPageCommand = new AsyncRelayCommand(NextPageAsync, () => HasNextPage);
    }

    public ObservableCollection<SupplierReturnOverviewItem> Returns { get; } = new();
    public ObservableCollection<SupplierReturnLineEditor> Lines { get; } = new();
    public ObservableCollection<SupplierReturnReceiptOption> ReceiptOptions { get; } = new();
    public ObservableCollection<SupplierReturnableLine> ReturnableLines { get; } = new();
    public ObservableCollection<ReasonCode> ReasonCodes { get; } = new();
    public ObservableCollection<MovementOverviewItem> Movements { get; } = new();
    public ObservableCollection<SupplierReturnSupplierFilter> SupplierFilters { get; }
    public IReadOnlyList<SupplierReturnStatusFilter> StatusFilters { get; }
    public RelayCommand NewReturnCommand { get; }
    public AsyncRelayCommand SaveCommand { get; }
    public AsyncRelayCommand PostCommand { get; }
    public AsyncRelayCommand CancelCommand { get; }
    public AsyncRelayCommand ReverseCommand { get; }
    public RelayCommand AddLineCommand { get; }
    public RelayCommand RemoveLineCommand { get; }
    public AsyncRelayCommand PreviousPageCommand { get; }
    public AsyncRelayCommand NextPageCommand { get; }

    public SupplierReturn Draft { get => _draft; private set { _draft = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDraft)); OnPropertyChanged(nameof(IsReadOnly)); OnPropertyChanged(nameof(CanReverse)); OnPropertyChanged(nameof(EditorTitle)); RaiseCommands(); } }
    public bool IsDraft => Draft.Status == SupplierReturnStatus.Draft;
    public bool IsReadOnly => !IsDraft;
    public bool HasMovements => Movements.Count > 0;
    public bool CanReverse => Draft.Status == SupplierReturnStatus.Posted && !Draft.IsReversed && Movements.Where(movement => movement.MovementType == StockMovementType.SupplierReturn).Any() && Movements.Where(movement => movement.MovementType == StockMovementType.SupplierReturn).All(movement => !movement.IsReversed);
    public string EditorTitle => Draft.Id == 0 ? "New Supplier Return" : Draft.ReturnNumber;
    public string SaveLineText => SelectedLine is null ? "Add Line" : "Update Line";
    public bool HasNextPage => (long)PageNumber * PageSize < TotalCount;
    public string PageDisplay => $"Page {PageNumber} · {TotalCount:N0} returns";
    public SupplierReturnOverviewItem? SelectedReturn { get => _selectedReturn; set { if (_selectedReturn == value) return; _selectedReturn = value; OnPropertyChanged(); _selectionCancellation?.Cancel(); _selectionCancellation?.Dispose(); _selectionCancellation = new CancellationTokenSource(); _ = LoadSelectedAsync(value, _selectionCancellation.Token); } }
    public SupplierReturnReceiptOption? SelectedReceipt { get => _selectedReceipt; set { if (_selectedReceipt == value) return; _selectedReceipt = value; OnPropertyChanged(); _ = ApplyReceiptAsync(value); } }
    public SupplierReturnableLine? SelectedAvailableLine { get => _selectedAvailableLine; set { if (_selectedAvailableLine == value) return; _selectedAvailableLine = value; OnPropertyChanged(); LineQuantity = Math.Max(1, Math.Min(value?.ReturnableQuantity ?? 1, value is null ? 1 : (int)Math.Min(value.AvailableStock, int.MaxValue))); AddLineCommand.RaiseCanExecuteChanged(); } }
    public SupplierReturnLineEditor? SelectedLine { get => _selectedLine; set { if (_selectedLine == value) return; _selectedLine = value; OnPropertyChanged(); _selectedAvailableLine = value is null ? null : ReturnableLines.FirstOrDefault(option => option.GoodsReceiptLineId == value.GoodsReceiptLineId); OnPropertyChanged(nameof(SelectedAvailableLine)); SelectedReasonCode = value is null ? null : ReasonCodes.FirstOrDefault(reason => reason.Id == value.ReasonCodeId); LineQuantity = value?.Quantity ?? 1; OnPropertyChanged(nameof(SaveLineText)); RaiseCommands(); } }
    public ReasonCode? SelectedReasonCode { get => _selectedReasonCode; set { if (_selectedReasonCode == value) return; _selectedReasonCode = value; OnPropertyChanged(); AddLineCommand.RaiseCanExecuteChanged(); } }
    public ReasonCode? SelectedReversalReasonCode { get => _selectedReversalReasonCode; set { if (_selectedReversalReasonCode == value) return; _selectedReversalReasonCode = value; OnPropertyChanged(); ReverseCommand.RaiseCanExecuteChanged(); } }
    public string ReversalReason { get => _reversalReason; set { if (_reversalReason == value) return; _reversalReason = value; OnPropertyChanged(); ReverseCommand.RaiseCanExecuteChanged(); } }
    public int LineQuantity { get => _lineQuantity; set { if (_lineQuantity == value) return; _lineQuantity = value; OnPropertyChanged(); AddLineCommand.RaiseCanExecuteChanged(); } }
    public string SearchText { get => _searchText; set { if (_searchText == value) return; _searchText = value; OnPropertyChanged(); PageNumber = 1; _ = _search.DebounceAsync(LoadPageAsync); } }
    public string ReceiptSearchText { get => _receiptSearchText; set { if (_receiptSearchText == value) return; _receiptSearchText = value; OnPropertyChanged(); _ = _receiptSearch.DebounceAsync(LoadReceiptOptionsAsync); } }
    public SupplierReturnSupplierFilter SelectedSupplierFilter { get => _selectedSupplierFilter; set { if (_selectedSupplierFilter == value) return; _selectedSupplierFilter = value; OnPropertyChanged(); PageNumber = 1; _ = LoadPageAsync(); _ = LoadReceiptOptionsAsync(); } }
    public SupplierReturnStatusFilter SelectedStatusFilter { get => _selectedStatusFilter; set { if (_selectedStatusFilter == value) return; _selectedStatusFilter = value; OnPropertyChanged(); PageNumber = 1; _ = LoadPageAsync(); } }
    public int PageNumber { get => _pageNumber; private set { if (_pageNumber == value) return; _pageNumber = value; OnPropertyChanged(); OnPropertyChanged(nameof(PageDisplay)); OnPropertyChanged(nameof(HasNextPage)); RaiseCommands(); } }
    public long TotalCount { get => _totalCount; private set { if (_totalCount == value) return; _totalCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(PageDisplay)); OnPropertyChanged(nameof(HasNextPage)); RaiseCommands(); } }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        BeginOperation("Supplier returns are loading");
        try
        {
            var suppliersTask = _suppliers.GetActiveAsync(cancellationToken);
            var reasonsTask = _reasonCodes.GetActiveAsync(cancellationToken);
            var receiptsTask = _service.SearchReceiptOptionsAsync(ReceiptSearchText, SelectedSupplierFilter.SupplierId, cancellationToken);
            var pageTask = _service.SearchAsync(SearchText, SelectedSupplierFilter.SupplierId, SelectedStatusFilter.Status, PageNumber, PageSize, cancellationToken);
            await Task.WhenAll(suppliersTask, reasonsTask, receiptsTask, pageTask);
            SupplierFilters.Clear(); SupplierFilters.Add(new("All suppliers", null)); foreach (var supplier in await suppliersTask) SupplierFilters.Add(new(supplier.Name, supplier.Id));
            Replace(ReasonCodes, await reasonsTask); Replace(ReceiptOptions, await receiptsTask); ApplyPage(await pageTask);
            CompleteOperation(Returns.Count == 0, $"{TotalCount:N0} supplier returns");
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { FailOperation(exception, "Supplier returns could not be loaded"); }
    }

    private async Task LoadPageAsync(CancellationToken token = default) { try { ApplyPage(await _service.SearchAsync(SearchText, SelectedSupplierFilter.SupplierId, SelectedStatusFilter.Status, PageNumber, PageSize, token)); } catch (Exception exception) when (exception is not OperationCanceledException) { FailOperation(exception, "Supplier returns could not be loaded"); } }
    private async Task LoadReceiptOptionsAsync(CancellationToken token = default) { try { Replace(ReceiptOptions, await _service.SearchReceiptOptionsAsync(ReceiptSearchText, SelectedSupplierFilter.SupplierId, token)); } catch (Exception exception) when (exception is not OperationCanceledException) { FailOperation(exception, "Goods receipts could not be loaded"); } }
    private async Task ApplyReceiptAsync(SupplierReturnReceiptOption? receipt)
    {
        if (receipt is null || !IsDraft) return;
        Draft.SupplierId = receipt.SupplierId; Draft.SupplierName = receipt.SupplierName; Draft.PurchaseOrderId = receipt.PurchaseOrderId; Draft.PurchaseOrderNumber = receipt.PurchaseOrderNumber; Draft.GoodsReceiptId = receipt.GoodsReceiptId; Draft.GoodsReceiptNumber = receipt.ReceiptNumber;
        OnPropertyChanged(nameof(Draft));
        try { Replace(ReturnableLines, await _service.GetReturnableLinesAsync(receipt.GoodsReceiptId)); Lines.Clear(); SelectedLine = null; RaiseCommands(); } catch (Exception exception) { FailOperation(exception, "Returnable receipt lines could not be loaded"); }
    }
    private async Task LoadSelectedAsync(SupplierReturnOverviewItem? overview, CancellationToken token)
    {
        if (overview is null) { Draft = NewDraft(); Lines.Clear(); Movements.Clear(); ReturnableLines.Clear(); _selectedReceipt = null; OnPropertyChanged(nameof(SelectedReceipt)); NotifyMovementState(); return; }
        BeginOperation("Supplier return is loading");
        try
        {
            var details = await _service.GetByIdAsync(overview.Id, token) ?? throw new InvalidOperationException("The supplier return was not found.");
            Draft = Copy(details); ReplaceLines(details.Lines); Replace(ReturnableLines, await _service.GetReturnableLinesAsync(details.GoodsReceiptId, token)); _selectedReceipt = ReceiptOptions.FirstOrDefault(value => value.GoodsReceiptId == details.GoodsReceiptId); OnPropertyChanged(nameof(SelectedReceipt)); Replace(Movements, await _service.GetMovementsAsync(details.Id, token)); NotifyMovementState(); CompleteOperation(false, "Supplier return loaded");
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { FailOperation(exception, "Supplier return could not be loaded"); }
    }

    private void NewReturn() { _selectedReturn = null; OnPropertyChanged(nameof(SelectedReturn)); Draft = NewDraft(); Lines.Clear(); Movements.Clear(); ReturnableLines.Clear(); _selectedReceipt = null; OnPropertyChanged(nameof(SelectedReceipt)); SelectedLine = null; NotifyMovementState(); }
    private void AddOrUpdateLine()
    {
        if (SelectedAvailableLine is null || SelectedReasonCode is null || LineQuantity <= 0) return;
        if (LineQuantity > SelectedAvailableLine.ReturnableQuantity || LineQuantity > SelectedAvailableLine.AvailableStock) { FailOperation(new InvalidOperationException("The quantity exceeds the returnable receipt quantity or available stock."), "Line could not be added"); return; }
        if (Lines.Any(line => line.GoodsReceiptLineId == SelectedAvailableLine.GoodsReceiptLineId && line != SelectedLine)) { FailOperation(new InvalidOperationException("The receipt line is already included."), "Line could not be added"); return; }
        var line = SelectedLine ?? new SupplierReturnLineEditor(); line.Apply(SelectedAvailableLine, SelectedReasonCode, LineQuantity); if (SelectedLine is null) Lines.Add(line); SelectedLine = null; RaiseCommands();
    }
    private void RemoveLine() { if (SelectedLine is null) return; Lines.Remove(SelectedLine); SelectedLine = null; RaiseCommands(); }
    private async Task SaveAsync(CancellationToken token) => await ExecuteChangeAsync("Supplier return is saving", "Supplier return saved", () => _service.SaveDraftAsync(ToModel(), token), token);
    private async Task PostAsync(CancellationToken token) { if (!_dialogs.Confirm(new ConfirmationDialogRequest("Post Supplier Return", $"Post {Draft.ReturnNumber} and remove the selected quantities from stock?", true))) return; await ExecuteChangeAsync("Stock is being checked and the supplier return is posting", "Supplier return posted", () => _service.PostAsync(Draft.Id, Draft.Version, token), token); }
    private async Task CancelAsync(CancellationToken token) { if (!_dialogs.Confirm(new ConfirmationDialogRequest("Cancel Draft", $"Cancel draft {Draft.ReturnNumber}?", true))) return; await ExecuteChangeAsync("Supplier return is cancelling", "Draft cancelled", () => _service.CancelAsync(Draft.Id, Draft.Version, token), token); }
    private async Task ReverseAsync(CancellationToken token)
    {
        if (SelectedReversalReasonCode is null || !_dialogs.Confirm(new ConfirmationDialogRequest("Reverse Supplier Return", $"Create counter-movements for {Draft.ReturnNumber}?", true))) return;
        BeginOperation("Supplier return is being reversed");
        try { await _service.ReverseAsync(Draft.Id, Draft.Version, SelectedReversalReasonCode.Id, ReversalReason, token); var details = await _service.GetByIdAsync(Draft.Id, token) ?? throw new InvalidOperationException("The supplier return was not found after reversal."); var overview = await _service.GetOverviewByIdAsync(Draft.Id, token) ?? throw new InvalidOperationException("The supplier return overview was not found after reversal."); ApplyOverview(overview); Draft = Copy(details); Replace(Movements, await _service.GetMovementsAsync(Draft.Id, token)); ReversalReason = string.Empty; NotifyMovementState(); CompleteOperation(false, "Supplier return reversed by counter-booking"); }
        catch (ConcurrencyConflictException exception) { FailOperation(exception, "The supplier return was changed by another user"); }
        catch (Exception exception) when (exception is not OperationCanceledException) { FailOperation(exception, "Supplier return could not be reversed"); }
    }
    private async Task ExecuteChangeAsync(string busy, string complete, Func<Task<SupplierReturn>> action, CancellationToken token)
    {
        BeginOperation(busy);
        try { var changed = await action(); var details = await _service.GetByIdAsync(changed.Id, token) ?? throw new InvalidOperationException("The supplier return was not found after the operation."); var overview = await _service.GetOverviewByIdAsync(changed.Id, token) ?? throw new InvalidOperationException("The supplier return overview was not found."); ApplyOverview(overview); Draft = Copy(details); ReplaceLines(details.Lines); Replace(ReturnableLines, await _service.GetReturnableLinesAsync(details.GoodsReceiptId, token)); Replace(Movements, await _service.GetMovementsAsync(details.Id, token)); NotifyMovementState(); CompleteOperation(false, complete); }
        catch (ConcurrencyConflictException exception) { FailOperation(exception, "The supplier return was changed by another user"); }
        catch (Exception exception) when (exception is not OperationCanceledException) { FailOperation(exception, "Supplier return operation failed"); }
    }
    private SupplierReturn ToModel() { var value = Copy(Draft); value.Lines = Lines.Select(line => line.ToModel()).ToArray(); return value; }
    private void ReplaceLines(IReadOnlyList<SupplierReturnLine> values) { Lines.Clear(); foreach (var value in values) Lines.Add(new(value)); }
    private void ApplyPage(PageResult<SupplierReturnOverviewItem> page) { var id = SelectedReturn?.Id; Replace(Returns, page.Items); TotalCount = page.TotalCount; PageNumber = page.PageNumber; if (id is not null) { _selectedReturn = Returns.FirstOrDefault(value => value.Id == id); OnPropertyChanged(nameof(SelectedReturn)); } }
    private void ApplyOverview(SupplierReturnOverviewItem overview)
    {
        var existing = Returns.FirstOrDefault(value => value.Id == overview.Id);
        if (!MatchesCurrentFilter(overview))
        {
            if (existing is not null) Returns.Remove(existing);
            if (existing is not null) TotalCount = Math.Max(0, TotalCount - 1);
            _selectedReturn = null;
            OnPropertyChanged(nameof(SelectedReturn));
            return;
        }
        if (existing is null) Returns.Insert(0, overview); else Returns[Returns.IndexOf(existing)] = overview;
        _selectedReturn = overview;
        OnPropertyChanged(nameof(SelectedReturn));
    }
    private bool MatchesCurrentFilter(SupplierReturnOverviewItem value)
    {
        if (SelectedSupplierFilter.SupplierId is long supplierId && value.SupplierId != supplierId) return false;
        if (SelectedStatusFilter.Status is SupplierReturnStatus status && value.Status != status) return false;
        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        var search = SearchText.Trim();
        return value.ReturnNumber.Contains(search, StringComparison.CurrentCultureIgnoreCase)
            || value.SupplierName.Contains(search, StringComparison.CurrentCultureIgnoreCase)
            || value.PurchaseOrderNumber.Contains(search, StringComparison.CurrentCultureIgnoreCase)
            || value.GoodsReceiptNumber.Contains(search, StringComparison.CurrentCultureIgnoreCase)
            || (value.SupplierReference?.Contains(search, StringComparison.CurrentCultureIgnoreCase) ?? false);
    }
    private async Task PreviousPageAsync(CancellationToken token) { if (PageNumber > 1) { PageNumber--; await LoadPageAsync(token); } }
    private async Task NextPageAsync(CancellationToken token) { if (HasNextPage) { PageNumber++; await LoadPageAsync(token); } }
    private void NotifyMovementState() { OnPropertyChanged(nameof(HasMovements)); OnPropertyChanged(nameof(CanReverse)); RaiseCommands(); }
    private void RaiseCommands() { SaveCommand.RaiseCanExecuteChanged(); PostCommand.RaiseCanExecuteChanged(); CancelCommand.RaiseCanExecuteChanged(); ReverseCommand.RaiseCanExecuteChanged(); AddLineCommand.RaiseCanExecuteChanged(); RemoveLineCommand.RaiseCanExecuteChanged(); PreviousPageCommand.RaiseCanExecuteChanged(); NextPageCommand.RaiseCanExecuteChanged(); }
    private static void Replace<T>(ObservableCollection<T> target, IReadOnlyList<T> values) { target.Clear(); foreach (var value in values) target.Add(value); }
    private static SupplierReturn NewDraft() => new() { ReturnDate = DateTime.Today };
    private static SupplierReturn Copy(SupplierReturn value) => new() { Id = value.Id, ReturnNumber = value.ReturnNumber, SupplierId = value.SupplierId, SupplierName = value.SupplierName, ReturnDate = value.ReturnDate, Status = value.Status, PurchaseOrderId = value.PurchaseOrderId, PurchaseOrderNumber = value.PurchaseOrderNumber, GoodsReceiptId = value.GoodsReceiptId, GoodsReceiptNumber = value.GoodsReceiptNumber, SupplierReference = value.SupplierReference, Notes = value.Notes, CreatedByUserId = value.CreatedByUserId, PostedByUserId = value.PostedByUserId, PostedAtUtc = value.PostedAtUtc, ReversedByUserId = value.ReversedByUserId, ReversedAtUtc = value.ReversedAtUtc, ReversalReason = value.ReversalReason, Version = value.Version, Lines = value.Lines };
    public void Dispose() { _search.Dispose(); _receiptSearch.Dispose(); _selectionCancellation?.Cancel(); _selectionCancellation?.Dispose(); SaveCommand.Dispose(); PostCommand.Dispose(); CancelCommand.Dispose(); ReverseCommand.Dispose(); PreviousPageCommand.Dispose(); NextPageCommand.Dispose(); }
}

public sealed record SupplierReturnStatusFilter(string Name, SupplierReturnStatus? Status);
public sealed record SupplierReturnSupplierFilter(string Name, long? SupplierId);

public sealed class SupplierReturnLineEditor
{
    public SupplierReturnLineEditor() { }
    public SupplierReturnLineEditor(SupplierReturnLine line) { Id = line.Id; InventoryId = line.InventoryId; ItemId = line.ItemId; Quantity = line.Quantity; UnitCost = line.UnitCost; ReasonCodeId = line.ReasonCodeId; GoodsReceiptLineId = line.GoodsReceiptLineId; Version = line.Version; PartNumber = line.PartNumber; ItemDescription = line.ItemDescription; InventoryDisplay = line.InventoryDisplay; ReasonCodeName = line.ReasonCodeName; ReceivedQuantity = line.ReceivedQuantity; AlreadyReturnedQuantity = line.AlreadyReturnedQuantity; AvailableStock = line.AvailableStock; }
    public long Id { get; set; }
    public long InventoryId { get; set; }
    public long ItemId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public long ReasonCodeId { get; set; }
    public long GoodsReceiptLineId { get; set; }
    public long Version { get; set; } = 1;
    public string PartNumber { get; set; } = string.Empty;
    public string ItemDescription { get; set; } = string.Empty;
    public string InventoryDisplay { get; set; } = string.Empty;
    public string ReasonCodeName { get; set; } = string.Empty;
    public int ReceivedQuantity { get; set; }
    public int AlreadyReturnedQuantity { get; set; }
    public long AvailableStock { get; set; }
    public int RemainingAfterReturn => ReceivedQuantity - AlreadyReturnedQuantity - Quantity;
    public void Apply(SupplierReturnableLine source, ReasonCode reason, int quantity) { InventoryId = source.InventoryId; ItemId = source.ItemId; Quantity = quantity; UnitCost = source.UnitCost; ReasonCodeId = reason.Id; GoodsReceiptLineId = source.GoodsReceiptLineId; PartNumber = source.PartNumber; ItemDescription = source.ItemDescription; InventoryDisplay = source.InventoryDisplay; ReasonCodeName = reason.Name; ReceivedQuantity = source.ReceivedQuantity; AlreadyReturnedQuantity = source.AlreadyReturnedQuantity; AvailableStock = source.AvailableStock; }
    public SupplierReturnLine ToModel() => new() { Id = Id, InventoryId = InventoryId, ItemId = ItemId, Quantity = Quantity, UnitCost = UnitCost, ReasonCodeId = ReasonCodeId, GoodsReceiptLineId = GoodsReceiptLineId, Version = Version, PartNumber = PartNumber, ItemDescription = ItemDescription, InventoryDisplay = InventoryDisplay, ReasonCodeName = ReasonCodeName, ReceivedQuantity = ReceivedQuantity, AlreadyReturnedQuantity = AlreadyReturnedQuantity, AvailableStock = AvailableStock };
}
