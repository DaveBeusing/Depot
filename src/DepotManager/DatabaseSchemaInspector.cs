using Depot.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySqlConnector;

namespace DepotManager;

public sealed class DatabaseSchemaInspector
{
    public async Task<int> ReadSchemaVersionAsync(DatabaseConnectionSettings settings, CancellationToken cancellationToken)
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

    private static async Task<int> ReadSqliteAsync(DatabaseConnectionSettings settings, CancellationToken cancellationToken)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(settings.LocalDatabasePath),
            ForeignKeys = true
        };
        await using var connection = new SqliteConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return await ReadAsync(connection, cancellationToken);
    }

    private static async Task<int> ReadSqlServerAsync(DatabaseConnectionSettings settings, CancellationToken cancellationToken)
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
        return await ReadAsync(connection, cancellationToken);
    }

    private static async Task<int> ReadMySqlAsync(DatabaseConnectionSettings settings, CancellationToken cancellationToken)
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
        return await ReadAsync(connection, cancellationToken);
    }

    private static async Task<int> ReadAsync(System.Data.Common.DbConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Version FROM DatabaseInfo;";
        var result = await command.ExecuteScalarAsync(cancellationToken);
        if (result is null || result is DBNull)
            throw new InvalidOperationException("The Depot database schema version could not be determined.");
        return Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    public static string DescribeTarget(DatabaseConnectionSettings settings) => settings.Provider switch
    {
        DatabaseProvider.Local => Path.GetFullPath(settings.LocalDatabasePath),
        DatabaseProvider.SqlServer => $"{settings.SqlServerHost}:{settings.SqlServerPort} / {settings.SqlServerDatabase}",
        DatabaseProvider.MySql => $"{settings.MySqlHost}:{settings.MySqlPort} / {settings.MySqlDatabase}",
        _ => "Unknown"
    };
}
