// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Services;

public static class PasswordPolicy
{
	public const int MinimumLength = 12;
	public const int MaximumLength = 128;

	public static void Validate(string password, string? email = null)
	{
		ArgumentNullException.ThrowIfNull(password);
		if (password.Length < MinimumLength || password.Length > MaximumLength)
			throw new ArgumentException($"The password must contain {MinimumLength}-{MaximumLength} characters.", nameof(password));
		if (!password.Any(char.IsUpper) || !password.Any(char.IsLower) || !password.Any(char.IsDigit) || !password.Any(character => !char.IsLetterOrDigit(character)))
			throw new ArgumentException("The password must include uppercase, lowercase, a number, and a symbol.", nameof(password));
		if (!string.IsNullOrWhiteSpace(email))
		{
			var localPart = email.Split('@', 2)[0];
			if (localPart.Length >= 3 && password.Contains(localPart, StringComparison.OrdinalIgnoreCase))
				throw new ArgumentException("The password must not contain the account name.", nameof(password));
		}
	}
}
