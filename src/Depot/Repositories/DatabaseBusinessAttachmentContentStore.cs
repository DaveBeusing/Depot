// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Services;

namespace Depot.Repositories;

public sealed class DatabaseBusinessAttachmentContentStore : IBusinessAttachmentContentStore
{
	private sealed record ContentRow(byte[] Content);

	private readonly DatabaseAccess _database;

	public DatabaseBusinessAttachmentContentStore(DatabaseAccess database)
	{
		_database = database ?? throw new ArgumentNullException(nameof(database));
	}

	public async Task<Stream> OpenReadAsync(Guid attachmentId, int revision, CancellationToken cancellationToken = default)
	{
		if (attachmentId == Guid.Empty) throw new ArgumentException("Attachment id is required.", nameof(attachmentId));
		if (revision < 1) throw new ArgumentOutOfRangeException(nameof(revision));

		var row = await _database.QuerySingleOrDefaultAsync(
			"SELECT Content FROM BusinessAttachmentContents WHERE AttachmentId=$AttachmentId AND Revision=$Revision;",
			reader => new ContentRow((byte[])reader.GetValue(0)),
			cancellationToken,
			new DatabaseParameter("$AttachmentId", attachmentId.ToString("D")),
			new DatabaseParameter("$Revision", revision))
			?? throw new InvalidOperationException("The requested attachment content was not found.");

		return new MemoryStream(row.Content, writable: false);
	}

	internal Task WriteAsync(
		DatabaseSession session,
		Guid attachmentId,
		int revision,
		byte[] content,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentNullException.ThrowIfNull(content);
		return session.ExecuteAsync(
			"INSERT INTO BusinessAttachmentContents (AttachmentId,Revision,Content) VALUES ($AttachmentId,$Revision,$Content);",
			cancellationToken,
			new DatabaseParameter("$AttachmentId", attachmentId.ToString("D")),
			new DatabaseParameter("$Revision", revision),
			new DatabaseParameter("$Content", content));
	}
}
