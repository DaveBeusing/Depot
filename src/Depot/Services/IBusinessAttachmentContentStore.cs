// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Services;

public interface IBusinessAttachmentContentStore
{
	Task<Stream> OpenReadAsync(Guid attachmentId, int revision, CancellationToken cancellationToken = default);
}
