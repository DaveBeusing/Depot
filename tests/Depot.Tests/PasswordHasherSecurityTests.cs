// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Services;

using Xunit;

namespace Depot.Tests;

public sealed class PasswordHasherSecurityTests
{
	[Fact]
	public void HashUsesExpectedVersionedFormatAndUniqueSalt()
	{
		var hasher = new PasswordHasher();
		var first = hasher.Hash("Correct horse battery staple");
		var second = hasher.Hash("Correct horse battery staple");
		Assert.StartsWith($"pbkdf2-sha256${PasswordHasher.CurrentIterations}$", first, StringComparison.Ordinal);
		Assert.StartsWith($"pbkdf2-sha256${PasswordHasher.CurrentIterations}$", second, StringComparison.Ordinal);
		Assert.NotEqual(first, second);
	}

	[Fact]
	public void VerifyAcceptsCorrectPasswordAndRejectsWrongPassword()
	{
		var hasher = new PasswordHasher();
		var encoded = hasher.Hash("Strong password 123!");
		Assert.True(hasher.Verify("Strong password 123!", encoded));
		Assert.False(hasher.Verify("Wrong password 123!", encoded));
	}

	[Fact]
	public void LegacyWorkFactorRemainsVerifiableButIsMarkedForUpgrade()
	{
		const string legacy = "pbkdf2-sha256$100000$Rud+hXZ518ixWQ0N37gQng==$iCVZruG4xsuNpG3pJfVRKigJX534QCbXGH4BWDm0T/s=";
		var hasher = new PasswordHasher();
		Assert.True(hasher.Verify("Depot123!", legacy));
		Assert.True(hasher.NeedsUpgrade(legacy));
	}

	[Theory]
	[InlineData("")]
	[InlineData("not-a-hash")]
	[InlineData("pbkdf2-sha1$600000$AAAA$AAAA")]
	[InlineData("pbkdf2-sha256$0$AAAA$AAAA")]
	[InlineData("pbkdf2-sha256$invalid$AAAA$AAAA")]
	[InlineData("pbkdf2-sha256$600000$not-base64$not-base64")]
	public void VerifyRejectsMalformedOrUnsupportedHashes(string encoded)
	{
		Assert.False(new PasswordHasher().Verify("password", encoded));
	}
}
