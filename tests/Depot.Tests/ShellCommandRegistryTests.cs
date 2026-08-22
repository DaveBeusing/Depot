// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.ViewModels;

using Xunit;

namespace Depot.Tests;

public sealed class ShellCommandRegistryTests
{
	[Fact]
	public void RegistryFiltersCommandsAcrossMetadata()
	{
		var registry = new ShellCommandRegistry();
		registry.Register(new ShellCommandDefinition("sales.new-order", "New Sales Order", "Create customer order", "Actions", "ACTION", string.Empty, () => Task.CompletedTask));
		registry.Register(new ShellCommandDefinition("warehouse.transfer", "Transfer Stock", "Move inventory", "Actions", "ACTION", string.Empty, () => Task.CompletedTask));

		var result = Assert.Single(registry.Search("sales order"));
		Assert.Equal("sales.new-order", result.Id);
	}

	[Fact]
	public void RegisterReplacesCommandWithSameStableId()
	{
		var registry = new ShellCommandRegistry();
		registry.Register(new ShellCommandDefinition("shell.refresh", "Refresh", "Old", "Shell", "SHELL", string.Empty, () => Task.CompletedTask));
		registry.Register(new ShellCommandDefinition("shell.refresh", "Refresh", "New", "Shell", "SHELL", string.Empty, () => Task.CompletedTask));

		Assert.Equal("New", Assert.Single(registry.Commands).Subtitle);
	}
}
