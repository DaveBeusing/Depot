// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

using Depot.Diagnostics;
using Depot.Models;

using Microsoft.Data.Sqlite;

namespace Depot.Data;

internal sealed class NormalizingSqlConnection : DbConnection
{
	private readonly DbConnection _inner;
	private readonly DatabaseProvider _provider;
	private readonly string _target;
	private readonly Func<string, string> _normalize;

	public NormalizingSqlConnection(
		DbConnection inner,
		DatabaseProvider provider,
		string target,
		Func<string, string>? normalize = null)
	{
		_inner = inner;
		_provider = provider;
		_target = target;
		_normalize = normalize ?? (sql => sql);
	}

	[AllowNull]
	public override string ConnectionString { get => _inner.ConnectionString; set => _inner.ConnectionString = value; }
	public override string Database => _inner.Database;
	public override string DataSource => _inner.DataSource;
	public override string ServerVersion => _inner.ServerVersion;
	public override System.Data.ConnectionState State => _inner.State;
	public override int ConnectionTimeout => _inner.ConnectionTimeout;

	public override void ChangeDatabase(string databaseName) => _inner.ChangeDatabase(databaseName);
	public override void Close() => _inner.Close();

	public override void Open()
	{
		DatabaseDiagnostics.ConnectionOpening(_provider, _target);
		try
		{
			_inner.Open();
			DatabaseDiagnostics.ConnectionOpened(_provider, _target);
		}
		catch (Exception exception)
		{
			DatabaseDiagnostics.ConnectionFailed(_provider, _target, exception);
			throw new DatabaseConnectionException(
				DatabaseErrorMessages.GetUserMessage(exception),
				exception);
		}
	}

	public override async Task OpenAsync(CancellationToken cancellationToken)
	{
		DatabaseDiagnostics.ConnectionOpening(_provider, _target);
		try
		{
			await _inner.OpenAsync(cancellationToken);
			DatabaseDiagnostics.ConnectionOpened(_provider, _target);
		}
		catch (Exception exception)
		{
			DatabaseDiagnostics.ConnectionFailed(_provider, _target, exception);
			throw new DatabaseConnectionException(
				DatabaseErrorMessages.GetUserMessage(exception),
				exception);
		}
	}

	internal DbTransaction BeginImmediateTransaction() =>
		new NormalizingSqlTransaction(
			((SqliteConnection)_inner).BeginTransaction(deferred: false),
			this);

	protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
		new NormalizingSqlTransaction(_inner.BeginTransaction(isolationLevel), this);

	protected override DbCommand CreateDbCommand() =>
		new NormalizingSqlCommand(_inner.CreateCommand(), this, _normalize);

	protected override void Dispose(bool disposing)
	{
		if (disposing) _inner.Dispose();
		base.Dispose(disposing);
	}

	public override async ValueTask DisposeAsync()
	{
		await _inner.DisposeAsync();
		GC.SuppressFinalize(this);
	}
}

internal sealed class NormalizingSqlTransaction : DbTransaction
{
	private readonly DbTransaction _inner;
	private readonly DbConnection _connection;

	public NormalizingSqlTransaction(DbTransaction inner, DbConnection connection)
	{
		_inner = inner;
		_connection = connection;
	}

	internal DbTransaction Inner => _inner;
	public override IsolationLevel IsolationLevel => _inner.IsolationLevel;
	protected override DbConnection DbConnection => _connection;
	public override void Commit() => _inner.Commit();
	public override void Rollback() => _inner.Rollback();
	protected override void Dispose(bool disposing) { if (disposing) _inner.Dispose(); base.Dispose(disposing); }
}

internal sealed class NormalizingSqlCommand : DbCommand
{
	private readonly DbCommand _inner;
	private readonly DbConnection _connection;
	private readonly Func<string, string> _normalize;
	private DbTransaction? _transaction;

	public NormalizingSqlCommand(DbCommand inner, DbConnection connection, Func<string, string> normalize)
	{
		_inner = inner;
		_connection = connection;
		_normalize = normalize;
	}

	[AllowNull]
	public override string CommandText { get => _inner.CommandText; set => _inner.CommandText = _normalize(value ?? string.Empty); }
	public override int CommandTimeout { get => _inner.CommandTimeout; set => _inner.CommandTimeout = value; }
	public override CommandType CommandType { get => _inner.CommandType; set => _inner.CommandType = value; }
	public override bool DesignTimeVisible { get => _inner.DesignTimeVisible; set => _inner.DesignTimeVisible = value; }
	public override UpdateRowSource UpdatedRowSource { get => _inner.UpdatedRowSource; set => _inner.UpdatedRowSource = value; }
	[AllowNull]
	protected override DbConnection DbConnection { get => _connection; set { } }
	protected override DbParameterCollection DbParameterCollection => _inner.Parameters;
	protected override DbTransaction? DbTransaction
	{
		get => _transaction;
		set
		{
			_transaction = value;
			_inner.Transaction = value is NormalizingSqlTransaction transaction ? transaction.Inner : null;
		}
	}

	public override void Cancel() => _inner.Cancel();
	public override int ExecuteNonQuery() => _inner.ExecuteNonQuery();
	public override object? ExecuteScalar() => _inner.ExecuteScalar();
	public override void Prepare() => _inner.Prepare();
	protected override DbParameter CreateDbParameter() => _inner.CreateParameter();
	protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => _inner.ExecuteReader(behavior);
	protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken) =>
		_inner.ExecuteReaderAsync(behavior, cancellationToken);
	protected override void Dispose(bool disposing) { if (disposing) _inner.Dispose(); base.Dispose(disposing); }
}
