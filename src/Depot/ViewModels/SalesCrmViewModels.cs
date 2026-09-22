// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;
using Depot.Commands;
using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels;

public sealed class SalesLeadsViewModel : BaseViewModel, IDisposable
{
	private readonly SalesCrmService _crm;
	private readonly CustomerService _customers;
	private string _searchText = string.Empty;
	private SalesLead? _selectedLead;
	private SalesActivity? _selectedActivity;
	private Customer? _conversionCustomer;
	private SalesLead _leadDraft = NewLeadDraft();
	private SalesActivity _activityDraft = NewActivityDraft();

	public SalesLeadsViewModel(SalesCrmService crm, CustomerService customers)
	{
		_crm = crm;
		_customers = customers;
		RefreshCommand = new AsyncRelayCommand(LoadAsync);
		NewLeadCommand = new AsyncRelayCommand(_ => { NewLead(); return Task.CompletedTask; }, () => _crm.CanManageRecords);
		SaveLeadCommand = new AsyncRelayCommand(SaveLeadAsync, () => _crm.CanManageRecords);
		ConvertLeadCommand = new AsyncRelayCommand(ConvertLeadAsync, () => _crm.CanManageRecords && SelectedLead is { Status: not SalesLeadStatus.Converted and not SalesLeadStatus.Disqualified });
		NewActivityCommand = new AsyncRelayCommand(_ => { NewActivity(); return Task.CompletedTask; }, () => _crm.CanManageActivities && SelectedLead is not null);
		SaveActivityCommand = new AsyncRelayCommand(SaveActivityAsync, () => _crm.CanManageActivities && SelectedLead is not null);
		CompleteActivityCommand = new AsyncRelayCommand(CompleteActivityAsync, () => _crm.CanManageActivities && SelectedActivity?.Status == SalesActivityStatus.Planned);
		CancelActivityCommand = new AsyncRelayCommand(CancelActivityAsync, () => _crm.CanManageActivities && SelectedActivity?.Status == SalesActivityStatus.Planned);
	}

	public ObservableCollection<SalesLead> Leads { get; } = [];
	public ObservableCollection<SalesActivity> Activities { get; } = [];
	public ObservableCollection<Customer> Customers { get; } = [];
	public IReadOnlyList<SalesLeadStatus> LeadStatuses { get; } = Enum.GetValues<SalesLeadStatus>();
	public IReadOnlyList<SalesActivityType> ActivityTypes { get; } = Enum.GetValues<SalesActivityType>();

	public AsyncRelayCommand RefreshCommand { get; }
	public AsyncRelayCommand NewLeadCommand { get; }
	public AsyncRelayCommand SaveLeadCommand { get; }
	public AsyncRelayCommand ConvertLeadCommand { get; }
	public AsyncRelayCommand NewActivityCommand { get; }
	public AsyncRelayCommand SaveActivityCommand { get; }
	public AsyncRelayCommand CompleteActivityCommand { get; }
	public AsyncRelayCommand CancelActivityCommand { get; }

	public string SearchText { get => _searchText; set { if (_searchText == value) return; _searchText = value; OnPropertyChanged(); } }
	public SalesLead LeadDraft { get => _leadDraft; private set { _leadDraft = value; OnPropertyChanged(); } }
	public SalesActivity ActivityDraft { get => _activityDraft; private set { _activityDraft = value; OnPropertyChanged(); } }
	public Customer? ConversionCustomer { get => _conversionCustomer; set { if (_conversionCustomer == value) return; _conversionCustomer = value; OnPropertyChanged(); } }

	public SalesLead? SelectedLead
	{
		get => _selectedLead;
		set
		{
			if (_selectedLead == value) return;
			_selectedLead = value;
			OnPropertyChanged();
			LeadDraft = value is null ? NewLeadDraft() : Copy(value);
			ConvertLeadCommand.RaiseCanExecuteChanged();
			NewActivityCommand.RaiseCanExecuteChanged();
			SaveActivityCommand.RaiseCanExecuteChanged();
			_ = LoadActivitiesAsync(value);
		}
	}

	public SalesActivity? SelectedActivity
	{
		get => _selectedActivity;
		set
		{
			if (_selectedActivity == value) return;
			_selectedActivity = value;
			OnPropertyChanged();
			if (value is not null) ActivityDraft = Copy(value);
			CompleteActivityCommand.RaiseCanExecuteChanged();
			CancelActivityCommand.RaiseCanExecuteChanged();
		}
	}

	public async Task LoadAsync(CancellationToken cancellationToken = default)
	{
		BeginOperation("Loading leads…");
		try
		{
			var selectedId = SelectedLead?.Id;
			var pageTask = _crm.SearchLeadsAsync(SearchText, pageNumber: 1, pageSize: 200, cancellationToken: cancellationToken);
			var customersTask = _customers.ListActiveAsync(cancellationToken);
			await Task.WhenAll(pageTask, customersTask);
			Replace(Leads, pageTask.Result.Items);
			Replace(Customers, customersTask.Result);
			SelectedLead = selectedId is > 0 ? Leads.FirstOrDefault(value => value.Id == selectedId) : Leads.FirstOrDefault();
			CompleteOperation(Leads.Count == 0, Leads.Count == 0 ? "No leads match the current search." : $"{Leads.Count:N0} leads loaded");
		}
		catch (Exception exception) { FailOperation(exception, "Lead loading failed"); }
	}

	public async Task OpenAsync(long id, CancellationToken cancellationToken = default)
	{
		var lead = await _crm.GetLeadAsync(id, cancellationToken);
		if (lead is null) return;
		if (Leads.All(value => value.Id != lead.Id)) Leads.Insert(0, lead);
		SelectedLead = Leads.First(value => value.Id == lead.Id);
	}

	private void NewLead()
	{
		SelectedLead = null;
		LeadDraft = NewLeadDraft();
		RequestEditorFocus();
	}

	private async Task SaveLeadAsync(CancellationToken token)
	{
		BeginOperation("Saving lead…");
		try
		{
			var saved = await _crm.SaveLeadAsync(LeadDraft, token);
			await LoadAsync(token);
			SelectedLead = Leads.FirstOrDefault(value => value.Id == saved.Id);
			CompleteOperation(false, $"Lead {saved.LeadNumber} saved");
		}
		catch (Exception exception) { FailOperation(exception, "Lead save failed"); }
	}

	private async Task ConvertLeadAsync(CancellationToken token)
	{
		if (SelectedLead is null) return;
		BeginOperation("Converting lead…");
		try
		{
			var result = await _crm.ConvertLeadAsync(
				SelectedLead.Id,
				SelectedLead.Version,
				ConversionCustomer?.Id,
				new SalesOpportunity
				{
					OwnerUserId = SelectedLead.OwnerUserId,
					ExpectedCloseDate = DateTime.Today.AddDays(30),
					Currency = ConversionCustomer?.Currency ?? "EUR",
					ProbabilityPercent = 0
				},
				token);
			await LoadAsync(token);
			SelectedLead = Leads.FirstOrDefault(value => value.Id == result.Lead.Id);
			CompleteOperation(false, $"Lead converted to {result.Opportunity.OpportunityNumber}");
		}
		catch (Exception exception) { FailOperation(exception, "Lead conversion failed"); }
	}

	private void NewActivity()
	{
		if (SelectedLead is null) return;
		SelectedActivity = null;
		ActivityDraft = NewActivityDraft();
		ActivityDraft.LeadId = SelectedLead.Id;
	}

	private async Task SaveActivityAsync(CancellationToken token)
	{
		if (SelectedLead is null) return;
		ActivityDraft.LeadId = SelectedLead.Id;
		ActivityDraft.OpportunityId = null;
		try
		{
			await _crm.SaveActivityAsync(ActivityDraft, token);
			await LoadActivitiesAsync(SelectedLead, token);
			CompleteOperation(false, "Activity saved");
		}
		catch (Exception exception) { FailOperation(exception, "Activity save failed"); }
	}

	private async Task CompleteActivityAsync(CancellationToken token)
	{
		if (SelectedActivity is null) return;
		try { await _crm.CompleteActivityAsync(SelectedActivity.Id, SelectedActivity.Version, token); await LoadActivitiesAsync(SelectedLead, token); CompleteOperation(false, "Activity completed"); }
		catch (Exception exception) { FailOperation(exception, "Activity completion failed"); }
	}

	private async Task CancelActivityAsync(CancellationToken token)
	{
		if (SelectedActivity is null) return;
		try { await _crm.CancelActivityAsync(SelectedActivity.Id, SelectedActivity.Version, token); await LoadActivitiesAsync(SelectedLead, token); CompleteOperation(false, "Activity cancelled"); }
		catch (Exception exception) { FailOperation(exception, "Activity cancellation failed"); }
	}

	private async Task LoadActivitiesAsync(SalesLead? lead, CancellationToken token = default)
	{
		Activities.Clear();
		SelectedActivity = null;
		if (lead is null || !_crm.CanViewActivities) return;
		try { Replace(Activities, await _crm.ListActivitiesAsync(lead.Id, null, 100, token)); }
		catch (OperationCanceledException) when (token.IsCancellationRequested) { }
	}

	private static SalesLead NewLeadDraft() => new() { Status = SalesLeadStatus.New };
	private static SalesActivity NewActivityDraft() => new() { Type = SalesActivityType.FollowUp, DueAtUtc = DateTime.Now.AddDays(1).ToUniversalTime(), Status = SalesActivityStatus.Planned };
	private static SalesLead Copy(SalesLead v) => new() { Id=v.Id,LeadNumber=v.LeadNumber,CompanyName=v.CompanyName,PersonName=v.PersonName,Email=v.Email,Phone=v.Phone,Source=v.Source,OwnerUserId=v.OwnerUserId,OwnerDisplayName=v.OwnerDisplayName,Status=v.Status,NotesSummary=v.NotesSummary,CreatedAtUtc=v.CreatedAtUtc,UpdatedAtUtc=v.UpdatedAtUtc,ConvertedCustomerId=v.ConvertedCustomerId,ConvertedOpportunityId=v.ConvertedOpportunityId,ConvertedAtUtc=v.ConvertedAtUtc,Version=v.Version };
	private static SalesActivity Copy(SalesActivity v) => new() { Id=v.Id,LeadId=v.LeadId,OpportunityId=v.OpportunityId,Type=v.Type,DueAtUtc=v.DueAtUtc,OwnerUserId=v.OwnerUserId,OwnerDisplayName=v.OwnerDisplayName,Status=v.Status,Subject=v.Subject,Notes=v.Notes,CompletedAtUtc=v.CompletedAtUtc,CompletedByUserId=v.CompletedByUserId,CancelledAtUtc=v.CancelledAtUtc,CancelledByUserId=v.CancelledByUserId,Version=v.Version };
	private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values) { target.Clear(); foreach (var value in values) target.Add(value); }
	public void Dispose() { RefreshCommand.Dispose(); NewLeadCommand.Dispose(); SaveLeadCommand.Dispose(); ConvertLeadCommand.Dispose(); NewActivityCommand.Dispose(); SaveActivityCommand.Dispose(); CompleteActivityCommand.Dispose(); CancelActivityCommand.Dispose(); }
}

public sealed class SalesOpportunitiesViewModel : BaseViewModel, IDisposable
{
	private readonly SalesCrmService _crm;
	private readonly CustomerService _customers;
	private readonly Func<SalesQuote, CancellationToken, Task>? _quoteNavigation;
	private string _searchText = string.Empty;
	private SalesOpportunity? _selectedOpportunity;
	private SalesActivity? _selectedActivity;
	private Customer? _selectedCustomer;
	private SalesOpportunityStage? _selectedStage;
	private SalesOpportunity _opportunityDraft = NewOpportunityDraft();
	private SalesActivity _activityDraft = NewActivityDraft();
	private string _closeReason = string.Empty;

	public SalesOpportunitiesViewModel(SalesCrmService crm, CustomerService customers, Func<SalesQuote, CancellationToken, Task>? quoteNavigation = null)
	{
		_crm = crm;
		_customers = customers;
		_quoteNavigation = quoteNavigation;
		RefreshCommand = new AsyncRelayCommand(LoadAsync);
		NewOpportunityCommand = new AsyncRelayCommand(_ => { NewOpportunity(); return Task.CompletedTask; }, () => _crm.CanManageRecords);
		SaveOpportunityCommand = new AsyncRelayCommand(SaveOpportunityAsync, () => _crm.CanManageRecords);
		WinCommand = new AsyncRelayCommand(token => CloseAsync(SalesOpportunityOutcome.Won, token), () => _crm.CanManageRecords && SelectedOpportunity?.Outcome == SalesOpportunityOutcome.Open);
		LoseCommand = new AsyncRelayCommand(token => CloseAsync(SalesOpportunityOutcome.Lost, token), () => _crm.CanManageRecords && SelectedOpportunity?.Outcome == SalesOpportunityOutcome.Open);
		CreateQuoteCommand = new AsyncRelayCommand(CreateQuoteAsync, () => _crm.CanManageRecords && SelectedOpportunity is { Outcome: SalesOpportunityOutcome.Open, LinkedSalesQuoteId: null });
		NewActivityCommand = new AsyncRelayCommand(_ => { NewActivity(); return Task.CompletedTask; }, () => _crm.CanManageActivities && SelectedOpportunity is not null);
		SaveActivityCommand = new AsyncRelayCommand(SaveActivityAsync, () => _crm.CanManageActivities && SelectedOpportunity is not null);
		CompleteActivityCommand = new AsyncRelayCommand(CompleteActivityAsync, () => _crm.CanManageActivities && SelectedActivity?.Status == SalesActivityStatus.Planned);
		CancelActivityCommand = new AsyncRelayCommand(CancelActivityAsync, () => _crm.CanManageActivities && SelectedActivity?.Status == SalesActivityStatus.Planned);
	}

	public ObservableCollection<SalesOpportunity> Opportunities { get; } = [];
	public ObservableCollection<SalesOpportunityStage> Stages { get; } = [];
	public ObservableCollection<SalesPipelineStageSummary> Pipeline { get; } = [];
	public ObservableCollection<Customer> Customers { get; } = [];
	public ObservableCollection<SalesActivity> Activities { get; } = [];
	public IReadOnlyList<SalesActivityType> ActivityTypes { get; } = Enum.GetValues<SalesActivityType>();

	public AsyncRelayCommand RefreshCommand { get; }
	public AsyncRelayCommand NewOpportunityCommand { get; }
	public AsyncRelayCommand SaveOpportunityCommand { get; }
	public AsyncRelayCommand WinCommand { get; }
	public AsyncRelayCommand LoseCommand { get; }
	public AsyncRelayCommand CreateQuoteCommand { get; }
	public AsyncRelayCommand NewActivityCommand { get; }
	public AsyncRelayCommand SaveActivityCommand { get; }
	public AsyncRelayCommand CompleteActivityCommand { get; }
	public AsyncRelayCommand CancelActivityCommand { get; }

	public string SearchText { get => _searchText; set { if (_searchText == value) return; _searchText = value; OnPropertyChanged(); } }
	public string CloseReason { get => _closeReason; set { if (_closeReason == value) return; _closeReason = value; OnPropertyChanged(); } }
	public SalesOpportunity OpportunityDraft { get => _opportunityDraft; private set { _opportunityDraft = value; OnPropertyChanged(); } }
	public SalesActivity ActivityDraft { get => _activityDraft; private set { _activityDraft = value; OnPropertyChanged(); } }
	public Customer? SelectedCustomer { get => _selectedCustomer; set { if (_selectedCustomer == value) return; _selectedCustomer = value; OnPropertyChanged(); } }
	public SalesOpportunityStage? SelectedStage { get => _selectedStage; set { if (_selectedStage == value) return; _selectedStage = value; OnPropertyChanged(); } }

	public SalesOpportunity? SelectedOpportunity
	{
		get => _selectedOpportunity;
		set
		{
			if (_selectedOpportunity == value) return;
			_selectedOpportunity = value;
			OnPropertyChanged();
			if (value is null)
			{
				OpportunityDraft = NewOpportunityDraft();
				SelectedCustomer = null;
				SelectedStage = Stages.FirstOrDefault();
			}
			else
			{
				OpportunityDraft = Copy(value);
				SelectedCustomer = Customers.FirstOrDefault(customer => customer.Id == value.CustomerId);
				SelectedStage = Stages.FirstOrDefault(stage => stage.Id == value.StageId);
			}
			CloseReason = value?.CloseReason ?? string.Empty;
			RaiseOpportunityCommands();
			_ = LoadActivitiesAsync(value);
		}
	}

	public SalesActivity? SelectedActivity
	{
		get => _selectedActivity;
		set
		{
			if (_selectedActivity == value) return;
			_selectedActivity = value;
			OnPropertyChanged();
			if (value is not null) ActivityDraft = Copy(value);
			CompleteActivityCommand.RaiseCanExecuteChanged();
			CancelActivityCommand.RaiseCanExecuteChanged();
		}
	}

	public async Task LoadAsync(CancellationToken cancellationToken = default)
	{
		BeginOperation("Loading opportunities…");
		try
		{
			var selectedId = SelectedOpportunity?.Id;
			var opportunitiesTask = _crm.SearchOpportunitiesAsync(SearchText, pageNumber: 1, pageSize: 200, cancellationToken: cancellationToken);
			var stagesTask = _crm.ListStagesAsync(true, cancellationToken);
			var pipelineTask = _crm.GetPipelineSummaryAsync(cancellationToken: cancellationToken);
			var customersTask = _customers.ListActiveAsync(cancellationToken);
			await Task.WhenAll(opportunitiesTask, stagesTask, pipelineTask, customersTask);
			Replace(Opportunities, opportunitiesTask.Result.Items);
			Replace(Stages, stagesTask.Result);
			Replace(Pipeline, pipelineTask.Result);
			Replace(Customers, customersTask.Result);
			SelectedOpportunity = selectedId is > 0 ? Opportunities.FirstOrDefault(value => value.Id == selectedId) : Opportunities.FirstOrDefault();
			CompleteOperation(Opportunities.Count == 0, Opportunities.Count == 0 ? "No opportunities match the current search." : $"{Opportunities.Count:N0} opportunities loaded");
		}
		catch (Exception exception) { FailOperation(exception, "Opportunity loading failed"); }
	}

	public async Task OpenAsync(long id, CancellationToken cancellationToken = default)
	{
		var opportunity = await _crm.GetOpportunityAsync(id, cancellationToken);
		if (opportunity is null) return;
		if (Opportunities.All(value => value.Id != opportunity.Id)) Opportunities.Insert(0, opportunity);
		SelectedOpportunity = Opportunities.First(value => value.Id == opportunity.Id);
	}

	private void NewOpportunity()
	{
		SelectedOpportunity = null;
		OpportunityDraft = NewOpportunityDraft();
		SelectedCustomer = Customers.FirstOrDefault();
		SelectedStage = Stages.FirstOrDefault();
		RequestEditorFocus();
	}

	private async Task SaveOpportunityAsync(CancellationToken token)
	{
		if (SelectedCustomer is null) { FailOperation(new InvalidOperationException("Select a customer."), "Opportunity save failed"); return; }
		BeginOperation("Saving opportunity…");
		try
		{
			OpportunityDraft.CustomerId = SelectedCustomer.Id;
			OpportunityDraft.Currency = string.IsNullOrWhiteSpace(OpportunityDraft.Currency) ? SelectedCustomer.Currency : OpportunityDraft.Currency;
			OpportunityDraft.StageId = SelectedStage?.Id ?? 0;
			var saved = await _crm.SaveOpportunityAsync(OpportunityDraft, token);
			await LoadAsync(token);
			SelectedOpportunity = Opportunities.FirstOrDefault(value => value.Id == saved.Id);
			CompleteOperation(false, $"Opportunity {saved.OpportunityNumber} saved");
		}
		catch (Exception exception) { FailOperation(exception, "Opportunity save failed"); }
	}

	private async Task CloseAsync(SalesOpportunityOutcome outcome, CancellationToken token)
	{
		if (SelectedOpportunity is null) return;
		BeginOperation(outcome == SalesOpportunityOutcome.Won ? "Closing opportunity as won…" : "Closing opportunity as lost…");
		try
		{
			var closed = await _crm.CloseOpportunityAsync(SelectedOpportunity.Id, SelectedOpportunity.Version, outcome, CloseReason, token);
			await LoadAsync(token);
			SelectedOpportunity = Opportunities.FirstOrDefault(value => value.Id == closed.Id);
			CompleteOperation(false, $"Opportunity marked {outcome}");
		}
		catch (Exception exception) { FailOperation(exception, "Opportunity close failed"); }
	}

	private async Task CreateQuoteAsync(CancellationToken token)
	{
		if (SelectedOpportunity is null) return;
		BeginOperation("Creating quote draft…");
		try
		{
			var quote = await _crm.CreateQuoteAsync(SelectedOpportunity.Id, SelectedOpportunity.Version, token);
			await LoadAsync(token);
			CompleteOperation(false, $"Quote {quote.QuoteNumber} created");
			if (_quoteNavigation is not null) await _quoteNavigation(quote, token);
		}
		catch (Exception exception) { FailOperation(exception, "Quote creation failed"); }
	}

	private void NewActivity()
	{
		if (SelectedOpportunity is null) return;
		SelectedActivity = null;
		ActivityDraft = NewActivityDraft();
		ActivityDraft.OpportunityId = SelectedOpportunity.Id;
	}

	private async Task SaveActivityAsync(CancellationToken token)
	{
		if (SelectedOpportunity is null) return;
		ActivityDraft.OpportunityId = SelectedOpportunity.Id;
		ActivityDraft.LeadId = null;
		try
		{
			await _crm.SaveActivityAsync(ActivityDraft, token);
			await LoadActivitiesAsync(SelectedOpportunity, token);
			CompleteOperation(false, "Activity saved");
		}
		catch (Exception exception) { FailOperation(exception, "Activity save failed"); }
	}

	private async Task CompleteActivityAsync(CancellationToken token)
	{
		if (SelectedActivity is null) return;
		try { await _crm.CompleteActivityAsync(SelectedActivity.Id, SelectedActivity.Version, token); await LoadActivitiesAsync(SelectedOpportunity, token); CompleteOperation(false, "Activity completed"); }
		catch (Exception exception) { FailOperation(exception, "Activity completion failed"); }
	}

	private async Task CancelActivityAsync(CancellationToken token)
	{
		if (SelectedActivity is null) return;
		try { await _crm.CancelActivityAsync(SelectedActivity.Id, SelectedActivity.Version, token); await LoadActivitiesAsync(SelectedOpportunity, token); CompleteOperation(false, "Activity cancelled"); }
		catch (Exception exception) { FailOperation(exception, "Activity cancellation failed"); }
	}

	private async Task LoadActivitiesAsync(SalesOpportunity? opportunity, CancellationToken token = default)
	{
		Activities.Clear();
		SelectedActivity = null;
		if (opportunity is null || !_crm.CanViewActivities) return;
		try { Replace(Activities, await _crm.ListActivitiesAsync(null, opportunity.Id, 100, token)); }
		catch (OperationCanceledException) when (token.IsCancellationRequested) { }
	}

	private void RaiseOpportunityCommands()
	{
		WinCommand.RaiseCanExecuteChanged();
		LoseCommand.RaiseCanExecuteChanged();
		CreateQuoteCommand.RaiseCanExecuteChanged();
		NewActivityCommand.RaiseCanExecuteChanged();
		SaveActivityCommand.RaiseCanExecuteChanged();
	}

	private static SalesOpportunity NewOpportunityDraft() => new() { ExpectedCloseDate = DateTime.Today.AddDays(30), Currency = "EUR", Outcome = SalesOpportunityOutcome.Open };
	private static SalesActivity NewActivityDraft() => new() { Type = SalesActivityType.FollowUp, DueAtUtc = DateTime.Now.AddDays(1).ToUniversalTime(), Status = SalesActivityStatus.Planned };
	private static SalesOpportunity Copy(SalesOpportunity v) => new() { Id=v.Id,OpportunityNumber=v.OpportunityNumber,CustomerId=v.CustomerId,CustomerName=v.CustomerName,LeadId=v.LeadId,OwnerUserId=v.OwnerUserId,OwnerDisplayName=v.OwnerDisplayName,StageId=v.StageId,StageName=v.StageName,StageSortOrder=v.StageSortOrder,ExpectedCloseDate=v.ExpectedCloseDate,Currency=v.Currency,ExpectedAmount=v.ExpectedAmount,ProbabilityPercent=v.ProbabilityPercent,NextActivityDate=v.NextActivityDate,Outcome=v.Outcome,CloseReason=v.CloseReason,ClosedAtUtc=v.ClosedAtUtc,LinkedSalesQuoteId=v.LinkedSalesQuoteId,CreatedAtUtc=v.CreatedAtUtc,UpdatedAtUtc=v.UpdatedAtUtc,Version=v.Version };
	private static SalesActivity Copy(SalesActivity v) => new() { Id=v.Id,LeadId=v.LeadId,OpportunityId=v.OpportunityId,Type=v.Type,DueAtUtc=v.DueAtUtc,OwnerUserId=v.OwnerUserId,OwnerDisplayName=v.OwnerDisplayName,Status=v.Status,Subject=v.Subject,Notes=v.Notes,CompletedAtUtc=v.CompletedAtUtc,CompletedByUserId=v.CompletedByUserId,CancelledAtUtc=v.CancelledAtUtc,CancelledByUserId=v.CancelledByUserId,Version=v.Version };
	private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values) { target.Clear(); foreach (var value in values) target.Add(value); }
	public void Dispose() { RefreshCommand.Dispose(); NewOpportunityCommand.Dispose(); SaveOpportunityCommand.Dispose(); WinCommand.Dispose(); LoseCommand.Dispose(); CreateQuoteCommand.Dispose(); NewActivityCommand.Dispose(); SaveActivityCommand.Dispose(); CompleteActivityCommand.Dispose(); CancelActivityCommand.Dispose(); }
}
