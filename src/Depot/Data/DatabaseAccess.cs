// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;

using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;

using MySqlConnector;

namespace Depot.Data;

public sealed class DatabaseAccess
{
	private readonly IDatabaseConnectionFactory _connectionFactory;

	public DatabaseAccess(IDatabaseConnectionFactory connectionFactory)
	{
		_connectionFactory = connectionFactory;
	}

	public string InventoryLockSql => _connectionFactory.GetInventoryLockSql();

	public static string CaseInsensitiveEquals(string column, string parameter) =>
		$"{column} = {parameter} COLLATE NOCASE";

	public int Execute(string sql, params DatabaseParameter[] parameters)
	{
		using var session = OpenSession();
		return session.Execute(sql, parameters);
	}

	public long Insert(string sql, params DatabaseParameter[] parameters)
	{
		using var session = OpenSession();
		return session.Insert(sql, parameters);
	}

	public IReadOnlyList<T> Query<T>(
		string sql,
		Func<DbDataReader, T> map,
		params DatabaseParameter[] parameters)
	{
		using var session = OpenSession();
		return session.Query(sql, map, parameters);
	}

	public T? QuerySingleOrDefault<T>(
		string sql,
		Func<DbDataReader, T> map,
		params DatabaseParameter[] parameters)
		where T : class
	{
		using var session = OpenSession();
		return session.QuerySingleOrDefault(sql, map, parameters);
	}

	public T ExecuteInWriteTransaction<T>(Func<DatabaseSession, T> operation)
	{
		for (var attempt = 1; ; attempt++)
		{
			try
			{
				return ExecuteInWriteTransactionCore(operation);
			}
			catch (Exception exception) when (attempt < 3 && IsTransientWriteConflict(exception))
			{
				Thread.Sleep(50 * attempt);
			}
		}
	}

	private T ExecuteInWriteTransactionCore<T>(Func<DatabaseSession, T> operation)
	{
		using var connection = _connectionFactory.CreateConnection();
		connection.Open();
		using var transaction = _connectionFactory.BeginWriteTransaction(connection);
		using var session = new DatabaseSession(connection, transaction);
		var result = operation(session);
		transaction.Commit();
		return result;
	}

	private static bool IsTransientWriteConflict(Exception exception) =>
		exception switch
		{
			SqlException sql => sql.Number is 1205 or 3960,
			SqliteException sqlite => sqlite.SqliteErrorCode is 5 or 6,
			MySqlException mySql => mySql.Number is 1205 or 1213,
			_ => false
		};

	private DatabaseSession OpenSession()
	{
		var connection = _connectionFactory.CreateConnection();
		try
		{
			connection.Open();
			return new DatabaseSession(connection, null);
		}
		catch
		{
			connection.Dispose();
			throw;
		}
	}
}

public sealed class DatabaseSession : IDisposable
{
	private readonly DbConnection _connection;
	private readonly DbTransaction? _transaction;

	internal DatabaseSession(DbConnection connection, DbTransaction? transaction)
	{
		_connection = connection;
		_transaction = transaction;
	}

	public int Execute(string sql, params DatabaseParameter[] parameters)
	{
		using var command = CreateCommand(sql, parameters);
		return command.ExecuteNonQuery();
	}

	public object? ExecuteScalar(string sql, params DatabaseParameter[] parameters)
	{
		using var command = CreateCommand(sql, parameters);
		return command.ExecuteScalar();
	}

	public long Insert(string sql, params DatabaseParameter[] parameters)
	{
		var value = ExecuteScalar($"{sql}{Environment.NewLine}SELECT last_insert_rowid();", parameters);
		return Convert.ToInt64(value, CultureInfo.InvariantCulture);
	}

	public IReadOnlyList<T> Query<T>(
		string sql,
		Func<DbDataReader, T> map,
		params DatabaseParameter[] parameters)
	{
		using var command = CreateCommand(sql, parameters);
		using var reader = command.ExecuteReader();
		var result = new List<T>();
		while (reader.Read())
		{
			result.Add(map(reader));
		}

		return result;
	}

	public T? QuerySingleOrDefault<T>(
		string sql,
		Func<DbDataReader, T> map,
		params DatabaseParameter[] parameters)
		where T : class
	{
		using var command = CreateCommand(sql, parameters);
		using var reader = command.ExecuteReader();
		return reader.Read() ? map(reader) : null;
	}

	public void Dispose()
	{
		if (_transaction is null)
		{
			_connection.Dispose();
		}
	}

	private DbCommand CreateCommand(string sql, IReadOnlyList<DatabaseParameter> parameters)
	{
		var command = _connection.CreateCommand();
		command.Transaction = _transaction;
		command.CommandText = sql;
		foreach (var parameter in parameters)
		{
			command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
		}

		return command;
	}
}
