// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public enum SalesLeadStatus
{
	New = 1,
	Contacted = 2,
	Qualified = 3,
	Converted = 4,
	Disqualified = 5
}

public enum SalesOpportunityOutcome
{
	Open = 0,
	Won = 1,
	Lost = 2
}

public enum SalesActivityType
{
	Call = 1,
	Email = 2,
	Meeting = 3,
	FollowUp = 4
}

public enum SalesActivityStatus
{
	Planned = 1,
	Completed = 2,
	Cancelled = 3
}

public sealed class SalesLead
{
	public long Id { get; set; }
	public string LeadNumber { get; set; } = string.Empty;
	public string? CompanyName { get; set; }
	public string? PersonName { get; set; }
	public string? Email { get; set; }
	public string? Phone { get; set; }
	public string? Source { get; set; }
	public long OwnerUserId { get; set; }
	public string? OwnerDisplayName { get; set; }
	public SalesLeadStatus Status { get; set; } = SalesLeadStatus.New;
	public string? NotesSummary { get; set; }
	public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
	public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
	public long? ConvertedCustomerId { get; set; }
	public long? ConvertedOpportunityId { get; set; }
	public DateTime? ConvertedAtUtc { get; set; }
	public long Version { get; set; } = 1;
	public string DisplayName => !string.IsNullOrWhiteSpace(CompanyName) ? CompanyName : PersonName ?? LeadNumber;
}

public sealed class SalesOpportunityStage
{
	public long Id { get; set; }
	public string Code { get; set; } = string.Empty;
	public string Name { get; set; } = string.Empty;
	public int SortOrder { get; set; }
	public bool IsActive { get; set; } = true;
	public long Version { get; set; } = 1;
}

public sealed class SalesOpportunity
{
	public long Id { get; set; }
	public string OpportunityNumber { get; set; } = string.Empty;
	public long CustomerId { get; set; }
	public string CustomerName { get; set; } = string.Empty;
	public long? LeadId { get; set; }
	public long OwnerUserId { get; set; }
	public string? OwnerDisplayName { get; set; }
	public long StageId { get; set; }
	public string StageName { get; set; } = string.Empty;
	public int StageSortOrder { get; set; }
	public DateTime? ExpectedCloseDate { get; set; }
	public string Currency { get; set; } = "EUR";
	public decimal ExpectedAmount { get; set; }
	public int ProbabilityPercent { get; set; }
	public DateTime? NextActivityDate { get; set; }
	public SalesOpportunityOutcome Outcome { get; set; } = SalesOpportunityOutcome.Open;
	public string? CloseReason { get; set; }
	public DateTime? ClosedAtUtc { get; set; }
	public long? LinkedSalesQuoteId { get; set; }
	public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
	public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
	public long Version { get; set; } = 1;
}

public sealed class SalesActivity
{
	public long Id { get; set; }
	public long? LeadId { get; set; }
	public long? OpportunityId { get; set; }
	public SalesActivityType Type { get; set; } = SalesActivityType.FollowUp;
	public DateTime DueAtUtc { get; set; }
	public long OwnerUserId { get; set; }
	public string? OwnerDisplayName { get; set; }
	public SalesActivityStatus Status { get; set; } = SalesActivityStatus.Planned;
	public string Subject { get; set; } = string.Empty;
	public string? Notes { get; set; }
	public DateTime? CompletedAtUtc { get; set; }
	public long? CompletedByUserId { get; set; }
	public DateTime? CancelledAtUtc { get; set; }
	public long? CancelledByUserId { get; set; }
	public long Version { get; set; } = 1;
}

public sealed record SalesPipelineStageSummary(
	long StageId,
	string StageName,
	int SortOrder,
	int OpportunityCount,
	decimal ExpectedAmount,
	decimal WeightedAmount);

public sealed record SalesLeadConversionResult(
	SalesLead Lead,
	Customer Customer,
	SalesOpportunity Opportunity);
