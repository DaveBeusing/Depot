// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Diagnostics;
using System.Text;
using System.Windows;

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
		CopyInformationCommand = new RelayCommand(CopyInformation);
	}

	public ApplicationVersionInfo Information { get; }
	public RelayCommand OpenRepositoryCommand { get; }
	public RelayCommand CopyInformationCommand { get; }

	public string VersionBadgeText => $"Version {Information.Version}";

	public string DatabaseSchemaDisplay => $"Schema {Information.DatabaseSchemaVersion}";

	private void CopyInformation()
	{
		var text = new StringBuilder()
			.AppendLine(Information.ProductName)
			.AppendLine($"Application version: {Information.Version}")
			.AppendLine($"Build metadata: {Information.BuildMetadata}")
			.AppendLine($"Runtime: {Information.Runtime}")
			.AppendLine($"Operating system: {Information.OperatingSystem}")
			.AppendLine($"Architecture: {Information.ProcessArchitecture}")
			.AppendLine($"Database schema: {Information.DatabaseSchemaVersion}")
			.AppendLine($"License: {Information.License}")
			.Append($"Repository: {Information.RepositoryUrl}")
			.ToString();
		try
		{
			Clipboard.SetText(text);
			CompleteOperation(false, "Product information copied");
		}
		catch (Exception exception)
		{
			FailOperation(exception, "Product information could not be copied");
		}
	}

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
