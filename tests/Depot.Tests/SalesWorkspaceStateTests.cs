// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Runtime.CompilerServices;

using Depot.ViewModels;

using Xunit;

namespace Depot.Tests;

public sealed class SalesWorkspaceStateTests
{
	[Fact]
	public void StateIsSharedForEveryPageUsingSameWorkspace()
	{
		var workspace = (SalesViewModel)RuntimeHelpers.GetUninitializedObject(typeof(SalesViewModel));

		Assert.Same(SalesWorkspaceState.For(workspace), SalesWorkspaceState.For(workspace));
	}
}
