// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed record PersonalDataRecord(
	string Category,
	string Source,
	long EntityId,
	string Subject,
	string? Email,
	IReadOnlyDictionary<string, string?> Fields);

public sealed record PersonalDataSearchResult(
	string Query,
	DateTime GeneratedUtc,
	IReadOnlyList<PersonalDataRecord> Records);
