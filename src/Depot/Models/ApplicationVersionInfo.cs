// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed record ApplicationVersionInfo(
	string ProductName,
	string Description,
	string Version,
	string InformationalVersion,
	string ReleaseChannel,
	string BuildMetadata,
	string AssemblyVersion,
	string FileVersion,
	string Runtime,
	string OperatingSystem,
	string ProcessArchitecture,
	string Copyright,
	string License,
	string RepositoryUrl,
	int DatabaseSchemaVersion);
