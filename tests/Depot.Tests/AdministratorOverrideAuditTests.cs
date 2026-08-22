// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Services;

using Xunit;

namespace Depot.Tests;

public sealed class AdministratorOverrideAuditTests
{
	[Fact]
	public void AdministratorSelfDecisionAlwaysHasAnExplicitOverrideReason()
	{
		var result = AdministratorOverrideAudit.NormalizeDecisionComment(isSelfDecision: true, isAdministrator: true, comment: null);
		Assert.NotNull(result);
		Assert.Contains("Administrator override", result, StringComparison.Ordinal);
		Assert.Contains("creator/approver", result, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void SuppliedOverrideReasonIsMarkedAsAdministratorOverride()
	{
		var result = AdministratorOverrideAudit.NormalizeDecisionComment(true, true, "Emergency procurement");
		Assert.Equal("Administrator override: Emergency procurement", result);
	}

	[Fact]
	public void NormalApprovalCommentIsNotChangedBeyondTrimming()
	{
		Assert.Equal("Reviewed", AdministratorOverrideAudit.NormalizeDecisionComment(false, false, " Reviewed "));
	}
}
