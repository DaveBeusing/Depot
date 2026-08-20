// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public static class PermissionCatalog
{
	private static readonly string[] Actions =
	[
		"Reverse", "Approve", "Create", "Manage", "Submit", "Release", "Cancel", "Convert", "Send", "Export", "Order", "Close", "Edit", "Post", "View"
	];
	private static readonly IReadOnlyList<PermissionDefinition> DefinitionsValue =
		Enum.GetValues<ApplicationPermission>()
			.Select(CreateDefinition)
			.ToArray();
	private static readonly IReadOnlyDictionary<ApplicationPermission, PermissionDefinition> ByPermission =
		DefinitionsValue.ToDictionary(definition => definition.Permission);
	private static readonly IReadOnlyDictionary<string, ApplicationPermission> ByCode =
		DefinitionsValue.ToDictionary(definition => definition.Code, definition => definition.Permission, StringComparer.Ordinal);

	public static IReadOnlyList<PermissionDefinition> Definitions => DefinitionsValue;
	public static IReadOnlySet<ApplicationPermission> All { get; } = DefinitionsValue.Select(value => value.Permission).ToHashSet();

	public static string Code(ApplicationPermission permission) => ByPermission[permission].Code;
	public static bool TryParse(string code, out ApplicationPermission permission) => ByCode.TryGetValue(code, out permission);

	private static PermissionDefinition CreateDefinition(ApplicationPermission permission)
	{
		var name = permission.ToString();
		var actionIndex = FindActionIndex(name);
		var module = name[..actionIndex];
		var action = name[actionIndex..];
		return new(permission, $"{module}.{action}", SplitWords(module), action, $"{SplitWords(module)}: {SplitWords(action)}");
	}

	private static int FindActionIndex(string value)
	{
		foreach (var action in Actions)
		{
			if (value.EndsWith(action, StringComparison.Ordinal)) return value.Length - action.Length;
		}
		throw new InvalidOperationException($"Permission '{value}' has no recognized action suffix.");
	}

	private static string SplitWords(string value) =>
		string.Concat(value.Select((character, index) => index > 0 && char.IsUpper(character) ? $" {character}" : character.ToString()));
}
