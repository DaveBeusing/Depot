// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Services;
using Depot.ViewModels;

using Xunit;

namespace Depot.Tests;

public sealed class OperationPresentationTests
{
	[Fact]
	public void ConcurrencyConflictUsesRecoverableWarningPresentation()
	{
		var viewModel = new TestViewModel();

		viewModel.Fail(new ConcurrencyConflictException("sales order"));

		Assert.True(viewModel.HasOperationError);
		Assert.True(viewModel.HasRecoverableConflict);
		Assert.Equal(OperationSeverity.Warning, viewModel.OperationSeverity);
		Assert.Equal("Reload", viewModel.OperationActionText);
		Assert.Contains("changed by another user", viewModel.OperationError, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void SuccessfulOperationUsesSuccessPresentation()
	{
		var viewModel = new TestViewModel();
		viewModel.Begin("Saving");
		viewModel.Complete("Saved");

		Assert.Equal(OperationSeverity.Success, viewModel.OperationSeverity);
		Assert.Equal("Saved", viewModel.StatusText);
		Assert.False(viewModel.HasOperationError);
	}

	private sealed class TestViewModel : BaseViewModel
	{
		public void Begin(string text) => BeginOperation(text);
		public void Complete(string text) => CompleteOperation(statusText: text);
		public void Fail(Exception exception) => FailOperation(exception);
	}
}
