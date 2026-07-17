// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Reflection;
using System.Runtime.InteropServices;

using Depot.Data;
using Depot.Models;

namespace Depot.Services;

public sealed class ApplicationInformationService
{
	private readonly Assembly _assembly;

	public ApplicationInformationService(Assembly assembly)
	{
		_assembly = assembly;
	}

	public ApplicationVersionInfo GetVersionInfo()
	{
		var assemblyName = _assembly.GetName();
		var informationalVersion = GetAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
			?? assemblyName.Version?.ToString()
			?? "0.0.0";
		var version = GetSemanticVersion(informationalVersion);

		return new ApplicationVersionInfo(
			GetAttribute<AssemblyProductAttribute>()?.Product ?? assemblyName.Name ?? "Depot",
			GetAttribute<AssemblyDescriptionAttribute>()?.Description ?? string.Empty,
			version,
			informationalVersion,
			GetReleaseChannel(version),
			GetBuildMetadata(informationalVersion),
			assemblyName.Version?.ToString() ?? "—",
			GetAttribute<AssemblyFileVersionAttribute>()?.Version ?? "—",
			RuntimeInformation.FrameworkDescription,
			RuntimeInformation.OSDescription,
			RuntimeInformation.ProcessArchitecture.ToString(),
			GetAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? string.Empty,
			GetMetadata("License") ?? "MIT",
			GetMetadata("RepositoryUrl") ?? string.Empty,
			DatabaseVersion.CurrentVersion);
	}

	public static string GetReleaseChannel(string version)
	{
		var separator = version.IndexOf('-', StringComparison.Ordinal);
		if (separator < 0)
		{
			return "Stable";
		}

		var identifier = version[(separator + 1)..]
			.Split('.', StringSplitOptions.RemoveEmptyEntries)[0];

		return identifier.ToLowerInvariant() switch
		{
			"dev" => "Development",
			"alpha" => "Alpha",
			"beta" => "Beta",
			"preview" => "Preview",
			"rc" => "Release Candidate",
			_ => "Pre-release"
		};
	}

	private T? GetAttribute<T>() where T : Attribute =>
		_assembly.GetCustomAttribute<T>();

	private string? GetMetadata(string key) =>
		_assembly
			.GetCustomAttributes<AssemblyMetadataAttribute>()
			.FirstOrDefault(attribute => string.Equals(attribute.Key, key, StringComparison.Ordinal))
			?.Value;

	private static string GetSemanticVersion(string informationalVersion)
	{
		var separator = informationalVersion.IndexOf('+', StringComparison.Ordinal);
		return separator < 0 ? informationalVersion : informationalVersion[..separator];
	}

	private static string GetBuildMetadata(string informationalVersion)
	{
		var separator = informationalVersion.IndexOf('+', StringComparison.Ordinal);
		return separator < 0 || separator == informationalVersion.Length - 1
			? "Local build"
			: informationalVersion[(separator + 1)..];
	}
}
