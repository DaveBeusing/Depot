// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;
using System.IO;
using Depot.Commands;
using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels;

public sealed class FinanceBudgetingViewModel : BaseViewModel, IDisposable
{
	private readonly FinanceBudgetingService _service;
	private readonly IFileDialogService _fileDialogs;
	private FinanceBudgetVersion? _selectedBudget;
	private FinanceBudgetLine? _selectedLine;
	private FinanceBudgetOption? _selectedLegalEntity;
	private FinanceBudgetOption? _selectedBook;
	private FinanceBudgetOption? _selectedCalendar;
	private FinanceBudgetAccountOption? _selectedAccount;
	private FinanceBudgetPeriodOption? _selectedPeriod;
	private FinanceBudgetDimensionOption? _selectedDimension;
	private FinanceBudgetDimensionValueOption? _selectedDimensionValue;
	private string _budgetName = "Operating Budget";
	private string _description = string.Empty;
	private int _fiscalYear = DateTime.Today.Year;
	private int _targetFiscalYear = DateTime.Today.Year + 1;
	private decimal _lineAmount;
	private decimal _annualAmount;
	private bool _replaceImport;
	private bool _profitLossVariance = true;
	private string _importSummary = "No CSV preview loaded.";
	private string? _pendingImportCsv;
	private FinanceBudgetSummary? _summary;
	private bool _disposed;

	public FinanceBudgetingViewModel(FinanceBudgetingService service, IFileDialogService fileDialogs)
	{
		_service = service;
		_fileDialogs = fileDialogs;
		RefreshCommand = new AsyncRelayCommand(LoadAsync);
		NewBudgetCommand = new AsyncRelayCommand(NewBudgetAsync);
		CreateBudgetCommand = new AsyncRelayCommand(CreateBudgetAsync);
		NewLineCommand = new AsyncRelayCommand(NewLineAsync);
		SaveLineCommand = new AsyncRelayCommand(SaveLineAsync);
		DeleteLineCommand = new AsyncRelayCommand(DeleteLineAsync);
		SpreadAnnualCommand = new AsyncRelayCommand(SpreadAnnualAsync);
		CopyVersionCommand = new AsyncRelayCommand(CopyVersionAsync);
		CreateAmendmentCommand = new AsyncRelayCommand(CreateAmendmentAsync);
		CreateFromPriorYearCommand = new AsyncRelayCommand(CreateFromPriorYearAsync);
		SubmitCommand = new AsyncRelayCommand(SubmitAsync);
		ApproveCommand = new AsyncRelayCommand(ApproveAsync);
		RejectCommand = new AsyncRelayCommand(RejectAsync);
		LockCommand = new AsyncRelayCommand(LockAsync);
		SupersedeCommand = new AsyncRelayCommand(SupersedeAsync);
		ArchiveCommand = new AsyncRelayCommand(ArchiveAsync);
		PreviewImportCommand = new AsyncRelayCommand(PreviewImportAsync);
		ApplyImportCommand = new AsyncRelayCommand(ApplyImportAsync);
		ExportCommand = new AsyncRelayCommand(ExportAsync);
		RefreshVarianceCommand = new AsyncRelayCommand(RefreshVarianceAsync);
	}

	public ObservableCollection<FinanceBudgetVersion> Versions { get; } = [];
	public ObservableCollection<FinanceBudgetLine> Lines { get; } = [];
	public ObservableCollection<FinanceBudgetVarianceRow> VarianceRows { get; } = [];
	public ObservableCollection<FinanceBudgetImportRow> ImportRows { get; } = [];
	public ObservableCollection<FinanceBudgetOption> LegalEntities { get; } = [];
	public ObservableCollection<FinanceBudgetOption> Books { get; } = [];
	public ObservableCollection<FinanceBudgetOption> Calendars { get; } = [];
	public ObservableCollection<FinanceBudgetAccountOption> Accounts { get; } = [];
	public ObservableCollection<FinanceBudgetPeriodOption> Periods { get; } = [];
	public ObservableCollection<FinanceBudgetDimensionOption> Dimensions { get; } = [];
	public ObservableCollection<FinanceBudgetDimensionValueOption> DimensionValues { get; } = [];

	public AsyncRelayCommand RefreshCommand { get; }
	public AsyncRelayCommand NewBudgetCommand { get; }
	public AsyncRelayCommand CreateBudgetCommand { get; }
	public AsyncRelayCommand NewLineCommand { get; }
	public AsyncRelayCommand SaveLineCommand { get; }
	public AsyncRelayCommand DeleteLineCommand { get; }
	public AsyncRelayCommand SpreadAnnualCommand { get; }
	public AsyncRelayCommand CopyVersionCommand { get; }
	public AsyncRelayCommand CreateAmendmentCommand { get; }
	public AsyncRelayCommand CreateFromPriorYearCommand { get; }
	public AsyncRelayCommand SubmitCommand { get; }
	public AsyncRelayCommand ApproveCommand { get; }
	public AsyncRelayCommand RejectCommand { get; }
	public AsyncRelayCommand LockCommand { get; }
	public AsyncRelayCommand SupersedeCommand { get; }
	public AsyncRelayCommand ArchiveCommand { get; }
	public AsyncRelayCommand PreviewImportCommand { get; }
	public AsyncRelayCommand ApplyImportCommand { get; }
	public AsyncRelayCommand ExportCommand { get; }
	public AsyncRelayCommand RefreshVarianceCommand { get; }

	public bool CanManage => _service.CanManage;
	public bool CanApprove => _service.CanApprove;
	public bool CanLock => _service.CanLock;

	public FinanceBudgetVersion? SelectedBudget
	{
		get => _selectedBudget;
		set
		{
			if (ReferenceEquals(_selectedBudget, value)) return;
			_selectedBudget = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(HasSelectedBudget));
			OnPropertyChanged(nameof(IsSelectedBudgetDraft));
			OnPropertyChanged(nameof(SelectedBudgetStatus));
			if (value is not null)
			{
				BudgetName = value.Name;
				Description = value.Description ?? string.Empty;
				FiscalYear = value.FiscalYear;
				TargetFiscalYear = value.FiscalYear + 1;
				_ = LoadSelectedBudgetAsync(value.Id, CancellationToken.None);
			}
		}
	}

	public FinanceBudgetLine? SelectedLine
	{
		get => _selectedLine;
		set
		{
			if (ReferenceEquals(_selectedLine, value)) return;
			_selectedLine = value;
			OnPropertyChanged();
			if (value is null) return;
			SelectedAccount = Accounts.FirstOrDefault(option => option.Id == value.AccountId);
			SelectedPeriod = Periods.FirstOrDefault(option => option.Id == value.AccountingPeriodId);
			SelectedDimension = value.DimensionId.HasValue ? Dimensions.FirstOrDefault(option => option.Id == value.DimensionId.Value) : null;
			LineAmount = value.Amount;
		}
	}

	public FinanceBudgetOption? SelectedLegalEntity
	{
		get => _selectedLegalEntity;
		set
		{
			if (ReferenceEquals(_selectedLegalEntity, value)) return;
			_selectedLegalEntity = value;
			OnPropertyChanged();
			if (value is not null) _ = LoadEntityOptionsAsync(value.Id, CancellationToken.None);
		}
	}

	public FinanceBudgetOption? SelectedBook { get => _selectedBook; set => SetRef(ref _selectedBook, value); }
	public FinanceBudgetOption? SelectedCalendar { get => _selectedCalendar; set => SetRef(ref _selectedCalendar, value); }
	public FinanceBudgetAccountOption? SelectedAccount { get => _selectedAccount; set => SetRef(ref _selectedAccount, value); }
	public FinanceBudgetPeriodOption? SelectedPeriod { get => _selectedPeriod; set => SetRef(ref _selectedPeriod, value); }

	public FinanceBudgetDimensionOption? SelectedDimension
	{
		get => _selectedDimension;
		set
		{
			if (ReferenceEquals(_selectedDimension, value)) return;
			_selectedDimension = value;
			OnPropertyChanged();
			SelectedDimensionValue = null;
			_ = LoadDimensionValuesAsync(value?.Id, CancellationToken.None);
		}
	}

	public FinanceBudgetDimensionValueOption? SelectedDimensionValue { get => _selectedDimensionValue; set => SetRef(ref _selectedDimensionValue, value); }
	public string BudgetName { get => _budgetName; set => Set(ref _budgetName, value); }
	public string Description { get => _description; set => Set(ref _description, value); }
	public int FiscalYear { get => _fiscalYear; set => Set(ref _fiscalYear, value); }
	public int TargetFiscalYear { get => _targetFiscalYear; set => Set(ref _targetFiscalYear, value); }
	public decimal LineAmount { get => _lineAmount; set => Set(ref _lineAmount, value); }
	public decimal AnnualAmount { get => _annualAmount; set => Set(ref _annualAmount, value); }
	public bool ReplaceImport { get => _replaceImport; set => Set(ref _replaceImport, value); }

	public bool ProfitLossVariance
	{
		get => _profitLossVariance;
		set
		{
			if (!Set(ref _profitLossVariance, value)) return;
			if (_selectedBudget is not null) _ = LoadVarianceAsync(_selectedBudget.Id, CancellationToken.None);
		}
	}

	public string ImportSummary { get => _importSummary; private set => Set(ref _importSummary, value); }
	public FinanceBudgetSummary? Summary
	{
		get => _summary;
		private set
		{
			if (ReferenceEquals(_summary, value)) return;
			_summary = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(SummaryText));
		}
	}

	public bool HasSelectedBudget => SelectedBudget is not null;
	public bool IsSelectedBudgetDraft => SelectedBudget?.Status == FinanceBudgetStatus.Draft;
	public string SelectedBudgetStatus => SelectedBudget?.Status.ToString() ?? "No selection";
	public string SummaryText => Summary is null
		? "No budget selected"
		: $"{Summary.LineCount:N0} lines · {Summary.TotalBudget:N2} signed budget total";

	public async Task LoadAsync(CancellationToken cancellationToken = default)
	{
		BeginOperation("Loading budgets...");
		try
		{
			var versionsTask = _service.SearchVersionsAsync(new FinanceBudgetListFilter(), 1, 200, cancellationToken);
			var entitiesTask = _service.GetLegalEntitiesAsync(cancellationToken);
			var dimensionsTask = _service.GetDimensionsAsync(cancellationToken);
			await Task.WhenAll(versionsTask, entitiesTask, dimensionsTask);
			Replace(Versions, (await versionsTask).Items);
			Replace(LegalEntities, await entitiesTask);
			Replace(Dimensions, await dimensionsTask);
			if (SelectedLegalEntity is null) SelectedLegalEntity = LegalEntities.FirstOrDefault();
			if (SelectedBudget is null || Versions.All(value => value.Id != SelectedBudget.Id))
				SelectedBudget = Versions.FirstOrDefault();
			CompleteOperation(Versions.Count == 0, Versions.Count == 0 ? "No budgets yet." : $"{Versions.Count:N0} budget versions loaded.");
		}
		catch (Exception exception) { FailOperation(exception, "Could not load Finance budgets"); }
	}

	private Task NewBudgetAsync(CancellationToken cancellationToken)
	{
		SelectedBudget = null;
		BudgetName = "Operating Budget";
		Description = string.Empty;
		FiscalYear = DateTime.Today.Year;
		TargetFiscalYear = FiscalYear + 1;
		return Task.CompletedTask;
	}

	private async Task CreateBudgetAsync(CancellationToken cancellationToken)
	{
		if (SelectedLegalEntity is null || SelectedBook is null || SelectedCalendar is null)
		{
			FailOperation(new InvalidOperationException("Select a legal entity, accounting book and fiscal calendar."), "Budget draft was not created");
			return;
		}
		BeginOperation("Creating budget draft...");
		try
		{
			var created = await _service.CreateDraftAsync(
				SelectedLegalEntity.Id,
				SelectedBook.Id,
				SelectedCalendar.Id,
				FiscalYear,
				BudgetName,
				description: Description,
				cancellationToken: cancellationToken);
			await LoadAsync(cancellationToken);
			SelectedBudget = Versions.FirstOrDefault(value => value.Id == created.Id) ?? created;
			CompleteOperation(false, $"Budget {created.Name} v{created.BudgetVersionNumber} created.");
		}
		catch (Exception exception) { FailOperation(exception, "Budget draft was not created"); }
	}

	private Task NewLineAsync(CancellationToken cancellationToken)
	{
		SelectedLine = null;
		SelectedAccount = null;
		SelectedPeriod = null;
		SelectedDimension = null;
		SelectedDimensionValue = null;
		LineAmount = 0m;
		return Task.CompletedTask;
	}

	private async Task SaveLineAsync(CancellationToken cancellationToken)
	{
		if (SelectedBudget is null || SelectedAccount is null || SelectedPeriod is null)
		{
			FailOperation(new InvalidOperationException("Select a draft budget, account and accounting period."), "Budget line was not saved");
			return;
		}
		BeginOperation("Saving budget line...");
		try
		{
			var line = new FinanceBudgetLine
			{
				Id = SelectedLine?.Id ?? 0,
				Version = SelectedLine?.Version ?? 1,
				BudgetVersionId = SelectedBudget.Id,
				AccountId = SelectedAccount.Id,
				AccountingPeriodId = SelectedPeriod.Id,
				DimensionId = SelectedDimension?.Id,
				DimensionValueId = SelectedDimensionValue?.Id,
				Amount = LineAmount,
				SourceEvidence = SelectedLine?.SourceEvidence
			};
			await _service.SaveLineAsync(SelectedBudget.Id, line, cancellationToken);
			await RefreshSelectedAsync(cancellationToken);
			CompleteOperation(false, "Budget line saved.");
		}
		catch (Exception exception) { FailOperation(exception, "Budget line was not saved"); }
	}

	private async Task DeleteLineAsync(CancellationToken cancellationToken)
	{
		if (SelectedBudget is null || SelectedLine is null) return;
		if (!_fileDialogs.Confirm(new ConfirmationDialogRequest("Delete budget line", "Delete the selected draft budget line?", true))) return;
		BeginOperation("Deleting budget line...");
		try
		{
			await _service.DeleteLineAsync(SelectedBudget.Id, SelectedLine.Id, SelectedLine.Version, cancellationToken);
			SelectedLine = null;
			await RefreshSelectedAsync(cancellationToken);
			CompleteOperation(false, "Budget line deleted.");
		}
		catch (Exception exception) { FailOperation(exception, "Budget line was not deleted"); }
	}

	private async Task SpreadAnnualAsync(CancellationToken cancellationToken)
	{
		if (SelectedBudget is null || SelectedAccount is null)
		{
			FailOperation(new InvalidOperationException("Select a draft budget and account first."), "Annual amount was not spread");
			return;
		}
		BeginOperation("Spreading annual amount...");
		try
		{
			await _service.SpreadAnnualAmountAsync(
				SelectedBudget.Id,
				SelectedAccount.Id,
				AnnualAmount,
				SelectedDimension?.Id,
				SelectedDimensionValue?.Id,
				cancellationToken);
			await RefreshSelectedAsync(cancellationToken);
			CompleteOperation(false, "Annual amount spread equally; deterministic remainder assigned to the final period.");
		}
		catch (Exception exception) { FailOperation(exception, "Annual amount was not spread"); }
	}

	private async Task CopyVersionAsync(CancellationToken cancellationToken)
	{
		if (SelectedBudget is null) return;
		BeginOperation("Copying budget version...");
		try
		{
			var created = await _service.CopyVersionAsync(SelectedBudget.Id, cancellationToken: cancellationToken);
			await LoadAsync(cancellationToken);
			SelectedBudget = Versions.FirstOrDefault(value => value.Id == created.Id) ?? created;
			CompleteOperation(false, "Budget version copied to a new draft.");
		}
		catch (Exception exception) { FailOperation(exception, "Budget version was not copied"); }
	}

	private async Task CreateAmendmentAsync(CancellationToken cancellationToken)
	{
		if (SelectedBudget is null) return;
		BeginOperation("Creating amendment...");
		try
		{
			var created = await _service.CreateAmendmentAsync(SelectedBudget.Id, cancellationToken);
			await LoadAsync(cancellationToken);
			SelectedBudget = Versions.FirstOrDefault(value => value.Id == created.Id) ?? created;
			CompleteOperation(false, "Amendment draft created without rewriting approved history.");
		}
		catch (Exception exception) { FailOperation(exception, "Amendment was not created"); }
	}

	private async Task CreateFromPriorYearAsync(CancellationToken cancellationToken)
	{
		if (SelectedBudget is null) return;
		BeginOperation("Creating prior-year budget copy...");
		try
		{
			var created = await _service.CreateFromPriorYearAsync(SelectedBudget.Id, TargetFiscalYear, cancellationToken: cancellationToken);
			await LoadAsync(cancellationToken);
			SelectedBudget = Versions.FirstOrDefault(value => value.Id == created.Id) ?? created;
			CompleteOperation(false, "Prior-year budget copied with source evidence.");
		}
		catch (Exception exception) { FailOperation(exception, "Prior-year budget was not created"); }
	}

	private Task SubmitAsync(CancellationToken token) => ApplyVersionActionAsync(
		(value, cancellationToken) => _service.SubmitAsync(value.Id, value.Version, cancellationToken),
		"Submitting budget for approval...",
		"Budget submitted for approval.",
		token);

	private Task ApproveAsync(CancellationToken token) => ApplyVersionActionAsync(
		(value, cancellationToken) => _service.ApproveAsync(value.Id, value.Version, cancellationToken: cancellationToken),
		"Approving budget...",
		"Approval decision recorded.",
		token);

	private Task RejectAsync(CancellationToken token) => ApplyVersionActionAsync(
		(value, cancellationToken) => _service.RejectAsync(value.Id, value.Version, cancellationToken: cancellationToken),
		"Rejecting budget...",
		"Budget returned to draft.",
		token);

	private Task LockAsync(CancellationToken token) => ApplyVersionActionAsync(
		(value, cancellationToken) => _service.LockAsync(value.Id, value.Version, cancellationToken),
		"Locking budget...",
		"Budget locked.",
		token);

	private Task SupersedeAsync(CancellationToken token) => ApplyVersionActionAsync(
		(value, cancellationToken) => _service.SupersedeAsync(value.Id, value.Version, cancellationToken),
		"Superseding budget...",
		"Budget superseded.",
		token);

	private Task ArchiveAsync(CancellationToken token) => ApplyVersionActionAsync(
		(value, cancellationToken) => _service.ArchiveAsync(value.Id, value.Version, cancellationToken),
		"Archiving budget...",
		"Budget archived.",
		token);

	private async Task ApplyVersionActionAsync(
		Func<FinanceBudgetVersion, CancellationToken, Task<FinanceBudgetVersion>> action,
		string busyText,
		string successText,
		CancellationToken cancellationToken)
	{
		if (SelectedBudget is null) return;
		BeginOperation(busyText);
		try
		{
			var changed = await action(SelectedBudget, cancellationToken);
			await LoadAsync(cancellationToken);
			SelectedBudget = Versions.FirstOrDefault(value => value.Id == changed.Id) ?? changed;
			CompleteOperation(false, successText);
		}
		catch (Exception exception) { FailOperation(exception, "Budget workflow action failed"); }
	}

	private async Task PreviewImportAsync(CancellationToken cancellationToken)
	{
		if (SelectedBudget is null) return;
		var path = _fileDialogs.ShowOpenFile(new OpenFileDialogRequest("Preview budget CSV", "CSV files (*.csv)|*.csv|All files (*.*)|*.*"));
		if (string.IsNullOrWhiteSpace(path)) return;
		BeginOperation("Validating budget CSV...");
		try
		{
			_pendingImportCsv = await File.ReadAllTextAsync(path, cancellationToken);
			var preview = await _service.PreviewImportAsync(SelectedBudget.Id, _pendingImportCsv, cancellationToken);
			Replace(ImportRows, preview.Rows);
			ImportSummary = $"{preview.ValidRowCount:N0} valid · {preview.InvalidRowCount:N0} invalid · {preview.DuplicateRowCount:N0} duplicate keys";
			CompleteOperation(false, preview.CanApply ? "CSV preview is valid and ready to apply." : "CSV preview contains validation errors.");
		}
		catch (Exception exception) { FailOperation(exception, "CSV preview failed"); }
	}

	private async Task ApplyImportAsync(CancellationToken cancellationToken)
	{
		if (SelectedBudget is null || string.IsNullOrEmpty(_pendingImportCsv))
		{
			FailOperation(new InvalidOperationException("Preview a CSV file before applying it."), "CSV import was not applied");
			return;
		}
		if (ReplaceImport && !_fileDialogs.Confirm(new ConfirmationDialogRequest("Replace budget lines", "Replace all current draft lines with the previewed CSV data?", true))) return;
		BeginOperation("Applying budget CSV...");
		try
		{
			var count = await _service.ApplyImportAsync(SelectedBudget.Id, _pendingImportCsv, ReplaceImport, cancellationToken);
			await RefreshSelectedAsync(cancellationToken);
			CompleteOperation(false, $"{count:N0} budget lines imported atomically.");
		}
		catch (Exception exception) { FailOperation(exception, "CSV import was not applied"); }
	}

	private async Task ExportAsync(CancellationToken cancellationToken)
	{
		if (SelectedBudget is null) return;
		var path = _fileDialogs.ShowSaveFile(new SaveFileDialogRequest(
			"Export budget CSV",
			"CSV files (*.csv)|*.csv|All files (*.*)|*.*",
			".csv",
			$"{SanitizeFileName(SelectedBudget.Name)}-{SelectedBudget.FiscalYear}-v{SelectedBudget.BudgetVersionNumber}.csv"));
		if (string.IsNullOrWhiteSpace(path)) return;
		BeginOperation("Exporting budget CSV...");
		try
		{
			var csv = await _service.ExportCsvAsync(SelectedBudget.Id, cancellationToken);
			await File.WriteAllTextAsync(path, csv, cancellationToken);
			CompleteOperation(false, "Budget CSV exported.");
		}
		catch (Exception exception) { FailOperation(exception, "Budget CSV was not exported"); }
	}

	private async Task RefreshVarianceAsync(CancellationToken cancellationToken)
	{
		if (SelectedBudget is null) return;
		BeginOperation("Refreshing Actual vs Budget variance...");
		try
		{
			await LoadVarianceAsync(SelectedBudget.Id, cancellationToken);
			CompleteOperation(VarianceRows.Count == 0, $"{VarianceRows.Count:N0} variance rows loaded from current Financial Reporting actuals.");
		}
		catch (Exception exception) { FailOperation(exception, "Variance could not be refreshed"); }
	}

	private async Task LoadSelectedBudgetAsync(long id, CancellationToken cancellationToken)
	{
		try
		{
			var value = await _service.GetVersionAsync(id, cancellationToken);
			if (value is null) return;
			await LoadBudgetReferenceDataAsync(value, cancellationToken);
			await LoadDetailsAsync(value, cancellationToken);
		}
		catch (OperationCanceledException) { }
		catch (Exception exception) { FailOperation(exception, "Budget details could not be loaded"); }
	}

	private async Task RefreshSelectedAsync(CancellationToken cancellationToken)
	{
		if (SelectedBudget is null) return;
		var refreshed = await _service.GetVersionAsync(SelectedBudget.Id, cancellationToken)
			?? throw new InvalidOperationException("Budget version was not found.");
		_selectedBudget = refreshed;
		OnPropertyChanged(nameof(SelectedBudget));
		OnPropertyChanged(nameof(IsSelectedBudgetDraft));
		OnPropertyChanged(nameof(SelectedBudgetStatus));
		await LoadDetailsAsync(refreshed, cancellationToken);
	}

	private async Task LoadDetailsAsync(FinanceBudgetVersion value, CancellationToken cancellationToken)
	{
		var linesTask = _service.GetLinesAsync(value.Id, 1, 500, cancellationToken);
		var summaryTask = _service.GetSummaryAsync(value.Id, cancellationToken);
		await Task.WhenAll(linesTask, summaryTask);
		Replace(Lines, (await linesTask).Items);
		Summary = await summaryTask;
		SelectedLine = null;
		await LoadVarianceAsync(value.Id, cancellationToken);
	}

	private async Task LoadVarianceAsync(long id, CancellationToken cancellationToken)
	{
		var rows = ProfitLossVariance
			? await _service.GetProfitLossVarianceAsync(id, cancellationToken: cancellationToken)
			: await _service.GetGeneralVarianceAsync(id, cancellationToken: cancellationToken);
		Replace(VarianceRows, rows);
	}

	private async Task LoadBudgetReferenceDataAsync(FinanceBudgetVersion value, CancellationToken cancellationToken)
	{
		var accountsTask = _service.GetAccountsAsync(value.AccountingBookId, cancellationToken);
		var periodsTask = _service.GetPeriodsAsync(value.FiscalCalendarId, cancellationToken);
		await Task.WhenAll(accountsTask, periodsTask);
		Replace(Accounts, await accountsTask);
		Replace(Periods, (await periodsTask).Where(period => period.StartDate.Year == value.FiscalYear));
	}

	private async Task LoadEntityOptionsAsync(Guid legalEntityId, CancellationToken cancellationToken)
	{
		try
		{
			var booksTask = _service.GetAccountingBooksAsync(legalEntityId, cancellationToken);
			var calendarsTask = _service.GetFiscalCalendarsAsync(legalEntityId, cancellationToken);
			await Task.WhenAll(booksTask, calendarsTask);
			Replace(Books, await booksTask);
			Replace(Calendars, await calendarsTask);
			SelectedBook = Books.FirstOrDefault();
			SelectedCalendar = Calendars.FirstOrDefault();
		}
		catch (OperationCanceledException) { }
		catch (Exception exception) { FailOperation(exception, "Finance reference data could not be loaded"); }
	}

	private async Task LoadDimensionValuesAsync(Guid? dimensionId, CancellationToken cancellationToken)
	{
		try
		{
			if (!dimensionId.HasValue)
			{
				DimensionValues.Clear();
				return;
			}
			Replace(DimensionValues, await _service.GetDimensionValuesAsync(dimensionId.Value, cancellationToken));
			if (SelectedLine?.DimensionValueId is Guid selected)
				SelectedDimensionValue = DimensionValues.FirstOrDefault(value => value.Id == selected);
		}
		catch (OperationCanceledException) { }
		catch (Exception exception) { FailOperation(exception, "Dimension values could not be loaded"); }
	}

	private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
	{
		target.Clear();
		foreach (var value in values) target.Add(value);
	}

	private bool Set<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
	{
		if (EqualityComparer<T>.Default.Equals(field, value)) return false;
		field = value;
		OnPropertyChanged(propertyName);
		return true;
	}

	private bool SetRef<T>(ref T? field, T? value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null) where T : class
	{
		if (ReferenceEquals(field, value)) return false;
		field = value;
		OnPropertyChanged(propertyName);
		return true;
	}

	private static string SanitizeFileName(string value)
	{
		var invalid = Path.GetInvalidFileNameChars();
		return new string(value.Select(character => invalid.Contains(character) ? '-' : character).ToArray());
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		foreach (var command in new[]
		{
			RefreshCommand, NewBudgetCommand, CreateBudgetCommand, NewLineCommand, SaveLineCommand, DeleteLineCommand,
			SpreadAnnualCommand, CopyVersionCommand, CreateAmendmentCommand, CreateFromPriorYearCommand, SubmitCommand,
			ApproveCommand, RejectCommand, LockCommand, SupersedeCommand, ArchiveCommand, PreviewImportCommand,
			ApplyImportCommand, ExportCommand, RefreshVarianceCommand
		}) command.Dispose();
	}
}
