// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Text.RegularExpressions;

using Depot.Models;

namespace Depot.Services.Help;

public sealed partial class HelpSearchService
{
	public IReadOnlyList<HelpTopic> Search(IEnumerable<HelpTopic> topics, string? query)
	{
		var terms = Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (terms.Length == 0) return topics.OrderBy(topic => topic.Definition.Category).ThenBy(topic => topic.Definition.Order).ToArray();

		return topics.Select(topic => new { Topic = topic, Score = Score(topic, terms) })
			.Where(result => result.Score > 0)
			.OrderByDescending(result => result.Score)
			.ThenBy(result => result.Topic.Definition.Order)
			.ThenBy(result => result.Topic.Definition.Title, StringComparer.OrdinalIgnoreCase)
			.Select(result => result.Topic)
			.ToArray();
	}

	public static IReadOnlyList<string> ExtractHeadings(string markdown) => markdown.Split('\n')
		.Select(line => HeadingRegex().Match(line.Trim()))
		.Where(match => match.Success)
		.Select(match => match.Groups[1].Value.Trim())
		.ToArray();

	public static string ToSearchableText(string markdown) => Normalize(MarkdownTokenRegex().Replace(markdown, " "));

	private static int Score(HelpTopic topic, IReadOnlyList<string> terms)
	{
		var title = Normalize(topic.Definition.Title);
		var headings = Normalize(string.Join(' ', topic.Headings));
		var keywords = Normalize(string.Join(' ', topic.Definition.Keywords));
		var body = topic.SearchableText;
		var score = 0;
		foreach (var term in terms)
		{
			if (!title.Contains(term, StringComparison.Ordinal) &&
				!headings.Contains(term, StringComparison.Ordinal) &&
				!keywords.Contains(term, StringComparison.Ordinal) &&
				!body.Contains(term, StringComparison.Ordinal)) return 0;
			if (title.Contains(term, StringComparison.Ordinal)) score += 8;
			if (keywords.Contains(term, StringComparison.Ordinal)) score += 6;
			if (headings.Contains(term, StringComparison.Ordinal)) score += 4;
			if (body.Contains(term, StringComparison.Ordinal)) score++;
		}
		return score;
	}

	private static string Normalize(string? value) => Regex.Replace(value?.ToLowerInvariant() ?? string.Empty, "\\s+", " ").Trim();

	[GeneratedRegex("^#{1,6}\\s+(.+)$")]
	private static partial Regex HeadingRegex();

	[GeneratedRegex("[`*_>#|!\\[\\]()]|https?://\\S+", RegexOptions.IgnoreCase)]
	private static partial Regex MarkdownTokenRegex();
}
