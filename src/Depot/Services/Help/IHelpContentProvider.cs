// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

namespace Depot.Services.Help;

public interface IHelpContentProvider
{
	Task<HelpManifest> LoadManifestAsync(CancellationToken cancellationToken = default);
	Task<string> LoadContentAsync(HelpTopicDefinition topic, CancellationToken cancellationToken = default);
}
