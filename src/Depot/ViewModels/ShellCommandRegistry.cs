// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.ViewModels;

public sealed record ShellCommandDefinition(
	string Id,
	string Title,
	string Subtitle,
	string Group,
	string TypeLabel,
	string IconData,
	Func<Task> ExecuteAsync);

public sealed class ShellCommandRegistry
{
	private readonly Dictionary<string, ShellCommandDefinition> _commands = new(StringComparer.OrdinalIgnoreCase);

	public IReadOnlyCollection<ShellCommandDefinition> Commands => _commands.Values;

	public void Register(ShellCommandDefinition command)
	{
		ArgumentNullException.ThrowIfNull(command);
		if (string.IsNullOrWhiteSpace(command.Id)) throw new ArgumentException("A command id is required.", nameof(command));
		_commands[command.Id] = command;
	}

	public IReadOnlyList<ShellCommandDefinition> Search(string? query)
	{
		var commands = _commands.Values.AsEnumerable();
		if (!string.IsNullOrWhiteSpace(query))
		{
			var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
			commands = commands.Where(command => terms.All(term =>
				$"{command.Title} {command.Subtitle} {command.Group} {command.TypeLabel}".Contains(term, StringComparison.OrdinalIgnoreCase)));
		}
		return commands.OrderBy(command => command.Group).ThenBy(command => command.Title).ToArray();
	}
}
