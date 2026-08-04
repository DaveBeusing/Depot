// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed class HelpManifest
{
	public required string Version { get; init; }
	public required IReadOnlyList<HelpTopicDefinition> Topics { get; init; }
}

public sealed class HelpTopicDefinition
{
	public required string Id { get; init; }
	public required string Title { get; init; }
	public required string Category { get; init; }
	public required string File { get; init; }
	public int Order { get; init; }
	public IReadOnlyList<string> Keywords { get; init; } = [];
	public string? RequiredPermission { get; init; }
	public IReadOnlyList<string> RelatedTopics { get; init; } = [];
}

public sealed record HelpTopic(
	HelpTopicDefinition Definition,
	string Markdown,
	IReadOnlyList<string> Headings,
	string SearchableText);

public sealed record HelpCategory(string Name, IReadOnlyList<HelpTopicDefinition> Topics);

public sealed record HelpCatalog(string Version, IReadOnlyList<HelpCategory> Categories)
{
	public IReadOnlyList<HelpTopicDefinition> Topics => Categories.SelectMany(category => category.Topics).ToArray();
}
