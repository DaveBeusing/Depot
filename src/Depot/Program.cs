// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Windows;

using Depot.Diagnostics;
using Depot.Provisioning;

namespace Depot;

public static class Program
{
	[STAThread]
	public static void Main(string[] args)
	{
		try
		{
			if (DepotManagerCommand.TryRun(args, out var exitCode))
			{
				Environment.ExitCode = exitCode;
				return;
			}

			StartupDiagnostics.Log("Program.Main entered.");
			var app = new App();
			StartupDiagnostics.Log("App instance created.");
			app.InitializeComponent();
			StartupDiagnostics.Log("App resources initialized.");
			app.Run();
		}
		catch (Exception ex)
		{
			StartupDiagnostics.LogException(ex);
			StartupDiagnostics.ShowStartupError(ex);
			Environment.ExitCode = 1;
		}
	}
}
