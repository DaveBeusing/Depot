// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Services;

public static class AdministratorOverrideAudit
{
	private const string Prefix = "Administrator override";

	public static string? NormalizeDecisionComment(bool isSelfDecision, bool isAdministrator, string? comment)
	{
		var normalized = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
		if (!isSelfDecision || !isAdministrator) return normalized;
		if (normalized is null) return $"{Prefix}: creator/approver separation overridden by administrator.";
		return $"{Prefix}: {normalized}";
	}
}
