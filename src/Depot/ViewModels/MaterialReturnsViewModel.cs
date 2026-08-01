// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Commands;
using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels;

public sealed class MaterialReturnsViewModel : BaseViewModel, IDisposable
{
	private const int PageSize = 50;
	private readonly MaterialReturnService _service;
	private readonly ReasonCodeService _reasonCodes;
	private readonly IFileDialogService _dialogs;
	private readonly AsyncDebouncer _search = new(TimeSpan.FromMilliseconds(300));
	private readonly AsyncDebouncer _inventorySearch = new(TimeSpan.FromMilliseconds(300));
	private CancellationTokenSource? _selectionCancellation;
	private MaterialReturnOverviewItem? _selectedReturn;
	private MaterialReturn _draft = NewDraft();
	private MaterialReturnLineEditor? _selectedLine;
	private InventoryOverviewItem? _selectedInventory;
	private ReasonCode? _selectedReasonCode;
	private MaterialIssueOverviewItem? _selectedOriginalIssue;
	private ReasonCode? _selectedCorrectionReasonCode;
	private string _correctionReason = string.Empty;
	private string _searchText = string.Empty;
	private string _inventorySearchText = string.Empty;
	private MaterialReturnStatusFilter _selectedStatusFilter;
	private int _lineQuantity = 1;
	private string? _lineNotes;
	private int _pageNumber = 1;
	private long _totalCount;

	public MaterialReturnsViewModel(MaterialReturnService service, ReasonCodeService reasonCodes, IFileDialogService dialogs)
	{
		_service = service; _reasonCodes = reasonCodes; _dialogs = dialogs;
		StatusFilters = [new("All statuses", null), .. Enum.GetValues<MaterialReturnStatus>().Select(status => new MaterialReturnStatusFilter(status.ToString(), status))]; _selectedStatusFilter = StatusFilters[0];
		NewReturnCommand = new RelayCommand(NewReturn, () => CanCreate); SaveCommand = new AsyncRelayCommand(SaveAsync, () => CanCreate && IsDraft && Lines.Count > 0); PostCommand = new AsyncRelayCommand(PostAsync, () => CanPost && Draft.Id > 0 && IsDraft); CancelCommand = new AsyncRelayCommand(CancelAsync, () => CanCreate && Draft.Id > 0 && IsDraft); CorrectCommand = new AsyncRelayCommand(CorrectAsync, () => CanCorrect && SelectedCorrectionReasonCode is not null && !string.IsNullOrWhiteSpace(CorrectionReason)); AddLineCommand = new RelayCommand(AddOrUpdateLine, () => IsDraft && SelectedInventory is not null && SelectedReasonCode is not null && LineQuantity > 0); RemoveLineCommand = new RelayCommand(RemoveLine, () => IsDraft && SelectedLine is not null); PreviousPageCommand = new AsyncRelayCommand(PreviousPageAsync, () => PageNumber > 1); NextPageCommand = new AsyncRelayCommand(NextPageAsync, () => HasNextPage);
	}

	public ObservableCollection<MaterialReturnOverviewItem> Returns { get; } = new();
	public ObservableCollection<MaterialReturnLineEditor> Lines { get; } = new();
	public ObservableCollection<InventoryOverviewItem> InventoryOptions { get; } = new();
	public ObservableCollection<ReasonCode> ReasonCodes { get; } = new();
	public ObservableCollection<MaterialIssueOverviewItem> OriginalIssues { get; } = new();
	public ObservableCollection<MovementOverviewItem> Movements { get; } = new();
	public IReadOnlyList<MaterialReturnStatusFilter> StatusFilters { get; }
	public RelayCommand NewReturnCommand { get; }
	public AsyncRelayCommand SaveCommand { get; }
	public AsyncRelayCommand PostCommand { get; }
	public AsyncRelayCommand CancelCommand { get; }
	public AsyncRelayCommand CorrectCommand { get; }
	public RelayCommand AddLineCommand { get; }
	public RelayCommand RemoveLineCommand { get; }
	public AsyncRelayCommand PreviousPageCommand { get; }
	public AsyncRelayCommand NextPageCommand { get; }

	public MaterialReturn Draft { get => _draft; private set { _draft = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDraft)); OnPropertyChanged(nameof(IsReadOnly)); OnPropertyChanged(nameof(CanCorrect)); OnPropertyChanged(nameof(EditorTitle)); RaiseCommands(); } }
	public bool CanCreate => _service.CanCreate;
	public bool CanPost => _service.CanPost;
	public bool IsDraft => Draft.Status == MaterialReturnStatus.Draft && CanCreate;
	public bool IsReadOnly => !IsDraft;
	public bool HasMovements => Movements.Count > 0;
	public bool CanCorrect => CanPost && Draft.Status == MaterialReturnStatus.Posted && Movements.Any(movement => movement.MovementType == StockMovementType.MaterialReturn) && Movements.Where(movement => movement.MovementType == StockMovementType.MaterialReturn).All(movement => !movement.IsReversed);
	public string EditorTitle => Draft.Id == 0 ? "New Material Return" : Draft.ReturnNumber;
	public string SaveLineText => SelectedLine is null ? "Add Line" : "Update Line";
	public bool HasNextPage => (long)PageNumber * PageSize < TotalCount;
	public string PageDisplay => $"Page {PageNumber} · {TotalCount:N0} returns";
	public MaterialReturnOverviewItem? SelectedReturn { get => _selectedReturn; set { if (_selectedReturn == value) return; _selectedReturn = value; OnPropertyChanged(); _selectionCancellation?.Cancel(); _selectionCancellation?.Dispose(); _selectionCancellation = new CancellationTokenSource(); _ = LoadSelectedAsync(value, _selectionCancellation.Token); } }
	public MaterialReturnLineEditor? SelectedLine { get => _selectedLine; set { if (_selectedLine == value) return; _selectedLine = value; OnPropertyChanged(); SelectedInventory = value is null ? null : InventoryOptions.FirstOrDefault(option => option.InventoryId == value.InventoryId); SelectedReasonCode = value is null ? null : ReasonCodes.FirstOrDefault(reason => reason.Id == value.ReasonCodeId); LineQuantity = value?.Quantity ?? 1; LineNotes = value?.Notes; OnPropertyChanged(nameof(SaveLineText)); RaiseCommands(); } }
	public InventoryOverviewItem? SelectedInventory { get => _selectedInventory; set { if (_selectedInventory == value) return; _selectedInventory = value; OnPropertyChanged(); AddLineCommand.RaiseCanExecuteChanged(); } }
	public ReasonCode? SelectedReasonCode { get => _selectedReasonCode; set { if (_selectedReasonCode == value) return; _selectedReasonCode = value; OnPropertyChanged(); AddLineCommand.RaiseCanExecuteChanged(); } }
	public MaterialIssueOverviewItem? SelectedOriginalIssue { get => _selectedOriginalIssue; set { if (_selectedOriginalIssue == value) return; _selectedOriginalIssue = value; Draft.OriginalMaterialIssueId = value?.Id; Draft.OriginalMaterialIssueNumber = value?.IssueNumber; OnPropertyChanged(); } }
	public ReasonCode? SelectedCorrectionReasonCode { get => _selectedCorrectionReasonCode; set { if (_selectedCorrectionReasonCode == value) return; _selectedCorrectionReasonCode = value; OnPropertyChanged(); CorrectCommand.RaiseCanExecuteChanged(); } }
	public string CorrectionReason { get => _correctionReason; set { if (_correctionReason == value) return; _correctionReason = value; OnPropertyChanged(); CorrectCommand.RaiseCanExecuteChanged(); } }
	public int LineQuantity { get => _lineQuantity; set { if (_lineQuantity == value) return; _lineQuantity = value; OnPropertyChanged(); AddLineCommand.RaiseCanExecuteChanged(); } }
	public string? LineNotes { get => _lineNotes; set { if (_lineNotes == value) return; _lineNotes = value; OnPropertyChanged(); } }
	public string SearchText { get => _searchText; set { if (_searchText == value) return; _searchText = value; OnPropertyChanged(); PageNumber = 1; _ = _search.DebounceAsync(LoadPageAsync); } }
	public string InventorySearchText { get => _inventorySearchText; set { if (_inventorySearchText == value) return; _inventorySearchText = value; OnPropertyChanged(); _ = _inventorySearch.DebounceAsync(LoadInventoryOptionsAsync); } }
	public MaterialReturnStatusFilter SelectedStatusFilter { get => _selectedStatusFilter; set { if (_selectedStatusFilter == value) return; _selectedStatusFilter = value; OnPropertyChanged(); PageNumber = 1; _ = LoadPageAsync(); } }
	public int PageNumber { get => _pageNumber; private set { if (_pageNumber == value) return; _pageNumber = value; OnPropertyChanged(); OnPropertyChanged(nameof(PageDisplay)); OnPropertyChanged(nameof(HasNextPage)); RaiseCommands(); } }
	public long TotalCount { get => _totalCount; private set { if (_totalCount == value) return; _totalCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(PageDisplay)); OnPropertyChanged(nameof(HasNextPage)); RaiseCommands(); } }

	public async Task LoadAsync(CancellationToken cancellationToken = default)
	{
		BeginOperation("Material returns are loading");
		try { var reasonsTask = _reasonCodes.GetActiveAsync(cancellationToken); var inventoriesTask = _service.SearchInventoryOptionsAsync(InventorySearchText, 1, 100, cancellationToken); var originalsTask = _service.SearchOriginalIssuesAsync(null, 1, 100, cancellationToken); var pageTask = _service.SearchAsync(SearchText, SelectedStatusFilter.Status, PageNumber, PageSize, cancellationToken); await Task.WhenAll(reasonsTask, inventoriesTask, originalsTask, pageTask); Replace(ReasonCodes, await reasonsTask); Replace(InventoryOptions, (await inventoriesTask).Items); Replace(OriginalIssues, (await originalsTask).Items); ApplyPage(await pageTask); CompleteOperation(Returns.Count == 0, $"{TotalCount:N0} material returns"); }
		catch (Exception exception) when (exception is not OperationCanceledException) { FailOperation(exception, "Material returns could not be loaded"); }
	}

	private async Task LoadPageAsync(CancellationToken cancellationToken = default) { try { ApplyPage(await _service.SearchAsync(SearchText, SelectedStatusFilter.Status, PageNumber, PageSize, cancellationToken)); } catch (Exception exception) when (exception is not OperationCanceledException) { FailOperation(exception, "Material returns could not be loaded"); } }
	private async Task LoadInventoryOptionsAsync(CancellationToken cancellationToken = default) { try { Replace(InventoryOptions, (await _service.SearchInventoryOptionsAsync(InventorySearchText, 1, 100, cancellationToken)).Items); } catch (Exception exception) when (exception is not OperationCanceledException) { FailOperation(exception, "Inventory options could not be loaded"); } }
	private async Task LoadSelectedAsync(MaterialReturnOverviewItem? overview, CancellationToken cancellationToken)
	{
		if (overview is null) { Draft = NewDraft(); Lines.Clear(); Movements.Clear(); SelectedOriginalIssue = null; NotifyMovementState(); return; }
		BeginOperation("Material return is loading");
		try { var details = await _service.GetByIdAsync(overview.Id, cancellationToken) ?? throw new InvalidOperationException("The material return was not found."); Draft = Copy(details); _selectedOriginalIssue = details.OriginalMaterialIssueId is null ? null : OriginalIssues.FirstOrDefault(issue => issue.Id == details.OriginalMaterialIssueId); OnPropertyChanged(nameof(SelectedOriginalIssue)); Lines.Clear(); foreach (var line in details.Lines) Lines.Add(new MaterialReturnLineEditor(line)); Replace(Movements, await _service.GetMovementsAsync(details.Id, cancellationToken)); NotifyMovementState(); CompleteOperation(false, "Material return loaded"); } catch (Exception exception) when (exception is not OperationCanceledException) { FailOperation(exception, "Material return could not be loaded"); }
	}

	private void NewReturn() { _selectedReturn = null; OnPropertyChanged(nameof(SelectedReturn)); Draft = NewDraft(); Lines.Clear(); Movements.Clear(); SelectedOriginalIssue = null; SelectedLine = null; NotifyMovementState(); }
	private void AddOrUpdateLine() { if (SelectedInventory is null || SelectedReasonCode is null || LineQuantity <= 0) return; if (Lines.Any(line => line.InventoryId == SelectedInventory.InventoryId && line != SelectedLine)) { FailOperation(new InvalidOperationException("The inventory is already included."), "Line could not be added"); return; } var line = SelectedLine ?? new MaterialReturnLineEditor(); line.InventoryId = SelectedInventory.InventoryId; line.Quantity = LineQuantity; line.ReasonCodeId = SelectedReasonCode.Id; line.Notes = LineNotes; line.PartNumber = SelectedInventory.PartNumber; line.ItemDescription = SelectedInventory.Description; line.WarehouseName = SelectedInventory.WarehouseName; line.StorageLocationName = SelectedInventory.LocationName; line.PurposeName = SelectedInventory.PurposeName; line.ReasonCodeName = SelectedReasonCode.Name; line.CurrentStock = SelectedInventory.CurrentStock; if (SelectedLine is null) { line.LineNumber = Lines.Count + 1; Lines.Add(line); } SelectedLine = null; RaiseCommands(); }
	private void RemoveLine() { if (SelectedLine is null) return; Lines.Remove(SelectedLine); for (var index = 0; index < Lines.Count; index++) Lines[index].LineNumber = index + 1; SelectedLine = null; RaiseCommands(); }
	private async Task SaveAsync(CancellationToken token) => await ExecuteChangeAsync("Material return is saving", "Material return saved", () => _service.SaveDraftAsync(ToModel(), token), token);
	private async Task PostAsync(CancellationToken token) { if (!_dialogs.Confirm(new ConfirmationDialogRequest("Post Material Return", $"Post {Draft.ReturnNumber} and add all quantities to stock?", false))) return; await ExecuteChangeAsync("Material return is posting", "Material return posted", () => _service.PostAsync(Draft.Id, Draft.Version, token), token); }
	private async Task CancelAsync(CancellationToken token) { if (!_dialogs.Confirm(new ConfirmationDialogRequest("Cancel Draft", $"Cancel draft {Draft.ReturnNumber}?", true))) return; await ExecuteChangeAsync("Material return is cancelling", "Draft cancelled", () => _service.CancelAsync(Draft.Id, Draft.Version, token), token); }
	private async Task CorrectAsync(CancellationToken token) { if (SelectedCorrectionReasonCode is null || !_dialogs.Confirm(new ConfirmationDialogRequest("Correct Material Return", $"Create counter-movements for {Draft.ReturnNumber}? The posted return remains unchanged.", true))) return; BeginOperation("Counter-movements are posting"); try { await _service.CorrectAsync(Draft.Id, Draft.Version, SelectedCorrectionReasonCode.Id, CorrectionReason, token); Replace(Movements, await _service.GetMovementsAsync(Draft.Id, token)); CorrectionReason = string.Empty; NotifyMovementState(); CompleteOperation(false, "Material return corrected by counter-booking"); } catch (Exception exception) when (exception is not OperationCanceledException) { FailOperation(exception, "Material return could not be corrected"); } }
	private async Task ExecuteChangeAsync(string busy, string complete, Func<Task<MaterialReturn>> action, CancellationToken token) { BeginOperation(busy); try { var changed = await action(); var details = await _service.GetByIdAsync(changed.Id, token) ?? throw new InvalidOperationException("The material return was not found after the operation."); var overview = await _service.GetOverviewByIdAsync(changed.Id, token) ?? throw new InvalidOperationException("The material return overview was not found."); ApplyOverview(overview); Draft = Copy(details); Lines.Clear(); foreach (var line in details.Lines) Lines.Add(new MaterialReturnLineEditor(line)); Replace(Movements, await _service.GetMovementsAsync(changed.Id, token)); NotifyMovementState(); CompleteOperation(false, complete); } catch (ConcurrencyConflictException exception) { FailOperation(exception, "The material return was changed by another user"); } catch (Exception exception) when (exception is not OperationCanceledException) { FailOperation(exception, "Material return operation failed"); } }
	private MaterialReturn ToModel() { var value = Copy(Draft); value.Lines = Lines.Select(line => line.ToModel()).ToArray(); return value; }
	private void ApplyPage(PageResult<MaterialReturnOverviewItem> page) { var id = SelectedReturn?.Id; Replace(Returns, page.Items); TotalCount = page.TotalCount; PageNumber = page.PageNumber; if (id is not null) { _selectedReturn = Returns.FirstOrDefault(value => value.Id == id); OnPropertyChanged(nameof(SelectedReturn)); } }
	private void ApplyOverview(MaterialReturnOverviewItem overview) { var existing = Returns.FirstOrDefault(value => value.Id == overview.Id); if (existing is null) Returns.Insert(0, overview); else Returns[Returns.IndexOf(existing)] = overview; _selectedReturn = overview; OnPropertyChanged(nameof(SelectedReturn)); }
	private async Task PreviousPageAsync(CancellationToken token) { if (PageNumber > 1) { PageNumber--; await LoadPageAsync(token); } }
	private async Task NextPageAsync(CancellationToken token) { if (HasNextPage) { PageNumber++; await LoadPageAsync(token); } }
	private void NotifyMovementState() { OnPropertyChanged(nameof(HasMovements)); OnPropertyChanged(nameof(CanCorrect)); RaiseCommands(); }
	private void RaiseCommands() { SaveCommand.RaiseCanExecuteChanged(); PostCommand.RaiseCanExecuteChanged(); CancelCommand.RaiseCanExecuteChanged(); CorrectCommand.RaiseCanExecuteChanged(); AddLineCommand.RaiseCanExecuteChanged(); RemoveLineCommand.RaiseCanExecuteChanged(); PreviousPageCommand.RaiseCanExecuteChanged(); NextPageCommand.RaiseCanExecuteChanged(); }
	private static void Replace<T>(ObservableCollection<T> target, IReadOnlyList<T> values) { target.Clear(); foreach (var value in values) target.Add(value); }
	private static MaterialReturn NewDraft() => new() { ReturnDate = DateTime.Today };
	private static MaterialReturn Copy(MaterialReturn value) => new() { Id = value.Id, ReturnNumber = value.ReturnNumber, ReturnDate = value.ReturnDate, Status = value.Status, RecipientOrSource = value.RecipientOrSource, OriginalMaterialIssueId = value.OriginalMaterialIssueId, OriginalMaterialIssueNumber = value.OriginalMaterialIssueNumber, Reference = value.Reference, Notes = value.Notes, CreatedByUserId = value.CreatedByUserId, PostedByUserId = value.PostedByUserId, PostedAtUtc = value.PostedAtUtc, Version = value.Version, Lines = value.Lines };
	public void Dispose() { _search.Dispose(); _inventorySearch.Dispose(); _selectionCancellation?.Cancel(); _selectionCancellation?.Dispose(); SaveCommand.Dispose(); PostCommand.Dispose(); CancelCommand.Dispose(); CorrectCommand.Dispose(); PreviousPageCommand.Dispose(); NextPageCommand.Dispose(); }
}

public sealed record MaterialReturnStatusFilter(string Name, MaterialReturnStatus? Status);

public sealed class MaterialReturnLineEditor
{
	public MaterialReturnLineEditor() { }
	public MaterialReturnLineEditor(MaterialReturnLine line) { Id = line.Id; LineNumber = line.LineNumber; InventoryId = line.InventoryId; Quantity = line.Quantity; ReasonCodeId = line.ReasonCodeId; Notes = line.Notes; Version = line.Version; PartNumber = line.PartNumber; ItemDescription = line.ItemDescription; WarehouseName = line.WarehouseName; StorageLocationName = line.StorageLocationName; PurposeName = line.PurposeName; ReasonCodeName = line.ReasonCodeName; CurrentStock = line.CurrentStock; }
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
	public long ResultingStock => CurrentStock + Quantity;
	public MaterialReturnLine ToModel() => new() { Id = Id, LineNumber = LineNumber, InventoryId = InventoryId, Quantity = Quantity, ReasonCodeId = ReasonCodeId, Notes = Notes, Version = Version, PartNumber = PartNumber, ItemDescription = ItemDescription, WarehouseName = WarehouseName, StorageLocationName = StorageLocationName, PurposeName = PurposeName, ReasonCodeName = ReasonCodeName, CurrentStock = CurrentStock };
}
