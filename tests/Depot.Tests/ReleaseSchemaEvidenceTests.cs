// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Text.Json;

using Depot.Data;
using Depot.Models;
using Depot.Provisioning;

using Xunit;

namespace Depot.Tests;

public sealed class ReleaseSchemaEvidenceTests
{
	[Fact]
	public void ReleaseEvidenceIncludesEveryCurrentSchemaDimension()
	{
		var root = FindRepositoryRoot();
		var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "release-integrity.yml"));
		var staging = ReadBlock(workflow, "- name: Stage manifest, hashes and release evidence", "- name: Upload immutable release candidate evidence");
		Assert.Contains("Start-Process -FilePath (Resolve-Path 'artifacts/release/depot/Depot.exe').Path", staging, StringComparison.Ordinal);
		Assert.Contains("-ArgumentList @('--manager-schema-contract',", staging, StringComparison.Ordinal);
		Assert.Contains("$contractProcess.ExitCode -ne 0 -or -not (Test-Path $contractPath)", staging, StringComparison.Ordinal);
		Assert.Contains("$contractResponse = Get-Content $contractPath -Raw | ConvertFrom-Json", staging, StringComparison.Ordinal);
		Assert.Contains("-not $contractResponse.Success -or $contractResponse.ManagerCommandProtocol -ne 2", staging, StringComparison.Ordinal);
		Assert.Contains("$compatibility = $contractResponse.SchemaCompatibility", staging, StringComparison.Ordinal);
		Assert.Contains($"$compatibility.ContractVersion -ne {DatabaseSchemaCompatibility.CurrentContractVersion}", staging, StringComparison.Ordinal);
		Assert.Contains("@($compatibility.FeatureVersions.PSObject.Properties).Count -lt 14", staging, StringComparison.Ordinal);
		Assert.Contains("@($compatibility.Tables.PSObject.Properties).Count -lt 1", staging, StringComparison.Ordinal);

		// Every exported feature flows into both evidence files, including future registry entries.
		Assert.Contains("$schemas = [ordered]@{ core = $compatibility.CoreVersion }", staging, StringComparison.Ordinal);
		Assert.Contains("foreach ($feature in $compatibility.FeatureVersions.PSObject.Properties)", staging, StringComparison.Ordinal);
		Assert.Contains("$schemas[$feature.Name] = $feature.Value", staging, StringComparison.Ordinal);
		var manifest = ReadBlock(staging, "$manifest = [ordered]@{", "$manifest | ConvertTo-Json");
		Assert.Contains("databaseSchemaVersion = $schemas.core", manifest, StringComparison.Ordinal);
		Assert.Contains("schemaVersions = $schemas", manifest, StringComparison.Ordinal);
		Assert.Contains("schemaCompatibility = $compatibility", manifest, StringComparison.Ordinal);
		Assert.Contains("managerCommandProtocol = 2", manifest, StringComparison.Ordinal);
		var evidence = ReadBlock(staging, "$evidence = [ordered]@{", "$evidence | ConvertTo-Json");
		Assert.Contains("schemaVersions = $schemas", evidence, StringComparison.Ordinal);
	}

	[Fact]
	public void SchemaContractCommandExportsEveryCurrentSchemaDimension()
	{
		var responsePath = Path.Combine(Path.GetTempPath(), $"depot-release-schema-{Guid.NewGuid():N}.json");
		try
		{
			Assert.True(DepotManagerCommand.TryRun(["--manager-schema-contract", responsePath], out var exitCode));
			Assert.Equal(0, exitCode);
			using var response = JsonDocument.Parse(File.ReadAllText(responsePath));
			var root = response.RootElement;
			Assert.True(root.GetProperty("Success").GetBoolean());
			Assert.Equal(2, root.GetProperty("ManagerCommandProtocol").GetInt32());
			var expected = DatabaseSchemaContract.Current;
			Assert.Equal(expected.CoreVersion, root.GetProperty("DatabaseSchemaVersion").GetInt32());
			var exported = Assert.IsType<DatabaseSchemaCompatibility>(root.GetProperty("SchemaCompatibility").Deserialize<DatabaseSchemaCompatibility>());
			exported.Validate(requirement: true);
			// Preserve the complete payload: Core, every feature, all columns and required index semantics.
			Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(exported));
		}
		finally
		{
			File.Delete(responsePath);
		}
	}

	private static string ReadBlock(string source, string start, string end)
	{
		var startIndex = source.IndexOf(start, StringComparison.Ordinal);
		Assert.True(startIndex >= 0, $"Required release workflow block '{start}' was not found.");
		var endIndex = source.IndexOf(end, startIndex, StringComparison.Ordinal);
		Assert.True(endIndex > startIndex, $"Required release workflow block ending '{end}' was not found.");
		return source[startIndex..endIndex];
	}

	private static string FindRepositoryRoot()
	{
		for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
		{
			if (File.Exists(Path.Combine(directory.FullName, "Depot.slnx")))
				return directory.FullName;
		}
		throw new DirectoryNotFoundException("Could not locate the Depot repository root.");
	}
}
