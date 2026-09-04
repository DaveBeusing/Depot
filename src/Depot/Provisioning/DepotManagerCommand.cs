// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Text.Json;

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;

namespace Depot.Provisioning;

internal static class DepotManagerCommand
{
	private const string TestDatabaseArgument = "--manager-test-database";
	private const string ProvisionArgument = "--manager-provision";
	private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

	public static bool TryRun(string[] args, out int exitCode)
	{
		exitCode = 0;
		if (args.Length != 2 || (args[0] != TestDatabaseArgument && args[0] != ProvisionArgument)) return false;
		var responsePath = args[1];
		try
		{
			var requestJson = Console.In.ReadToEnd();
			var request = JsonSerializer.Deserialize<ProvisioningRequest>(requestJson, JsonOptions)
				?? throw new InvalidOperationException("The provisioning request is empty or invalid.");
			var settingsService = new SettingsService(new SettingsRepository("depot.settings"));
			var settings = settingsService.Validate(request.Database);
			new DatabaseConnectionTester().Test(settings);

			if (args[0] == ProvisionArgument)
			{
				settingsService.Save(settings);
				InitializeDatabase(settings);
				CreateAdministratorIfRequired(settings, request.Administrator);
			}

			WriteResponse(responsePath, true, args[0] == ProvisionArgument ? "Provisioning completed." : "Connection successful.");
			return true;
		}
		catch (Exception exception)
		{
			exitCode = 2;
			try { WriteResponse(responsePath, false, exception.Message); } catch { }
			return true;
		}
	}

	private static void InitializeDatabase(DatabaseConnectionSettings settings)
	{
		var factory = DatabaseProviderFactory.CreateConnectionFactory(settings);
		DatabaseProviderFactory.CreateInitializer(factory).Initialize();
		SalesSchemaMigration.Migrate(factory);
		FinanceInventoryAccountingSchemaMigration.Migrate(factory);
		UserSessionSchemaMigration.Migrate(factory);
		SecurityEventSchemaMigration.Migrate(factory);
	}

	private static void CreateAdministratorIfRequired(DatabaseConnectionSettings settings, InitialAdministrator administrator)
	{
		var factory = DatabaseProviderFactory.CreateConnectionFactory(settings);
		var data = new DatabaseAccess(factory);
		var bootstrap = new AdministratorBootstrapService(data, new DatabaseTransactionRunner(data), new AuthorizationService());
		if (!bootstrap.RequiresSetupAsync(CancellationToken.None).GetAwaiter().GetResult()) return;
		bootstrap.CreateAdministratorAsync(administrator.Email, administrator.DisplayName, administrator.Password, CancellationToken.None).GetAwaiter().GetResult();
	}

	private static void WriteResponse(string path, bool success, string message)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
		File.WriteAllText(path, JsonSerializer.Serialize(new { Success = success, Message = message }, JsonOptions));
	}

	private sealed class ProvisioningRequest
	{
		public DatabaseConnectionSettings Database { get; set; } = new();
		public InitialAdministrator Administrator { get; set; } = new();
	}

	private sealed class InitialAdministrator
	{
		public string DisplayName { get; set; } = string.Empty;
		public string Email { get; set; } = string.Empty;
		public string Password { get; set; } = string.Empty;
	}
}
