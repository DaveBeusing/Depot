// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Diagnostics;

using Depot.Commands;
using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels.Administration;

public sealed class AboutViewModel : BaseViewModel
{
	public AboutViewModel(ApplicationInformationService applicationInformationService)
	{
		Information = applicationInformationService.GetVersionInfo();
		OpenRepositoryCommand = new RelayCommand(OpenRepository, CanOpenRepository);
	}

	public ApplicationVersionInfo Information { get; }
	public RelayCommand OpenRepositoryCommand { get; }

	public string VersionBadgeText => $"Version {Information.Version}";

	public string DatabaseSchemaDisplay => $"Schema {Information.DatabaseSchemaVersion}";

	private bool CanOpenRepository() => Uri.TryCreate(Information.RepositoryUrl, UriKind.Absolute, out _);

	private void OpenRepository()
	{
		if (!Uri.TryCreate(Information.RepositoryUrl, UriKind.Absolute, out var uri)) return;
		try
		{
			Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
		}
		catch (Exception exception)
		{
			FailOperation(exception, "The Depot repository could not be opened");
		}
	}
}
