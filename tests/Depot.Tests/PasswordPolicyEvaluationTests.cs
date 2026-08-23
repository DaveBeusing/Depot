// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Services;

using Xunit;

namespace Depot.Tests;

public sealed class PasswordPolicyEvaluationTests
{
	[Fact]
	public void Evaluate_ReportsEveryMissingRequirement()
	{
		var result = PasswordPolicy.Evaluate("short", "owner@depot.test");

		Assert.False(result.HasValidLength);
		Assert.False(result.HasUppercase);
		Assert.True(result.HasLowercase);
		Assert.False(result.HasDigit);
		Assert.False(result.HasSymbol);
		Assert.True(result.ExcludesAccountName);
		Assert.False(result.IsValid);
	}

	[Fact]
	public void Evaluate_RejectsPasswordContainingAccountName()
	{
		var result = PasswordPolicy.Evaluate("Owner-Secure-92!", "owner@depot.test");

		Assert.True(result.HasValidLength);
		Assert.True(result.HasUppercase);
		Assert.True(result.HasLowercase);
		Assert.True(result.HasDigit);
		Assert.True(result.HasSymbol);
		Assert.False(result.ExcludesAccountName);
		Assert.False(result.IsValid);
	}

	[Fact]
	public void Evaluate_AcceptsPasswordMeetingAllRequirements()
	{
		var result = PasswordPolicy.Evaluate("Secure-Depot-92!Admin", "owner@depot.test");

		Assert.True(result.IsValid);
	}
}
