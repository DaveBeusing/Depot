// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;
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
    private const string HealthCheckArgument = "--manager-health-check";
    private const string MigrateArgument = "--manager-migrate";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

    public static bool TryRun(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (args.Length != 2 || !IsManagerCommand(args[0])) return false;
        var responsePath = args[1];
        try
        {
            if (args[0] is TestDatabaseArgument or ProvisionArgument)
            {
                RunProvisioningCommand(args[0], responsePath);
            }
            else
            {
                RunMaintenanceCommand(args[0], responsePath);
            }
            return true;
        }
        catch (Exception exception)
        {
            exitCode = 2;
            try { WriteResponse(responsePath, false, exception.Message, null); } catch { }
            return true;
        }
    }

    private static bool IsManagerCommand(string argument) => argument is
        TestDatabaseArgument or ProvisionArgument or HealthCheckArgument or MigrateArgument;

    private static void RunProvisioningCommand(string argument, string responsePath)
    {
        var requestJson = Console.In.ReadToEnd();
        var request = JsonSerializer.Deserialize<ProvisioningRequest>(requestJson, JsonOptions)
            ?? throw new InvalidOperationException("The provisioning request is empty or invalid.");
        var settingsService = new SettingsService(new SettingsRepository("depot.settings"));
        var settings = settingsService.Validate(request.Database);
        new DatabaseConnectionTester().Test(settings);

        int? schemaVersion = null;
        if (argument == ProvisionArgument)
        {
            settingsService.Save(settings);
            var factory = DatabaseProvisioningService.Initialize(settings);
            CreateAdministratorIfRequired(factory, request.Administrator);
            schemaVersion = ReadSchemaVersion(factory);
        }

        WriteResponse(
            responsePath,
            true,
            argument == ProvisionArgument ? "Provisioning completed." : "Connection successful.",
            schemaVersion);
    }

    private static void RunMaintenanceCommand(string argument, string responsePath)
    {
        var repository = new SettingsRepository("depot.settings");
        if (!repository.Exists()) throw new InvalidOperationException("Depot configuration is incomplete because depot.settings is missing.");
        var settingsService = new SettingsService(repository);
        var settings = settingsService.Validate(repository.Load());
        new DatabaseConnectionTester().Test(settings);

        IDatabaseConnectionFactory factory;
        if (argument == MigrateArgument)
        {
            factory = DatabaseProvisioningService.Initialize(settings);
        }
        else
        {
            factory = DatabaseProviderFactory.CreateConnectionFactory(settings);
        }

        var schemaVersion = ReadSchemaVersion(factory);
        if (schemaVersion != DatabaseVersion.CurrentVersion)
            throw new InvalidOperationException(
                $"Database schema {schemaVersion} is not compatible with this Depot version, which requires schema {DatabaseVersion.CurrentVersion}.");

        if (argument == HealthCheckArgument)
        {
            var data = new DatabaseAccess(factory);
            var bootstrap = new AdministratorBootstrapService(data, new DatabaseTransactionRunner(data), new AuthorizationService());
            if (bootstrap.RequiresSetupAsync(CancellationToken.None).GetAwaiter().GetResult())
                throw new InvalidOperationException("Depot administrator provisioning is incomplete.");
        }

        WriteResponse(
            responsePath,
            true,
            argument == MigrateArgument ? "Database migration completed." : "Depot health check passed.",
            schemaVersion);
    }

    private static int ReadSchemaVersion(IDatabaseConnectionFactory factory)
    {
        using var connection = factory.CreateConnection();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Version FROM DatabaseInfo;";
        var result = command.ExecuteScalar();
        if (result is null || result is DBNull) throw new InvalidOperationException("The Depot database schema version could not be determined.");
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private static void CreateAdministratorIfRequired(IDatabaseConnectionFactory factory, InitialAdministrator administrator)
    {
        var data = new DatabaseAccess(factory);
        var bootstrap = new AdministratorBootstrapService(data, new DatabaseTransactionRunner(data), new AuthorizationService());
        if (!bootstrap.RequiresSetupAsync(CancellationToken.None).GetAwaiter().GetResult()) return;
        bootstrap.CreateAdministratorAsync(administrator.Email, administrator.DisplayName, administrator.Password, CancellationToken.None).GetAwaiter().GetResult();
    }

    private static void WriteResponse(string path, bool success, string message, int? databaseSchemaVersion)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(new { Success = success, Message = message, DatabaseSchemaVersion = databaseSchemaVersion }, JsonOptions));
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
