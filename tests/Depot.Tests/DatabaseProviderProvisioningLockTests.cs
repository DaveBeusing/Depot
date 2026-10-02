// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;

using Xunit;

namespace Depot.Tests;

public sealed class DatabaseProviderProvisioningLockTests
{
	[Fact]
	public async Task IndependentSqliteTargetsDoNotContendOnProvisioningLock()
	{
		var firstPath = Path.Combine(Path.GetTempPath(), $"depot-lock-a-{Guid.NewGuid():N}.db");
		var secondPath = Path.Combine(Path.GetTempPath(), $"depot-lock-b-{Guid.NewGuid():N}.db");
		using var firstReady = new ManualResetEventSlim();
		using var releaseFirst = new ManualResetEventSlim();

		var holder = StartLockHolder(new SqliteConnectionFactory(firstPath), firstReady, releaseFirst);
		Assert.True(firstReady.Wait(TimeSpan.FromSeconds(5)));

		using var contenderStarted = new ManualResetEventSlim();
		var contender = StartAcquireAndRelease(new SqliteConnectionFactory(secondPath), contenderStarted);
		Assert.True(contenderStarted.Wait(TimeSpan.FromSeconds(5)));
		var completedWhileFirstWasHeld = await CompletesWithinAsync(contender, TimeSpan.FromSeconds(2));

		releaseFirst.Set();
		await holder;
		await contender;

		Assert.True(completedWhileFirstWasHeld);
	}

	[Fact]
	public async Task SameSqliteTargetRemainsSerializedAcrossFactories()
	{
		var path = Path.Combine(Path.GetTempPath(), $"depot-lock-shared-{Guid.NewGuid():N}.db");
		using var firstReady = new ManualResetEventSlim();
		using var releaseFirst = new ManualResetEventSlim();

		var holder = StartLockHolder(new SqliteConnectionFactory(path), firstReady, releaseFirst);
		Assert.True(firstReady.Wait(TimeSpan.FromSeconds(5)));

		using var contenderStarted = new ManualResetEventSlim();
		var contender = StartAcquireAndRelease(new SqliteConnectionFactory(path), contenderStarted);
		Assert.True(contenderStarted.Wait(TimeSpan.FromSeconds(5)));
		var completedWhileFirstWasHeld = await CompletesWithinAsync(contender, TimeSpan.FromMilliseconds(300));

		releaseFirst.Set();
		await holder;
		await contender;

		Assert.False(completedWhileFirstWasHeld);
	}

	[Fact]
	public async Task EquivalentSqlitePathsShareProvisioningLock()
	{
		var directory = Path.Combine(Path.GetTempPath(), $"depot-lock-normalized-{Guid.NewGuid():N}");
		var canonicalPath = Path.Combine(directory, "depot.db");
		var equivalentPath = Path.Combine(directory, ".", "depot.db");
		using var firstReady = new ManualResetEventSlim();
		using var releaseFirst = new ManualResetEventSlim();

		var holder = StartLockHolder(new SqliteConnectionFactory(canonicalPath), firstReady, releaseFirst);
		Assert.True(firstReady.Wait(TimeSpan.FromSeconds(5)));

		using var contenderStarted = new ManualResetEventSlim();
		var contender = StartAcquireAndRelease(new SqliteConnectionFactory(equivalentPath), contenderStarted);
		Assert.True(contenderStarted.Wait(TimeSpan.FromSeconds(5)));
		var completedWhileFirstWasHeld = await CompletesWithinAsync(contender, TimeSpan.FromMilliseconds(300));

		releaseFirst.Set();
		await holder;
		await contender;

		Assert.False(completedWhileFirstWasHeld);
	}

	[Fact]
	public async Task FailedProvisioningScopeReleasesSqliteLock()
	{
		var path = Path.Combine(Path.GetTempPath(), $"depot-lock-release-{Guid.NewGuid():N}.db");
		var factory = new SqliteConnectionFactory(path);

		Assert.Throws<InvalidOperationException>(() =>
		{
			using var held = DatabaseProvisioningLock.Acquire(factory);
			throw new InvalidOperationException("Simulated provisioning failure.");
		});

		var contender = StartAcquireAndRelease(new SqliteConnectionFactory(path));
		Assert.True(await CompletesWithinAsync(contender, TimeSpan.FromSeconds(2)));
		await contender;
	}

	private static Task StartLockHolder(
		SqliteConnectionFactory factory,
		ManualResetEventSlim ready,
		ManualResetEventSlim release) =>
		Task.Factory.StartNew(
			() =>
			{
				using var held = DatabaseProvisioningLock.Acquire(factory);
				ready.Set();
				if (!release.Wait(TimeSpan.FromSeconds(10)))
					throw new TimeoutException("Test holder was not released.");
			},
			CancellationToken.None,
			TaskCreationOptions.LongRunning,
			TaskScheduler.Default);

	private static Task StartAcquireAndRelease(
		SqliteConnectionFactory factory,
		ManualResetEventSlim? started = null) =>
		Task.Factory.StartNew(
			() =>
			{
				started?.Set();
				using var held = DatabaseProvisioningLock.Acquire(factory);
			},
			CancellationToken.None,
			TaskCreationOptions.LongRunning,
			TaskScheduler.Default);

	private static async Task<bool> CompletesWithinAsync(Task task, TimeSpan timeout) =>
		await Task.WhenAny(task, Task.Delay(timeout)) == task;
}
