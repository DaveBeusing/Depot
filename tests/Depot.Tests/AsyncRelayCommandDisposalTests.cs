// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Commands;
using Xunit;

namespace Depot.Tests;

public sealed class AsyncRelayCommandDisposalTests
{
	[Fact]
	public async Task DisposeDuringExecutionCompletesCancellationAndResetsState()
	{
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		using var command = new AsyncRelayCommand<string>(async (_, token) =>
		{
			entered.SetResult();
			await resume.Task;
			token.ThrowIfCancellationRequested();
		});
		var execution = command.ExecuteAsync("role");
		await entered.Task;
		command.Dispose();
		command.Dispose();
		Assert.False(command.CanExecute("role"));
		resume.SetResult();
		await execution;
		Assert.False(command.CanExecute("role"));
	}

	[Fact]
	public async Task DisposeDuringSuccessfulExecutionDoesNotInvalidateCleanup()
	{
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		using var command = new AsyncRelayCommand<string>(async (_, _) =>
		{
			entered.SetResult();
			await resume.Task;
		});
		var execution = command.ExecuteAsync("role");
		await entered.Task;
		command.Dispose();
		resume.SetResult();
		await execution;
		Assert.False(command.CanExecute("role"));
	}

	[Fact]
	public async Task DisposeDoesNotMaskDelegateFailure()
	{
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		using var command = new AsyncRelayCommand<string>(async (_, _) =>
		{
			entered.SetResult();
			await resume.Task;
			throw new InvalidOperationException("delegate failed");
		});
		var execution = command.ExecuteAsync("role");
		await entered.Task;
		command.Dispose();
		resume.SetResult();
		var error = await Assert.ThrowsAsync<InvalidOperationException>(() => execution);
		Assert.Equal("delegate failed", error.Message);
		Assert.False(command.CanExecute("role"));
	}
}
