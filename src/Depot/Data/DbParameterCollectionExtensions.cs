// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;

using Microsoft.Data.SqlClient;

namespace Depot.Data;

public static class DbParameterCollectionExtensions
{
	public static void AddWithValue(
		this DbParameterCollection parameters,
		string parameterName,
		object? value)
	{
		var normalizedName = parameters is SqlParameterCollection && parameterName.StartsWith('$')
			? $"@{parameterName[1..]}"
			: parameterName;

		DbParameter parameter = parameters is SqlParameterCollection
			? new SqlParameter(normalizedName, value)
			: parameters is Microsoft.Data.Sqlite.SqliteParameterCollection
				? new Microsoft.Data.Sqlite.SqliteParameter(normalizedName, value)
				: throw new NotSupportedException("Unsupported database parameter collection.");

		parameters.Add(parameter);
	}
}
