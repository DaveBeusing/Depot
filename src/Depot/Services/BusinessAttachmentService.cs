// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Buffers;
using System.Security.Cryptography;
using System.Text;

using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class BusinessAttachmentService
{
	public const int MaxAttachmentBytes = 25 * 1024 * 1024;
	public const int MaxListResults = 200;

	private static readonly HashSet<string> BlockedExtensions = new(StringComparer.OrdinalIgnoreCase)
	{
		".exe", ".com", ".dll", ".bat", ".cmd", ".ps1", ".psm1", ".vbs", ".vbe",
		".js", ".jse", ".wsf", ".wsh", ".msi", ".msp", ".scr", ".hta", ".cpl",
		".jar", ".reg", ".lnk"
	};

	private readonly BusinessAttachmentRepository _attachments;
	private readonly IBusinessAttachmentContentStore _contentStore;
	private readonly AuditService _audit;
	private readonly IAuthorizationService _authorization;

	public BusinessAttachmentService(
		BusinessAttachmentRepository attachments,
		IBusinessAttachmentContentStore contentStore,
		AuditService audit,
		IAuthorizationService authorization)
	{
		_attachments = attachments ?? throw new ArgumentNullException(nameof(attachments));
		_contentStore = contentStore ?? throw new ArgumentNullException(nameof(contentStore));
		_audit = audit ?? throw new ArgumentNullException(nameof(audit));
		_authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
	}

	public bool CanView(BusinessAttachmentEntityKind entityKind) =>
		_authorization.HasPermission(ViewPermission(entityKind));

	public bool CanManage(BusinessAttachmentEntityKind entityKind) =>
		_authorization.HasPermission(ManagePermission(entityKind));

	public async Task<BusinessAttachment> AddAsync(
		BusinessAttachmentEntityKind entityKind,
		long entityId,
		string fileName,
		string? mediaType,
		Stream content,
		string? description = null,
		string? category = null,
		CancellationToken cancellationToken = default)
	{
		RequireManage(entityKind);
		await RequireEntityAsync(entityKind, entityId, cancellationToken);
		await RequireMutableEntityAsync(entityKind, entityId, cancellationToken);
		var normalizedFileName = NormalizeFileName(fileName);
		var normalizedMediaType = NormalizeMediaType(mediaType);
		var normalizedDescription = NormalizeOptional(description, 1000, nameof(description));
		var normalizedCategory = NormalizeOptional(category, 100, nameof(category));
		var bytes = await ReadBoundedAsync(content, cancellationToken);
		var sha256 = Hash(bytes);
		var now = DateTime.UtcNow;
		var id = Guid.NewGuid();
		var userId = _authorization.CurrentUser?.Id;
		var attachment = new BusinessAttachment(
			id,
			entityKind,
			entityId,
			normalizedFileName,
			normalizedMediaType,
			bytes.LongLength,
			sha256,
			normalizedDescription,
			normalizedCategory,
			userId,
			now,
			1,
			BusinessAttachmentStatus.Active,
			1);
		var revision = new BusinessAttachmentRevision(
			id,
			1,
			normalizedFileName,
			normalizedMediaType,
			bytes.LongLength,
			sha256,
			userId,
			now);

		var created = await _attachments.CreateAsync(attachment, revision, bytes, cancellationToken);
		await _audit.RecordActionAsync(entityId, "AttachmentAdded", null, created, cancellationToken);
		return created;
	}

	public async Task<IReadOnlyList<BusinessAttachment>> ListAsync(
		BusinessAttachmentEntityKind entityKind,
		long entityId,
		bool includeRetired = false,
		int maxResults = 100,
		CancellationToken cancellationToken = default)
	{
		RequireView(entityKind);
		await RequireEntityAsync(entityKind, entityId, cancellationToken);
		return await _attachments.ListAsync(
			entityKind,
			entityId,
			includeRetired,
			Math.Clamp(maxResults, 1, MaxListResults),
			cancellationToken);
	}

	public async Task<IReadOnlyList<BusinessAttachmentRevision>> ListRevisionsAsync(
		Guid attachmentId,
		CancellationToken cancellationToken = default)
	{
		var attachment = await RequireAttachmentAsync(attachmentId, cancellationToken);
		RequireView(attachment.EntityKind);
		return await _attachments.ListRevisionsAsync(attachmentId, MaxListResults, cancellationToken);
	}

	public async Task<BusinessAttachmentContent> OpenAsync(
		Guid attachmentId,
		int? revision = null,
		CancellationToken cancellationToken = default)
	{
		var attachment = await RequireAttachmentAsync(attachmentId, cancellationToken);
		RequireView(attachment.EntityKind);
		var revisionNumber = revision ?? attachment.CurrentRevision;
		var metadata = await _attachments.GetRevisionAsync(attachmentId, revisionNumber, cancellationToken)
			?? throw new InvalidOperationException("The requested attachment revision was not found.");
		await using var source = await _contentStore.OpenReadAsync(attachmentId, revisionNumber, cancellationToken);
		var bytes = await ReadBoundedAsync(source, cancellationToken);
		if (bytes.LongLength != metadata.ByteLength ||
			!string.Equals(Hash(bytes), metadata.Sha256, StringComparison.OrdinalIgnoreCase))
			throw new InvalidDataException("Attachment content integrity verification failed.");
		return new BusinessAttachmentContent(attachment, metadata, new MemoryStream(bytes, writable: false));
	}

	public async Task<BusinessAttachment> ReplaceAsync(
		Guid attachmentId,
		long expectedVersion,
		string fileName,
		string? mediaType,
		Stream content,
		CancellationToken cancellationToken = default)
	{
		var before = await RequireAttachmentAsync(attachmentId, cancellationToken);
		RequireManage(before.EntityKind);
		await RequireMutableEntityAsync(before.EntityKind, before.EntityId, cancellationToken);
		var normalizedFileName = NormalizeFileName(fileName);
		var normalizedMediaType = NormalizeMediaType(mediaType);
		var bytes = await ReadBoundedAsync(content, cancellationToken);
		var updated = await _attachments.ReplaceAsync(
			attachmentId,
			expectedVersion,
			normalizedFileName,
			normalizedMediaType,
			bytes.LongLength,
			Hash(bytes),
			_authorization.CurrentUser?.Id,
			DateTime.UtcNow,
			bytes,
			cancellationToken);
		await _audit.RecordActionAsync(before.EntityId, "AttachmentReplaced", before, updated, cancellationToken);
		return updated;
	}

	public async Task<BusinessAttachment> UpdateMetadataAsync(
		Guid attachmentId,
		BusinessAttachmentMetadataUpdate update,
		CancellationToken cancellationToken = default)
	{
		var before = await RequireAttachmentAsync(attachmentId, cancellationToken);
		RequireManage(before.EntityKind);
		await RequireMutableEntityAsync(before.EntityKind, before.EntityId, cancellationToken);
		var updated = await _attachments.UpdateMetadataAsync(
			attachmentId,
			NormalizeOptional(update.Description, 1000, nameof(update.Description)),
			NormalizeOptional(update.Category, 100, nameof(update.Category)),
			update.ExpectedVersion,
			cancellationToken);
		await _audit.RecordActionAsync(before.EntityId, "AttachmentMetadataUpdated", before, updated, cancellationToken);
		return updated;
	}

	public async Task<BusinessAttachment> RetireAsync(
		Guid attachmentId,
		long expectedVersion,
		CancellationToken cancellationToken = default)
	{
		var before = await RequireAttachmentAsync(attachmentId, cancellationToken);
		RequireManage(before.EntityKind);
		await RequireMutableEntityAsync(before.EntityKind, before.EntityId, cancellationToken);
		var updated = await _attachments.RetireAsync(attachmentId, expectedVersion, cancellationToken);
		if (before.Status != updated.Status)
			await _audit.RecordActionAsync(before.EntityId, "AttachmentRetired", before, updated, cancellationToken);
		return updated;
	}

	public async Task<IReadOnlyList<BusinessAttachment>> SearchMetadataAsync(
		string query,
		int maxResults = 30,
		CancellationToken cancellationToken = default)
	{
		var bounded = Math.Clamp(maxResults, 1, 50);
		var rows = await _attachments.SearchMetadataAsync(query, 50, cancellationToken);
		return rows.Where(row => CanView(row.EntityKind)).Take(bounded).ToArray();
	}

	private async Task<BusinessAttachment> RequireAttachmentAsync(Guid id, CancellationToken cancellationToken) =>
		await _attachments.GetAsync(id, cancellationToken)
		?? throw new InvalidOperationException("The attachment was not found.");

	private async Task RequireEntityAsync(
		BusinessAttachmentEntityKind entityKind,
		long entityId,
		CancellationToken cancellationToken)
	{
		ValidateEntityKind(entityKind);
		if (entityId <= 0) throw new ArgumentOutOfRangeException(nameof(entityId));
		if (!await _attachments.EntityExistsAsync(entityKind, entityId, cancellationToken))
			throw new InvalidOperationException($"The referenced {entityKind} record was not found.");
	}

	private async Task RequireMutableEntityAsync(BusinessAttachmentEntityKind entityKind, long entityId, CancellationToken cancellationToken)
	{
		if (!await _attachments.CanMutateEntityAsync(entityKind, entityId, cancellationToken))
			throw new InvalidOperationException("Supplier quote evidence is immutable after award or purchase-order conversion.");
	}

	private void RequireView(BusinessAttachmentEntityKind entityKind) =>
		_authorization.RequirePermission(ViewPermission(entityKind));

	private void RequireManage(BusinessAttachmentEntityKind entityKind) =>
		_authorization.RequirePermission(ManagePermission(entityKind));

	private static ApplicationPermission ViewPermission(BusinessAttachmentEntityKind entityKind) => entityKind switch
	{
		BusinessAttachmentEntityKind.Customer => ApplicationPermission.CustomersView,
		BusinessAttachmentEntityKind.Supplier => ApplicationPermission.SuppliersView,
		BusinessAttachmentEntityKind.Item => ApplicationPermission.ItemsView,
		BusinessAttachmentEntityKind.SalesQuote => ApplicationPermission.SalesQuotesView,
		BusinessAttachmentEntityKind.SalesOrder => ApplicationPermission.SalesOrdersView,
		BusinessAttachmentEntityKind.SalesInvoice => ApplicationPermission.SalesInvoicesView,
		BusinessAttachmentEntityKind.PurchaseOrder => ApplicationPermission.PurchaseOrdersView,
		BusinessAttachmentEntityKind.GoodsReceipt => ApplicationPermission.GoodsReceiptsView,
		BusinessAttachmentEntityKind.SupplierDocument => ApplicationPermission.FinancePayablesView,
		BusinessAttachmentEntityKind.PurchaseRequisition => ApplicationPermission.PurchaseRequisitionsView,
		BusinessAttachmentEntityKind.RequestForQuotation or BusinessAttachmentEntityKind.SupplierQuoteResponse => ApplicationPermission.SupplierSourcingView,
		BusinessAttachmentEntityKind.Project => ApplicationPermission.ProjectsView,
		_ => throw new ArgumentOutOfRangeException(nameof(entityKind))
	};

	private static ApplicationPermission ManagePermission(BusinessAttachmentEntityKind entityKind) => entityKind switch
	{
		BusinessAttachmentEntityKind.Customer => ApplicationPermission.CustomersEdit,
		BusinessAttachmentEntityKind.Supplier => ApplicationPermission.SuppliersManage,
		BusinessAttachmentEntityKind.Item => ApplicationPermission.ItemsEdit,
		BusinessAttachmentEntityKind.SalesQuote => ApplicationPermission.SalesQuotesEdit,
		BusinessAttachmentEntityKind.SalesOrder => ApplicationPermission.SalesOrdersEdit,
		BusinessAttachmentEntityKind.SalesInvoice => ApplicationPermission.SalesInvoicesCreate,
		BusinessAttachmentEntityKind.PurchaseOrder => ApplicationPermission.PurchaseOrdersEdit,
		BusinessAttachmentEntityKind.GoodsReceipt => ApplicationPermission.GoodsReceiptsCreate,
		BusinessAttachmentEntityKind.SupplierDocument => ApplicationPermission.FinancePayablesManage,
		BusinessAttachmentEntityKind.PurchaseRequisition => ApplicationPermission.PurchaseRequisitionsManage,
		BusinessAttachmentEntityKind.RequestForQuotation or BusinessAttachmentEntityKind.SupplierQuoteResponse => ApplicationPermission.SupplierSourcingManage,
		BusinessAttachmentEntityKind.Project => ApplicationPermission.ProjectsManage,
		_ => throw new ArgumentOutOfRangeException(nameof(entityKind))
	};

	private static void ValidateEntityKind(BusinessAttachmentEntityKind entityKind)
	{
		if (!Enum.IsDefined(entityKind)) throw new ArgumentOutOfRangeException(nameof(entityKind));
	}

	private static string NormalizeFileName(string fileName)
	{
		if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("File name is required.", nameof(fileName));
		var normalized = Path.GetFileName(fileName.Trim()).Normalize(NormalizationForm.FormC);
		if (string.IsNullOrWhiteSpace(normalized) || normalized is "." or "..")
			throw new ArgumentException("File name is invalid.", nameof(fileName));
		if (normalized.Length > 255) throw new ArgumentException("File name must not exceed 255 characters.", nameof(fileName));
		if (normalized.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || normalized.Any(char.IsControl))
			throw new ArgumentException("File name contains invalid characters.", nameof(fileName));
		if (BlockedExtensions.Contains(Path.GetExtension(normalized)))
			throw new InvalidDataException($"Files with the '{Path.GetExtension(normalized)}' extension cannot be attached.");
		return normalized;
	}

	private static string NormalizeMediaType(string? mediaType)
	{
		var normalized = string.IsNullOrWhiteSpace(mediaType)
			? "application/octet-stream"
			: mediaType.Trim().ToLowerInvariant();
		if (normalized.Length > 127 || normalized.Any(char.IsControl))
			throw new ArgumentException("Media type is invalid.", nameof(mediaType));
		return normalized;
	}

	private static string? NormalizeOptional(string? value, int maxLength, string parameterName)
	{
		var normalized = string.IsNullOrWhiteSpace(value)
			? null
			: value.Trim().Normalize(NormalizationForm.FormC);
		if (normalized?.Length > maxLength)
			throw new ArgumentException($"Value must not exceed {maxLength} characters.", parameterName);
		return normalized;
	}

	private static async Task<byte[]> ReadBoundedAsync(Stream source, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(source);
		if (!source.CanRead) throw new ArgumentException("Attachment content stream must be readable.", nameof(source));
		if (source.CanSeek && source.Length - source.Position > MaxAttachmentBytes)
			throw new InvalidDataException($"Attachments must not exceed {MaxAttachmentBytes / (1024 * 1024)} MiB.");

		using var output = new MemoryStream();
		var buffer = ArrayPool<byte>.Shared.Rent(81920);
		try
		{
			var total = 0;
			while (true)
			{
				var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
				if (read == 0) break;
				total = checked(total + read);
				if (total > MaxAttachmentBytes)
					throw new InvalidDataException($"Attachments must not exceed {MaxAttachmentBytes / (1024 * 1024)} MiB.");
				await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
			}
			return output.ToArray();
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(buffer);
		}
	}

	private static string Hash(byte[] content) =>
		Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
}
