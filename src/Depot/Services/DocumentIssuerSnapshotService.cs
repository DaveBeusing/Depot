// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;
using System.Text.Json;

using Depot.Data;
using Depot.Models;

namespace Depot.Services;

public sealed class DocumentIssuerSnapshotService
{
	private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
	private readonly DatabaseAccess _dataAccess;

	public DocumentIssuerSnapshotService(DatabaseAccess dataAccess)
	{
		_dataAccess = dataAccess;
	}

	public DocumentIssuerProfile? TryLoad(DocumentIssuerSnapshotType documentType, long documentId)
	{
		var rows = _dataAccess.Query(
			"SELECT Payload FROM SalesDocumentIssuerSnapshots WHERE DocumentType=$Type AND DocumentId=$Id;",
			reader => reader.GetString(0),
			new DatabaseParameter("$Type", (int)documentType),
			new DatabaseParameter("$Id", documentId));
		if (rows.Count == 0) return null;
		return JsonSerializer.Deserialize<DocumentIssuerProfile>(rows[0], JsonOptions)
			?? throw new InvalidOperationException("Stored issuer snapshot could not be read.");
	}

	public DocumentIssuerProfile LoadRequired(DocumentIssuerSnapshotType documentType, long documentId) =>
		TryLoad(documentType, documentId)
		?? throw new InvalidOperationException($"Posted {documentType} {documentId} has no issuer snapshot. Historical documents cannot be regenerated from current company master data.");

	public static async Task<DocumentIssuerProfile> CaptureCurrentAsync(
		DatabaseTransactionContext transaction,
		DocumentIssuerSnapshotType documentType,
		long documentId,
		DateTime capturedAtUtc,
		CancellationToken cancellationToken)
	{
		var profileRows = await transaction.Session.QueryAsync(
			"SELECT Payload FROM CompanyProfile WHERE Id=1;",
			reader => reader.GetString(0),
			cancellationToken);
		if (profileRows.Count == 0)
			throw new InvalidOperationException("Company master data must be configured before a financial document can be posted.");

		var profile = JsonSerializer.Deserialize<CompanyProfile>(profileRows[0], JsonOptions)
			?? throw new InvalidOperationException("Company master data could not be read.");
		var issuer = CompanyDocumentIdentityService.Project(profile);
		var payload = JsonSerializer.Serialize(issuer, JsonOptions);

		var existing = await transaction.Session.ExecuteScalarAsync(
			"SELECT COUNT(*) FROM SalesDocumentIssuerSnapshots WHERE DocumentType=$Type AND DocumentId=$Id;",
			cancellationToken,
			new DatabaseParameter("$Type", (int)documentType),
			new DatabaseParameter("$Id", documentId));
		if (Convert.ToInt32(existing, CultureInfo.InvariantCulture) != 0)
			throw new InvalidOperationException("An issuer snapshot already exists for this posted document and cannot be replaced.");

		await transaction.Session.ExecuteAsync(
			"INSERT INTO SalesDocumentIssuerSnapshots (DocumentType,DocumentId,Payload,CapturedAtUtc) VALUES ($Type,$Id,$Payload,$CapturedAtUtc);",
			cancellationToken,
			new DatabaseParameter("$Type", (int)documentType),
			new DatabaseParameter("$Id", documentId),
			new DatabaseParameter("$Payload", payload),
			new DatabaseParameter("$CapturedAtUtc", capturedAtUtc.ToString("O", CultureInfo.InvariantCulture)));
		return issuer;
	}
}
