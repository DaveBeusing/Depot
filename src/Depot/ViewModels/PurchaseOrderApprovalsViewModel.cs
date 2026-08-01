// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Commands;
using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels;

public sealed class PurchaseOrderApprovalsViewModel : BaseViewModel, IDisposable
{
	private const int PageSize = 50;
	private readonly PurchaseOrderApprovalService _approvals;
	private readonly AsyncDebouncer _searchDebouncer = new(TimeSpan.FromMilliseconds(300));
	private CancellationTokenSource? _detailsCancellation;
	private string _searchText = string.Empty;
	private string _supplierFilter = string.Empty;
	private string _creatorFilter = string.Empty;
	private DateTime? _fromDate;
	private DateTime? _toDate;
	private string _decisionComment = string.Empty;
	private PurchaseOrderApprovalWorkItem? _selectedApproval;
	private PurchaseOrderApprovalDetails? _details;
	private PurchaseOrderApprovalSummary _summary = new(0, null, 0);
	private int _pageNumber = 1;
	private long _totalCount;
	private bool _isLoadingDetails;

	public PurchaseOrderApprovalsViewModel(PurchaseOrderApprovalService approvals)
	{
		_approvals = approvals;
		ApproveCommand = new AsyncRelayCommand(ApproveAsync, () => CanDecideSelected);
		RejectCommand = new AsyncRelayCommand(RejectAsync, () => CanDecideSelected);
		PreviousPageCommand = new AsyncRelayCommand(PreviousPageAsync, () => PageNumber > 1);
		NextPageCommand = new AsyncRelayCommand(NextPageAsync, () => HasNextPage);
		ClearFiltersCommand = new RelayCommand(ClearFilters);
	}

	public ObservableCollection<PurchaseOrderApprovalWorkItem> Approvals { get; } = new();
	public AsyncRelayCommand ApproveCommand { get; }
	public AsyncRelayCommand RejectCommand { get; }
	public AsyncRelayCommand PreviousPageCommand { get; }
	public AsyncRelayCommand NextPageCommand { get; }
	public RelayCommand ClearFiltersCommand { get; }
	public bool HasNextPage => (long)PageNumber * PageSize < TotalCount;
	public string PageDisplay => $"Page {PageNumber} · {TotalCount:N0} approvals";
	public bool CanDecideSelected => SelectedApproval is not null && _approvals.CanDecide(SelectedApproval.CreatedByUserId);

	public string SearchText { get => _searchText; set => SetFilter(ref _searchText, value); }
	public string SupplierFilter { get => _supplierFilter; set => SetFilter(ref _supplierFilter, value); }
	public string CreatorFilter { get => _creatorFilter; set => SetFilter(ref _creatorFilter, value); }
	public DateTime? FromDate { get => _fromDate; set => SetFilter(ref _fromDate, value); }
	public DateTime? ToDate { get => _toDate; set => SetFilter(ref _toDate, value); }

	public string DecisionComment
	{
		get => _decisionComment;
		set { if (_decisionComment == value) return; _decisionComment = value; OnPropertyChanged(); }
	}

	public PurchaseOrderApprovalWorkItem? SelectedApproval
	{
		get => _selectedApproval;
		set
		{
			if (_selectedApproval == value) return;
			_selectedApproval = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(CanDecideSelected));
			DecisionComment = string.Empty;
			ApproveCommand.RaiseCanExecuteChanged();
			RejectCommand.RaiseCanExecuteChanged();
			_ = LoadDetailsAsync(value);
		}
	}

	public PurchaseOrderApprovalDetails? Details
	{
		get => _details;
		private set { _details = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasDetails)); OnPropertyChanged(nameof(HasNoDetails)); }
	}

	public PurchaseOrderApprovalSummary Summary
	{
		get => _summary;
		private set { _summary = value; OnPropertyChanged(); }
	}

	public int PageNumber
	{
		get => _pageNumber;
		private set
		{
			if (_pageNumber == value) return;
			_pageNumber = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(PageDisplay));
			RaisePagingCommands();
		}
	}

	public long TotalCount
	{
		get => _totalCount;
		private set
		{
			if (_totalCount == value) return;
			_totalCount = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(HasNextPage));
			OnPropertyChanged(nameof(PageDisplay));
			RaisePagingCommands();
		}
	}

	public bool HasDetails => Details is not null;
	public bool HasNoDetails => Details is null;
	public bool IsLoadingDetails
	{
		get => _isLoadingDetails;
		private set { if (_isLoadingDetails == value) return; _isLoadingDetails = value; OnPropertyChanged(); }
	}

	public Task LoadAsync(CancellationToken cancellationToken = default) => LoadPageAsync(cancellationToken);

	private async Task LoadPageAsync(CancellationToken cancellationToken)
	{
		BeginOperation("Loading pending approvals");
		try
		{
			var result = await _approvals.SearchAsync(CreateFilter(), PageNumber, PageSize, cancellationToken);
			var selectedId = SelectedApproval?.Id;
			CollectionSynchronizer.Replace(Approvals, result.Page.Items);
			TotalCount = result.Page.TotalCount;
			Summary = result.Summary;
			SelectedApproval = selectedId is null ? null : Approvals.FirstOrDefault(item => item.Id == selectedId);
			CompleteOperation(Approvals.Count == 0, $"{result.Summary.OpenCount:N0} pending approvals");
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
		catch (Exception exception) { FailOperation(exception, "Pending approvals could not be loaded"); }
	}

	private async Task LoadDetailsAsync(PurchaseOrderApprovalWorkItem? approval)
	{
		_detailsCancellation?.Cancel();
		_detailsCancellation?.Dispose();
		_detailsCancellation = null;
		Details = null;
		IsLoadingDetails = false;
		if (approval is null) return;
		_detailsCancellation = new CancellationTokenSource();
		var token = _detailsCancellation.Token;
		IsLoadingDetails = true;
		try
		{
			Details = await _approvals.GetDetailsAsync(approval.Id, token)
				?? throw new InvalidOperationException("The purchase order no longer exists.");
		}
		catch (OperationCanceledException) when (token.IsCancellationRequested) { }
		catch (Exception exception) { FailOperation(exception, "Approval details could not be loaded"); }
		finally { if (!token.IsCancellationRequested) IsLoadingDetails = false; }
	}

	private Task ApproveAsync(CancellationToken cancellationToken) =>
		DecideAsync(true, cancellationToken);

	private Task RejectAsync(CancellationToken cancellationToken) =>
		DecideAsync(false, cancellationToken);

	private async Task DecideAsync(bool approve, CancellationToken cancellationToken)
	{
		var selected = SelectedApproval;
		if (selected is null) return;
		BeginOperation(approve ? "Approving purchase order" : "Rejecting purchase order");
		try
		{
			if (approve)
				await _approvals.ApproveAsync(selected.Id, selected.Version, DecisionComment, cancellationToken);
			else
				await _approvals.RejectAsync(selected.Id, selected.Version, DecisionComment, cancellationToken);
			RemoveApproval(selected);
			await RefreshSummaryAsync(cancellationToken);
			CompleteOperation(Approvals.Count == 0, approve ? "Purchase order approved" : "Purchase order rejected");
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			await ReconcileAfterInterruptedDecisionAsync(selected, "The operation was cancelled before its result was confirmed.");
		}
		catch (ConcurrencyConflictException)
		{
			await ReconcileAfterInterruptedDecisionAsync(selected, "The purchase order was changed by another user.");
		}
		catch (Exception exception)
		{
			await ReconcileAfterInterruptedDecisionAsync(selected, exception.Message);
		}
	}

	private async Task ReconcileAfterInterruptedDecisionAsync(PurchaseOrderApprovalWorkItem selected, string originalMessage)
	{
		try
		{
			var current = await _approvals.GetCurrentAsync(selected.Id, CancellationToken.None);
			if (current is null || current.Status != PurchaseOrderStatus.PendingApproval)
			{
				RemoveApproval(selected);
				await RefreshSummaryAsync(CancellationToken.None);
				CompleteOperation(Approvals.Count == 0, current is null
					? "The purchase order no longer exists. The work queue was reconciled."
					: $"Current status confirmed as {current.StatusDisplayName}. The work queue was reconciled.");
				return;
			}

			var refreshed = ToWorkItem(current);
			var index = Approvals.IndexOf(selected);
			if (index >= 0) Approvals[index] = refreshed;
			SelectedApproval = refreshed;
			FailOperation(new InvalidOperationException(originalMessage), "Decision was not applied; current status is still Pending Approval");
		}
		catch (Exception verificationException)
		{
			FailOperation(
				new InvalidOperationException($"{originalMessage} The current server status could not be verified: {verificationException.Message}"),
				"Approval status could not be verified");
		}
	}

	private void RemoveApproval(PurchaseOrderApprovalWorkItem selected)
	{
		Approvals.Remove(selected);
		SelectedApproval = null;
		TotalCount = Math.Max(0, TotalCount - 1);
	}

	private async Task RefreshSummaryAsync(CancellationToken cancellationToken) =>
		Summary = await _approvals.GetSummaryAsync(CreateFilter(), cancellationToken);

	private PurchaseOrderApprovalFilter CreateFilter()
	{
		DateTime? fromUtc = FromDate is null
			? null
			: DateTime.SpecifyKind(FromDate.Value.Date, DateTimeKind.Local).ToUniversalTime();
		DateTime? toUtc = ToDate is null
			? null
			: DateTime.SpecifyKind(ToDate.Value.Date.AddDays(1), DateTimeKind.Local).ToUniversalTime();
		return new PurchaseOrderApprovalFilter(SearchText, SupplierFilter, CreatorFilter, fromUtc, toUtc);
	}

	private static PurchaseOrderApprovalWorkItem ToWorkItem(PurchaseOrder order) =>
		new(
			order.Id,
			order.OrderNumber,
			order.SupplierId,
			order.SupplierName,
			order.OrderDate,
			order.ExpectedDeliveryDate,
			order.Notes,
			order.CreatedByUserId,
			order.CreatedByUserDisplay ?? "Unknown user",
			order.SubmittedAtUtc ?? DateTime.UtcNow,
			order.Lines.Sum(line => line.Quantity * line.UnitPrice),
			order.Version);

	private async Task PreviousPageAsync(CancellationToken cancellationToken)
	{
		if (PageNumber <= 1) return;
		PageNumber--;
		await LoadPageAsync(cancellationToken);
	}

	private async Task NextPageAsync(CancellationToken cancellationToken)
	{
		if (!HasNextPage) return;
		PageNumber++;
		await LoadPageAsync(cancellationToken);
	}

	private void ClearFilters()
	{
		_searchText = _supplierFilter = _creatorFilter = string.Empty;
		_fromDate = _toDate = null;
		OnPropertyChanged(string.Empty);
		PageNumber = 1;
		_ = LoadPageAsync(CancellationToken.None);
	}

	private void SetFilter<T>(ref T field, T value)
	{
		if (EqualityComparer<T>.Default.Equals(field, value)) return;
		field = value;
		OnPropertyChanged();
		PageNumber = 1;
		_ = _searchDebouncer.DebounceAsync(LoadPageAsync);
	}

	private void RaisePagingCommands()
	{
		PreviousPageCommand.RaiseCanExecuteChanged();
		NextPageCommand.RaiseCanExecuteChanged();
	}

	public void Dispose()
	{
		_detailsCancellation?.Cancel();
		_detailsCancellation?.Dispose();
		_searchDebouncer.Dispose();
		ApproveCommand.Dispose();
		RejectCommand.Dispose();
		PreviousPageCommand.Dispose();
		NextPageCommand.Dispose();
	}
}
