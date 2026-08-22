// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Services;

using Xunit;

namespace Depot.Tests;

public sealed class AuthenticationHardeningTests
{
	[Theory]
	[InlineData("Short1!")]
	[InlineData("alllowercase123!")]
	[InlineData("ALLUPPERCASE123!")]
	[InlineData("NoNumbersHere!!")]
	[InlineData("NoSymbolsHere123")]
	public void PasswordPolicyRejectsWeakPasswords(string password)
	{
		Assert.Throws<ArgumentException>(() => PasswordPolicy.Validate(password, "user@example.com"));
	}

	[Fact]
	public void PasswordPolicyRejectsAccountName()
	{
		Assert.Throws<ArgumentException>(() => PasswordPolicy.Validate("User-Secure-123!", "user@example.com"));
	}

	[Fact]
	public void PasswordPolicyAcceptsStrongPassword()
	{
		PasswordPolicy.Validate("Correct-Horse-92!Battery", "operator@example.com");
	}

	[Fact]
	public void AttemptLimiterBlocksAfterRepeatedFailuresAndClearsOnSuccess()
	{
		var limiter = new LoginAttemptLimiter();
		for (var i = 0; i < 5; i++) limiter.RecordFailure("user@example.com");
		Assert.True(limiter.IsBlocked("USER@example.com", out var retryAfter));
		Assert.True(retryAfter > TimeSpan.Zero);
		limiter.RecordSuccess("user@example.com");
		Assert.False(limiter.IsBlocked("user@example.com", out _));
	}
}
