// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

namespace Depot.ViewModels.Users;

public sealed class UserListItemViewModel : BaseViewModel
{
	public UserListItemViewModel(User user)
	{
		Id = user.Id;
		UserName = user.UserName;
		DisplayName = user.DisplayName;
		Role = user.IsAdministrator ? "Administrator" : "User";
		Status = user.IsActive ? "Active" : "Inactive";
		CreatedUtc = user.CreatedUtc;
		IsAdministrator = user.IsAdministrator;
		IsActive = user.IsActive;
	}

	public long Id { get; }
	public string UserName { get; }
	public string DisplayName { get; }
	public string Role { get; } 
	public string Status { get; }
	public DateTime CreatedUtc { get; }
	public bool IsAdministrator { get; }
	public bool IsActive { get; }

}
