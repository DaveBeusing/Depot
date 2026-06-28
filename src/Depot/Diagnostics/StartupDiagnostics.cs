// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.IO;
using System.Text;
using System.Windows;

namespace Depot.Diagnostics;

/// <summary>
/// Provides startup and fatal error diagnostics for the application.
/// </summary>
public static class StartupDiagnostics
{
	private static readonly string LogFilePath =
		Path.Combine(
			AppContext.BaseDirectory,
			"startup.log");

	private static readonly string ErrorFilePath =
		Path.Combine(
			AppContext.BaseDirectory,
			"startup-error.log");

	public static void Log(
		string message)
	{
		var line =
			$"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} {message}";

		File.AppendAllText(
			LogFilePath,
			line + Environment.NewLine,
			Encoding.UTF8);
	}

	public static void LogException(
		Exception exception)
	{
		File.WriteAllText(
			ErrorFilePath,
			exception.ToString(),
			Encoding.UTF8);
	}

	public static void ShowStartupError(
		Exception exception)
	{
		MessageBox.Show(
			$"""
			Depot could not be started.

			An error log was written to:

			{ErrorFilePath}

			{exception}
			""",
			"Startup Error",
			MessageBoxButton.OK,
			MessageBoxImage.Error);
	}

	public static void ShowRuntimeError(
		Exception exception)
	{
		MessageBox.Show(
			$"""
			An unexpected error occurred.

			An error log was written to:

			{ErrorFilePath}

			{exception}
			""",
			"Unexpected Error",
			MessageBoxButton.OK,
			MessageBoxImage.Error);
	}
}