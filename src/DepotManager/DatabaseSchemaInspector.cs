using Depot.Models;
using Depot.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySqlConnector;

namespace DepotManager;

public sealed class DatabaseSchemaInspector
{
    public async Task<int> ReadSchemaVersionAsync(DatabaseConnectionSettings settings, CancellationToken cancellationToken) =>
        (await ReadCompatibilityAsync(settings, cancellationToken)).CoreVersion;

    public async Task<DatabaseSchemaCompatibility> ReadCompatibilityAsync(DatabaseConnectionSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.Provider switch
        {
            DatabaseProvider.Local => await ReadSqliteAsync(settings, cancellationToken),
            DatabaseProvider.SqlServer => await ReadSqlServerAsync(settings, cancellationToken),
            DatabaseProvider.MySql => await ReadMySqlAsync(settings, cancellationToken),
            _ => throw new NotSupportedException($"Database provider '{settings.Provider}' is not supported.")
        };
    }

    public async Task<DatabaseSchemaCompatibility> ReadStableCompatibilityAsync(DatabaseConnectionSettings settings, CancellationToken cancellationToken)
    {
        var before = await ReadCompatibilityAsync(settings, cancellationToken);
        var after = await ReadCompatibilityAsync(settings, cancellationToken);
        if (!before.HasSameState(after))
            throw new InvalidOperationException("Database schema changed during inspection. Stop all Depot clients and retry maintenance.");
        return after;
    }

    private static async Task<DatabaseSchemaCompatibility> ReadSqliteAsync(DatabaseConnectionSettings settings, CancellationToken cancellationToken)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(settings.LocalDatabasePath),
            ForeignKeys = true,
            Mode = SqliteOpenMode.ReadOnly
        };
        await using var connection = new SqliteConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return await DatabaseCompatibilityReader.ReadAsync(connection, settings.Provider, cancellationToken);
    }

    private static async Task<DatabaseSchemaCompatibility> ReadSqlServerAsync(DatabaseConnectionSettings settings, CancellationToken cancellationToken)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = $"{settings.SqlServerHost},{settings.SqlServerPort}",
            InitialCatalog = settings.SqlServerDatabase,
            UserID = settings.SqlServerUserName,
            Password = settings.SqlServerPassword,
            Encrypt = settings.EncryptSqlServerConnection,
            TrustServerCertificate = settings.TrustSqlServerCertificate,
            ConnectTimeout = 10,
            Pooling = true,
            ApplicationName = "Depot Manager"
        };
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return await DatabaseCompatibilityReader.ReadAsync(connection, settings.Provider, cancellationToken);
    }

    private static async Task<DatabaseSchemaCompatibility> ReadMySqlAsync(DatabaseConnectionSettings settings, CancellationToken cancellationToken)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = settings.MySqlHost,
            Port = (uint)settings.MySqlPort,
            Database = settings.MySqlDatabase,
            UserID = settings.MySqlUserName,
            Password = settings.MySqlPassword,
            SslMode = settings.UseMySqlTls ? MySqlSslMode.Required : MySqlSslMode.Disabled,
            ConnectionTimeout = 10,
            Pooling = true,
            ApplicationName = "Depot Manager"
        };
        await using var connection = new MySqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return await DatabaseCompatibilityReader.ReadAsync(connection, settings.Provider, cancellationToken);
    }

    public static string DescribeTarget(DatabaseConnectionSettings settings) => settings.Provider switch
    {
        DatabaseProvider.Local => Path.GetFullPath(settings.LocalDatabasePath),
        DatabaseProvider.SqlServer => $"{settings.SqlServerHost}:{settings.SqlServerPort} / {settings.SqlServerDatabase}",
        DatabaseProvider.MySql => $"{settings.MySqlHost}:{settings.MySqlPort} / {settings.MySqlDatabase}",
        _ => "Unknown"
    };
}
