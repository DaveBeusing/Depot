// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;
using System.Text.Json;

using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class ApprovalPolicyRepository : DatabaseRepository
{
	private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

	public ApprovalPolicyRepository(DatabaseAccess database) : base(database)
	{
	}

	public Task<IReadOnlyList<ApprovalPolicy>> GetActiveCandidatesAsync(ApprovalSubjectKind subjectKind, DateTime atUtc, CancellationToken cancellationToken = default) =>
		Database.QuerySliceAsync(
			"""
			SELECT PolicyId,Name,Description,SubjectKind,Priority,IsActive,EffectiveFromUtc,EffectiveToUtc,Version,DefinitionJson,
				CreatedByUserId,CreatedByUserDisplay,CreatedAtUtc,UpdatedByUserId,UpdatedByUserDisplay,UpdatedAtUtc
			FROM ApprovalPolicies
			WHERE SubjectKind=$SubjectKind AND IsActive=1
				AND (EffectiveFromUtc IS NULL OR EffectiveFromUtc <= $AtUtc)
				AND (EffectiveToUtc IS NULL OR EffectiveToUtc > $AtUtc)
			ORDER BY Priority DESC, Name, PolicyId
			""",
			ReadPolicy,
			0,
			201,
			cancellationToken,
			Parameter("$SubjectKind", (int)subjectKind),
			Parameter("$AtUtc", Iso(atUtc)));

	public Task<IReadOnlyList<ApprovalPolicy>> GetPageAsync(ApprovalSubjectKind? subjectKind, int offset, int count, CancellationToken cancellationToken = default)
	{
		var where = subjectKind is null ? string.Empty : "WHERE SubjectKind=$SubjectKind";
		DatabaseParameter[] parameters = subjectKind is null ? [] : [Parameter("$SubjectKind", (int)subjectKind.Value)];
		return Database.QuerySliceAsync(
			$"""
			SELECT PolicyId,Name,Description,SubjectKind,Priority,IsActive,EffectiveFromUtc,EffectiveToUtc,Version,DefinitionJson,
				CreatedByUserId,CreatedByUserDisplay,CreatedAtUtc,UpdatedByUserId,UpdatedByUserDisplay,UpdatedAtUtc
			FROM ApprovalPolicies
			{where}
			ORDER BY SubjectKind, Priority DESC, Name, PolicyId
			""",
			ReadPolicy,
			offset,
			count,
			cancellationToken,
			parameters);
	}

	public Task<ApprovalPolicy?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
		Database.QuerySingleOrDefaultAsync(
			"""
			SELECT PolicyId,Name,Description,SubjectKind,Priority,IsActive,EffectiveFromUtc,EffectiveToUtc,Version,DefinitionJson,
				CreatedByUserId,CreatedByUserDisplay,CreatedAtUtc,UpdatedByUserId,UpdatedByUserDisplay,UpdatedAtUtc
			FROM ApprovalPolicies WHERE PolicyId=$PolicyId;
			""",
			ReadPolicy,
			cancellationToken,
			Parameter("$PolicyId", id.ToString("D")));

	public async Task CreateAsync(ApprovalPolicy policy, CancellationToken cancellationToken = default)
	{
		var affected = await Database.ExecuteAsync(
			"""
			INSERT INTO ApprovalPolicies
			(PolicyId,Name,Description,SubjectKind,Priority,IsActive,EffectiveFromUtc,EffectiveToUtc,Version,DefinitionJson,
			 CreatedByUserId,CreatedByUserDisplay,CreatedAtUtc,UpdatedByUserId,UpdatedByUserDisplay,UpdatedAtUtc)
			VALUES
			($PolicyId,$Name,$Description,$SubjectKind,$Priority,$IsActive,$EffectiveFromUtc,$EffectiveToUtc,$Version,$DefinitionJson,
			 $CreatedByUserId,$CreatedByUserDisplay,$CreatedAtUtc,$UpdatedByUserId,$UpdatedByUserDisplay,$UpdatedAtUtc);
			""",
			cancellationToken,
			PolicyParameters(policy));
		if (affected != 1) throw new InvalidOperationException("The approval policy was not created.");
	}

	public async Task UpdateAsync(ApprovalPolicy policy, int expectedVersion, CancellationToken cancellationToken = default)
	{
		var parameters = PolicyParameters(policy).Append(Parameter("$ExpectedVersion", expectedVersion)).ToArray();
		await Database.ExecuteInWriteTransactionAsync(async (session, token) =>
		{
			var captured = await session.ExecuteAsync(
				"""
				INSERT INTO ApprovalPolicyRevisions
				(PolicyId,Version,DefinitionJson,ChangedByUserId,ChangedByUserDisplay,ChangedAtUtc)
				SELECT PolicyId,Version,DefinitionJson,UpdatedByUserId,UpdatedByUserDisplay,UpdatedAtUtc
				FROM ApprovalPolicies
				WHERE PolicyId=$PolicyId AND Version=$ExpectedVersion;
				""",
				token,
				parameters);
			if (captured != 1) throw new ConcurrencyConflictException("approval policy");

			var affected = await session.ExecuteAsync(
				"""
				UPDATE ApprovalPolicies SET
					Name=$Name,Description=$Description,SubjectKind=$SubjectKind,Priority=$Priority,IsActive=$IsActive,
					EffectiveFromUtc=$EffectiveFromUtc,EffectiveToUtc=$EffectiveToUtc,Version=$Version,DefinitionJson=$DefinitionJson,
					UpdatedByUserId=$UpdatedByUserId,UpdatedByUserDisplay=$UpdatedByUserDisplay,UpdatedAtUtc=$UpdatedAtUtc
				WHERE PolicyId=$PolicyId AND Version=$ExpectedVersion;
				""",
				token,
				parameters);
			if (affected != 1) throw new ConcurrencyConflictException("approval policy");
			return 0;
		}, cancellationToken);
	}

	public Task<ApprovalInstance?> GetInstanceAsync(Guid instanceId, CancellationToken cancellationToken = default) =>
		Database.QuerySingleOrDefaultAsync(
			"""
			SELECT InstanceId,SubjectKind,SubjectId,PolicyId,PolicyVersion,PolicyName,SnapshotJson,CurrentStageOrder,Status,Version,CreatedAtUtc
			FROM ApprovalInstances WHERE InstanceId=$InstanceId;
			""",
			ReadInstance,
			cancellationToken,
			Parameter("$InstanceId", instanceId.ToString("D")));

	internal Task<ApprovalInstance?> GetPendingInstanceAsync(Depot.Data.DatabaseTransactionContext transaction, ApprovalSubjectKind subjectKind, string subjectId, CancellationToken cancellationToken) =>
		transaction.Session.QuerySingleOrDefaultAsync(
			"""
			SELECT InstanceId,SubjectKind,SubjectId,PolicyId,PolicyVersion,PolicyName,SnapshotJson,CurrentStageOrder,Status,Version,CreatedAtUtc
			FROM ApprovalInstances
			WHERE SubjectKind=$SubjectKind AND SubjectId=$SubjectId AND Status=$Status
			ORDER BY CreatedAtUtc DESC;
			""",
			ReadInstance,
			cancellationToken,
			Parameter("$SubjectKind", (int)subjectKind),
			Parameter("$SubjectId", subjectId),
			Parameter("$Status", (int)ApprovalInstanceStatus.Pending));

	public Task<ApprovalInstance?> GetPendingInstanceAsync(ApprovalSubjectKind subjectKind, string subjectId, CancellationToken cancellationToken = default) =>
		Database.QuerySingleOrDefaultAsync(
			"""
			SELECT InstanceId,SubjectKind,SubjectId,PolicyId,PolicyVersion,PolicyName,SnapshotJson,CurrentStageOrder,Status,Version,CreatedAtUtc
			FROM ApprovalInstances
			WHERE SubjectKind=$SubjectKind AND SubjectId=$SubjectId AND Status=$Status
			ORDER BY CreatedAtUtc DESC;
			""",
			ReadInstance,
			cancellationToken,
			Parameter("$SubjectKind", (int)subjectKind),
			Parameter("$SubjectId", subjectId),
			Parameter("$Status", (int)ApprovalInstanceStatus.Pending));

	internal async Task CreateInstanceAsync(Depot.Data.DatabaseTransactionContext transaction, ApprovalInstance instance, CancellationToken cancellationToken)
	{
		var affected = await transaction.Session.ExecuteAsync(
			"""
			INSERT INTO ApprovalInstances
			(InstanceId,SubjectKind,SubjectId,PolicyId,PolicyVersion,PolicyName,SnapshotJson,CurrentStageOrder,Status,Version,CreatedAtUtc)
			VALUES ($InstanceId,$SubjectKind,$SubjectId,$PolicyId,$PolicyVersion,$PolicyName,$SnapshotJson,$CurrentStageOrder,$Status,$Version,$CreatedAtUtc);
			""",
			cancellationToken,
			InstanceParameters(instance));
		if (affected != 1) throw new InvalidOperationException("The approval instance was not created.");
	}

	public async Task CreateInstanceAsync(ApprovalInstance instance, CancellationToken cancellationToken = default)
	{
		var affected = await Database.ExecuteAsync(
			"""
			INSERT INTO ApprovalInstances
			(InstanceId,SubjectKind,SubjectId,PolicyId,PolicyVersion,PolicyName,SnapshotJson,CurrentStageOrder,Status,Version,CreatedAtUtc)
			VALUES ($InstanceId,$SubjectKind,$SubjectId,$PolicyId,$PolicyVersion,$PolicyName,$SnapshotJson,$CurrentStageOrder,$Status,$Version,$CreatedAtUtc);
			""",
			cancellationToken,
			InstanceParameters(instance));
		if (affected != 1) throw new InvalidOperationException("The approval instance was not created.");
	}

	public Task<IReadOnlyList<ApprovalDecisionEvidence>> GetDecisionsAsync(Guid instanceId, CancellationToken cancellationToken = default) =>
		Database.QueryAsync(
			"""
			SELECT DecisionId,InstanceId,StageOrder,Decision,UserId,UserDisplay,Comment,DecidedAtUtc
			FROM ApprovalDecisions WHERE InstanceId=$InstanceId ORDER BY StageOrder;
			""",
			ReadDecision,
			cancellationToken,
			Parameter("$InstanceId", instanceId.ToString("D")));

	internal Task RecordDecisionAsync(Depot.Data.DatabaseTransactionContext transaction, ApprovalInstance updatedInstance, int expectedVersion, ApprovalDecisionEvidence decision, CancellationToken cancellationToken) =>
		RecordDecisionCoreAsync(transaction.Session, updatedInstance, expectedVersion, decision, cancellationToken);

	public async Task RecordDecisionAsync(ApprovalInstance updatedInstance, int expectedVersion, ApprovalDecisionEvidence decision, CancellationToken cancellationToken = default)
	{
		await Database.ExecuteInWriteTransactionAsync(async (session, token) =>
		{
			await RecordDecisionCoreAsync(session, updatedInstance, expectedVersion, decision, token);
			return 0;
		}, cancellationToken);
	}

	private async Task RecordDecisionCoreAsync(Depot.Data.DatabaseSession session, ApprovalInstance updatedInstance, int expectedVersion, ApprovalDecisionEvidence decision, CancellationToken cancellationToken)
	{
		var inserted = await session.ExecuteAsync(
				"""
				INSERT INTO ApprovalDecisions
				(DecisionId,InstanceId,StageOrder,Decision,UserId,UserDisplay,Comment,DecidedAtUtc)
				VALUES ($DecisionId,$InstanceId,$StageOrder,$Decision,$UserId,$UserDisplay,$Comment,$DecidedAtUtc);
				""",
				cancellationToken,
				Parameter("$DecisionId", decision.Id.ToString("D")),
				Parameter("$InstanceId", decision.InstanceId.ToString("D")),
				Parameter("$StageOrder", decision.StageOrder),
				Parameter("$Decision", (int)decision.Decision),
				Parameter("$UserId", decision.UserId),
				Parameter("$UserDisplay", decision.UserDisplay),
				Parameter("$Comment", decision.Comment),
				Parameter("$DecidedAtUtc", Iso(decision.DecidedAtUtc)));
			if (inserted != 1) throw new InvalidOperationException("The approval decision was not recorded.");

			var updateParameters = InstanceParameters(updatedInstance).Append(Parameter("$ExpectedVersion", expectedVersion)).ToArray();
			var updated = await session.ExecuteAsync(
				"""
				UPDATE ApprovalInstances SET
					CurrentStageOrder=$CurrentStageOrder,Status=$Status,Version=$Version
				WHERE InstanceId=$InstanceId AND Version=$ExpectedVersion;
				""",
				cancellationToken,
				updateParameters);
		if (updated != 1) throw new ConcurrencyConflictException("approval decision");
	}

	public Task<IReadOnlyList<ApprovalDelegation>> GetEffectiveDelegationsAsync(long fromUserId, ApprovalSubjectKind subjectKind, DateTime atUtc, CancellationToken cancellationToken = default) =>
		Database.QuerySliceAsync(
			"""
			SELECT DelegationId,FromUserId,ToUserId,SubjectKind,EffectiveFromUtc,EffectiveToUtc,CreatedByUserId,CreatedByUserDisplay,CreatedAtUtc
			FROM ApprovalDelegations
			WHERE FromUserId=$FromUserId
				AND (SubjectKind IS NULL OR SubjectKind=$SubjectKind)
				AND EffectiveFromUtc <= $AtUtc AND EffectiveToUtc > $AtUtc
			ORDER BY EffectiveFromUtc DESC, DelegationId
			""",
			ReadDelegation,
			0,
			100,
			cancellationToken,
			Parameter("$FromUserId", fromUserId),
			Parameter("$SubjectKind", (int)subjectKind),
			Parameter("$AtUtc", Iso(atUtc)));

	public async Task CreateDelegationAsync(ApprovalDelegation delegation, CancellationToken cancellationToken = default)
	{
		var affected = await Database.ExecuteAsync(
			"""
			INSERT INTO ApprovalDelegations
			(DelegationId,FromUserId,ToUserId,SubjectKind,EffectiveFromUtc,EffectiveToUtc,CreatedByUserId,CreatedByUserDisplay,CreatedAtUtc)
			VALUES ($DelegationId,$FromUserId,$ToUserId,$SubjectKind,$EffectiveFromUtc,$EffectiveToUtc,$CreatedByUserId,$CreatedByUserDisplay,$CreatedAtUtc);
			""",
			cancellationToken,
			Parameter("$DelegationId", delegation.Id.ToString("D")),
			Parameter("$FromUserId", delegation.FromUserId),
			Parameter("$ToUserId", delegation.ToUserId),
			Parameter("$SubjectKind", delegation.SubjectKind is null ? null : (int)delegation.SubjectKind.Value),
			Parameter("$EffectiveFromUtc", Iso(delegation.EffectiveFromUtc)),
			Parameter("$EffectiveToUtc", Iso(delegation.EffectiveToUtc)),
			Parameter("$CreatedByUserId", delegation.CreatedByUserId),
			Parameter("$CreatedByUserDisplay", delegation.CreatedByUserDisplay),
			Parameter("$CreatedAtUtc", Iso(delegation.CreatedAtUtc)));
		if (affected != 1) throw new InvalidOperationException("The approval delegation was not created.");
	}

	private static DatabaseParameter[] PolicyParameters(ApprovalPolicy policy) =>
	[
		Parameter("$PolicyId", policy.Id.ToString("D")),
		Parameter("$Name", policy.Name),
		Parameter("$Description", policy.Description),
		Parameter("$SubjectKind", (int)policy.SubjectKind),
		Parameter("$Priority", policy.Priority),
		Parameter("$IsActive", policy.IsActive ? 1 : 0),
		Parameter("$EffectiveFromUtc", policy.EffectiveFromUtc is null ? null : Iso(policy.EffectiveFromUtc.Value)),
		Parameter("$EffectiveToUtc", policy.EffectiveToUtc is null ? null : Iso(policy.EffectiveToUtc.Value)),
		Parameter("$Version", policy.Version),
		Parameter("$DefinitionJson", JsonSerializer.Serialize(policy, SerializerOptions)),
		Parameter("$CreatedByUserId", policy.CreatedByUserId),
		Parameter("$CreatedByUserDisplay", policy.CreatedByUserDisplay),
		Parameter("$CreatedAtUtc", Iso(policy.CreatedAtUtc)),
		Parameter("$UpdatedByUserId", policy.UpdatedByUserId),
		Parameter("$UpdatedByUserDisplay", policy.UpdatedByUserDisplay),
		Parameter("$UpdatedAtUtc", Iso(policy.UpdatedAtUtc))
	];

	private static DatabaseParameter[] InstanceParameters(ApprovalInstance instance) =>
	[
		Parameter("$InstanceId", instance.Id.ToString("D")),
		Parameter("$SubjectKind", (int)instance.SubjectKind),
		Parameter("$SubjectId", instance.SubjectId),
		Parameter("$PolicyId", instance.PolicyId.ToString("D")),
		Parameter("$PolicyVersion", instance.PolicyVersion),
		Parameter("$PolicyName", instance.PolicyName),
		Parameter("$SnapshotJson", JsonSerializer.Serialize(instance.Snapshot, SerializerOptions)),
		Parameter("$CurrentStageOrder", instance.CurrentStageOrder),
		Parameter("$Status", (int)instance.Status),
		Parameter("$Version", instance.Version),
		Parameter("$CreatedAtUtc", Iso(instance.CreatedAtUtc))
	];

	private static ApprovalPolicy ReadPolicy(DbDataReader reader)
	{
		var policy = JsonSerializer.Deserialize<ApprovalPolicy>(reader.GetString(9), SerializerOptions)
			?? throw new InvalidOperationException("Persisted approval policy JSON is invalid.");
		policy.Id = Guid.Parse(reader.GetString(0));
		policy.Name = reader.GetString(1);
		policy.Description = reader.IsDBNull(2) ? null : reader.GetString(2);
		policy.SubjectKind = (ApprovalSubjectKind)Convert.ToInt32(reader.GetValue(3), CultureInfo.InvariantCulture);
		policy.Priority = Convert.ToInt32(reader.GetValue(4), CultureInfo.InvariantCulture);
		policy.IsActive = Convert.ToInt32(reader.GetValue(5), CultureInfo.InvariantCulture) != 0;
		policy.EffectiveFromUtc = reader.IsDBNull(6) ? null : ParseUtc(reader.GetString(6));
		policy.EffectiveToUtc = reader.IsDBNull(7) ? null : ParseUtc(reader.GetString(7));
		policy.Version = Convert.ToInt32(reader.GetValue(8), CultureInfo.InvariantCulture);
		policy.CreatedByUserId = reader.IsDBNull(10) ? null : Convert.ToInt64(reader.GetValue(10), CultureInfo.InvariantCulture);
		policy.CreatedByUserDisplay = reader.IsDBNull(11) ? null : reader.GetString(11);
		policy.CreatedAtUtc = ParseUtc(reader.GetString(12));
		policy.UpdatedByUserId = reader.IsDBNull(13) ? null : Convert.ToInt64(reader.GetValue(13), CultureInfo.InvariantCulture);
		policy.UpdatedByUserDisplay = reader.IsDBNull(14) ? null : reader.GetString(14);
		policy.UpdatedAtUtc = ParseUtc(reader.GetString(15));
		return policy;
	}

	private static ApprovalInstance ReadInstance(DbDataReader reader)
	{
		var snapshot = JsonSerializer.Deserialize<ApprovalPlanSnapshot>(reader.GetString(6), SerializerOptions)
			?? throw new InvalidOperationException("Persisted approval snapshot JSON is invalid.");
		return new ApprovalInstance
		{
			Id = Guid.Parse(reader.GetString(0)),
			SubjectKind = (ApprovalSubjectKind)Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture),
			SubjectId = reader.GetString(2),
			PolicyId = Guid.Parse(reader.GetString(3)),
			PolicyVersion = Convert.ToInt32(reader.GetValue(4), CultureInfo.InvariantCulture),
			PolicyName = reader.GetString(5),
			Snapshot = snapshot,
			CurrentStageOrder = Convert.ToInt32(reader.GetValue(7), CultureInfo.InvariantCulture),
			Status = (ApprovalInstanceStatus)Convert.ToInt32(reader.GetValue(8), CultureInfo.InvariantCulture),
			Version = Convert.ToInt32(reader.GetValue(9), CultureInfo.InvariantCulture),
			CreatedAtUtc = ParseUtc(reader.GetString(10))
		};
	}

	private static ApprovalDecisionEvidence ReadDecision(DbDataReader reader) => new()
	{
		Id = Guid.Parse(reader.GetString(0)),
		InstanceId = Guid.Parse(reader.GetString(1)),
		StageOrder = Convert.ToInt32(reader.GetValue(2), CultureInfo.InvariantCulture),
		Decision = (ApprovalDecisionKind)Convert.ToInt32(reader.GetValue(3), CultureInfo.InvariantCulture),
		UserId = Convert.ToInt64(reader.GetValue(4), CultureInfo.InvariantCulture),
		UserDisplay = reader.GetString(5),
		Comment = reader.IsDBNull(6) ? null : reader.GetString(6),
		DecidedAtUtc = ParseUtc(reader.GetString(7))
	};

	private static ApprovalDelegation ReadDelegation(DbDataReader reader) => new()
	{
		Id = Guid.Parse(reader.GetString(0)),
		FromUserId = Convert.ToInt64(reader.GetValue(1), CultureInfo.InvariantCulture),
		ToUserId = Convert.ToInt64(reader.GetValue(2), CultureInfo.InvariantCulture),
		SubjectKind = reader.IsDBNull(3) ? null : (ApprovalSubjectKind)Convert.ToInt32(reader.GetValue(3), CultureInfo.InvariantCulture),
		EffectiveFromUtc = ParseUtc(reader.GetString(4)),
		EffectiveToUtc = ParseUtc(reader.GetString(5)),
		CreatedByUserId = Convert.ToInt64(reader.GetValue(6), CultureInfo.InvariantCulture),
		CreatedByUserDisplay = reader.GetString(7),
		CreatedAtUtc = ParseUtc(reader.GetString(8))
	};

	private static string Iso(DateTime value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
	private static DateTime ParseUtc(string value) => DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
}
