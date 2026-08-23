// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Nodes;

using Depot.Models;

namespace Depot.Services;

public sealed class AuditJsonSanitizer
{
	private const string Mask = "[REDACTED]";
	private const string InvalidPayload = "[Invalid audit payload hidden]";
	private static readonly string[] SensitiveNames =
	[
		"password", "hash", "salt", "connectionstring", "connection_string", "secret", "credential",
		"token", "accesstoken", "refresh_token", "bearer", "apikey", "api_key", "clientsecret",
		"protectedconfiguration", "encryptedsettings", "encryptionkey", "privatekey", "certificatepassword"
	];
	private static readonly JsonSerializerOptions DisplayOptions = new() { WriteIndented = true };

	public string Sanitize(string? json)
	{
		if (string.IsNullOrWhiteSpace(json)) return "—";
		try
		{
			var root = JsonNode.Parse(json);
			if (root is null) return "null";
			SanitizeNode(root);
			return root.ToJsonString(DisplayOptions);
		}
		catch (JsonException) { return InvalidPayload; }
	}

	public IReadOnlyList<AuditValueChange> Compare(string? beforeJson, string? afterJson)
	{
		var before = Flatten(Sanitize(beforeJson));
		var after = Flatten(Sanitize(afterJson));
		return before.Keys.Union(after.Keys, StringComparer.OrdinalIgnoreCase)
			.OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
			.Select(key => new AuditValueChange(key, before.GetValueOrDefault(key, "—"), after.GetValueOrDefault(key, "—")))
			.Where(change => !string.Equals(change.Before, change.After, StringComparison.Ordinal)).ToArray();
	}

	private static void SanitizeNode(JsonNode node)
	{
		if (node is JsonObject objectNode)
		{
			foreach (var property in objectNode.ToArray())
			{
				if (IsSensitive(property.Key)) objectNode[property.Key] = Mask;
				else if (property.Value is not null) SanitizeNode(property.Value);
			}
		}
		else if (node is JsonArray arrayNode) foreach (var child in arrayNode) if (child is not null) SanitizeNode(child);
	}

	private static bool IsSensitive(string propertyName)
	{
		var normalized = propertyName.Replace("-", string.Empty, StringComparison.Ordinal).Replace("_", string.Empty, StringComparison.Ordinal);
		return SensitiveNames.Any(name => normalized.Contains(name.Replace("_", string.Empty, StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase));
	}

	private static Dictionary<string, string> Flatten(string sanitizedJson)
	{
		if (sanitizedJson is "—" or InvalidPayload) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		try
		{
			var root = JsonNode.Parse(sanitizedJson);
			var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			if (root is not null) FlattenNode(root, "$", result);
			return result;
		}
		catch (JsonException) { return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); }
	}

	private static void FlattenNode(JsonNode node, string path, IDictionary<string, string> result)
	{
		switch (node)
		{
			case JsonObject objectNode:
				foreach (var property in objectNode) if (property.Value is not null) FlattenNode(property.Value, $"{path}.{property.Key}", result);
				break;
			case JsonArray arrayNode:
				for (var index = 0; index < arrayNode.Count; index++) if (arrayNode[index] is { } child) FlattenNode(child, $"{path}[{index}]", result);
				break;
			default:
				result[path] = node.ToJsonString().Trim('"');
				break;
		}
	}
}
