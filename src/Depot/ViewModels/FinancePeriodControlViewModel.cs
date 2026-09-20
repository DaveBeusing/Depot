// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Commands;
using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels;

public sealed class FinancePeriodControlViewModel : BaseViewModel, IDisposable
{
	private readonly FinanceGeneralLedgerService _ledger;
	private readonly IFileDialogService _dialogs;
	private DateTime? _selectedDate = DateTime.Today;
	private AccountingPeriod? _selectedPeriod;
	private bool _disposed;

	public FinancePeriodControlViewModel(FinanceGeneralLedgerService ledger, IFileDialogService dialogs)
	{
		_ledger = ledger;
		_dialogs = dialogs;
		RefreshCommand = new AsyncRelayCommand(LoadAsync);
		ClosePeriodCommand = new AsyncRelayCommand(ClosePeriodAsync, () => CanManage && SelectedPeriod?.Status == AccountingPeriodStatus.Open);
		ReopenPeriodCommand = new AsyncRelayCommand(ReopenPeriodAsync, () => CanManage && SelectedPeriod?.Status == AccountingPeriodStatus.Closed);
	}

	public ObservableCollection<AccountingPeriod> Periods { get; } = [];
	public AsyncRelayCommand RefreshCommand { get; }
	public AsyncRelayCommand ClosePeriodCommand { get; }
	public AsyncRelayCommand ReopenPeriodCommand { get; }
	public bool CanManage => _ledger.CanManagePeriods;

	public DateTime? SelectedDate
	{
		get => _selectedDate;
		set
		{
			if (_selectedDate == value) return;
			_selectedDate = value;
			OnPropertyChanged();
		}
	}

	public AccountingPeriod? SelectedPeriod
	{
		get => _selectedPeriod;
		set
		{
			if (ReferenceEquals(_selectedPeriod, value)) return;
			_selectedPeriod = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(SelectedPeriodSummary));
			RaiseCommands();
		}
	}

	public string SelectedPeriodSummary => SelectedPeriod is null
		? "Select an accounting period to review its operational status."
		: $"{SelectedPeriod.Code}: {SelectedPeriod.StartDate:yyyy-MM-dd} through {SelectedPeriod.EndDate:yyyy-MM-dd} · {SelectedPeriod.Status}";

	public async Task LoadAsync(CancellationToken cancellationToken = default)
	{
		BeginOperation("Loading accounting periods...");
		try
		{
			var date = DateOnly.FromDateTime((SelectedDate ?? DateTime.Today).Date);
			var selectedId = SelectedPeriod?.Id;
			var periods = await _ledger.GetPeriodsForDateAsync(date, cancellationToken);
			Periods.Clear();
			foreach (var period in periods) Periods.Add(period);
			SelectedPeriod = selectedId is Guid id
				? Periods.FirstOrDefault(period => period.Id == id) ?? Periods.FirstOrDefault()
				: Periods.FirstOrDefault();
			CompleteOperation(Periods.Count == 0, Periods.Count == 0
				? $"No accounting period covers {date:yyyy-MM-dd}."
				: $"Loaded {Periods.Count} accounting period(s) for {date:yyyy-MM-dd}.");
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
		catch (Exception exception) { FailOperation(exception, "Accounting periods could not be loaded."); }
	}

	private async Task ClosePeriodAsync(CancellationToken cancellationToken)
	{
		var period = SelectedPeriod;
		if (period is null) return;
		if (!_dialogs.Confirm(new ConfirmationDialogRequest(
			"Close Accounting Period",
			$"Close period {period.Code}? New Finance postings dated inside this period will be blocked. This action does not certify that accounting review or statutory period-end procedures are complete.",
			true))) return;
		await ChangeStatusAsync(period, AccountingPeriodStatus.Closed, "Accounting period closed.", cancellationToken);
	}

	private async Task ReopenPeriodAsync(CancellationToken cancellationToken)
	{
		var period = SelectedPeriod;
		if (period is null) return;
		if (!_dialogs.Confirm(new ConfirmationDialogRequest(
			"Reopen Accounting Period",
			$"Reopen period {period.Code}? New Finance postings dated inside this period can be accepted again. Reopening requires the deployment's approved period-end procedure.",
			true))) return;
		await ChangeStatusAsync(period, AccountingPeriodStatus.Open, "Accounting period reopened.", cancellationToken);
	}

	private async Task ChangeStatusAsync(
		AccountingPeriod period,
		AccountingPeriodStatus status,
		string completed,
		CancellationToken cancellationToken)
	{
		BeginOperation(status == AccountingPeriodStatus.Closed ? "Closing accounting period..." : "Reopening accounting period...");
		try
		{
			await _ledger.SetPeriodStatusAsync(period.Id, status, cancellationToken);
			await LoadAsync(cancellationToken);
			CompleteOperation(false, completed);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
		catch (Exception exception) { FailOperation(exception, "Accounting period status could not be changed."); }
	}

	private void RaiseCommands()
	{
		ClosePeriodCommand.RaiseCanExecuteChanged();
		ReopenPeriodCommand.RaiseCanExecuteChanged();
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		RefreshCommand.Dispose();
		ClosePeriodCommand.Dispose();
		ReopenPeriodCommand.Dispose();
	}
}
