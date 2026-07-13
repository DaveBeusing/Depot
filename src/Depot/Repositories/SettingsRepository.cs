// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Depot.Models;

namespace Depot.Repositories;

public sealed class SettingsRepository
{
	private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Depot.Settings.v1");
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		WriteIndented = true
	};

	private readonly string _settingsPath;

	public SettingsRepository(string settingsPath)
	{
		_settingsPath = settingsPath;
	}

	public bool Exists() => File.Exists(_settingsPath);

	public DatabaseConnectionSettings Load()
	{
		var envelopeJson = File.ReadAllText(_settingsPath, Encoding.UTF8);
		var envelope = JsonSerializer.Deserialize<SettingsEnvelope>(envelopeJson, JsonOptions)
			?? throw new InvalidOperationException("The Depot settings file is empty or invalid.");

		if (envelope.Version != 1 ||
			!string.Equals(envelope.Protection, "DPAPI-CurrentUser", StringComparison.Ordinal) ||
			string.IsNullOrWhiteSpace(envelope.Payload))
		{
			throw new InvalidOperationException("The Depot settings file uses an unsupported format.");
		}

		try
		{
			var encryptedBytes = Convert.FromBase64String(envelope.Payload);
			var plainBytes = ProtectedData.Unprotect(
				encryptedBytes,
				Entropy,
				DataProtectionScope.CurrentUser);
			var settings = JsonSerializer.Deserialize<DatabaseConnectionSettings>(plainBytes, JsonOptions);
			return settings ?? throw new InvalidOperationException("The protected Depot settings are empty.");
		}
		catch (CryptographicException exception)
		{
			throw new InvalidOperationException(
				"The Depot settings cannot be decrypted for the current Windows user.",
				exception);
		}
		catch (FormatException exception)
		{
			throw new InvalidOperationException("The Depot settings payload is invalid.", exception);
		}
	}

	public void Save(DatabaseConnectionSettings settings)
	{
		var plainBytes = JsonSerializer.SerializeToUtf8Bytes(settings, JsonOptions);
		var encryptedBytes = ProtectedData.Protect(
			plainBytes,
			Entropy,
			DataProtectionScope.CurrentUser);
		var envelope = new SettingsEnvelope
		{
			Version = 1,
			Protection = "DPAPI-CurrentUser",
			Payload = Convert.ToBase64String(encryptedBytes)
		};
		var envelopeJson = JsonSerializer.Serialize(envelope, JsonOptions);
		var temporaryPath = _settingsPath + ".tmp";

		File.WriteAllText(temporaryPath, envelopeJson, new UTF8Encoding(false));
		File.Move(temporaryPath, _settingsPath, true);
	}

	private sealed class SettingsEnvelope
	{
		public int Version { get; set; }

		public string Protection { get; set; } = string.Empty;

		public string Payload { get; set; } = string.Empty;
	}
}
