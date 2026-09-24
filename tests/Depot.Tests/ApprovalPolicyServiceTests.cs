// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class ApprovalPolicyServiceTests : IDisposable
{
	private readonly string _path = Path.Combine(Path.GetTempPath(), $"depot-approval-policy-{Guid.NewGuid():N}.db");

	[Fact]
	public void FeatureMigrationSeedsAllSupportedApprovalSubjects()
	{
		var factory = Initialize();
		ApprovalPolicySchemaMigration.Migrate(factory);
		ApprovalPolicySchemaMigration.Migrate(factory);

		using var connection = Open();
		Assert.Equal(ApprovalPolicySchemaMigration.CurrentVersion, Scalar(connection, "SELECT Version FROM DepotFeatureVersions WHERE Name='ApprovalPolicies';"));
		var supportedSubjects = Enum.GetValues<ApprovalSubjectKind>().Length;
		Assert.Equal(supportedSubjects, Scalar(connection, "SELECT COUNT(*) FROM ApprovalPolicies;"));
		Assert.Equal(supportedSubjects, Scalar(connection, "SELECT COUNT(DISTINCT SubjectKind) FROM ApprovalPolicies WHERE IsActive=1;"));
		Assert.Equal(1, Scalar(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='ApprovalInstances';"));
		Assert.Equal(1, Scalar(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='ApprovalDecisions';"));
	}

	[Fact]
	public async Task ResolutionFailsClosedWhenHighestPriorityIsAmbiguous()
	{
		var context = await CreateContextAsync();
		await context.CreateAndActivatePolicyAsync("First", 100, [Stage(1, context.Role.Code)]);
		await context.CreateAndActivatePolicyAsync("Second", 100, [Stage(1, context.Role.Code)]);

		var error = await Assert.ThrowsAsync<InvalidOperationException>(() => context.Service.PreviewAsync(
			new ApprovalSubjectAttributes(ApprovalSubjectKind.PurchaseOrder, "42", 500m)));

		Assert.Contains("ambiguous", error.Message, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task ResolvedSnapshotRemainsImmutableAfterPolicyEdit()
	{
		var context = await CreateContextAsync();
		var policy = await context.CreateAndActivatePolicyAsync("Stable routing", 100, [Stage(1, context.Role.Code)]);
		var snapshot = await context.Service.ResolveSnapshotAsync(
			new ApprovalSubjectAttributes(ApprovalSubjectKind.PurchaseOrder, "73", 250m));

		policy.Name = "Changed routing";
		policy.Stages[0].Name = "Changed stage";
		var updated = await context.Service.UpdateAsync(policy, policy.Version);

		Assert.Equal("Stable routing", snapshot.PolicyName);
		Assert.Equal("Approval 1", Assert.Single(snapshot.Stages).Name);
		Assert.Equal(policy.Version + 1, updated.Version);
		Assert.Equal("Changed routing", updated.Name);
	}

	[Fact]
	public async Task SequentialApprovalOnlyCompletesAfterFinalStage()
	{
		var context = await CreateContextAsync();
		await context.CreateAndActivatePolicyAsync(
			"Two stage",
			100,
			[Stage(1, context.Role.Code), Stage(2, context.Role.Code)]);

		var instance = await context.Service.StartAsync(
			new ApprovalSubjectAttributes(ApprovalSubjectKind.PurchaseOrder, "99", 900m));

		var first = await context.Service.ApproveStageAsync(instance.Id, "First stage");
		Assert.Equal(ApprovalInstanceStatus.Pending, first.Status);
		Assert.Equal(2, first.CurrentStageOrder);

		var second = await context.Service.ApproveStageAsync(instance.Id, "Second stage");
		Assert.Equal(ApprovalInstanceStatus.Approved, second.Status);
		Assert.Equal(2, second.CurrentStageOrder);

		var decisions = await context.Repository.GetDecisionsAsync(instance.Id);
		Assert.Equal(2, decisions.Count);
		Assert.Equal([1, 2], decisions.Select(value => value.StageOrder).ToArray());
	}


	[Fact]
	public async Task ResolutionHonorsAmountAndCurrencyConditions()
	{
		var context = await CreateContextAsync();
		await context.CreateAndActivatePolicyAsync(
			"Bounded EUR",
			200,
			[Stage(1, context.Role.Code)],
			[
				new ApprovalPolicyCondition { Kind = ApprovalConditionKind.MinimumAmount, DecimalValue = 100m },
				new ApprovalPolicyCondition { Kind = ApprovalConditionKind.MaximumAmount, DecimalValue = 1000m },
				new ApprovalPolicyCondition { Kind = ApprovalConditionKind.Currency, StringValue = "eur" }
			]);

		var matching = await context.Service.PreviewAsync(
			new ApprovalSubjectAttributes(ApprovalSubjectKind.PurchaseOrder, "amount-currency-match", 500m, "EUR"));
		var belowThreshold = await context.Service.PreviewAsync(
			new ApprovalSubjectAttributes(ApprovalSubjectKind.PurchaseOrder, "amount-currency-fallback", 50m, "EUR"));

		Assert.Equal("Bounded EUR", matching.Policy.Name);
		Assert.NotEqual("Bounded EUR", belowThreshold.Policy.Name);
	}

	[Fact]
	public async Task FutureEffectivePolicyIsIgnoredUntilEffective()
	{
		var context = await CreateContextAsync();
		var draft = await context.Service.CreateAsync(new ApprovalPolicy
		{
			Name = "Future policy",
			SubjectKind = ApprovalSubjectKind.PurchaseOrder,
			Priority = 500,
			EffectiveFromUtc = DateTime.UtcNow.AddDays(1),
			Stages = [Stage(1, context.Role.Code)]
		});
		await context.Service.ActivateAsync(draft.Id, draft.Version);

		var preview = await context.Service.PreviewAsync(
			new ApprovalSubjectAttributes(ApprovalSubjectKind.PurchaseOrder, "future-policy", 100m));

		Assert.NotEqual("Future policy", preview.Policy.Name);
	}

	[Fact]
	public async Task RejectionTerminatesApprovalAndRecordsEvidence()
	{
		var context = await CreateContextAsync();
		await context.CreateAndActivatePolicyAsync("Rejectable", 100, [Stage(1, context.Role.Code)]);
		var instance = await context.Service.StartAsync(
			new ApprovalSubjectAttributes(ApprovalSubjectKind.PurchaseOrder, "reject-1", 250m));

		var rejected = await context.Service.RejectStageAsync(instance.Id, "Rejected for test coverage");
		var decisions = await context.Repository.GetDecisionsAsync(instance.Id);

		Assert.Equal(ApprovalInstanceStatus.Rejected, rejected.Status);
		var decision = Assert.Single(decisions);
		Assert.Equal(ApprovalDecisionKind.Rejected, decision.Decision);
		Assert.Equal("Rejected for test coverage", decision.Comment);
	}

	[Fact]
	public async Task CompletedApprovalRejectsDuplicateDecision()
	{
		var context = await CreateContextAsync();
		await context.CreateAndActivatePolicyAsync("Single stage", 100, [Stage(1, context.Role.Code)]);
		var instance = await context.Service.StartAsync(
			new ApprovalSubjectAttributes(ApprovalSubjectKind.PurchaseOrder, "duplicate-decision", 250m));

		var approved = await context.Service.ApproveStageAsync(instance.Id);
		Assert.Equal(ApprovalInstanceStatus.Approved, approved.Status);

		await Assert.ThrowsAsync<InvalidOperationException>(() => context.Service.ApproveStageAsync(instance.Id));
	}

	[Fact]
	public async Task ValidationRejectsFinanceOnlySelectorForPurchaseOrder()
	{
		var context = await CreateContextAsync();
		var issues = await context.Service.ValidateAsync(new ApprovalPolicy
		{
			Name = "Invalid selector",
			SubjectKind = ApprovalSubjectKind.PurchaseOrder,
			Conditions =
			[
				new ApprovalPolicyCondition
				{
					Kind = ApprovalConditionKind.LegalEntityId,
					GuidValue = Guid.NewGuid()
				}
			],
			Stages = [Stage(1, context.Role.Code)]
		});

		Assert.Contains(issues, issue => issue.Code == "condition.subject");
	}

	private async Task<TestContext> CreateContextAsync()
	{
		var factory = Initialize();
		ApprovalPolicySchemaMigration.Migrate(factory);
		var access = new DatabaseAccess(factory);
		var transactions = new DatabaseTransactionRunner(access);
		var users = new UserRepository(access);
		var roles = new RoleRepository(access);
		var repository = new ApprovalPolicyRepository(access);
		var authorization = new AuthorizationService();

		var user = new User
		{
			Email = "approval-policy@example.test",
			DisplayName = "Approval Policy User",
			IsActive = true,
			CreatedUtc = DateTime.UtcNow
		};
		user.Id = await users.CreateAsync(user, "unused", CancellationToken.None);
		var role = new Role
		{
			Code = $"approval-test-{Guid.NewGuid():N}",
			Name = "Approval Test",
			Description = "Approval policy test role",
			IsActive = true
		};
		await transactions.ExecuteAsync(async (transaction, token) =>
		{
			role.Id = await RoleRepository.CreateAsync(transaction, role, token);
			await RoleRepository.ReplaceUserRolesAsync(transaction, user.Id, [role.Id], token);
			return true;
		});
		authorization.SignIn(
			user,
			[
				ApplicationPermission.ApprovalPoliciesView,
				ApplicationPermission.ApprovalPoliciesManage,
				ApplicationPermission.PurchaseOrdersApprove
			]);
		var service = new ApprovalPolicyService(repository, roles, users, authorization);
		return new TestContext(service, repository, role);
	}

	private SqliteConnectionFactory Initialize()
	{
		var factory = new SqliteConnectionFactory(_path);
		new DepotDatabase(factory).Initialize();
		return factory;
	}

	private SqliteConnection Open()
	{
		var connection = new SqliteConnection($"Data Source={_path}");
		connection.Open();
		return connection;
	}

	private static int Scalar(SqliteConnection connection, string sql)
	{
		using var command = connection.CreateCommand();
		command.CommandText = sql;
		return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
	}

	private static ApprovalPolicyStage Stage(int order, string roleCode) => new()
	{
		Order = order,
		Name = $"Approval {order}",
		Approvers =
		[
			new ApprovalApproverTarget
			{
				Kind = ApprovalApproverKind.Role,
				RoleCode = roleCode
			}
		]
	};

	public void Dispose()
	{
		SqliteConnection.ClearAllPools();
		if (File.Exists(_path)) File.Delete(_path);
	}

	private sealed record TestContext(
		ApprovalPolicyService Service,
		ApprovalPolicyRepository Repository,
		Role Role)
	{
		public async Task<ApprovalPolicy> CreateAndActivatePolicyAsync(
			string name,
			int priority,
			IReadOnlyList<ApprovalPolicyStage> stages,
			IReadOnlyList<ApprovalPolicyCondition>? conditions = null)
		{
			var created = await Service.CreateAsync(new ApprovalPolicy
			{
				Name = name,
				SubjectKind = ApprovalSubjectKind.PurchaseOrder,
				Priority = priority,
				Conditions = conditions?.ToList() ?? [],
				Stages = stages.ToList()
			});
			return await Service.ActivateAsync(created.Id, created.Version);
		}
	}
}
