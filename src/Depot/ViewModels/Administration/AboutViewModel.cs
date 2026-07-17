// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels.Administration;

public sealed class AboutViewModel : BaseViewModel
{
	public AboutViewModel(ApplicationInformationService applicationInformationService)
	{
		Information = applicationInformationService.GetVersionInfo();
	}

	public ApplicationVersionInfo Information { get; }

	public string VersionBadgeText => $"Version {Information.Version}";

	public string DatabaseSchemaDisplay => $"Schema {Information.DatabaseSchemaVersion}";
}
