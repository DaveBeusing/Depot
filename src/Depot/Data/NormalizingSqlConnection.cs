// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

using Microsoft.Data.SqlClient;

namespace Depot.Data;

internal sealed class NormalizingSqlConnection : DbConnection
{
	private readonly SqlConnection _inner;

	public NormalizingSqlConnection(SqlConnection inner) => _inner = inner;

	[AllowNull]
	public override string ConnectionString { get => _inner.ConnectionString; set => _inner.ConnectionString = value; }
	public override string Database => _inner.Database;
	public override string DataSource => _inner.DataSource;
	public override string ServerVersion => _inner.ServerVersion;
	public override ConnectionState State => _inner.State;
	public override int ConnectionTimeout => _inner.ConnectionTimeout;

	public override void ChangeDatabase(string databaseName) => _inner.ChangeDatabase(databaseName);
	public override void Close() => _inner.Close();
	public override void Open() => _inner.Open();
	public override Task OpenAsync(CancellationToken cancellationToken) => _inner.OpenAsync(cancellationToken);

	protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
		new NormalizingSqlTransaction(_inner.BeginTransaction(isolationLevel), this);

	protected override DbCommand CreateDbCommand() =>
		new NormalizingSqlCommand(_inner.CreateCommand(), this);

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_inner.Dispose();
		}

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
	private readonly SqlTransaction _inner;
	private readonly DbConnection _connection;

	public NormalizingSqlTransaction(SqlTransaction inner, DbConnection connection)
	{
		_inner = inner;
		_connection = connection;
	}

	internal SqlTransaction Inner => _inner;
	public override IsolationLevel IsolationLevel => _inner.IsolationLevel;
	protected override DbConnection DbConnection => _connection;
	public override void Commit() => _inner.Commit();
	public override void Rollback() => _inner.Rollback();
	protected override void Dispose(bool disposing) { if (disposing) _inner.Dispose(); base.Dispose(disposing); }
}

internal sealed class NormalizingSqlCommand : DbCommand
{
	private readonly SqlCommand _inner;
	private readonly DbConnection _connection;
	private DbTransaction? _transaction;

	public NormalizingSqlCommand(SqlCommand inner, DbConnection connection)
	{
		_inner = inner;
		_connection = connection;
	}

	[AllowNull]
	public override string CommandText { get => _inner.CommandText; set => _inner.CommandText = Normalize(value ?? string.Empty); }
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
		_inner.ExecuteReaderAsync(behavior, cancellationToken).ContinueWith<DbDataReader>(task => task.Result, cancellationToken);
	protected override void Dispose(bool disposing) { if (disposing) _inner.Dispose(); base.Dispose(disposing); }

	private static string Normalize(string sql) =>
		sql
			.Replace("$", "@", StringComparison.Ordinal)
			.Replace("SELECT last_insert_rowid();", "SELECT CAST(SCOPE_IDENTITY() AS bigint);", StringComparison.OrdinalIgnoreCase)
			.Replace(" COLLATE NOCASE", string.Empty, StringComparison.OrdinalIgnoreCase);
}
