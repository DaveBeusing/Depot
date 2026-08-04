// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

namespace Depot.Services.Help;

public interface IHelpService
{
	Task<HelpCatalog> GetCatalogAsync(CancellationToken cancellationToken = default);
	Task<HelpTopic?> GetTopicAsync(string id, CancellationToken cancellationToken = default);
	Task<IReadOnlyList<HelpTopic>> SearchAsync(string? query, string? category = null, CancellationToken cancellationToken = default);
	Task<IReadOnlyList<HelpTopicDefinition>> GetRelatedTopicsAsync(string id, CancellationToken cancellationToken = default);
	Task ValidateAsync(CancellationToken cancellationToken = default);
}
