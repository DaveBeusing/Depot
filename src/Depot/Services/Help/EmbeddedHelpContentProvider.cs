// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Reflection;
using System.IO;
using System.Text.Json;

using Depot.Models;

namespace Depot.Services.Help;

public sealed class EmbeddedHelpContentProvider : IHelpContentProvider
{
	private const string ManifestResourceName = "Depot.Help.manifest.json";
	private readonly Assembly _assembly;
	private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

	public EmbeddedHelpContentProvider(Assembly assembly) => _assembly = assembly;

	public async Task<HelpManifest> LoadManifestAsync(CancellationToken cancellationToken = default)
	{
		await using var stream = OpenResource(ManifestResourceName);
		return await JsonSerializer.DeserializeAsync<HelpManifest>(stream, _jsonOptions, cancellationToken)
			?? throw new InvalidDataException("The embedded help manifest is empty.");
	}

	public async Task<string> LoadContentAsync(HelpTopicDefinition topic, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(topic);
		var normalized = topic.File.Replace('\\', '/').Trim('/');
		if (normalized.Contains("..", StringComparison.Ordinal) || !normalized.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
			throw new InvalidDataException($"Help topic '{topic.Id}' has an invalid content path.");
		var segments = normalized.Split('/');
		for (var index = 0; index < segments.Length - 1; index++) segments[index] = segments[index].Replace('-', '_');
		var resourceName = $"Depot.Help.{string.Join('.', segments)}";
		await using var stream = OpenResource(resourceName);
		using var reader = new StreamReader(stream);
		return await reader.ReadToEndAsync(cancellationToken);
	}

	private Stream OpenResource(string resourceName) =>
		_assembly.GetManifestResourceStream(resourceName)
		?? throw new FileNotFoundException($"Embedded help resource '{resourceName}' was not found.", resourceName);
}
