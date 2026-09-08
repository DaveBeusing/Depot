// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;

using Depot.Models;

namespace Depot.Data;

internal sealed class DatabaseProvisioningLock : IDisposable
{
	private const int TimeoutSeconds = 60;
	private readonly DbConnection? _connection;
	private readonly Mutex? _mutex;
	private readonly string? _resource;
	private readonly DatabaseProvider _provider;
	private bool _ownsMutex;

	private DatabaseProvisioningLock(DbConnection connection, DatabaseProvider provider, string resource)
	{
		_connection = connection;
		_provider = provider;
		_resource = resource;
	}

	private DatabaseProvisioningLock(Mutex mutex, bool ownsMutex)
	{
		_mutex = mutex;
		_ownsMutex = ownsMutex;
		_provider = DatabaseProvider.Local;
	}

	public static DatabaseProvisioningLock Acquire(IDatabaseConnectionFactory connectionFactory)
	{
		ArgumentNullException.ThrowIfNull(connectionFactory);
		return connectionFactory switch
		{
			SqlServerConnectionFactory sqlServer => AcquireSqlServer(sqlServer),
			MySqlConnectionFactory mySql => AcquireMySql(mySql),
			SqliteConnectionFactory => AcquireSqlite(),
			_ => throw new NotSupportedException($"Provisioning lock is not supported for provider '{connectionFactory.Provider}'.")
		};
	}

	private static DatabaseProvisioningLock AcquireSqlServer(SqlServerConnectionFactory factory)
	{
		var connection = factory.CreateMasterConnection();
		try
		{
			connection.Open();
			var resource = $"Depot:Provisioning:{factory.DatabaseName}";
			using var command = connection.CreateCommand();
			command.CommandText =
				"DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource=@Resource, @LockMode='Exclusive', @LockOwner='Session', @LockTimeout=60000; SELECT @result;";
			var parameter = command.CreateParameter();
			parameter.ParameterName = "@Resource";
			parameter.Value = resource;
			command.Parameters.Add(parameter);
			var result = Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
			if (result < 0) throw new TimeoutException($"Timed out waiting for SQL Server provisioning lock '{resource}' (result {result}).");
			return new DatabaseProvisioningLock(connection, DatabaseProvider.SqlServer, resource);
		}
		catch
		{
			connection.Dispose();
			throw;
		}
	}

	private static DatabaseProvisioningLock AcquireMySql(MySqlConnectionFactory factory)
	{
		var connection = factory.CreateServerConnection();
		try
		{
			connection.Open();
			var resource = $"Depot:Provisioning:{factory.DatabaseName}";
			using var command = connection.CreateCommand();
			command.CommandText = "SELECT GET_LOCK(@Resource, 60);";
			var parameter = command.CreateParameter();
			parameter.ParameterName = "@Resource";
			parameter.Value = resource;
			command.Parameters.Add(parameter);
			var result = command.ExecuteScalar();
			if (result is null || result is DBNull || Convert.ToInt32(result, CultureInfo.InvariantCulture) != 1)
				throw new TimeoutException($"Timed out waiting for MySQL/MariaDB provisioning lock '{resource}'.");
			return new DatabaseProvisioningLock(connection, DatabaseProvider.MySql, resource);
		}
		catch
		{
			connection.Dispose();
			throw;
		}
	}

	private static DatabaseProvisioningLock AcquireSqlite()
	{
		var mutex = new Mutex(false, @"Local\Depot.DatabaseProvisioning.SQLite");
		var acquired = false;
		try
		{
			try
			{
				acquired = mutex.WaitOne(TimeSpan.FromSeconds(TimeoutSeconds));
			}
			catch (AbandonedMutexException)
			{
				acquired = true;
			}
			if (!acquired) throw new TimeoutException("Timed out waiting for the SQLite provisioning lock.");
			return new DatabaseProvisioningLock(mutex, true);
		}
		catch
		{
			mutex.Dispose();
			throw;
		}
	}

	public void Dispose()
	{
		if (_connection is not null)
		{
			try
			{
				if (_connection.State == System.Data.ConnectionState.Open && !string.IsNullOrWhiteSpace(_resource))
				{
					using var command = _connection.CreateCommand();
					if (_provider == DatabaseProvider.SqlServer)
						command.CommandText = "EXEC sys.sp_releaseapplock @Resource=@Resource, @LockOwner='Session';";
					else
						command.CommandText = "SELECT RELEASE_LOCK(@Resource);";
					var parameter = command.CreateParameter();
					parameter.ParameterName = "@Resource";
					parameter.Value = _resource;
					command.Parameters.Add(parameter);
					command.ExecuteNonQuery();
				}
			}
			catch
			{
				// Session-scoped locks are released when the connection is disposed.
			}
			finally
			{
				_connection.Dispose();
			}
		}

		if (_mutex is not null)
		{
			if (_ownsMutex)
			{
				_mutex.ReleaseMutex();
				_ownsMutex = false;
			}
			_mutex.Dispose();
		}
	}
}
