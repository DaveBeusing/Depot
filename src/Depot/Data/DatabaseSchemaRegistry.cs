// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Data;

/// <summary>The authoritative registry of independently versioned feature migrations.</summary>
public static class DatabaseSchemaRegistry
{
	public static IReadOnlyDictionary<string, int> CurrentFeatureVersions { get; } = new Dictionary<string, int>(StringComparer.Ordinal)
	{
		["Sales"] = SalesSchemaMigration.CurrentVersion,
		["Finance"] = FinanceInventoryAccountingSchemaMigration.CurrentVersion,
		["UserSessions"] = UserSessionSchemaMigration.CurrentVersion,
		["SecurityEvents"] = SecurityEventSchemaMigration.CurrentVersion,
		["UserPreferences"] = UserPreferenceSchemaMigration.CurrentVersion,
		["DocumentTemplates"] = DocumentTemplateSchemaMigration.CurrentVersion,
		["ApprovalPolicies"] = ApprovalPolicySchemaMigration.CurrentVersion,
		["EnterpriseIdentity"] = EnterpriseIdentitySchemaMigration.CurrentVersion,
		["BusinessAttachments"] = BusinessAttachmentSchemaMigration.CurrentVersion,
		["ProcurementSourcing"] = ProcurementSourcingSchemaMigration.CurrentVersion,
		["InventoryReplenishment"] = InventoryReplenishmentSchemaMigration.CurrentVersion,
		["Production"] = ProductionSchemaMigration.CurrentVersion,
		["ProjectAccounting"] = ProjectAccountingSchemaMigration.CurrentVersion,
		["ServiceManagement"] = ServiceManagementSchemaMigration.CurrentVersion
	};

}
