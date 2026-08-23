// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Services;

public readonly record struct PasswordPolicyEvaluation(
	bool HasValidLength,
	bool HasUppercase,
	bool HasLowercase,
	bool HasDigit,
	bool HasSymbol,
	bool ExcludesAccountName)
{
	public bool IsValid =>
		HasValidLength &&
		HasUppercase &&
		HasLowercase &&
		HasDigit &&
		HasSymbol &&
		ExcludesAccountName;
}

public static class PasswordPolicy
{
	public const int MinimumLength = 12;
	public const int MaximumLength = 128;

	public static PasswordPolicyEvaluation Evaluate(string password, string? email = null)
	{
		ArgumentNullException.ThrowIfNull(password);
		var localPart = string.IsNullOrWhiteSpace(email) ? string.Empty : email.Trim().Split('@', 2)[0];
		var excludesAccountName = localPart.Length < 3 || !password.Contains(localPart, StringComparison.OrdinalIgnoreCase);

		return new PasswordPolicyEvaluation(
			password.Length >= MinimumLength && password.Length <= MaximumLength,
			password.Any(char.IsUpper),
			password.Any(char.IsLower),
			password.Any(char.IsDigit),
			password.Any(character => !char.IsLetterOrDigit(character)),
			excludesAccountName);
	}

	public static void Validate(string password, string? email = null)
	{
		var result = Evaluate(password, email);
		if (!result.HasValidLength)
			throw new ArgumentException($"The password must contain {MinimumLength}-{MaximumLength} characters.", nameof(password));
		if (!result.HasUppercase || !result.HasLowercase || !result.HasDigit || !result.HasSymbol)
			throw new ArgumentException("The password must include uppercase, lowercase, a number, and a symbol.", nameof(password));
		if (!result.ExcludesAccountName)
			throw new ArgumentException("The password must not contain the account name.", nameof(password));
	}
}
