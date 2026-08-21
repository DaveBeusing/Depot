// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.ViewModels;

using Xunit;

namespace Depot.Tests;

public sealed class WorkspaceDocumentTests
{
	[Fact]
	public void DocumentFactoryCreatesStableKeyedTab()
	{
		using var item = WorkspaceDocumentFactory.Create(
			new WorkspaceDocumentDescriptor("sales-order:42", "SO-000042", ShellRoutes.Sales.Orders, string.Empty, "sales.orders"),
			() => new TestViewModel(),
			(_, _) => Task.CompletedTask);

		Assert.True(item.IsDocument);
		Assert.Equal("sales-order:42", item.TabKey);
		Assert.Equal(ShellRoutes.Sales.Orders, item.Route);
	}

	private sealed class TestViewModel : BaseViewModel;
}
