// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;
using System.Runtime.CompilerServices;

using Depot.Models;

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
	public string PurchaseOrderLockSql => _connectionFactory.GetPurchaseOrderLockSql();

	public static string CaseInsensitiveEquals(string column, string parameter) =>
		$"{column} = {parameter} COLLATE NOCASE";

	public async Task<PageResult<T>> QueryPageAsync<T>(
		string sql,
		string countSql,
		Func<DbDataReader, T> map,
		int pageNumber,
		int pageSize,
		CancellationToken cancellationToken,
		params DatabaseParameter[] parameters)
	{
		if (pageNumber < 1) throw new ArgumentOutOfRangeException(nameof(pageNumber));
		if (pageSize < 1) throw new ArgumentOutOfRangeException(nameof(pageSize));

		await using var session = await OpenSessionAsync(cancellationToken);
		var countValue = await session.ExecuteScalarAsync(countSql, cancellationToken, parameters);
		var totalCount = Convert.ToInt64(countValue, CultureInfo.InvariantCulture);
		var pagingParameters = parameters
			.Append(new DatabaseParameter("$PageSize", pageSize))
			.Append(new DatabaseParameter("$Offset", (pageNumber - 1) * pageSize))
			.ToArray();
		var items = await session.QueryAsync(
			$"{sql}{Environment.NewLine}{_connectionFactory.GetPagingClause()};",
			map,
			cancellationToken,
			pagingParameters);
		return new PageResult<T>(items, pageNumber, pageSize, totalCount);
	}

	public async Task<IReadOnlyList<T>> QuerySliceAsync<T>(
		string sql,
		Func<DbDataReader, T> map,
		int offset,
		int count,
		CancellationToken cancellationToken,
		params DatabaseParameter[] parameters)
	{
		if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
		if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));

		var pagingParameters = parameters
			.Append(new DatabaseParameter("$PageSize", count))
			.Append(new DatabaseParameter("$Offset", offset))
			.ToArray();
		return await QueryAsync(
			$"{sql}{Environment.NewLine}{_connectionFactory.GetPagingClause()};",
			map,
			cancellationToken,
			pagingParameters);
	}

	public async IAsyncEnumerable<T> StreamAsync<T>(
		string sql,
		Func<DbDataReader, T> map,
		IReadOnlyList<DatabaseParameter> parameters,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		await using var session = await OpenSessionAsync(cancellationToken);
		await foreach (var item in session.StreamAsync(sql, map, parameters, cancellationToken))
		{
			yield return item;
		}
	}

	public int Execute(string sql, params DatabaseParameter[] parameters)
	{
		using var session = OpenSession();
		return session.Execute(sql, parameters);
	}

	public async Task<int> ExecuteAsync(
		string sql,
		CancellationToken cancellationToken,
		params DatabaseParameter[] parameters)
	{
		await using var session = await OpenSessionAsync(cancellationToken);
		return await session.ExecuteAsync(sql, cancellationToken, parameters);
	}

	public async Task<object?> ExecuteScalarAsync(
		string sql,
		CancellationToken cancellationToken,
		params DatabaseParameter[] parameters)
	{
		await using var session = await OpenSessionAsync(cancellationToken);
		return await session.ExecuteScalarAsync(sql, cancellationToken, parameters);
	}

	public long Insert(string sql, params DatabaseParameter[] parameters)
	{
		using var session = OpenSession();
		return session.Insert(sql, parameters);
	}

	public async Task<long> InsertAsync(
		string sql,
		CancellationToken cancellationToken,
		params DatabaseParameter[] parameters)
	{
		await using var session = await OpenSessionAsync(cancellationToken);
		return await session.InsertAsync(sql, cancellationToken, parameters);
	}

	public IReadOnlyList<T> Query<T>(
		string sql,
		Func<DbDataReader, T> map,
		params DatabaseParameter[] parameters)
	{
		using var session = OpenSession();
		return session.Query(sql, map, parameters);
	}

	public async Task<IReadOnlyList<T>> QueryAsync<T>(
		string sql,
		Func<DbDataReader, T> map,
		CancellationToken cancellationToken,
		params DatabaseParameter[] parameters)
	{
		await using var session = await OpenSessionAsync(cancellationToken);
		return await session.QueryAsync(sql, map, cancellationToken, parameters);
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

	public async Task<T?> QuerySingleOrDefaultAsync<T>(
		string sql,
		Func<DbDataReader, T> map,
		CancellationToken cancellationToken,
		params DatabaseParameter[] parameters)
		where T : class
	{
		await using var session = await OpenSessionAsync(cancellationToken);
		return await session.QuerySingleOrDefaultAsync(sql, map, cancellationToken, parameters);
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

	public async Task<T> ExecuteInWriteTransactionAsync<T>(
		Func<DatabaseSession, CancellationToken, Task<T>> operation,
		CancellationToken cancellationToken)
	{
		for (var attempt = 1; ; attempt++)
		{
			try
			{
				return await ExecuteInWriteTransactionCoreAsync(operation, cancellationToken);
			}
			catch (Exception exception) when (attempt < 3 && IsTransientWriteConflict(exception))
			{
				await Task.Delay(50 * attempt, cancellationToken);
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

	private async Task<T> ExecuteInWriteTransactionCoreAsync<T>(
		Func<DatabaseSession, CancellationToken, Task<T>> operation,
		CancellationToken cancellationToken)
	{
		await using var connection = _connectionFactory.CreateConnection();
		await connection.OpenAsync(cancellationToken);
		await using var transaction = await _connectionFactory.BeginWriteTransactionAsync(
			connection,
			cancellationToken);
		await using var session = new DatabaseSession(connection, transaction);
		var result = await operation(session, cancellationToken);
		await transaction.CommitAsync(cancellationToken);
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

	private async Task<DatabaseSession> OpenSessionAsync(CancellationToken cancellationToken)
	{
		var connection = _connectionFactory.CreateConnection();
		try
		{
			await connection.OpenAsync(cancellationToken);
			return new DatabaseSession(connection, null);
		}
		catch
		{
			await connection.DisposeAsync();
			throw;
		}
	}
}

public sealed class DatabaseSession : IDisposable, IAsyncDisposable
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

	public async Task<int> ExecuteAsync(
		string sql,
		CancellationToken cancellationToken,
		params DatabaseParameter[] parameters)
	{
		await using var command = CreateCommand(sql, parameters);
		return await command.ExecuteNonQueryAsync(cancellationToken);
	}

	public object? ExecuteScalar(string sql, params DatabaseParameter[] parameters)
	{
		using var command = CreateCommand(sql, parameters);
		return command.ExecuteScalar();
	}

	public async Task<object?> ExecuteScalarAsync(
		string sql,
		CancellationToken cancellationToken,
		params DatabaseParameter[] parameters)
	{
		await using var command = CreateCommand(sql, parameters);
		return await command.ExecuteScalarAsync(cancellationToken);
	}

	public long Insert(string sql, params DatabaseParameter[] parameters)
	{
		var value = ExecuteScalar($"{sql}{Environment.NewLine}SELECT last_insert_rowid();", parameters);
		return Convert.ToInt64(value, CultureInfo.InvariantCulture);
	}

	public async Task<long> InsertAsync(
		string sql,
		CancellationToken cancellationToken,
		params DatabaseParameter[] parameters)
	{
		var value = await ExecuteScalarAsync(
			$"{sql}{Environment.NewLine}SELECT last_insert_rowid();",
			cancellationToken,
			parameters);
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

	public async Task<IReadOnlyList<T>> QueryAsync<T>(
		string sql,
		Func<DbDataReader, T> map,
		CancellationToken cancellationToken,
		params DatabaseParameter[] parameters)
	{
		await using var command = CreateCommand(sql, parameters);
		await using var reader = await command.ExecuteReaderAsync(cancellationToken);
		var result = new List<T>();
		while (await reader.ReadAsync(cancellationToken))
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

	public async Task<T?> QuerySingleOrDefaultAsync<T>(
		string sql,
		Func<DbDataReader, T> map,
		CancellationToken cancellationToken,
		params DatabaseParameter[] parameters)
		where T : class
	{
		await using var command = CreateCommand(sql, parameters);
		await using var reader = await command.ExecuteReaderAsync(cancellationToken);
		return await reader.ReadAsync(cancellationToken) ? map(reader) : null;
	}

	public async IAsyncEnumerable<T> StreamAsync<T>(
		string sql,
		Func<DbDataReader, T> map,
		IReadOnlyList<DatabaseParameter> parameters,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		await using var command = CreateCommand(sql, parameters);
		await using var reader = await command.ExecuteReaderAsync(cancellationToken);
		while (await reader.ReadAsync(cancellationToken))
		{
			yield return map(reader);
		}
	}

	public void Dispose()
	{
		if (_transaction is null)
		{
			_connection.Dispose();
		}
	}

	public async ValueTask DisposeAsync()
	{
		if (_transaction is null)
		{
			await _connection.DisposeAsync();
		}
		GC.SuppressFinalize(this);
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
