// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data;
using System.Text.Json;

using Depot.Models;
using Depot.Services;

using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class DatabaseManagementParameterTests
{
	[Theory]
	[InlineData("date:3:10:0", SqlDbType.Date, 0)]
	[InlineData("datetime2:8:27:7", SqlDbType.DateTime2, 7)]
	[InlineData("datetime2:8:26:6", SqlDbType.DateTime2, 6)]
	[InlineData("datetime2:6:19:0", SqlDbType.DateTime2, 0)]
	[InlineData("datetimeoffset:10:34:7", SqlDbType.DateTimeOffset, 7)]
	[InlineData("time:5:16:7", SqlDbType.Time, 7)]
	public void SqlServerTemporalParametersKeepColumnTypeAcrossNullAndPreciseValues(string storeType, SqlDbType expectedType, int expectedScale)
	{
		using var command = new SqlCommand();
		var parameter = command.CreateParameter();
		var column = new DatabaseBackupColumn("Value", storeType, true, false, false, 0);
		DatabaseBackupArchive.ConfigureRestoreParameter(parameter, column, DatabaseProvider.SqlServer);
		Assert.Equal(expectedType, parameter.SqlDbType);
		Assert.Equal((byte)expectedScale, parameter.Scale);

		// A modern SQL temporal value can predate SQL datetime's minimum year and
		// contain 100-nanosecond precision. Null-first rows must not choose its type.
		var precise = new DateTime(1666, 9, 2, 1, 2, 3, DateTimeKind.Unspecified).AddTicks(1234567);
		object value = expectedType switch
		{
			SqlDbType.Date => precise.Date,
			SqlDbType.Time => precise.TimeOfDay,
			SqlDbType.DateTimeOffset => new DateTimeOffset(precise, TimeSpan.FromHours(5.5)),
			_ => precise
		};
		foreach (var original in new[] { DBNull.Value, value, DBNull.Value, value })
		{
			parameter.Value = RoundTripValue(original, column, DatabaseProvider.SqlServer);
			Assert.Equal(expectedType, parameter.SqlDbType);
			Assert.Equal((byte)expectedScale, parameter.Scale);
			Assert.Equal(original, parameter.Value);
		}
	}

	[Fact]
	public async Task SqliteRestoreParametersPreserveNullsAndExactTemporalText()
	{
		await using var connection = new SqliteConnection("Data Source=:memory:");
		await connection.OpenAsync();
		await using var command = connection.CreateCommand();
		command.CommandText = "CREATE TABLE TemporalText(Value TEXT NULL);";
		await command.ExecuteNonQueryAsync();
		command.CommandText = "INSERT INTO TemporalText(Value) VALUES(@Value);";
		var parameter = command.CreateParameter();
		parameter.ParameterName = "@Value";
		var column = new DatabaseBackupColumn("Value", "TEXT", true, false, false, 0);
		DatabaseBackupArchive.ConfigureRestoreParameter(parameter, column, DatabaseProvider.Local);
		command.Parameters.Add(parameter);
		const string preciseText = "1666-09-02T01:02:03.1234567+05:30";
		object[] values = [DBNull.Value, preciseText];
		foreach (var value in values)
		{
			parameter.Value = RoundTripValue(value, column, DatabaseProvider.Local);
			await command.ExecuteNonQueryAsync();
		}
		command.Parameters.Clear();
		command.CommandText = "SELECT typeof(Value),Value FROM TemporalText ORDER BY rowid;";
		await using var reader = await command.ExecuteReaderAsync();
		Assert.True(await reader.ReadAsync());
		Assert.Equal("null", reader.GetString(0));
		Assert.True(reader.IsDBNull(1));
		Assert.True(await reader.ReadAsync());
		Assert.Equal("text", reader.GetString(0));
		Assert.Equal(preciseText, reader.GetString(1));
		Assert.False(await reader.ReadAsync());
	}

	private static object RoundTripValue(object value, DatabaseBackupColumn column, DatabaseProvider provider)
	{
		using var stream = new MemoryStream();
		using (var writer = new Utf8JsonWriter(stream))
		{
			DatabaseBackupArchive.WriteValue(writer, value);
			writer.Flush();
		}
		using var document = JsonDocument.Parse(stream.ToArray());
		return DatabaseBackupArchive.ReadValue(document.RootElement, column, provider);
	}
}
