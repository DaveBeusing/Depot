// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;
using System.IO;

using Depot.Models;

using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;

using MySqlConnector;

namespace Depot.Diagnostics;

public static class DatabaseDiagnostics
{
	private static readonly object SyncRoot = new();
	private static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "depot.database.log");

	public static void ConnectionOpening(DatabaseProvider provider, string target) =>
		Write("INFO", provider, target, "Opening database connection.");

	public static void ConnectionOpened(DatabaseProvider provider, string target) =>
		Write("INFO", provider, target, "Database connection opened.");

	public static void ConnectionFailed(DatabaseProvider provider, string target, Exception exception) =>
		Write("ERROR", provider, target, $"Connection failed ({GetCode(exception)}): {Sanitize(exception.Message)}");

	public static void OperationFailed(DatabaseProvider provider, string target, string operation, Exception exception) =>
		Write("ERROR", provider, target, $"{operation} failed ({GetCode(exception)}): {Sanitize(exception.Message)}");

	private static string Sanitize(string message)
	{
		var singleLine = message.Replace('\r', ' ').Replace('\n', ' ').Trim();
		return singleLine.Length <= 1000 ? singleLine : singleLine[..1000];
	}

	private static string GetCode(Exception exception) =>
		exception switch
		{
			SqlException sql => sql.Number.ToString(CultureInfo.InvariantCulture),
			SqliteException sqlite => sqlite.SqliteErrorCode.ToString(CultureInfo.InvariantCulture),
			MySqlException mysql => $"{mysql.Number}/{mysql.SqlState}",
			_ => exception.GetType().Name
		};

	private static void Write(string level, DatabaseProvider provider, string target, string message)
	{
		var line = $"{DateTime.UtcNow:O} [{level}] [{provider}] [{target}] {message}{Environment.NewLine}";
		lock (SyncRoot)
		{
			try
			{
				File.AppendAllText(LogPath, line);
			}
			catch (IOException)
			{
				System.Diagnostics.Debug.WriteLine(line);
			}
			catch (UnauthorizedAccessException)
			{
				System.Diagnostics.Debug.WriteLine(line);
			}
		}
	}
}
