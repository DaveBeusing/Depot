// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public enum ApprovalSubjectKind
{
	PurchaseOrder = 1,
	SalesOrder = 2,
	AccountsPayableException = 3,
	PaymentProposal = 4,
	FinanceBudget = 5,
	PurchaseRequisition = 6
}

public enum ApprovalConditionKind
{
	MinimumAmount = 1,
	MaximumAmount = 2,
	Currency = 3,
	LegalEntityId = 4,
	AccountingBookId = 5
}

public enum ApprovalApproverKind
{
	Role = 1,
	User = 2
}

public enum ApprovalInstanceStatus
{
	Pending = 1,
	Approved = 2,
	Rejected = 3,
	Cancelled = 4,
	Superseded = 5
}

public enum ApprovalDecisionKind
{
	Approved = 1,
	Rejected = 2,
	Cancelled = 3,
	Superseded = 4
}

public sealed class ApprovalPolicy
{
	public Guid Id { get; set; }
	public string Name { get; set; } = string.Empty;
	public string? Description { get; set; }
	public ApprovalSubjectKind SubjectKind { get; set; }
	public int Priority { get; set; }
	public bool IsActive { get; set; }
	public DateTime? EffectiveFromUtc { get; set; }
	public DateTime? EffectiveToUtc { get; set; }
	public int Version { get; set; }
	public long? CreatedByUserId { get; set; }
	public string? CreatedByUserDisplay { get; set; }
	public DateTime CreatedAtUtc { get; set; }
	public long? UpdatedByUserId { get; set; }
	public string? UpdatedByUserDisplay { get; set; }
	public DateTime UpdatedAtUtc { get; set; }
	public List<ApprovalPolicyCondition> Conditions { get; set; } = [];
	public List<ApprovalPolicyStage> Stages { get; set; } = [];
}

public sealed class ApprovalPolicyCondition
{
	public ApprovalConditionKind Kind { get; set; }
	public decimal? DecimalValue { get; set; }
	public string? StringValue { get; set; }
	public Guid? GuidValue { get; set; }
}

public sealed class ApprovalPolicyStage
{
	public int Order { get; set; }
	public string Name { get; set; } = string.Empty;
	public List<ApprovalApproverTarget> Approvers { get; set; } = [];
}

public sealed class ApprovalApproverTarget
{
	public ApprovalApproverKind Kind { get; set; }
	public string? RoleCode { get; set; }
	public long? UserId { get; set; }
}

public sealed record ApprovalSubjectAttributes(
	ApprovalSubjectKind SubjectKind,
	string SubjectId,
	decimal? Amount = null,
	string? Currency = null,
	Guid? LegalEntityId = null,
	Guid? AccountingBookId = null);

public sealed class ApprovalPlanSnapshot
{
	public Guid InstanceId { get; set; }
	public Guid PolicyId { get; set; }
	public int PolicyVersion { get; set; }
	public string PolicyName { get; set; } = string.Empty;
	public ApprovalSubjectKind SubjectKind { get; set; }
	public string SubjectId { get; set; } = string.Empty;
	public DateTime CreatedAtUtc { get; set; }
	public List<ApprovalSnapshotStage> Stages { get; set; } = [];
}

public sealed class ApprovalSnapshotStage
{
	public int Order { get; set; }
	public string Name { get; set; } = string.Empty;
	public List<ApprovalApproverTarget> Approvers { get; set; } = [];
}

public sealed class ApprovalInstance
{
	public Guid Id { get; set; }
	public ApprovalSubjectKind SubjectKind { get; set; }
	public string SubjectId { get; set; } = string.Empty;
	public Guid PolicyId { get; set; }
	public int PolicyVersion { get; set; }
	public string PolicyName { get; set; } = string.Empty;
	public ApprovalPlanSnapshot Snapshot { get; set; } = new();
	public int CurrentStageOrder { get; set; }
	public ApprovalInstanceStatus Status { get; set; }
	public int Version { get; set; }
	public DateTime CreatedAtUtc { get; set; }
}

public sealed class ApprovalDecisionEvidence
{
	public Guid Id { get; set; }
	public Guid InstanceId { get; set; }
	public int StageOrder { get; set; }
	public ApprovalDecisionKind Decision { get; set; }
	public long UserId { get; set; }
	public string UserDisplay { get; set; } = string.Empty;
	public string? Comment { get; set; }
	public DateTime DecidedAtUtc { get; set; }
}

public sealed class ApprovalDelegation
{
	public Guid Id { get; set; }
	public long FromUserId { get; set; }
	public long ToUserId { get; set; }
	public ApprovalSubjectKind? SubjectKind { get; set; }
	public DateTime EffectiveFromUtc { get; set; }
	public DateTime EffectiveToUtc { get; set; }
	public long CreatedByUserId { get; set; }
	public string CreatedByUserDisplay { get; set; } = string.Empty;
	public DateTime CreatedAtUtc { get; set; }
}


public sealed record ApprovalPolicyValidationIssue(string Code, string Message);

public sealed record ApprovalResolutionPreview(
	ApprovalPolicy Policy,
	ApprovalPlanSnapshot Snapshot);
