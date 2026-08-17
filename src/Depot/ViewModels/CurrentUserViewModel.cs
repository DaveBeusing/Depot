// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

namespace Depot.ViewModels;

public sealed class CurrentUserViewModel : BaseViewModel
{
	public CurrentUserViewModel(User user)
	{
		User = user ?? throw new ArgumentNullException(nameof(user));
		Initials = CreateInitials(user.DisplayName, user.Email);
		EffectivePermissions = user.EffectivePermissions
			.Select(permission => permission.ToString())
			.OrderBy(permission => permission, StringComparer.OrdinalIgnoreCase)
			.ToArray();
	}

	public User User { get; }
	public string Initials { get; }
	public IReadOnlyList<Role> Roles => User.Roles;
	public IReadOnlyList<string> EffectivePermissions { get; }

	public string RolesSummary => Roles.Count == 0
		? "No assigned roles"
		: string.Join(", ", Roles.Select(role => role.Name));

	private static string CreateInitials(string? displayName, string? email)
	{
		var parts = (displayName ?? string.Empty)
			.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

		if (parts.Length >= 2)
			return string.Concat(parts[0][0], parts[^1][0]).ToUpperInvariant();

		if (parts.Length == 1)
			return parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant();

		var localPart = (email ?? string.Empty).Split('@')[0];
		return string.IsNullOrWhiteSpace(localPart)
			? "?"
			: localPart[..Math.Min(2, localPart.Length)].ToUpperInvariant();
	}
}
