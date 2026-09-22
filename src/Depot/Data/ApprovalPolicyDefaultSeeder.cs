// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;
using System.Text.Json;

using Depot.Models;

namespace Depot.Data;

internal static class ApprovalPolicyDefaultSeeder
{
	private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
	private static readonly DateTime SeedTimestampUtc = DateTime.SpecifyKind(new DateTime(2026, 1, 1), DateTimeKind.Utc);

	public static void Seed(IDatabaseConnectionFactory connectionFactory)
	{
		ArgumentNullException.ThrowIfNull(connectionFactory);
		var database = new DatabaseAccess(connectionFactory);
		SeedIfMissing(database, DefaultPolicy(
			new Guid("8ca52645-14c7-4a4b-8c1c-2acdd3061001"),
			"Default Purchase Order Approval",
			ApprovalSubjectKind.PurchaseOrder,
			SystemRoleCatalog.ApproverCode));
		SeedIfMissing(database, DefaultPolicy(
			new Guid("8ca52645-14c7-4a4b-8c1c-2acdd3061002"),
			"Default Sales Order Approval",
			ApprovalSubjectKind.SalesOrder,
			SystemRoleCatalog.SalesManagerCode));
		SeedIfMissing(database, DefaultPolicy(
			new Guid("8ca52645-14c7-4a4b-8c1c-2acdd3061003"),
			"Default AP Exception Approval",
			ApprovalSubjectKind.AccountsPayableException,
			SystemRoleCatalog.ApproverCode));
		SeedIfMissing(database, DefaultPolicy(
			new Guid("8ca52645-14c7-4a4b-8c1c-2acdd3061004"),
			"Default Payment Proposal Approval",
			ApprovalSubjectKind.PaymentProposal,
			SystemRoleCatalog.ApproverCode));
	}

	private static ApprovalPolicy DefaultPolicy(Guid id, string name, ApprovalSubjectKind subjectKind, string businessRole) => new()
	{
		Id = id,
		Name = name,
		Description = "Migration-compatible default policy. Configure a higher-priority policy or deactivate this policy after validating replacement routing.",
		SubjectKind = subjectKind,
		Priority = 0,
		IsActive = true,
		Version = 1,
		CreatedByUserDisplay = "system",
		CreatedAtUtc = SeedTimestampUtc,
		UpdatedByUserDisplay = "system",
		UpdatedAtUtc = SeedTimestampUtc,
		Stages =
		[
			new ApprovalPolicyStage
			{
				Order = 1,
				Name = "Approval",
				Approvers =
				[
					new ApprovalApproverTarget { Kind = ApprovalApproverKind.Role, RoleCode = businessRole },
					new ApprovalApproverTarget { Kind = ApprovalApproverKind.Role, RoleCode = SystemRoleCatalog.AdministratorCode }
				]
			}
		]
	};

	private static void SeedIfMissing(DatabaseAccess database, ApprovalPolicy policy)
	{
		var count = database.Query(
			"SELECT COUNT(*) FROM ApprovalPolicies WHERE SubjectKind=$SubjectKind;",
			reader => Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture),
			new DatabaseParameter("$SubjectKind", (int)policy.SubjectKind)).Single();
		if (count != 0) return;

		database.Execute(
			"""
			INSERT INTO ApprovalPolicies
			(PolicyId,Name,Description,SubjectKind,Priority,IsActive,EffectiveFromUtc,EffectiveToUtc,Version,DefinitionJson,
			 CreatedByUserId,CreatedByUserDisplay,CreatedAtUtc,UpdatedByUserId,UpdatedByUserDisplay,UpdatedAtUtc)
			VALUES
			($PolicyId,$Name,$Description,$SubjectKind,$Priority,$IsActive,NULL,NULL,$Version,$DefinitionJson,
			 NULL,$CreatedByUserDisplay,$CreatedAtUtc,NULL,$UpdatedByUserDisplay,$UpdatedAtUtc);
			""",
			new DatabaseParameter("$PolicyId", policy.Id.ToString("D")),
			new DatabaseParameter("$Name", policy.Name),
			new DatabaseParameter("$Description", policy.Description),
			new DatabaseParameter("$SubjectKind", (int)policy.SubjectKind),
			new DatabaseParameter("$Priority", policy.Priority),
			new DatabaseParameter("$IsActive", 1),
			new DatabaseParameter("$Version", policy.Version),
			new DatabaseParameter("$DefinitionJson", JsonSerializer.Serialize(policy, SerializerOptions)),
			new DatabaseParameter("$CreatedByUserDisplay", policy.CreatedByUserDisplay),
			new DatabaseParameter("$CreatedAtUtc", policy.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture)),
			new DatabaseParameter("$UpdatedByUserDisplay", policy.UpdatedByUserDisplay),
			new DatabaseParameter("$UpdatedAtUtc", policy.UpdatedAtUtc.ToString("O", CultureInfo.InvariantCulture)));
	}
}
