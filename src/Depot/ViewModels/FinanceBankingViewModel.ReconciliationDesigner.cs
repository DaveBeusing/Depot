// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;
using Depot.Commands;
using Depot.Models;

namespace Depot.ViewModels;

public sealed partial class FinanceBankingViewModel
{
	private const int ReconciliationDesignerPageSize = 100;
	private FinanceBankStatementLine? _selectedReconciliationLine;
	private FinanceBankReconciliationCandidate? _selectedReconciliationCandidate;
	private FinanceBankReconciliationCandidate? _previewReconciliationCandidate;
	private FinanceBankReconciliationMatchPreview? _reconciliationMatchPreview;
	private int _reconciliationPageNumber = 1;
	private long _reconciliationTotalCount;
	private FinanceBankReconciliationHistoryItem? _selectedReconciliationHistoryItem;

	public ObservableCollection<FinanceBankStatementLine> ReconciliationDesignerLines { get; } = [];
	public ObservableCollection<FinanceBankReconciliationCandidate> ReconciliationCandidates { get; } = [];
	public ObservableCollection<FinanceBankReconciliationHistoryItem> ReconciliationHistory { get; } = [];

	public AsyncRelayCommand LoadReconciliationPageCommand { get; private set; } = null!;
	public AsyncRelayCommand PreviousReconciliationPageCommand { get; private set; } = null!;
	public AsyncRelayCommand NextReconciliationPageCommand { get; private set; } = null!;
	public AsyncRelayCommand LoadReconciliationCandidatesCommand { get; private set; } = null!;
	public AsyncRelayCommand UseReconciliationCandidateCommand { get; private set; } = null!;
	public AsyncRelayCommand MatchReconciliationCandidateCommand { get; private set; } = null!;
	public AsyncRelayCommand ReverseSelectedReconciliationCommand { get; private set; } = null!;

	public FinanceBankStatementLine? SelectedReconciliationLine
	{
		get => _selectedReconciliationLine;
		set
		{
			if (ReferenceEquals(_selectedReconciliationLine, value)) return;
			_selectedReconciliationLine = value;
			OnPropertyChanged();
			ReconciliationCandidates.Clear();
			SelectedReconciliationCandidate = null;
			PreviewReconciliationCandidate = null;
			OnPropertyChanged(nameof(ReconciliationBankEvidence));
			LoadReconciliationCandidatesCommand.RaiseCanExecuteChanged();
		}
	}

	public FinanceBankReconciliationCandidate? SelectedReconciliationCandidate
	{
		get => _selectedReconciliationCandidate;
		set
		{
			if (ReferenceEquals(_selectedReconciliationCandidate, value)) return;
			_selectedReconciliationCandidate = value;
			OnPropertyChanged();
			UseReconciliationCandidateCommand.RaiseCanExecuteChanged();
		}
	}

	public FinanceBankReconciliationHistoryItem? SelectedReconciliationHistoryItem
	{
		get => _selectedReconciliationHistoryItem;
		set
		{
			if (ReferenceEquals(_selectedReconciliationHistoryItem, value)) return;
			_selectedReconciliationHistoryItem = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(ReconciliationHistoryBankEvidence));
			OnPropertyChanged(nameof(ReconciliationHistoryTargetEvidence));
			ReverseSelectedReconciliationCommand.RaiseCanExecuteChanged();
		}
	}

	public string ReconciliationHistoryBankEvidence => SelectedReconciliationHistoryItem?.BankEvidence ?? "Select a history item.";
	public string ReconciliationHistoryTargetEvidence => SelectedReconciliationHistoryItem?.TargetEvidence ?? string.Empty;

	public FinanceBankReconciliationCandidate? PreviewReconciliationCandidate
	{
		get => _previewReconciliationCandidate;
		private set
		{
			if (ReferenceEquals(_previewReconciliationCandidate, value)) return;
			_previewReconciliationCandidate = value;
			ReconciliationMatchPreview = SelectedReconciliationLine is not null && value is not null
				? new FinanceBankReconciliationMatchPreview(SelectedReconciliationLine, value)
				: null;
			OnPropertyChanged();
			OnPropertyChanged(nameof(ReconciliationTargetEvidence));
			MatchReconciliationCandidateCommand.RaiseCanExecuteChanged();
		}
	}

	public FinanceBankReconciliationMatchPreview? ReconciliationMatchPreview
	{
		get => _reconciliationMatchPreview;
		private set
		{
			if (ReferenceEquals(_reconciliationMatchPreview, value)) return;
			_reconciliationMatchPreview = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(ReconciliationMatchAmountEvidence));
			OnPropertyChanged(nameof(ReconciliationMatchReferenceEvidence));
		}
	}

	public int ReconciliationPageNumber
	{
		get => _reconciliationPageNumber;
		private set
		{
			if (_reconciliationPageNumber == value) return;
			_reconciliationPageNumber = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(ReconciliationPageSummary));
		}
	}

	public long ReconciliationTotalCount
	{
		get => _reconciliationTotalCount;
		private set
		{
			if (_reconciliationTotalCount == value) return;
			_reconciliationTotalCount = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(ReconciliationPageSummary));
		}
	}

	public string ReconciliationPageSummary =>
		$"Page {ReconciliationPageNumber} · {ReconciliationTotalCount} unreconciled line(s)";

	public string ReconciliationBankEvidence => SelectedReconciliationLine is null
		? "Select an unreconciled bank statement line."
		: $"{SelectedReconciliationLine.BookingDate:yyyy-MM-dd} · {SelectedReconciliationLine.Amount:N2} {SelectedReconciliationLine.Currency.Value} · {SelectedReconciliationLine.CounterpartyName ?? "Unknown counterparty"} · {SelectedReconciliationLine.Reference ?? "No reference"}";

	public string ReconciliationTargetEvidence => PreviewReconciliationCandidate is null
		? "Choose a target with “Use selected target” or drag it here."
		: $"{PreviewReconciliationCandidate.TargetDisplay} · {PreviewReconciliationCandidate.Date:yyyy-MM-dd} · {PreviewReconciliationCandidate.Amount:N2} {PreviewReconciliationCandidate.Currency.Value} · {PreviewReconciliationCandidate.Counterparty} · {PreviewReconciliationCandidate.Reference ?? "No reference"}";

	public string ReconciliationMatchAmountEvidence => ReconciliationMatchPreview?.AmountEvidence ?? "No match preview.";
	public string ReconciliationMatchReferenceEvidence => ReconciliationMatchPreview?.ReferenceEvidence ?? string.Empty;

	private void InitializeReconciliationDesigner()
	{
		LoadReconciliationPageCommand = new AsyncRelayCommand(token => LoadReconciliationDesignerPageAsync(ReconciliationPageNumber, token));
		PreviousReconciliationPageCommand = new AsyncRelayCommand(token => LoadReconciliationDesignerPageAsync(ReconciliationPageNumber - 1, token), () => ReconciliationPageNumber > 1);
		NextReconciliationPageCommand = new AsyncRelayCommand(token => LoadReconciliationDesignerPageAsync(ReconciliationPageNumber + 1, token), () => (long)ReconciliationPageNumber * ReconciliationDesignerPageSize < ReconciliationTotalCount);
		LoadReconciliationCandidatesCommand = new AsyncRelayCommand(LoadReconciliationCandidatesAsync, () => SelectedReconciliationLine is not null);
		UseReconciliationCandidateCommand = new AsyncRelayCommand(_ =>
		{
			if (SelectedReconciliationCandidate is not null) UseReconciliationCandidate(SelectedReconciliationCandidate);
			return Task.CompletedTask;
		}, () => SelectedReconciliationCandidate is not null);
		MatchReconciliationCandidateCommand = new AsyncRelayCommand(MatchReconciliationCandidateAsync, () => CanReconcile && SelectedReconciliationLine is not null && PreviewReconciliationCandidate is not null);
		ReverseSelectedReconciliationCommand = new AsyncRelayCommand(ReverseSelectedReconciliationAsync, () => CanReconcile && SelectedReconciliationHistoryItem is { IsReversed: false });
	}

	private async Task LoadReconciliationDesignerPageAsync(int pageNumber, CancellationToken token)
	{
		var bankAccountId = SelectedBankAccount?.Id;
		var page = await _banking.SearchUnreconciledLinesAsync(bankAccountId, Math.Max(1, pageNumber), ReconciliationDesignerPageSize, token);
		if (page.Items.Count == 0 && page.PageNumber > 1 && page.TotalCount > 0)
		{
			await LoadReconciliationDesignerPageAsync(page.PageNumber - 1, token);
			return;
		}
		var selectedId = SelectedReconciliationLine?.Id;
		Replace(ReconciliationDesignerLines, page.Items);
		ReconciliationPageNumber = page.PageNumber;
		ReconciliationTotalCount = page.TotalCount;
		SelectedReconciliationLine = selectedId.HasValue
			? ReconciliationDesignerLines.FirstOrDefault(value => value.Id == selectedId.Value)
			: ReconciliationDesignerLines.FirstOrDefault();
		PreviousReconciliationPageCommand.RaiseCanExecuteChanged();
		NextReconciliationPageCommand.RaiseCanExecuteChanged();
		await LoadReconciliationHistoryAsync(token);
	}

	private async Task LoadReconciliationHistoryAsync(CancellationToken token)
	{
		var page = await _banking.SearchReconciliationHistoryAsync(SelectedBankAccount?.Id, 1, 100, token);
		var selectedId = SelectedReconciliationHistoryItem?.Reconciliation.Id;
		Replace(ReconciliationHistory, page.Items);
		SelectedReconciliationHistoryItem = selectedId.HasValue
			? ReconciliationHistory.FirstOrDefault(value => value.Reconciliation.Id == selectedId.Value)
			: ReconciliationHistory.FirstOrDefault();
	}

	private async Task ReverseSelectedReconciliationAsync(CancellationToken token)
	{
		BeginOperation("Reversing bank reconciliation...");
		try
		{
			var history = SelectedReconciliationHistoryItem ?? throw new InvalidOperationException("Select an active reconciliation.");
			if (history.IsReversed) throw new InvalidOperationException("Reversed reconciliation history is read-only.");
			await _banking.ReverseReconciliationAsync(history.Reconciliation.Id, Guid.NewGuid(), ReversalReason, token);
			await LoadReconciliationDesignerPageAsync(ReconciliationPageNumber, token);
			if (SelectedStatement is not null) await LoadStatementAsync(token);
			CompleteOperation(false, "Bank reconciliation reversed.");
		}
		catch (OperationCanceledException) when (token.IsCancellationRequested) { }
		catch (Exception exception) { FailOperation(exception, "Bank reconciliation reversal failed."); }
	}

	private async Task LoadReconciliationCandidatesAsync(CancellationToken token)
	{
		var line = SelectedReconciliationLine ?? throw new InvalidOperationException("Select an unreconciled bank statement line.");
		var candidates = await _banking.GetReconciliationCandidatesAsync(line.Id, 200, token);
		Replace(ReconciliationCandidates, candidates);
		SelectedReconciliationCandidate = ReconciliationCandidates.FirstOrDefault();
		PreviewReconciliationCandidate = null;
	}

	public bool UseReconciliationCandidate(FinanceBankReconciliationCandidate candidate)
	{
		ArgumentNullException.ThrowIfNull(candidate);
		if (SelectedReconciliationLine is null) return false;
		PreviewReconciliationCandidate = candidate;
		return true;
	}

	private async Task MatchReconciliationCandidateAsync(CancellationToken token)
	{
		BeginOperation("Reconciling selected bank line...");
		try
		{
			var line = SelectedReconciliationLine ?? throw new InvalidOperationException("Select an unreconciled bank statement line.");
			var candidate = PreviewReconciliationCandidate ?? throw new InvalidOperationException("Choose a reconciliation target.");
			var request = candidate.CreateRequest(Guid.NewGuid(), line.Id);
			await _banking.ReconcileAsync(request, token);
			await LoadReconciliationDesignerPageAsync(ReconciliationPageNumber, token);
			if (SelectedStatement is not null) await LoadStatementAsync(token);
			CompleteOperation(false, "Bank statement line reconciled.");
		}
		catch (OperationCanceledException) when (token.IsCancellationRequested) { }
		catch (Exception exception) { FailOperation(exception, "Bank reconciliation failed."); }
	}

	private void DisposeReconciliationDesigner()
	{
		LoadReconciliationPageCommand.Dispose();
		PreviousReconciliationPageCommand.Dispose();
		NextReconciliationPageCommand.Dispose();
		LoadReconciliationCandidatesCommand.Dispose();
		UseReconciliationCandidateCommand.Dispose();
		MatchReconciliationCandidateCommand.Dispose();
		ReverseSelectedReconciliationCommand.Dispose();
	}
}
