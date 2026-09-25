// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;

using Depot.Data;
using Depot.Models;
using Depot.Services;

namespace Depot.Repositories;

public sealed class BusinessAttachmentRepository : DatabaseRepository
{
	private const string AttachmentColumns =
		"Id,EntityKind,EntityId,FileName,MediaType,ByteLength,Sha256,Description,Category,CreatedByUserId,CreatedAtUtc,CurrentRevision,Status,Version";
	private const string RevisionColumns =
		"AttachmentId,Revision,FileName,MediaType,ByteLength,Sha256,CreatedByUserId,CreatedAtUtc";

	private readonly DatabaseBusinessAttachmentContentStore _contentStore;

	public BusinessAttachmentRepository(DatabaseAccess database, DatabaseBusinessAttachmentContentStore contentStore)
		: base(database)
	{
		_contentStore = contentStore ?? throw new ArgumentNullException(nameof(contentStore));
	}

	public Task<BusinessAttachment?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
		Database.QuerySingleOrDefaultAsync(
			$"SELECT {AttachmentColumns} FROM BusinessAttachments WHERE Id=$Id;",
			ReadAttachment,
			cancellationToken,
			Parameter("$Id", FormatId(id)));

	public Task<BusinessAttachmentRevision?> GetRevisionAsync(Guid id, int revision, CancellationToken cancellationToken = default)
	{
		if (revision < 1) throw new ArgumentOutOfRangeException(nameof(revision));
		return Database.QuerySingleOrDefaultAsync(
			$"SELECT {RevisionColumns} FROM BusinessAttachmentRevisions WHERE AttachmentId=$Id AND Revision=$Revision;",
			ReadRevision,
			cancellationToken,
			Parameter("$Id", FormatId(id)),
			Parameter("$Revision", revision));
	}

	public Task<IReadOnlyList<BusinessAttachment>> ListAsync(
		BusinessAttachmentEntityKind entityKind,
		long entityId,
		bool includeRetired,
		int maxResults,
		CancellationToken cancellationToken = default)
	{
		if (entityId <= 0) throw new ArgumentOutOfRangeException(nameof(entityId));
		var limit = Math.Clamp(maxResults, 1, 200);
		if (includeRetired)
		{
			return Database.QuerySliceAsync(
				$"SELECT {AttachmentColumns} FROM BusinessAttachments WHERE EntityKind=$EntityKind AND EntityId=$EntityId ORDER BY CreatedAtUtc DESC,Id",
				ReadAttachment,
				0,
				limit,
				cancellationToken,
				Parameter("$EntityKind", (int)entityKind),
				Parameter("$EntityId", entityId));
		}

		return Database.QuerySliceAsync(
			$"SELECT {AttachmentColumns} FROM BusinessAttachments WHERE EntityKind=$EntityKind AND EntityId=$EntityId AND Status=$Active ORDER BY CreatedAtUtc DESC,Id",
			ReadAttachment,
			0,
			limit,
			cancellationToken,
			Parameter("$EntityKind", (int)entityKind),
			Parameter("$EntityId", entityId),
			Parameter("$Active", (int)BusinessAttachmentStatus.Active));
	}

	public Task<IReadOnlyList<BusinessAttachmentRevision>> ListRevisionsAsync(
		Guid id,
		int maxResults = 200,
		CancellationToken cancellationToken = default)
	{
		var limit = Math.Clamp(maxResults, 1, 200);
		return Database.QuerySliceAsync(
			$"SELECT {RevisionColumns} FROM BusinessAttachmentRevisions WHERE AttachmentId=$Id ORDER BY Revision DESC",
			ReadRevision,
			0,
			limit,
			cancellationToken,
			Parameter("$Id", FormatId(id)));
	}

	public async Task<bool> EntityExistsAsync(
		BusinessAttachmentEntityKind entityKind,
		long entityId,
		CancellationToken cancellationToken = default)
	{
		if (entityId <= 0) return false;
		var table = entityKind switch
		{
			BusinessAttachmentEntityKind.Customer => "Customers",
			BusinessAttachmentEntityKind.Supplier => "Suppliers",
			BusinessAttachmentEntityKind.Item => "Items",
			BusinessAttachmentEntityKind.SalesQuote => "SalesQuotes",
			BusinessAttachmentEntityKind.SalesOrder => "SalesOrders",
			BusinessAttachmentEntityKind.SalesInvoice => "SalesInvoices",
			BusinessAttachmentEntityKind.PurchaseOrder => "PurchaseOrders",
			BusinessAttachmentEntityKind.GoodsReceipt => "GoodsReceipts",
			BusinessAttachmentEntityKind.SupplierDocument => "FinanceSupplierDocuments",
			BusinessAttachmentEntityKind.PurchaseRequisition => "PurchaseRequisitions",
			BusinessAttachmentEntityKind.RequestForQuotation => "RequestsForQuotation",
			BusinessAttachmentEntityKind.SupplierQuoteResponse => "SupplierQuoteResponses",
			BusinessAttachmentEntityKind.Project => "Projects",
			BusinessAttachmentEntityKind.SubscriptionContract => "SubscriptionContracts",
			BusinessAttachmentEntityKind.ServiceCase => "ServiceCases",
			BusinessAttachmentEntityKind.ServiceOrder => "ServiceOrders",
			_ => throw new ArgumentOutOfRangeException(nameof(entityKind))
		};
		var value = await Database.ExecuteScalarAsync(
			$"SELECT COUNT(*) FROM {table} WHERE Id=$EntityId;",
			cancellationToken,
			Parameter("$EntityId", entityId));
		return Convert.ToInt32(value, CultureInfo.InvariantCulture) == 1;
	}

	public async Task<bool> CanMutateEntityAsync(BusinessAttachmentEntityKind entityKind, long entityId, CancellationToken cancellationToken = default)
	{
		if (entityKind == BusinessAttachmentEntityKind.SupplierQuoteResponse)
		{
			var value = await Database.ExecuteScalarAsync(
				"SELECT COUNT(*) FROM SupplierQuoteResponses q INNER JOIN RequestsForQuotation r ON r.Id=q.RequestForQuotationId WHERE q.Id=$Id AND r.Status=$Open;",
				cancellationToken,
				Parameter("$Id", entityId),
				Parameter("$Open", (int)RequestForQuotationStatus.Open));
			return Convert.ToInt32(value, CultureInfo.InvariantCulture) == 1;
		}
		if (entityKind == BusinessAttachmentEntityKind.ServiceCase)
		{
			var value = await Database.ExecuteScalarAsync(
				"SELECT COUNT(*) FROM ServiceCases WHERE Id=$Id AND Status NOT IN ($Closed,$Cancelled);",
				cancellationToken, Parameter("$Id", entityId), Parameter("$Closed", (int)ServiceCaseStatus.Closed), Parameter("$Cancelled", (int)ServiceCaseStatus.Cancelled));
			return Convert.ToInt32(value, CultureInfo.InvariantCulture) == 1;
		}
		if (entityKind == BusinessAttachmentEntityKind.ServiceOrder)
		{
			var value = await Database.ExecuteScalarAsync(
				"SELECT COUNT(*) FROM ServiceOrders WHERE Id=$Id AND Status NOT IN ($Completed,$Cancelled);",
				cancellationToken, Parameter("$Id", entityId), Parameter("$Completed", (int)ServiceOrderStatus.Completed), Parameter("$Cancelled", (int)ServiceOrderStatus.Cancelled));
			return Convert.ToInt32(value, CultureInfo.InvariantCulture) == 1;
		}
		return true;
	}

	public Task<BusinessAttachment> CreateAsync(
		BusinessAttachment attachment,
		BusinessAttachmentRevision revision,
		byte[] content,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(content);
		return Database.ExecuteInWriteTransactionAsync(async (session, token) =>
		{
			await session.ExecuteAsync(
				"""
				INSERT INTO BusinessAttachments
				(Id,EntityKind,EntityId,FileName,MediaType,ByteLength,Sha256,Description,Category,CreatedByUserId,CreatedAtUtc,CurrentRevision,Status,Version)
				VALUES
				($Id,$EntityKind,$EntityId,$FileName,$MediaType,$ByteLength,$Sha256,$Description,$Category,$CreatedByUserId,$CreatedAtUtc,$CurrentRevision,$Status,$Version);
				""",
				token,
				AttachmentParameters(attachment));
			await InsertRevisionAsync(session, revision, token);
			await _contentStore.WriteAsync(session, attachment.Id, revision.Revision, content, token);
			return attachment;
		}, cancellationToken);
	}

	public Task<BusinessAttachment> ReplaceAsync(
		Guid id,
		long expectedVersion,
		string fileName,
		string mediaType,
		long byteLength,
		string sha256,
		long? userId,
		DateTime createdAtUtc,
		byte[] content,
		CancellationToken cancellationToken = default) =>
		Database.ExecuteInWriteTransactionAsync(async (session, token) =>
		{
			var before = await GetAsync(session, id, token)
				?? throw new InvalidOperationException("The attachment was not found.");
			if (before.Version != expectedVersion) throw new ConcurrencyConflictException("attachment");
			if (before.Status != BusinessAttachmentStatus.Active) throw new InvalidOperationException("Retired attachments cannot be replaced.");

			var revision = new BusinessAttachmentRevision(
				id,
				before.CurrentRevision + 1,
				fileName,
				mediaType,
				byteLength,
				sha256,
				userId,
				createdAtUtc);
			await InsertRevisionAsync(session, revision, token);
			await _contentStore.WriteAsync(session, id, revision.Revision, content, token);
			var changed = await session.ExecuteAsync(
				"""
				UPDATE BusinessAttachments
				SET FileName=$FileName,MediaType=$MediaType,ByteLength=$ByteLength,Sha256=$Sha256,CurrentRevision=$Revision,Version=Version+1
				WHERE Id=$Id AND Version=$ExpectedVersion;
				""",
				token,
				Parameter("$FileName", fileName),
				Parameter("$MediaType", mediaType),
				Parameter("$ByteLength", byteLength),
				Parameter("$Sha256", sha256),
				Parameter("$Revision", revision.Revision),
				Parameter("$Id", FormatId(id)),
				Parameter("$ExpectedVersion", expectedVersion));
			if (changed != 1) throw new ConcurrencyConflictException("attachment");
			return before with
			{
				FileName = fileName,
				MediaType = mediaType,
				ByteLength = byteLength,
				Sha256 = sha256,
				CurrentRevision = revision.Revision,
				Version = expectedVersion + 1
			};
		}, cancellationToken);

	public Task<BusinessAttachment> UpdateMetadataAsync(
		Guid id,
		string? description,
		string? category,
		long expectedVersion,
		CancellationToken cancellationToken = default) =>
		Database.ExecuteInWriteTransactionAsync(async (session, token) =>
		{
			var before = await GetAsync(session, id, token)
				?? throw new InvalidOperationException("The attachment was not found.");
			if (before.Version != expectedVersion) throw new ConcurrencyConflictException("attachment");
			var changed = await session.ExecuteAsync(
				"UPDATE BusinessAttachments SET Description=$Description,Category=$Category,Version=Version+1 WHERE Id=$Id AND Version=$ExpectedVersion;",
				token,
				Parameter("$Description", description),
				Parameter("$Category", category),
				Parameter("$Id", FormatId(id)),
				Parameter("$ExpectedVersion", expectedVersion));
			if (changed != 1) throw new ConcurrencyConflictException("attachment");
			return before with { Description = description, Category = category, Version = expectedVersion + 1 };
		}, cancellationToken);

	public Task<BusinessAttachment> RetireAsync(
		Guid id,
		long expectedVersion,
		CancellationToken cancellationToken = default) =>
		Database.ExecuteInWriteTransactionAsync(async (session, token) =>
		{
			var before = await GetAsync(session, id, token)
				?? throw new InvalidOperationException("The attachment was not found.");
			if (before.Version != expectedVersion) throw new ConcurrencyConflictException("attachment");
			if (before.Status == BusinessAttachmentStatus.Retired) return before;
			var changed = await session.ExecuteAsync(
				"UPDATE BusinessAttachments SET Status=$Status,Version=Version+1 WHERE Id=$Id AND Version=$ExpectedVersion;",
				token,
				Parameter("$Status", (int)BusinessAttachmentStatus.Retired),
				Parameter("$Id", FormatId(id)),
				Parameter("$ExpectedVersion", expectedVersion));
			if (changed != 1) throw new ConcurrencyConflictException("attachment");
			return before with { Status = BusinessAttachmentStatus.Retired, Version = expectedVersion + 1 };
		}, cancellationToken);

	public Task<IReadOnlyList<BusinessAttachment>> SearchMetadataAsync(
		string query,
		int maxResults,
		CancellationToken cancellationToken = default)
	{
		var normalized = query?.Trim() ?? string.Empty;
		if (normalized.Length < 2) return Task.FromResult<IReadOnlyList<BusinessAttachment>>([]);
		var limit = Math.Clamp(maxResults, 1, 50);
		return Database.QuerySliceAsync(
			$"""
			SELECT {AttachmentColumns}
			FROM BusinessAttachments
			WHERE Status=$Active
			  AND (FileName LIKE $Search OR Description LIKE $Search OR Category LIKE $Search)
			ORDER BY CreatedAtUtc DESC,Id
			""",
			ReadAttachment,
			0,
			limit,
			cancellationToken,
			Parameter("$Active", (int)BusinessAttachmentStatus.Active),
			Parameter("$Search", $"%{normalized}%"));
	}

	private static async Task<BusinessAttachment?> GetAsync(DatabaseSession session, Guid id, CancellationToken cancellationToken) =>
		await session.QuerySingleOrDefaultAsync(
			$"SELECT {AttachmentColumns} FROM BusinessAttachments WHERE Id=$Id;",
			ReadAttachment,
			cancellationToken,
			Parameter("$Id", FormatId(id)));

	private static Task<int> InsertRevisionAsync(DatabaseSession session, BusinessAttachmentRevision revision, CancellationToken cancellationToken) =>
		session.ExecuteAsync(
			"""
			INSERT INTO BusinessAttachmentRevisions
			(AttachmentId,Revision,FileName,MediaType,ByteLength,Sha256,CreatedByUserId,CreatedAtUtc)
			VALUES
			($AttachmentId,$Revision,$FileName,$MediaType,$ByteLength,$Sha256,$CreatedByUserId,$CreatedAtUtc);
			""",
			cancellationToken,
			Parameter("$AttachmentId", FormatId(revision.AttachmentId)),
			Parameter("$Revision", revision.Revision),
			Parameter("$FileName", revision.FileName),
			Parameter("$MediaType", revision.MediaType),
			Parameter("$ByteLength", revision.ByteLength),
			Parameter("$Sha256", revision.Sha256),
			Parameter("$CreatedByUserId", revision.CreatedByUserId),
			Parameter("$CreatedAtUtc", revision.CreatedAtUtc));

	private static DatabaseParameter[] AttachmentParameters(BusinessAttachment value) =>
	[
		Parameter("$Id", FormatId(value.Id)),
		Parameter("$EntityKind", (int)value.EntityKind),
		Parameter("$EntityId", value.EntityId),
		Parameter("$FileName", value.FileName),
		Parameter("$MediaType", value.MediaType),
		Parameter("$ByteLength", value.ByteLength),
		Parameter("$Sha256", value.Sha256),
		Parameter("$Description", value.Description),
		Parameter("$Category", value.Category),
		Parameter("$CreatedByUserId", value.CreatedByUserId),
		Parameter("$CreatedAtUtc", value.CreatedAtUtc),
		Parameter("$CurrentRevision", value.CurrentRevision),
		Parameter("$Status", (int)value.Status),
		Parameter("$Version", value.Version)
	];

	private static BusinessAttachment ReadAttachment(DbDataReader reader) =>
		new(
			ReadGuid(reader, 0),
			(BusinessAttachmentEntityKind)Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture),
			Convert.ToInt64(reader.GetValue(2), CultureInfo.InvariantCulture),
			reader.GetString(3),
			reader.GetString(4),
			Convert.ToInt64(reader.GetValue(5), CultureInfo.InvariantCulture),
			reader.GetString(6),
			reader.IsDBNull(7) ? null : reader.GetString(7),
			reader.IsDBNull(8) ? null : reader.GetString(8),
			reader.IsDBNull(9) ? null : Convert.ToInt64(reader.GetValue(9), CultureInfo.InvariantCulture),
			ReadUtc(reader, 10),
			Convert.ToInt32(reader.GetValue(11), CultureInfo.InvariantCulture),
			(BusinessAttachmentStatus)Convert.ToInt32(reader.GetValue(12), CultureInfo.InvariantCulture),
			Convert.ToInt64(reader.GetValue(13), CultureInfo.InvariantCulture));

	private static BusinessAttachmentRevision ReadRevision(DbDataReader reader) =>
		new(
			ReadGuid(reader, 0),
			Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture),
			reader.GetString(2),
			reader.GetString(3),
			Convert.ToInt64(reader.GetValue(4), CultureInfo.InvariantCulture),
			reader.GetString(5),
			reader.IsDBNull(6) ? null : Convert.ToInt64(reader.GetValue(6), CultureInfo.InvariantCulture),
			ReadUtc(reader, 7));

	private static Guid ReadGuid(DbDataReader reader, int ordinal)
	{
		var value = reader.GetValue(ordinal);
		if (value is Guid guid) return guid;
		return Guid.Parse(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
	}

	private static DateTime ReadUtc(DbDataReader reader, int ordinal)
	{
		var value = reader.GetValue(ordinal);
		if (value is DateTime dateTime) return DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
		return DateTime.Parse(
			Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
			CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
	}

	private static string FormatId(Guid id)
	{
		if (id == Guid.Empty) throw new ArgumentException("Attachment id is required.", nameof(id));
		return id.ToString("D");
	}
}
