// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.ViewModels;

using Xunit;

namespace Depot.Tests;

public sealed class WelcomeViewModelTests
{
	[Theory]
	[InlineData(8, "Good morning")]
	[InlineData(12, "Good afternoon")]
	[InlineData(17, "Good afternoon")]
	[InlineData(18, "Good evening")]
	public void GreetingUsesLocalTimeOfDay(int hour, string expected)
	{
		Assert.Equal(expected, WelcomeViewModel.GetGreeting(hour));
	}

	[Fact]
	public void WelcomeTipsMatchSupportedShellShortcuts()
	{
		var viewModel = new WelcomeViewModel("Alex", new DateTime(2026, 8, 22, 9, 0, 0));

		Assert.Equal("Good morning, Alex", viewModel.Greeting);
		Assert.Equal(
			["Ctrl+P", "Ctrl+Shift+P", "Ctrl+Tab", "Ctrl+W", "F1"],
			viewModel.Tips.Select(tip => tip.Shortcut).ToArray());
	}
}