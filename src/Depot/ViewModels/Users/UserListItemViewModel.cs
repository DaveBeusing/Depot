// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels.Users;

public sealed class UserListItemViewModel : BaseViewModel
{
	public UserListItemViewModel(User user)
	{
		Id = user.Id;
		Email = user.Email;
		DisplayName = user.DisplayName;
		UserRole = AuthorizationService.EffectiveRole(user);
		Role = UserRole.ToString();
		Status = user.IsActive ? "Active" : "Inactive";
		CreatedUtc = user.CreatedUtc;
		IsAdministrator = user.IsAdministrator;
		CanApprovePurchaseOrders = user.CanApprovePurchaseOrders;
		IsActive = user.IsActive;
		Version = user.Version;
	}

	public long Id { get; }
	public string Email { get; }
	public string DisplayName { get; }
	public string Role { get; } 
	public UserRole UserRole { get; }
	public string Status { get; }
	public DateTime CreatedUtc { get; }
	public bool IsAdministrator { get; }
	public bool CanApprovePurchaseOrders { get; }
	public bool IsActive { get; }
	public long Version { get; }

}
