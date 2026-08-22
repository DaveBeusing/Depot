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

		Assert.StartsWith("pbkdf2-sha256$210000$", first, StringComparison.Ordinal);
		Assert.StartsWith("pbkdf2-sha256$210000$", second, StringComparison.Ordinal);
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

	[Theory]
	[InlineData("")]
	[InlineData("not-a-hash")]
	[InlineData("pbkdf2-sha1$210000$AAAA$AAAA")]
	[InlineData("pbkdf2-sha256$0$AAAA$AAAA")]
	[InlineData("pbkdf2-sha256$invalid$AAAA$AAAA")]
	[InlineData("pbkdf2-sha256$210000$not-base64$not-base64")]
	public void VerifyRejectsMalformedOrUnsupportedHashes(string encoded)
	{
		var hasher = new PasswordHasher();

		Assert.False(hasher.Verify("password", encoded));
	}
}
