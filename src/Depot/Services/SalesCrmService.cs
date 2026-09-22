// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class SalesCrmService
{
	private readonly IDatabaseTransactionRunner _transactions;
	private readonly SalesCrmRepository _crm;
	private readonly CustomerRepository _customers;
	private readonly AuditRepository _auditEntries;
	private readonly AuditService _audit;
	private readonly CustomerService _customerService;
	private readonly SalesQuoteService _quotes;
	private readonly IAuthorizationService _authorization;

	public SalesCrmService(
		IDatabaseTransactionRunner transactions,
		SalesCrmRepository crm,
		CustomerRepository customers,
		AuditRepository auditEntries,
		AuditService audit,
		CustomerService customerService,
		SalesQuoteService quotes,
		IAuthorizationService authorization)
	{
		_transactions = transactions;
		_crm = crm;
		_customers = customers;
		_auditEntries = auditEntries;
		_audit = audit;
		_customerService = customerService;
		_quotes = quotes;
		_authorization = authorization;
	}

	public bool CanView => _authorization.HasPermission(ApplicationPermission.SalesCrmView);
	public bool CanManageRecords => _authorization.HasAnyPermission(ApplicationPermission.SalesCrmRecordsManage, ApplicationPermission.SalesCrmManage);
	public bool CanManageConfiguration => _authorization.HasPermission(ApplicationPermission.SalesCrmManage);
	public bool CanViewActivities => _authorization.HasAnyPermission(ApplicationPermission.SalesCrmActivitiesView, ApplicationPermission.SalesCrmManage);
	public bool CanManageActivities => _authorization.HasAnyPermission(ApplicationPermission.SalesCrmActivitiesManage, ApplicationPermission.SalesCrmManage);

	public Task<PageResult<SalesLead>> SearchLeadsAsync(string? searchText, SalesLeadStatus? status = null, long? ownerUserId = null, int pageNumber = 1, int pageSize = 100, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.SalesCrmView);
		return _crm.SearchLeadsAsync(searchText, status, ownerUserId, pageNumber, pageSize, cancellationToken);
	}

	public Task<SalesLead?> GetLeadAsync(long id, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.SalesCrmView);
		return _crm.GetLeadAsync(id, cancellationToken);
	}

	public Task<PageResult<SalesOpportunity>> SearchOpportunitiesAsync(string? searchText, long? stageId = null, SalesOpportunityOutcome? outcome = null, long? ownerUserId = null, int pageNumber = 1, int pageSize = 100, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.SalesCrmView);
		return _crm.SearchOpportunitiesAsync(searchText, stageId, outcome, ownerUserId, pageNumber, pageSize, cancellationToken);
	}

	public Task<SalesOpportunity?> GetOpportunityAsync(long id, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.SalesCrmView);
		return _crm.GetOpportunityAsync(id, cancellationToken);
	}

	public Task<IReadOnlyList<SalesOpportunityStage>> ListStagesAsync(bool activeOnly = true, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.SalesCrmView);
		return _crm.ListStagesAsync(activeOnly, cancellationToken);
	}

	public Task<IReadOnlyList<SalesPipelineStageSummary>> GetPipelineSummaryAsync(long? ownerUserId = null, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.SalesCrmView);
		return _crm.GetPipelineSummaryAsync(ownerUserId, cancellationToken);
	}

	public async Task<SalesLead> SaveLeadAsync(SalesLead value, CancellationToken cancellationToken = default)
	{
		RequireRecordManagement();
		var user = RequireUser();
		NormalizeLead(value);
		if (value.OwnerUserId <= 0) value.OwnerUserId = user.Id;
		RequireOwnershipAuthority(value.OwnerUserId, user.Id);
		if (value.Status == SalesLeadStatus.Converted && value.Id == 0)
			throw new InvalidOperationException("A lead can only become converted through lead conversion.");

		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			SalesLead? before = null;
			if (value.Id > 0)
			{
				before = await _crm.GetLeadAsync(transaction, value.Id, token) ?? throw new InvalidOperationException("Lead was not found.");
				if (before.Version != value.Version) throw new ConcurrencyConflictException("sales lead");
				RequireOwnershipAuthority(before.OwnerUserId, user.Id);
				if (before.Status == SalesLeadStatus.Converted) throw new InvalidOperationException("A converted lead is immutable.");
				if (value.Status == SalesLeadStatus.Converted) throw new InvalidOperationException("A lead can only become converted through lead conversion.");
			}

			var saved = before is null
				? await _crm.CreateLeadAsync(transaction, value, token)
				: await UpdateLeadAsync(transaction, value, before.Version, token);
			await _auditEntries.CreateAsync(transaction, before is null ? _audit.CreateCreatedEntry(saved.Id, saved) : _audit.CreateUpdatedEntry(saved.Id, before, saved), token);
			return saved;
		}, cancellationToken);
	}

	public async Task<SalesLeadConversionResult> ConvertLeadAsync(
		long leadId,
		long version,
		long? existingCustomerId,
		SalesOpportunity opportunity,
		CancellationToken cancellationToken = default)
	{
		RequireRecordManagement();
		var user = RequireUser();
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var lead = await _crm.GetLeadAsync(transaction, leadId, token) ?? throw new InvalidOperationException("Lead was not found.");
			if (lead.Version != version) throw new ConcurrencyConflictException("sales lead");
			RequireOwnershipAuthority(lead.OwnerUserId, user.Id);
			if (lead.Status is SalesLeadStatus.Converted or SalesLeadStatus.Disqualified || lead.ConvertedCustomerId is not null || lead.ConvertedOpportunityId is not null)
				throw new InvalidOperationException("The lead cannot be converted in its current state.");
			if (await _crm.GetOpportunityByLeadAsync(transaction, lead.Id, token) is not null)
				throw new InvalidOperationException("The lead already has a converted opportunity.");

			Customer customer;
			if (existingCustomerId is > 0)
			{
				_authorization.RequirePermission(ApplicationPermission.CustomersView);
				customer = await _customers.GetByIdAsync(transaction, existingCustomerId.Value, token) ?? throw new InvalidOperationException("Customer was not found.");
				if (!customer.IsActive) throw new InvalidOperationException("An inactive customer cannot be linked to a lead conversion.");
			}
			else
			{
				customer = new Customer
				{
					Name = lead.CompanyName ?? lead.PersonName ?? throw new InvalidOperationException("Lead identity is incomplete."),
					ContactName = lead.PersonName,
					Email = lead.Email,
					Phone = lead.Phone,
					Currency = string.IsNullOrWhiteSpace(opportunity.Currency) ? "EUR" : opportunity.Currency,
					IsActive = true
				};
				_customerService.PrepareForCreate(customer);
				await _customers.CreateAsync(transaction, customer, token);
				await _auditEntries.CreateAsync(transaction, _audit.CreateCreatedEntry(customer.Id, customer), token);
			}

			var stage = opportunity.StageId > 0
				? await _crm.GetStageAsync(transaction, opportunity.StageId, token)
				: await _crm.GetFirstActiveStageAsync(transaction, token);
			if (stage is null || !stage.IsActive) throw new InvalidOperationException("An active opportunity stage is required.");

			opportunity.Id = 0;
			opportunity.CustomerId = customer.Id;
			opportunity.CustomerName = customer.Name;
			opportunity.LeadId = lead.Id;
			opportunity.OwnerUserId = opportunity.OwnerUserId > 0 ? opportunity.OwnerUserId : lead.OwnerUserId;
			RequireOwnershipAuthority(opportunity.OwnerUserId, user.Id);
			opportunity.StageId = stage.Id;
			opportunity.StageName = stage.Name;
			opportunity.StageSortOrder = stage.SortOrder;
			opportunity.Outcome = SalesOpportunityOutcome.Open;
			opportunity.CloseReason = null;
			opportunity.ClosedAtUtc = null;
			opportunity.LinkedSalesQuoteId = null;
			NormalizeOpportunity(opportunity);
			var createdOpportunity = await _crm.CreateOpportunityAsync(transaction, opportunity, token);
			await _auditEntries.CreateAsync(transaction, _audit.CreateCreatedEntry(createdOpportunity.Id, createdOpportunity), token);

			var beforeLead = CloneLead(lead);
			lead.Status = SalesLeadStatus.Converted;
			lead.ConvertedCustomerId = customer.Id;
			lead.ConvertedOpportunityId = createdOpportunity.Id;
			lead.ConvertedAtUtc = DateTime.UtcNow;
			if (!await _crm.UpdateLeadAsync(transaction, lead, version, token)) throw new ConcurrencyConflictException("sales lead");
			await _auditEntries.CreateAsync(transaction, _audit.CreateActionEntry(lead.Id, "Converted", beforeLead, lead), token);
			return new SalesLeadConversionResult(lead, customer, createdOpportunity);
		}, cancellationToken);
	}

	public async Task<SalesOpportunity> SaveOpportunityAsync(SalesOpportunity value, CancellationToken cancellationToken = default)
	{
		RequireRecordManagement();
		var user = RequireUser();
		if (value.CustomerId <= 0) throw new ArgumentException("A customer is required.", nameof(value));
		if (value.OwnerUserId <= 0) value.OwnerUserId = user.Id;
		RequireOwnershipAuthority(value.OwnerUserId, user.Id);
		NormalizeOpportunity(value);

		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var customer = await _customers.GetByIdAsync(transaction, value.CustomerId, token) ?? throw new InvalidOperationException("Customer was not found.");
			if (!customer.IsActive) throw new InvalidOperationException("An inactive customer cannot own an opportunity.");
			var stage = value.StageId > 0
				? await _crm.GetStageAsync(transaction, value.StageId, token)
				: await _crm.GetFirstActiveStageAsync(transaction, token);
			if (stage is null || !stage.IsActive) throw new InvalidOperationException("An active opportunity stage is required.");
			value.StageId = stage.Id;

			SalesOpportunity? before = null;
			if (value.Id > 0)
			{
				before = await _crm.GetOpportunityAsync(transaction, value.Id, token) ?? throw new InvalidOperationException("Opportunity was not found.");
				if (before.Version != value.Version) throw new ConcurrencyConflictException("sales opportunity");
				RequireOwnershipAuthority(before.OwnerUserId, user.Id);
				if (before.Outcome != SalesOpportunityOutcome.Open || value.Outcome != SalesOpportunityOutcome.Open)
					throw new InvalidOperationException("Closed opportunities can only be changed through the close workflow.");
			}
			else
			{
				value.Outcome = SalesOpportunityOutcome.Open;
				value.CloseReason = null;
				value.ClosedAtUtc = null;
				value.LinkedSalesQuoteId = null;
			}

			var saved = before is null
				? await _crm.CreateOpportunityAsync(transaction, value, token)
				: await UpdateOpportunityAsync(transaction, value, before.Version, token);
			await _auditEntries.CreateAsync(transaction, before is null ? _audit.CreateCreatedEntry(saved.Id, saved) : _audit.CreateUpdatedEntry(saved.Id, before, saved), token);
			return saved;
		}, cancellationToken);
	}

	public async Task<SalesOpportunity> CloseOpportunityAsync(long id, long version, SalesOpportunityOutcome outcome, string reason, CancellationToken cancellationToken = default)
	{
		RequireRecordManagement();
		if (outcome == SalesOpportunityOutcome.Open) throw new ArgumentException("A closing outcome must be Won or Lost.", nameof(outcome));
		var normalizedReason = Normalize(reason);
		if (string.IsNullOrWhiteSpace(normalizedReason)) throw new ArgumentException("A close reason is required.", nameof(reason));
		if (normalizedReason.Length > 500) throw new ArgumentException("Close reason must not exceed 500 characters.", nameof(reason));
		var user = RequireUser();

		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var value = await _crm.GetOpportunityAsync(transaction, id, token) ?? throw new InvalidOperationException("Opportunity was not found.");
			if (value.Version != version) throw new ConcurrencyConflictException("sales opportunity");
			RequireOwnershipAuthority(value.OwnerUserId, user.Id);
			if (value.Outcome != SalesOpportunityOutcome.Open) throw new InvalidOperationException("The opportunity is already closed.");
			var before = CloneOpportunity(value);
			value.Outcome = outcome;
			value.CloseReason = normalizedReason;
			value.ClosedAtUtc = DateTime.UtcNow;
			value.NextActivityDate = null;
			if (!await _crm.UpdateOpportunityAsync(transaction, value, version, token)) throw new ConcurrencyConflictException("sales opportunity");
			await _auditEntries.CreateAsync(transaction, _audit.CreateActionEntry(value.Id, outcome == SalesOpportunityOutcome.Won ? "Won" : "Lost", before, value), token);
			return value;
		}, cancellationToken);
	}

	public async Task<SalesQuote> CreateQuoteAsync(long opportunityId, long version, SalesQuote quote, CancellationToken cancellationToken = default)
	{
		RequireRecordManagement();
		var opportunity = await GetOpportunityAsync(opportunityId, cancellationToken) ?? throw new InvalidOperationException("Opportunity was not found.");
		if (opportunity.Version != version) throw new ConcurrencyConflictException("sales opportunity");
		if (opportunity.Outcome != SalesOpportunityOutcome.Open) throw new InvalidOperationException("A closed opportunity cannot create a quote.");
		if (opportunity.LinkedSalesQuoteId is not null) throw new InvalidOperationException("The opportunity already has a linked quote.");
		RequireOwnershipAuthority(opportunity.OwnerUserId, RequireUser().Id);

		quote.Id = 0;
		quote.CustomerId = opportunity.CustomerId;
		quote.Currency = opportunity.Currency;
		quote.Status = SalesQuoteStatus.Draft;
		quote.CustomerReference = string.IsNullOrWhiteSpace(quote.CustomerReference) ? opportunity.OpportunityNumber : quote.CustomerReference;
		quote.Notes = string.IsNullOrWhiteSpace(quote.Notes)
			? $"Created from opportunity {opportunity.OpportunityNumber}."
			: $"Created from opportunity {opportunity.OpportunityNumber}. {quote.Notes.Trim()}";
		var savedQuote = await _quotes.SaveDraftAsync(quote, cancellationToken);

		await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var current = await _crm.GetOpportunityAsync(transaction, opportunityId, token) ?? throw new InvalidOperationException("Opportunity was not found.");
			if (current.Version != version || current.LinkedSalesQuoteId is not null) throw new ConcurrencyConflictException("sales opportunity");
			var before = CloneOpportunity(current);
			current.LinkedSalesQuoteId = savedQuote.Id;
			if (!await _crm.UpdateOpportunityAsync(transaction, current, version, token)) throw new ConcurrencyConflictException("sales opportunity");
			await _auditEntries.CreateAsync(transaction, _audit.CreateActionEntry(current.Id, "QuoteLinked", before, current), token);
			return true;
		}, cancellationToken);
		return savedQuote;
	}

	public async Task<SalesOpportunityStage> SaveStageAsync(SalesOpportunityStage stage, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.SalesCrmManage);
		stage.Code = (stage.Code ?? string.Empty).Trim().ToUpperInvariant();
		stage.Name = (stage.Name ?? string.Empty).Trim();
		if (stage.Code.Length is < 1 or > 50 || stage.Name.Length is < 1 or > 120)
			throw new ArgumentException("Opportunity stage code or name is invalid.", nameof(stage));
		if (stage.SortOrder is < 1 or > 10000) throw new ArgumentOutOfRangeException(nameof(stage.SortOrder));
		return await _crm.SaveStageAsync(stage, cancellationToken);
	}

	public Task<IReadOnlyList<SalesActivity>> ListActivitiesAsync(long? leadId, long? opportunityId, int count = 100, CancellationToken cancellationToken = default)
	{
		RequireActivityView();
		return _crm.ListActivitiesAsync(leadId, opportunityId, count, cancellationToken);
	}

	public async Task<SalesActivity> SaveActivityAsync(SalesActivity value, CancellationToken cancellationToken = default)
	{
		RequireActivityManagement();
		var user = RequireUser();
		NormalizeActivity(value);
		if (value.OwnerUserId <= 0) value.OwnerUserId = user.Id;
		RequireActivityOwnership(value.OwnerUserId, user.Id);

		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			SalesActivity? before = null;
			if (value.Id > 0)
			{
				before = await _crm.GetActivityAsync(transaction, value.Id, token) ?? throw new InvalidOperationException("Activity was not found.");
				if (before.Version != value.Version) throw new ConcurrencyConflictException("sales activity");
				RequireActivityOwnership(before.OwnerUserId, user.Id);
				if (before.Status != SalesActivityStatus.Planned) throw new InvalidOperationException("Only planned activities can be edited.");
			}
			value.Status = SalesActivityStatus.Planned;
			value.CompletedAtUtc = null;
			value.CompletedByUserId = null;
			value.CancelledAtUtc = null;
			value.CancelledByUserId = null;
			var saved = before is null
				? await _crm.CreateActivityAsync(transaction, value, token)
				: await UpdateActivityAsync(transaction, value, before.Version, token);
			await _auditEntries.CreateAsync(transaction, before is null ? _audit.CreateCreatedEntry(saved.Id, saved) : _audit.CreateUpdatedEntry(saved.Id, before, saved), token);
			return saved;
		}, cancellationToken);
	}

	public Task<SalesActivity> CompleteActivityAsync(long id, long version, CancellationToken cancellationToken = default) =>
		ChangeActivityStatusAsync(id, version, SalesActivityStatus.Completed, cancellationToken);

	public Task<SalesActivity> CancelActivityAsync(long id, long version, CancellationToken cancellationToken = default) =>
		ChangeActivityStatusAsync(id, version, SalesActivityStatus.Cancelled, cancellationToken);

	internal Task<IReadOnlyList<SalesActivity>> GetOwnedOpenActivitiesAsync(long userId, int count, CancellationToken cancellationToken) =>
		_crm.GetOwnedOpenActivitiesAsync(userId, count, cancellationToken);

	internal Task<IReadOnlyList<SalesOpportunity>> GetOwnedFollowUpOpportunitiesAsync(long userId, DateTime nowUtc, int count, CancellationToken cancellationToken) =>
		_crm.GetOwnedFollowUpOpportunitiesAsync(userId, nowUtc, count, cancellationToken);

	private async Task<SalesActivity> ChangeActivityStatusAsync(long id, long version, SalesActivityStatus target, CancellationToken cancellationToken)
	{
		RequireActivityManagement();
		var user = RequireUser();
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var value = await _crm.GetActivityAsync(transaction, id, token) ?? throw new InvalidOperationException("Activity was not found.");
			if (value.Version != version) throw new ConcurrencyConflictException("sales activity");
			RequireActivityOwnership(value.OwnerUserId, user.Id);
			if (value.Status != SalesActivityStatus.Planned) throw new InvalidOperationException("Only a planned activity can be completed or cancelled.");
			var before = CloneActivity(value);
			value.Status = target;
			if (target == SalesActivityStatus.Completed)
			{
				value.CompletedAtUtc = DateTime.UtcNow;
				value.CompletedByUserId = user.Id;
			}
			else
			{
				value.CancelledAtUtc = DateTime.UtcNow;
				value.CancelledByUserId = user.Id;
			}
			if (!await _crm.UpdateActivityAsync(transaction, value, version, token)) throw new ConcurrencyConflictException("sales activity");
			await _auditEntries.CreateAsync(transaction, _audit.CreateActionEntry(value.Id, target == SalesActivityStatus.Completed ? "Completed" : "Cancelled", before, value), token);
			return value;
		}, cancellationToken);
	}

	private async Task<SalesLead> UpdateLeadAsync(DatabaseTransactionContext transaction, SalesLead value, long version, CancellationToken token)
	{
		if (!await _crm.UpdateLeadAsync(transaction, value, version, token)) throw new ConcurrencyConflictException("sales lead");
		return value;
	}

	private async Task<SalesOpportunity> UpdateOpportunityAsync(DatabaseTransactionContext transaction, SalesOpportunity value, long version, CancellationToken token)
	{
		if (!await _crm.UpdateOpportunityAsync(transaction, value, version, token)) throw new ConcurrencyConflictException("sales opportunity");
		return value;
	}

	private async Task<SalesActivity> UpdateActivityAsync(DatabaseTransactionContext transaction, SalesActivity value, long version, CancellationToken token)
	{
		if (!await _crm.UpdateActivityAsync(transaction, value, version, token)) throw new ConcurrencyConflictException("sales activity");
		return value;
	}

	private void RequireRecordManagement()
	{
		if (!CanManageRecords) throw new UnauthorizedAccessException("The current user cannot manage Sales CRM records.");
	}

	private void RequireActivityView()
	{
		if (!CanViewActivities) throw new UnauthorizedAccessException("The current user cannot view Sales CRM activities.");
	}

	private void RequireActivityManagement()
	{
		if (!CanManageActivities) throw new UnauthorizedAccessException("The current user cannot manage Sales CRM activities.");
	}

	private void RequireOwnershipAuthority(long ownerUserId, long currentUserId)
	{
		if (ownerUserId != currentUserId && !_authorization.HasPermission(ApplicationPermission.SalesCrmManage))
			throw new UnauthorizedAccessException("Only Sales CRM managers can assign or change another user's CRM ownership.");
	}

	private void RequireActivityOwnership(long ownerUserId, long currentUserId)
	{
		if (ownerUserId != currentUserId && !_authorization.HasPermission(ApplicationPermission.SalesCrmManage))
			throw new UnauthorizedAccessException("Only Sales CRM managers can manage another user's activity.");
	}

	private User RequireUser() =>
		_authorization.CurrentUser is { IsActive: true } user
			? user
			: throw new UnauthorizedAccessException("An active signed-in user is required for Sales CRM.");

	private static void NormalizeLead(SalesLead value)
	{
		ArgumentNullException.ThrowIfNull(value);
		value.CompanyName = Normalize(value.CompanyName);
		value.PersonName = Normalize(value.PersonName);
		value.Email = Normalize(value.Email);
		value.Phone = Normalize(value.Phone);
		value.Source = Normalize(value.Source);
		value.NotesSummary = Normalize(value.NotesSummary);
		if (value.CompanyName is null && value.PersonName is null) throw new ArgumentException("A company or person name is required.", nameof(value));
		if (value.CompanyName?.Length > 250 || value.PersonName?.Length > 250 || value.Email?.Length > 250 || value.Phone?.Length > 100 || value.Source?.Length > 120 || value.NotesSummary?.Length > 2000)
			throw new ArgumentException("Lead data exceeds its maximum length.", nameof(value));
	}

	private static void NormalizeOpportunity(SalesOpportunity value)
	{
		ArgumentNullException.ThrowIfNull(value);
		value.Currency = string.IsNullOrWhiteSpace(value.Currency) ? "EUR" : value.Currency.Trim().ToUpperInvariant();
		value.CloseReason = Normalize(value.CloseReason);
		if (value.Currency.Length != 3) throw new ArgumentException("Currency must be a three-letter code.", nameof(value));
		if (value.ExpectedAmount < 0) throw new ArgumentOutOfRangeException(nameof(value.ExpectedAmount));
		if (value.ProbabilityPercent is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(value.ProbabilityPercent));
		if (value.CloseReason?.Length > 500) throw new ArgumentException("Close reason must not exceed 500 characters.", nameof(value));
	}

	private static void NormalizeActivity(SalesActivity value)
	{
		ArgumentNullException.ThrowIfNull(value);
		value.Subject = (value.Subject ?? string.Empty).Trim();
		value.Notes = Normalize(value.Notes);
		if (value.LeadId is not > 0 && value.OpportunityId is not > 0) throw new ArgumentException("An activity requires a lead or opportunity.", nameof(value));
		if (value.Subject.Length is < 1 or > 250) throw new ArgumentException("Activity subject is required and must not exceed 250 characters.", nameof(value));
		if (value.Notes?.Length > 4000) throw new ArgumentException("Activity notes must not exceed 4000 characters.", nameof(value));
	}

	private static SalesLead CloneLead(SalesLead value) => new()
	{
		Id=value.Id,LeadNumber=value.LeadNumber,CompanyName=value.CompanyName,PersonName=value.PersonName,Email=value.Email,Phone=value.Phone,Source=value.Source,OwnerUserId=value.OwnerUserId,OwnerDisplayName=value.OwnerDisplayName,Status=value.Status,NotesSummary=value.NotesSummary,CreatedAtUtc=value.CreatedAtUtc,UpdatedAtUtc=value.UpdatedAtUtc,ConvertedCustomerId=value.ConvertedCustomerId,ConvertedOpportunityId=value.ConvertedOpportunityId,ConvertedAtUtc=value.ConvertedAtUtc,Version=value.Version
	};

	private static SalesOpportunity CloneOpportunity(SalesOpportunity value) => new()
	{
		Id=value.Id,OpportunityNumber=value.OpportunityNumber,CustomerId=value.CustomerId,CustomerName=value.CustomerName,LeadId=value.LeadId,OwnerUserId=value.OwnerUserId,OwnerDisplayName=value.OwnerDisplayName,StageId=value.StageId,StageName=value.StageName,StageSortOrder=value.StageSortOrder,ExpectedCloseDate=value.ExpectedCloseDate,Currency=value.Currency,ExpectedAmount=value.ExpectedAmount,ProbabilityPercent=value.ProbabilityPercent,NextActivityDate=value.NextActivityDate,Outcome=value.Outcome,CloseReason=value.CloseReason,ClosedAtUtc=value.ClosedAtUtc,LinkedSalesQuoteId=value.LinkedSalesQuoteId,CreatedAtUtc=value.CreatedAtUtc,UpdatedAtUtc=value.UpdatedAtUtc,Version=value.Version
	};

	private static SalesActivity CloneActivity(SalesActivity value) => new()
	{
		Id=value.Id,LeadId=value.LeadId,OpportunityId=value.OpportunityId,Type=value.Type,DueAtUtc=value.DueAtUtc,OwnerUserId=value.OwnerUserId,OwnerDisplayName=value.OwnerDisplayName,Status=value.Status,Subject=value.Subject,Notes=value.Notes,CompletedAtUtc=value.CompletedAtUtc,CompletedByUserId=value.CompletedByUserId,CancelledAtUtc=value.CancelledAtUtc,CancelledByUserId=value.CancelledByUserId,Version=value.Version
	};

	private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
