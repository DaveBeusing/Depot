// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.ViewModels.Users;

namespace Depot.Tests;

public sealed class UserEditorPasswordValidationTests
{
	[Fact]
	public void NewUser_RequiresValidPasswordAndConfirmation()
	{
		var editor = new UserEditorViewModel { Email = "user@example.com", Password = "Strong!Pass123", ConfirmPassword = "Strong!Pass123" };
		Assert.True(editor.PasswordValidationRequired);
		Assert.True(editor.PasswordInputIsValid);
	}

	[Fact]
	public void ExistingUser_AllowsBlankPasswordToRemainUnchanged()
	{
		var editor = new UserEditorViewModel { Id = 42, Email = "user@example.com" };
		Assert.False(editor.PasswordValidationRequired);
		Assert.True(editor.PasswordInputIsValid);
	}

	[Fact]
	public void ExistingUser_NewPasswordRequiresMatchingConfirmation()
	{
		var editor = new UserEditorViewModel { Id = 42, Email = "user@example.com", Password = "Strong!Pass123", ConfirmPassword = "Different!123" };
		Assert.True(editor.PasswordValidationRequired);
		Assert.False(editor.PasswordConfirmationMatches);
		Assert.False(editor.PasswordInputIsValid);
	}
}
