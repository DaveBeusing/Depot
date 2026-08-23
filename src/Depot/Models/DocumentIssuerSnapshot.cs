// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public enum DocumentIssuerSnapshotType
{
	SalesInvoice = 1,
	SalesCreditNote = 2
}

public sealed record DocumentIssuerSnapshot(
	DocumentIssuerSnapshotType DocumentType,
	long DocumentId,
	DocumentIssuerProfile Issuer,
	DateTime CapturedAtUtc);
