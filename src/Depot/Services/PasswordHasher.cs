// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;
using System.Security.Cryptography;

namespace Depot.Services;

public sealed class PasswordHasher
{
	private const string Algorithm = "pbkdf2-sha256";
	private const int Iterations = 210_000;
	private const int SaltSize = 16;
	private const int HashSize = 32;

	public string Hash(string password)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(password);

		var salt = RandomNumberGenerator.GetBytes(SaltSize);
		var hash = Rfc2898DeriveBytes.Pbkdf2(
			password,
			salt,
			Iterations,
			HashAlgorithmName.SHA256,
			HashSize);

		return string.Join(
			'$',
			Algorithm,
			Iterations.ToString(CultureInfo.InvariantCulture),
			Convert.ToBase64String(salt),
			Convert.ToBase64String(hash));
	}

	public bool Verify(string password, string encodedHash)
	{
		if (string.IsNullOrEmpty(password) || string.IsNullOrWhiteSpace(encodedHash))
		{
			return false;
		}

		var parts = encodedHash.Split('$');
		if (parts.Length != 4 ||
			!string.Equals(parts[0], Algorithm, StringComparison.Ordinal) ||
			!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var iterations) ||
			iterations <= 0)
		{
			return false;
		}

		try
		{
			var salt = Convert.FromBase64String(parts[2]);
			var expectedHash = Convert.FromBase64String(parts[3]);
			var actualHash = Rfc2898DeriveBytes.Pbkdf2(
				password,
				salt,
				iterations,
				HashAlgorithmName.SHA256,
				expectedHash.Length);

			return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
		}
		catch (FormatException)
		{
			return false;
		}
	}
}
