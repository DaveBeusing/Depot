// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Text.RegularExpressions;
using System.IO;

using Depot.Models;

namespace Depot.Services.Help;

public sealed partial class HelpService : IHelpService
{
	public const string FallbackTopicId = "getting-started.first-login";
	private readonly IHelpContentProvider _contentProvider;
	private readonly IAuthorizationService _authorization;
	private readonly HelpSearchService _search;
	private HelpManifest? _manifest;
	private IReadOnlyDictionary<string, HelpTopic>? _topics;

	public HelpService(IHelpContentProvider contentProvider, IAuthorizationService authorization, HelpSearchService search)
	{
		_contentProvider = contentProvider;
		_authorization = authorization;
		_search = search;
	}

	public async Task<HelpCatalog> GetCatalogAsync(CancellationToken cancellationToken = default)
	{
		await EnsureLoadedAsync(cancellationToken);
		var available = AvailableDefinitions().GroupBy(topic => topic.Category)
			.OrderBy(group => group.Min(topic => topic.Order))
			.Select(group => new HelpCategory(group.Key, group.OrderBy(topic => topic.Order).ThenBy(topic => topic.Title).ToArray()))
			.ToArray();
		return new HelpCatalog(Manifest.Version, available);
	}

	public async Task<HelpTopic?> GetTopicAsync(string id, CancellationToken cancellationToken = default)
	{
		await EnsureLoadedAsync(cancellationToken);
		return Topics.TryGetValue(id, out var topic) && IsAvailable(topic.Definition) ? topic : null;
	}

	public async Task<IReadOnlyList<HelpTopic>> SearchAsync(string? query, string? category = null, CancellationToken cancellationToken = default)
	{
		await EnsureLoadedAsync(cancellationToken);
		var candidates = Topics.Values.Where(topic => IsAvailable(topic.Definition));
		if (!string.IsNullOrWhiteSpace(category))
			candidates = candidates.Where(topic => string.Equals(topic.Definition.Category, category, StringComparison.OrdinalIgnoreCase));
		return _search.Search(candidates, query);
	}

	public async Task<IReadOnlyList<HelpTopicDefinition>> GetRelatedTopicsAsync(string id, CancellationToken cancellationToken = default)
	{
		var topic = await GetTopicAsync(id, cancellationToken);
		if (topic is null) return [];
		return topic.Definition.RelatedTopics.Select(relatedId => Topics.GetValueOrDefault(relatedId)?.Definition)
			.Where(definition => definition is not null && IsAvailable(definition))
			.Cast<HelpTopicDefinition>()
			.ToArray();
	}

	public async Task ValidateAsync(CancellationToken cancellationToken = default)
	{
		await EnsureLoadedAsync(cancellationToken);
		var duplicate = Manifest.Topics.GroupBy(topic => topic.Id, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
		if (duplicate is not null) throw new InvalidDataException($"Duplicate help topic ID '{duplicate.Key}'.");
		if (!Topics.ContainsKey(FallbackTopicId)) throw new InvalidDataException($"Fallback help topic '{FallbackTopicId}' is missing.");

		foreach (var topic in Topics.Values)
		{
			if (topic.Definition.RequiredPermission is { Length: > 0 } permission && !PermissionCatalog.TryParse(permission, out _))
				throw new InvalidDataException($"Help topic '{topic.Definition.Id}' references unknown permission '{permission}'.");
			var links = topic.Definition.RelatedTopics.Concat(TopicLinkRegex().Matches(topic.Markdown).Select(match => match.Groups[1].Value));
			foreach (var link in links)
				if (!Topics.ContainsKey(link)) throw new InvalidDataException($"Help topic '{topic.Definition.Id}' references missing topic '{link}'.");
		}
	}

	private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
	{
		if (_topics is not null) return;
		var manifest = await _contentProvider.LoadManifestAsync(cancellationToken);
		var loaded = new Dictionary<string, HelpTopic>(StringComparer.Ordinal);
		foreach (var definition in manifest.Topics)
		{
			if (loaded.ContainsKey(definition.Id)) throw new InvalidDataException($"Duplicate help topic ID '{definition.Id}'.");
			var markdown = await _contentProvider.LoadContentAsync(definition, cancellationToken);
			loaded.Add(definition.Id, new HelpTopic(definition, markdown, HelpSearchService.ExtractHeadings(markdown), HelpSearchService.ToSearchableText(markdown)));
		}
		_manifest = manifest;
		_topics = loaded;
	}

	private IEnumerable<HelpTopicDefinition> AvailableDefinitions() => Manifest.Topics.Where(IsAvailable);

	private bool IsAvailable(HelpTopicDefinition topic) => topic.RequiredPermission is not { Length: > 0 } code ||
		(PermissionCatalog.TryParse(code, out var permission) && _authorization.HasPermission(permission));

	private HelpManifest Manifest => _manifest ?? throw new InvalidOperationException("Help content has not been loaded.");
	private IReadOnlyDictionary<string, HelpTopic> Topics => _topics ?? throw new InvalidOperationException("Help content has not been loaded.");

	[GeneratedRegex("\\[[^\\]]+\\]\\(topic:([a-z0-9.-]+)\\)", RegexOptions.IgnoreCase)]
	private static partial Regex TopicLinkRegex();
}
