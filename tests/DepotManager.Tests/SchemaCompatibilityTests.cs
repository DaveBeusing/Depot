using Depot.Models;
using DepotManager;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Depot.Tests;

public sealed class SchemaCompatibilityTests
{
    [Theory]
    [InlineData(DatabaseProvider.Local)]
    [InlineData(DatabaseProvider.SqlServer)]
    [InlineData(DatabaseProvider.MySql)]
    public void SameCoreFeatureUpgradeRequiresProtectedMigrationAndBlocksRollback(DatabaseProvider provider)
    {
        var previous = SchemaCompatibilityFixture.Create(provider: provider, salesVersion: 15);
        var target = SchemaCompatibilityFixture.Create(provider: provider, salesVersion: 16);
        Assert.Equal(previous.CoreVersion, target.CoreVersion);
        Assert.True(previous.RequiresMigrationTo(target));
        Assert.Throws<InvalidOperationException>(() => previous.RequireCompatibleWith(target));
        Assert.False(RollbackCompatibility.IsCompatible(target, previous));
        Assert.True(RollbackCompatibility.IsCompatible(previous, SchemaCompatibilityFixture.Create(provider: provider, salesVersion: 15)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(17)]
    public void HealthRejectsMissingOlderAndNewerFeatureVersions(int salesVersion)
    {
        var actual = SchemaCompatibilityFixture.Create(salesVersion: salesVersion == 0 ? 16 : salesVersion);
        if (salesVersion == 0) actual.FeatureVersions.Remove("Sales");
        Assert.Throws<InvalidOperationException>(() => actual.RequireCompatibleWith(SchemaCompatibilityFixture.Create()));
    }

    [Fact]
    public void UnknownFeatureCannotBeSilentlyIgnored()
    {
        var actual = SchemaCompatibilityFixture.Create();
        actual.FeatureVersions.Add("FutureFeature", 1);
        Assert.Throws<InvalidOperationException>(() => actual.RequiresMigrationTo(SchemaCompatibilityFixture.Create()));
    }

    [Fact]
    public void StructuralDefinitionOrIndexChangeBlocksRollbackAtTheSameVersions()
    {
        var baseline = SchemaCompatibilityFixture.Create();
        var changed = SchemaCompatibilityFixture.Create();
        changed.TableDefinitions["Items"] = "Gtin INTEGER NULL";
        Assert.False(RollbackCompatibility.IsCompatible(changed, baseline));
        changed = SchemaCompatibilityFixture.Create();
        changed.Indexes["Items"] = [new("IX_Items_Gtin", ["Gtin"], false, "Gtin IS NOT NULL")];
        Assert.True(changed.RequiresMigrationTo(baseline));
        Assert.False(RollbackCompatibility.IsCompatible(changed, baseline));
    }

    [Fact]
    public void DisabledSqlServerIndexDoesNotPassHealth()
    {
        var actual = SchemaCompatibilityFixture.Create(provider: DatabaseProvider.SqlServer);
        actual.Indexes["Items"] = [new("IX_Items_Gtin", ["Gtin"], true, "Gtin IS NOT NULL", IsEnabled: false)];
        Assert.Throws<InvalidOperationException>(() => actual.RequireCompatibleWith(SchemaCompatibilityFixture.Create()));
    }

    [Fact]
    public void PrefixIndexDoesNotSatisfyAFullColumnRequirement()
    {
        var actual = SchemaCompatibilityFixture.Create(provider: DatabaseProvider.MySql);
        actual.Indexes["Items"] = [new("IX_Items_Gtin", ["Gtin PREFIX(5)"], true)];
        Assert.Throws<InvalidOperationException>(() => actual.RequireCompatibleWith(SchemaCompatibilityFixture.Create()));
    }

    [Fact]
    public async Task RollbackReadsTheLiveFeatureStateInsteadOfTrustingACachedCandidate()
    {
        var root = Path.Combine(Path.GetTempPath(), $"depot-live-rollback-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var settings = new DatabaseConnectionSettings { Provider = DatabaseProvider.Local, LocalDatabasePath = Path.Combine(root, "depot.db") };
        try
        {
            await using var connection = new SqliteConnection($"Data Source={settings.LocalDatabasePath};Pooling=False");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE DatabaseInfo(Version INTEGER NOT NULL); INSERT INTO DatabaseInfo VALUES(30); CREATE TABLE DepotFeatureVersions(Name TEXT PRIMARY KEY,Version INTEGER NOT NULL); INSERT INTO DepotFeatureVersions VALUES('Sales',15);";
            await command.ExecuteNonQueryAsync();
            var baseline = await new DatabaseSchemaInspector().ReadStableCompatibilityAsync(settings, CancellationToken.None);
            var candidate = new RollbackCandidate(Path.Combine(root, "previous.exe"), new Version(1, 0), 30, DateTimeOffset.UtcNow, true, "Available", baseline);
            command.CommandText = "UPDATE DepotFeatureVersions SET Version=16 WHERE Name='Sales';";
            await command.ExecuteNonQueryAsync();

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                RollbackMetadataService.RestoreExecutableAsync(new InstallationService(root, _ => { }), candidate, settings, CancellationToken.None));
            Assert.Contains("structural schema differs", exception.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(root, "Depot.exe")));
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }
}

internal static class SchemaCompatibilityFixture
{
    public static DatabaseSchemaCompatibility Create(int coreVersion = 30, int salesVersion = 16, DatabaseProvider provider = DatabaseProvider.Local) => new()
    {
        CoreVersion = coreVersion,
        Provider = provider,
        FeatureVersions = new() { ["Sales"] = salesVersion },
        Tables = new()
        {
            ["DatabaseInfo"] = ["Version"], ["DepotFeatureVersions"] = ["Name", "Version"], ["Items"] = ["Id", "Gtin"]
        },
        TableDefinitions = new() { ["DatabaseInfo"] = "Version INTEGER", ["DepotFeatureVersions"] = "Name TEXT, Version INTEGER", ["Items"] = "Gtin TEXT NULL" },
        Indexes = new() { ["Items"] = [new("IX_Items_Gtin", ["Gtin"], true, provider == DatabaseProvider.MySql ? null : "Gtin IS NOT NULL", "UNIQUE INDEX IX_Items_Gtin(Gtin)")] }
    };
}
