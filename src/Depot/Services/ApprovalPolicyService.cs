// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class ApprovalPolicyService
{
	private const int MaxPolicyCandidates = 200;
	private const int MaxConditions = 16;
	private const int MaxStages = 20;
	private const int MaxApproversPerStage = 50;
	private readonly ApprovalPolicyRepository _policies;
	private readonly RoleRepository _roles;
	private readonly UserRepository _users;
	private readonly IAuthorizationService _authorization;
	private readonly TimeProvider _timeProvider;

	public ApprovalPolicyService(
		ApprovalPolicyRepository policies,
		RoleRepository roles,
		UserRepository users,
		IAuthorizationService authorization,
		TimeProvider? timeProvider = null)
	{
		_policies = policies;
		_roles = roles;
		_users = users;
		_authorization = authorization;
		_timeProvider = timeProvider ?? TimeProvider.System;
	}

	public Task<IReadOnlyList<ApprovalPolicy>> GetPageAsync(
		ApprovalSubjectKind? subjectKind,
		int offset,
		int count,
		CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.ApprovalPoliciesView);
		if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
		if (count is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(count));
		return _policies.GetPageAsync(subjectKind, offset, count, cancellationToken);
	}

	public async Task<ApprovalPolicy?> GetAsync(Guid id, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.ApprovalPoliciesView);
		return await _policies.GetAsync(id, cancellationToken);
	}

	public async Task<ApprovalPolicy> CreateAsync(ApprovalPolicy policy, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.ApprovalPoliciesManage);
		ArgumentNullException.ThrowIfNull(policy);
		if (policy.IsActive)
			throw new InvalidOperationException("New approval policies must be saved as inactive drafts before activation.");

		var now = UtcNow();
		var user = CurrentUser();
		var candidate = Copy(policy);
		candidate.Id = candidate.Id == Guid.Empty ? Guid.NewGuid() : candidate.Id;
		candidate.Version = 1;
		candidate.IsActive = false;
		candidate.CreatedByUserId = user.Id;
		candidate.CreatedByUserDisplay = user.DisplayName;
		candidate.CreatedAtUtc = now;
		candidate.UpdatedByUserId = user.Id;
		candidate.UpdatedByUserDisplay = user.DisplayName;
		candidate.UpdatedAtUtc = now;
		Normalize(candidate);
		await ValidateAndThrowAsync(candidate, cancellationToken);
		await _policies.CreateAsync(candidate, cancellationToken);
		return candidate;
	}

	public async Task<ApprovalPolicy> UpdateAsync(ApprovalPolicy policy, int expectedVersion, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.ApprovalPoliciesManage);
		ArgumentNullException.ThrowIfNull(policy);
		if (policy.Id == Guid.Empty) throw new ArgumentException("Approval policy id is required.", nameof(policy));
		if (expectedVersion < 1) throw new ArgumentOutOfRangeException(nameof(expectedVersion));

		var current = await _policies.GetAsync(policy.Id, cancellationToken)
			?? throw new KeyNotFoundException("Approval policy was not found.");
		if (current.Version != expectedVersion) throw new ConcurrencyConflictException("approval policy");

		var user = CurrentUser();
		var candidate = Copy(policy);
		candidate.CreatedByUserId = current.CreatedByUserId;
		candidate.CreatedByUserDisplay = current.CreatedByUserDisplay;
		candidate.CreatedAtUtc = current.CreatedAtUtc;
		candidate.UpdatedByUserId = user.Id;
		candidate.UpdatedByUserDisplay = user.DisplayName;
		candidate.UpdatedAtUtc = UtcNow();
		candidate.Version = expectedVersion + 1;
		Normalize(candidate);
		await ValidateAndThrowAsync(candidate, cancellationToken);
		await _policies.UpdateAsync(candidate, expectedVersion, cancellationToken);
		return candidate;
	}

	public async Task<ApprovalPolicy> ActivateAsync(Guid id, int expectedVersion, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.ApprovalPoliciesManage);
		var current = await _policies.GetAsync(id, cancellationToken)
			?? throw new KeyNotFoundException("Approval policy was not found.");
		if (current.Version != expectedVersion) throw new ConcurrencyConflictException("approval policy");
		if (current.IsActive) return current;
		current.IsActive = true;
		return await UpdateAsync(current, expectedVersion, cancellationToken);
	}

	public async Task<ApprovalPolicy> DeactivateAsync(Guid id, int expectedVersion, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.ApprovalPoliciesManage);
		var current = await _policies.GetAsync(id, cancellationToken)
			?? throw new KeyNotFoundException("Approval policy was not found.");
		if (current.Version != expectedVersion) throw new ConcurrencyConflictException("approval policy");
		if (!current.IsActive) return current;
		current.IsActive = false;
		return await UpdateAsync(current, expectedVersion, cancellationToken);
	}

	public async Task<ApprovalDelegation> CreateDelegationAsync(
		ApprovalDelegation delegation,
		CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.ApprovalPoliciesManage);
		ArgumentNullException.ThrowIfNull(delegation);
		if (delegation.FromUserId <= 0 || delegation.ToUserId <= 0 || delegation.FromUserId == delegation.ToUserId)
			throw new InvalidOperationException("Delegation requires two different active users.");
		if (delegation.SubjectKind is not null && !Enum.IsDefined(delegation.SubjectKind.Value))
			throw new InvalidOperationException("Delegation subject kind is not supported.");
		var from = await _users.GetByIdAsync(delegation.FromUserId, cancellationToken);
		var to = await _users.GetByIdAsync(delegation.ToUserId, cancellationToken);
		if (from is null || !from.IsActive || to is null || !to.IsActive)
			throw new InvalidOperationException("Delegation requires two active users.");

		var candidate = new ApprovalDelegation
		{
			Id = delegation.Id == Guid.Empty ? Guid.NewGuid() : delegation.Id,
			FromUserId = delegation.FromUserId,
			ToUserId = delegation.ToUserId,
			SubjectKind = delegation.SubjectKind,
			EffectiveFromUtc = delegation.EffectiveFromUtc.ToUniversalTime(),
			EffectiveToUtc = delegation.EffectiveToUtc.ToUniversalTime(),
			CreatedByUserId = CurrentUser().Id,
			CreatedByUserDisplay = CurrentUser().DisplayName,
			CreatedAtUtc = UtcNow()
		};
		if (candidate.EffectiveFromUtc >= candidate.EffectiveToUtc)
			throw new InvalidOperationException("Delegation Effective To must be later than Effective From.");
		await _policies.CreateDelegationAsync(candidate, cancellationToken);
		return candidate;
	}

	public async Task<IReadOnlyList<ApprovalPolicyValidationIssue>> ValidateAsync(
		ApprovalPolicy policy,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(policy);
		var issues = new List<ApprovalPolicyValidationIssue>();

		if (!Enum.IsDefined(policy.SubjectKind))
			issues.Add(new("subject.unsupported", "The approval subject kind is not supported."));
		if (string.IsNullOrWhiteSpace(policy.Name))
			issues.Add(new("name.required", "A policy name is required."));
		else if (policy.Name.Trim().Length > 200)
			issues.Add(new("name.length", "The policy name must not exceed 200 characters."));
		if (policy.Description?.Trim().Length > 2000)
			issues.Add(new("description.length", "The policy description must not exceed 2000 characters."));
		if (policy.Priority is < -10000 or > 10000)
			issues.Add(new("priority.range", "Policy priority must be between -10000 and 10000."));
		if (policy.EffectiveFromUtc is not null && policy.EffectiveToUtc is not null &&
			policy.EffectiveFromUtc.Value.ToUniversalTime() >= policy.EffectiveToUtc.Value.ToUniversalTime())
			issues.Add(new("effective.range", "Effective To must be later than Effective From."));
		if (policy.Conditions.Count > MaxConditions)
			issues.Add(new("conditions.limit", $"A policy supports at most {MaxConditions} conditions."));
		if (policy.Stages.Count is < 1 or > MaxStages)
			issues.Add(new("stages.count", $"A policy requires between 1 and {MaxStages} approval stages."));

		var duplicateCondition = policy.Conditions
			.GroupBy(value => value.Kind)
			.FirstOrDefault(group => group.Count() > 1);
		if (duplicateCondition is not null)
			issues.Add(new("conditions.duplicate", $"Condition '{duplicateCondition.Key}' may only occur once."));

		decimal? minimum = null;
		decimal? maximum = null;
		foreach (var condition in policy.Conditions)
		{
			ValidateCondition(policy.SubjectKind, condition, issues);
			if (condition.Kind == ApprovalConditionKind.MinimumAmount) minimum = condition.DecimalValue;
			if (condition.Kind == ApprovalConditionKind.MaximumAmount) maximum = condition.DecimalValue;
		}
		if (minimum is not null && maximum is not null && minimum > maximum)
			issues.Add(new("amount.range", "Minimum amount must not exceed maximum amount."));

		var orders = new HashSet<int>();
		foreach (var stage in policy.Stages)
		{
			if (stage.Order < 1 || !orders.Add(stage.Order))
				issues.Add(new("stage.order", "Stage order values must be unique positive integers."));
			if (string.IsNullOrWhiteSpace(stage.Name))
				issues.Add(new("stage.name", "Every approval stage requires a name."));
			else if (stage.Name.Trim().Length > 200)
				issues.Add(new("stage.name.length", "Approval stage names must not exceed 200 characters."));
			if (stage.Approvers.Count is < 1 or > MaxApproversPerStage)
				issues.Add(new("stage.approvers", $"Every approval stage requires between 1 and {MaxApproversPerStage} approver targets."));

			var targetKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var target in stage.Approvers)
			{
				if (!Enum.IsDefined(target.Kind))
				{
					issues.Add(new("approver.kind", "An approver target uses an unsupported kind."));
					continue;
				}
				if (target.Kind == ApprovalApproverKind.Role)
				{
					var code = target.RoleCode?.Trim();
					if (string.IsNullOrWhiteSpace(code) || target.UserId is not null)
					{
						issues.Add(new("approver.role", "Role approvers require only a role code."));
						continue;
					}
					if (!targetKeys.Add($"role:{code}"))
						issues.Add(new("approver.duplicate", $"Role approver '{code}' is duplicated in stage {stage.Order}."));
					var role = await _roles.GetByCodeAsync(code, cancellationToken);
					if (role is null || !role.IsActive)
						issues.Add(new("approver.role.inactive", $"Role '{code}' does not exist or is inactive."));
				}
				else
				{
					if (target.UserId is not > 0 || !string.IsNullOrWhiteSpace(target.RoleCode))
					{
						issues.Add(new("approver.user", "User approvers require only a positive user id."));
						continue;
					}
					if (!targetKeys.Add($"user:{target.UserId.Value}"))
						issues.Add(new("approver.duplicate", $"User approver '{target.UserId.Value}' is duplicated in stage {stage.Order}."));
					var user = await _users.GetByIdAsync(target.UserId.Value, cancellationToken);
					if (user is null || !user.IsActive)
						issues.Add(new("approver.user.inactive", $"User '{target.UserId.Value}' does not exist or is inactive."));
				}
			}
		}

		var orderedStages = policy.Stages.Select(value => value.Order).Order().ToArray();
		for (var index = 0; index < orderedStages.Length; index++)
		{
			if (orderedStages[index] != index + 1)
			{
				issues.Add(new("stage.sequence", "Approval stages must be sequential and start at stage 1."));
				break;
			}
		}

		return issues;
	}

	public async Task<bool> CanCurrentUserDecideAsync(
		ApprovalSubjectKind subjectKind,
		string subjectId,
		CancellationToken cancellationToken = default)
	{
		if (!_authorization.HasPermission(RequiredDecisionPermission(subjectKind))) return false;
		if (_authorization.CurrentUser is not { IsActive: true } user) return false;
		var instance = await _policies.GetPendingInstanceAsync(subjectKind, NormalizeSubjectId(subjectId), cancellationToken);
		if (instance is null) return true;
		var stage = instance.Snapshot.Stages.SingleOrDefault(value => value.Order == instance.CurrentStageOrder);
		return stage is not null && await IsEligibleAsync(user, subjectKind, stage, UtcNow(), cancellationToken);
	}

	public async Task<ApprovalResolutionPreview> PreviewAsync(
		ApprovalSubjectAttributes attributes,
		CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.ApprovalPoliciesView);
		return await ResolveInternalAsync(attributes, UtcNow(), cancellationToken);
	}

	public async Task<ApprovalPlanSnapshot> ResolveSnapshotAsync(
		ApprovalSubjectAttributes attributes,
		CancellationToken cancellationToken = default) =>
		(await ResolveInternalAsync(attributes, UtcNow(), cancellationToken)).Snapshot;

	public async Task<ApprovalInstance> StartAsync(
		ApprovalSubjectAttributes attributes,
		CancellationToken cancellationToken = default)
	{
		var subjectId = NormalizeSubjectId(attributes.SubjectId);
		var existing = await _policies.GetPendingInstanceAsync(attributes.SubjectKind, subjectId, cancellationToken);
		if (existing is not null) return existing;
		var snapshot = await ResolveSnapshotAsync(attributes with { SubjectId = subjectId }, cancellationToken);
		var instance = CreateInstance(snapshot);
		await _policies.CreateInstanceAsync(instance, cancellationToken);
		return instance;
	}

	internal async Task<ApprovalInstance> StartResolvedAsync(
		Depot.Data.DatabaseTransactionContext transaction,
		ApprovalPlanSnapshot snapshot,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(transaction);
		ArgumentNullException.ThrowIfNull(snapshot);
		var existing = await _policies.GetPendingInstanceAsync(transaction, snapshot.SubjectKind, NormalizeSubjectId(snapshot.SubjectId), cancellationToken);
		if (existing is not null) return existing;
		var instance = CreateInstance(snapshot);
		await _policies.CreateInstanceAsync(transaction, instance, cancellationToken);
		return instance;
	}

	internal async Task<ApprovalPreparedDecision> PrepareDecisionAsync(
		ApprovalSubjectKind subjectKind,
		string subjectId,
		ApprovalDecisionKind decision,
		string? comment,
		CancellationToken cancellationToken)
	{
		if (decision is not (ApprovalDecisionKind.Approved or ApprovalDecisionKind.Rejected))
			throw new ArgumentOutOfRangeException(nameof(decision));
		var user = CurrentUser();
		var instance = await _policies.GetPendingInstanceAsync(subjectKind, NormalizeSubjectId(subjectId), cancellationToken)
			?? throw new InvalidOperationException($"No pending approval instance exists for {subjectKind} subject '{subjectId}'.");
		_authorization.RequirePermission(RequiredDecisionPermission(instance.SubjectKind));
		if (comment?.Trim().Length > 2000)
			throw new ArgumentException("Approval comments must not exceed 2000 characters.", nameof(comment));
		var stage = instance.Snapshot.Stages.SingleOrDefault(value => value.Order == instance.CurrentStageOrder)
			?? throw new InvalidOperationException("The approval snapshot does not contain the expected current stage.");
		if (!await IsEligibleAsync(user, instance.SubjectKind, stage, UtcNow(), cancellationToken))
			throw new UnauthorizedAccessException("The current user is not an eligible approver for the current approval stage.");

		var expectedVersion = instance.Version;
		var isFinalApproval = false;
		if (decision == ApprovalDecisionKind.Rejected)
		{
			instance.Status = ApprovalInstanceStatus.Rejected;
		}
		else
		{
			var next = instance.Snapshot.Stages.Where(value => value.Order > stage.Order).OrderBy(value => value.Order).FirstOrDefault();
			if (next is null)
			{
				instance.Status = ApprovalInstanceStatus.Approved;
				isFinalApproval = true;
			}
			else instance.CurrentStageOrder = next.Order;
		}
		instance.Version++;
		var evidence = new ApprovalDecisionEvidence
		{
			Id = Guid.NewGuid(),
			InstanceId = instance.Id,
			StageOrder = stage.Order,
			Decision = decision,
			UserId = user.Id,
			UserDisplay = user.DisplayName,
			Comment = NormalizeOptional(comment),
			DecidedAtUtc = UtcNow()
		};
		return new ApprovalPreparedDecision(instance, expectedVersion, evidence, isFinalApproval);
	}

	internal Task ApplyPreparedDecisionAsync(
		Depot.Data.DatabaseTransactionContext transaction,
		ApprovalPreparedDecision prepared,
		CancellationToken cancellationToken) =>
		_policies.RecordDecisionAsync(transaction, prepared.Instance, prepared.ExpectedVersion, prepared.Evidence, cancellationToken);

	internal Task ApplyPreparedDecisionAsync(
		ApprovalPreparedDecision prepared,
		CancellationToken cancellationToken) =>
		_policies.RecordDecisionAsync(prepared.Instance, prepared.ExpectedVersion, prepared.Evidence, cancellationToken);

	public Task<ApprovalInstance?> GetPendingInstanceAsync(
		ApprovalSubjectKind subjectKind,
		string subjectId,
		CancellationToken cancellationToken = default) =>
		_policies.GetPendingInstanceAsync(subjectKind, NormalizeSubjectId(subjectId), cancellationToken);

	public async Task<ApprovalInstance> ApproveStageAsync(
		Guid instanceId,
		string? comment = null,
		CancellationToken cancellationToken = default) =>
		await DecideStageAsync(instanceId, ApprovalDecisionKind.Approved, comment, cancellationToken);

	public async Task<ApprovalInstance> RejectStageAsync(
		Guid instanceId,
		string? comment = null,
		CancellationToken cancellationToken = default) =>
		await DecideStageAsync(instanceId, ApprovalDecisionKind.Rejected, comment, cancellationToken);

	private async Task<ApprovalInstance> DecideStageAsync(
		Guid instanceId,
		ApprovalDecisionKind decision,
		string? comment,
		CancellationToken cancellationToken)
	{
		var current = await GetInstanceRequiredAsync(instanceId, cancellationToken);
		var prepared = await PrepareDecisionAsync(current.SubjectKind, current.SubjectId, decision, comment, cancellationToken);
		if (prepared.Instance.Id != instanceId)
			throw new ConcurrencyConflictException("approval instance");
		await ApplyPreparedDecisionAsync(prepared, cancellationToken);
		return prepared.Instance;
	}

	private async Task<ApprovalResolutionPreview> ResolveInternalAsync(
		ApprovalSubjectAttributes attributes,
		DateTime atUtc,
		CancellationToken cancellationToken)
	{
		if (!Enum.IsDefined(attributes.SubjectKind))
			throw new ArgumentOutOfRangeException(nameof(attributes), "The approval subject kind is not supported.");
		var subjectId = NormalizeSubjectId(attributes.SubjectId);
		var normalizedAttributes = attributes with
		{
			SubjectId = subjectId,
			Currency = NormalizeOptional(attributes.Currency)?.ToUpperInvariant()
		};
		var candidates = await _policies.GetActiveCandidatesAsync(attributes.SubjectKind, atUtc, cancellationToken);
		if (candidates.Count > MaxPolicyCandidates)
			throw new InvalidOperationException($"Approval policy resolution exceeded the bounded candidate limit of {MaxPolicyCandidates}.");

		var matching = new List<ApprovalPolicy>();
		foreach (var candidate in candidates)
		{
			var issues = await ValidateAsync(candidate, cancellationToken);
			if (issues.Count != 0)
				throw new InvalidOperationException($"Active approval policy '{candidate.Name}' is invalid: {string.Join("; ", issues.Select(value => value.Message))}");
			if (Matches(candidate, normalizedAttributes)) matching.Add(candidate);
		}
		if (matching.Count == 0)
			throw new InvalidOperationException($"No active approval policy matches {attributes.SubjectKind} subject '{subjectId}'.");

		var highestPriority = matching.Max(value => value.Priority);
		var winners = matching.Where(value => value.Priority == highestPriority).ToArray();
		if (winners.Length != 1)
			throw new InvalidOperationException($"Approval policy resolution is ambiguous for {attributes.SubjectKind} subject '{subjectId}': {winners.Length} equally applicable policies have priority {highestPriority}.");

		var policy = winners[0];
		var snapshot = new ApprovalPlanSnapshot
		{
			InstanceId = Guid.NewGuid(),
			PolicyId = policy.Id,
			PolicyVersion = policy.Version,
			PolicyName = policy.Name,
			SubjectKind = policy.SubjectKind,
			SubjectId = subjectId,
			CreatedAtUtc = atUtc,
			Stages = policy.Stages
				.OrderBy(value => value.Order)
				.Select(value => new ApprovalSnapshotStage
				{
					Order = value.Order,
					Name = value.Name,
					Approvers = value.Approvers.Select(CopyTarget).ToList()
				})
				.ToList()
		};
		return new ApprovalResolutionPreview(Copy(policy), snapshot);
	}

	private async Task<bool> IsEligibleAsync(
		User currentUser,
		ApprovalSubjectKind subjectKind,
		ApprovalSnapshotStage stage,
		DateTime atUtc,
		CancellationToken cancellationToken)
	{
		var activeRoles = await _roles.GetUserRolesAsync(currentUser.Id, cancellationToken);
		if (stage.Approvers.Any(target =>
			(target.Kind == ApprovalApproverKind.User && target.UserId == currentUser.Id) ||
			(target.Kind == ApprovalApproverKind.Role && activeRoles.Any(role => role.IsActive && string.Equals(role.Code, target.RoleCode, StringComparison.Ordinal)))))
			return true;

		foreach (var target in stage.Approvers.Where(value => value.Kind == ApprovalApproverKind.User && value.UserId is > 0))
		{
			var delegations = await _policies.GetEffectiveDelegationsAsync(target.UserId!.Value, subjectKind, atUtc, cancellationToken);
			if (delegations.Any(value => value.ToUserId == currentUser.Id)) return true;
		}

		foreach (var target in stage.Approvers.Where(value => value.Kind == ApprovalApproverKind.Role && !string.IsNullOrWhiteSpace(value.RoleCode)))
		{
			var role = await _roles.GetByCodeAsync(target.RoleCode!, cancellationToken);
			if (role is null || !role.IsActive) continue;
			var members = await _roles.GetRoleEffectivePermissionUsersAsync(role.Id, cancellationToken);
			foreach (var member in members.Where(value => value.IsActive))
			{
				var delegations = await _policies.GetEffectiveDelegationsAsync(member.UserId, subjectKind, atUtc, cancellationToken);
				if (delegations.Any(value => value.ToUserId == currentUser.Id)) return true;
			}
		}

		return false;
	}

	private static bool Matches(ApprovalPolicy policy, ApprovalSubjectAttributes attributes)
	{
		foreach (var condition in policy.Conditions)
		{
			var matches = condition.Kind switch
			{
				ApprovalConditionKind.MinimumAmount => attributes.Amount is not null && attributes.Amount >= condition.DecimalValue,
				ApprovalConditionKind.MaximumAmount => attributes.Amount is not null && attributes.Amount <= condition.DecimalValue,
				ApprovalConditionKind.Currency => !string.IsNullOrWhiteSpace(attributes.Currency) &&
					string.Equals(attributes.Currency, condition.StringValue, StringComparison.OrdinalIgnoreCase),
				ApprovalConditionKind.LegalEntityId => attributes.LegalEntityId is not null && attributes.LegalEntityId == condition.GuidValue,
				ApprovalConditionKind.AccountingBookId => attributes.AccountingBookId is not null && attributes.AccountingBookId == condition.GuidValue,
				_ => false
			};
			if (!matches) return false;
		}
		return true;
	}

	private static void ValidateCondition(
		ApprovalSubjectKind subjectKind,
		ApprovalPolicyCondition condition,
		ICollection<ApprovalPolicyValidationIssue> issues)
	{
		if (!Enum.IsDefined(condition.Kind))
		{
			issues.Add(new("condition.unsupported", "An approval condition uses an unsupported kind."));
			return;
		}
		var financeSubject = subjectKind is ApprovalSubjectKind.AccountsPayableException or ApprovalSubjectKind.PaymentProposal or ApprovalSubjectKind.FinanceBudget;
		if ((condition.Kind is ApprovalConditionKind.LegalEntityId or ApprovalConditionKind.AccountingBookId) && !financeSubject)
		{
			issues.Add(new("condition.subject", $"Condition '{condition.Kind}' is not supported for '{subjectKind}'."));
			return;
		}
		switch (condition.Kind)
		{
			case ApprovalConditionKind.MinimumAmount:
			case ApprovalConditionKind.MaximumAmount:
				if (condition.DecimalValue is null || condition.DecimalValue < 0 || condition.StringValue is not null || condition.GuidValue is not null)
					issues.Add(new("condition.amount", $"Condition '{condition.Kind}' requires only a non-negative decimal value."));
				break;
			case ApprovalConditionKind.Currency:
				var currency = condition.StringValue?.Trim();
				if (currency is null || currency.Length != 3 || !currency.All(char.IsLetter) || condition.DecimalValue is not null || condition.GuidValue is not null)
					issues.Add(new("condition.currency", "Currency conditions require only a three-letter currency code."));
				break;
			case ApprovalConditionKind.LegalEntityId:
			case ApprovalConditionKind.AccountingBookId:
				if (condition.GuidValue is null || condition.GuidValue == Guid.Empty || condition.DecimalValue is not null || condition.StringValue is not null)
					issues.Add(new("condition.selector", $"Condition '{condition.Kind}' requires only a non-empty identifier."));
				break;
		}
	}

	private async Task ValidateAndThrowAsync(ApprovalPolicy policy, CancellationToken cancellationToken)
	{
		var issues = await ValidateAsync(policy, cancellationToken);
		if (issues.Count != 0)
			throw new InvalidOperationException($"Approval policy is invalid: {string.Join("; ", issues.Select(value => value.Message))}");
	}

	private async Task<ApprovalInstance> GetInstanceRequiredAsync(Guid instanceId, CancellationToken cancellationToken) =>
		await _policies.GetInstanceAsync(instanceId, cancellationToken)
			?? throw new KeyNotFoundException("Approval instance was not found.");

	private static ApprovalInstance CreateInstance(ApprovalPlanSnapshot snapshot) => new()
	{
		Id = snapshot.InstanceId,
		SubjectKind = snapshot.SubjectKind,
		SubjectId = snapshot.SubjectId,
		PolicyId = snapshot.PolicyId,
		PolicyVersion = snapshot.PolicyVersion,
		PolicyName = snapshot.PolicyName,
		Snapshot = snapshot,
		CurrentStageOrder = 1,
		Status = ApprovalInstanceStatus.Pending,
		Version = 1,
		CreatedAtUtc = snapshot.CreatedAtUtc
	};

	private User CurrentUser() =>
		_authorization.CurrentUser is { IsActive: true } user
			? user
			: throw new UnauthorizedAccessException("An active signed-in user is required.");

	private static ApplicationPermission RequiredDecisionPermission(ApprovalSubjectKind subjectKind) => subjectKind switch
	{
		ApprovalSubjectKind.PurchaseOrder => ApplicationPermission.PurchaseOrdersApprove,
		ApprovalSubjectKind.SalesOrder => ApplicationPermission.SalesOrdersApprove,
		ApprovalSubjectKind.AccountsPayableException => ApplicationPermission.FinanceSupplierMatchExceptionsApprove,
		ApprovalSubjectKind.PaymentProposal => ApplicationPermission.FinancePaymentProposalsApprove,
		ApprovalSubjectKind.FinanceBudget => ApplicationPermission.FinanceBudgetingApprove,
		_ => throw new InvalidOperationException($"Approval subject kind '{subjectKind}' is not supported.")
	};

	private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

	private static string NormalizeSubjectId(string value)
	{
		if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Approval subject id is required.", nameof(value));
		var result = value.Trim();
		if (result.Length > 100) throw new ArgumentException("Approval subject id must not exceed 100 characters.", nameof(value));
		return result;
	}

	private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

	private static void Normalize(ApprovalPolicy policy)
	{
		policy.Name = policy.Name.Trim();
		policy.Description = NormalizeOptional(policy.Description);
		policy.EffectiveFromUtc = policy.EffectiveFromUtc?.ToUniversalTime();
		policy.EffectiveToUtc = policy.EffectiveToUtc?.ToUniversalTime();
		foreach (var condition in policy.Conditions)
		{
			if (condition.Kind == ApprovalConditionKind.Currency)
				condition.StringValue = NormalizeOptional(condition.StringValue)?.ToUpperInvariant();
		}
		foreach (var stage in policy.Stages)
		{
			stage.Name = stage.Name.Trim();
			foreach (var target in stage.Approvers)
				target.RoleCode = NormalizeOptional(target.RoleCode);
		}
	}

	private static ApprovalPolicy Copy(ApprovalPolicy source) => new()
	{
		Id = source.Id,
		Name = source.Name,
		Description = source.Description,
		SubjectKind = source.SubjectKind,
		Priority = source.Priority,
		IsActive = source.IsActive,
		EffectiveFromUtc = source.EffectiveFromUtc,
		EffectiveToUtc = source.EffectiveToUtc,
		Version = source.Version,
		CreatedByUserId = source.CreatedByUserId,
		CreatedByUserDisplay = source.CreatedByUserDisplay,
		CreatedAtUtc = source.CreatedAtUtc,
		UpdatedByUserId = source.UpdatedByUserId,
		UpdatedByUserDisplay = source.UpdatedByUserDisplay,
		UpdatedAtUtc = source.UpdatedAtUtc,
		Conditions = source.Conditions.Select(value => new ApprovalPolicyCondition
		{
			Kind = value.Kind,
			DecimalValue = value.DecimalValue,
			StringValue = value.StringValue,
			GuidValue = value.GuidValue
		}).ToList(),
		Stages = source.Stages.Select(value => new ApprovalPolicyStage
		{
			Order = value.Order,
			Name = value.Name,
			Approvers = value.Approvers.Select(CopyTarget).ToList()
		}).ToList()
	};

	private static ApprovalApproverTarget CopyTarget(ApprovalApproverTarget source) => new()
	{
		Kind = source.Kind,
		RoleCode = source.RoleCode,
		UserId = source.UserId
	};
}


internal sealed record ApprovalPreparedDecision(
	ApprovalInstance Instance,
	int ExpectedVersion,
	ApprovalDecisionEvidence Evidence,
	bool IsFinalApproval);
