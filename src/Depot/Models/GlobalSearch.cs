// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public enum GlobalSearchResultKind
{
	Item = 1,
	Customer = 2,
	Supplier = 3,
	SalesOrder = 4,
	PurchaseOrder = 5,
	Invoice = 6,
	JournalEntry = 7,
	Lead = 8,
	Opportunity = 9
}

public sealed record GlobalSearchResult(
	string StableId,
	GlobalSearchResultKind Kind,
	long EntityId,
	string Title,
	string Subtitle,
	string Group,
	string TypeLabel,
	int Rank);
