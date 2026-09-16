// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public interface IWorkspaceProductivityService
{
	Task<WorkspaceProductivitySnapshot> GetAsync(CancellationToken cancellationToken = default);
	Task PinAsync(string routeId, CancellationToken cancellationToken = default);
	Task UnpinAsync(string routeId, CancellationToken cancellationToken = default);
	Task RecordVisitAsync(string routeId, CancellationToken cancellationToken = default);
	Task SetDefaultLandingAsync(string? routeId, CancellationToken cancellationToken = default);
}

public sealed class WorkspaceProductivityService : IWorkspaceProductivityService
{
	public const int MaximumFavorites = 12;
	public const int MaximumRecents = 12;

	private readonly IDatabaseTransactionRunner _transactions;
	private readonly WorkspaceProductivityRepository _repository;
	private readonly IAuthorizationService _authorization;

	public WorkspaceProductivityService(
		IDatabaseTransactionRunner transactions,
		WorkspaceProductivityRepository repository,
		IAuthorizationService authorization)
	{
		_transactions = transactions;
		_repository = repository;
		_authorization = authorization;
	}

	public async Task<WorkspaceProductivitySnapshot> GetAsync(CancellationToken cancellationToken = default)
	{
		var userId = RequireUser();
		var favoritesTask = _repository.ListFavoritesAsync(userId, cancellationToken);
		var recentsTask = _repository.ListRecentsAsync(userId, MaximumRecents, cancellationToken);
		var preferenceTask = _repository.GetPreferenceAsync(userId, cancellationToken);
		await Task.WhenAll(favoritesTask, recentsTask, preferenceTask);
		return new WorkspaceProductivitySnapshot(
			favoritesTask.Result,
			recentsTask.Result,
			preferenceTask.Result?.DefaultLandingRouteId);
	}

	public async Task PinAsync(string routeId, CancellationToken cancellationToken = default)
	{
		var userId = RequireUser();
		routeId = NormalizeRoute(routeId);
		var favorites = await _repository.ListFavoritesAsync(userId, cancellationToken);
		if (!favorites.Contains(routeId, StringComparer.OrdinalIgnoreCase) && favorites.Count >= MaximumFavorites)
			throw new InvalidOperationException($"A user can pin at most {MaximumFavorites} workspaces.");
		await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			await _repository.PinAsync(transaction, userId, routeId, DateTime.UtcNow, token);
			return true;
		}, cancellationToken);
	}

	public Task UnpinAsync(string routeId, CancellationToken cancellationToken = default)
	{
		var userId = RequireUser();
		routeId = NormalizeRoute(routeId);
		return _transactions.ExecuteAsync(async (transaction, token) =>
		{
			await _repository.UnpinAsync(transaction, userId, routeId, token);
			return true;
		}, cancellationToken);
	}

	public Task RecordVisitAsync(string routeId, CancellationToken cancellationToken = default)
	{
		var userId = RequireUser();
		routeId = NormalizeRoute(routeId);
		return _transactions.ExecuteAsync(async (transaction, token) =>
		{
			await _repository.RecordRecentAsync(transaction, userId, routeId, DateTime.UtcNow, MaximumRecents, token);
			return true;
		}, cancellationToken);
	}

	public Task SetDefaultLandingAsync(string? routeId, CancellationToken cancellationToken = default)
	{
		var userId = RequireUser();
		var normalized = routeId is null ? null : NormalizeRoute(routeId);
		return _transactions.ExecuteAsync(async (transaction, token) =>
		{
			await _repository.SetDefaultLandingAsync(transaction, userId, normalized, DateTime.UtcNow, token);
			return true;
		}, cancellationToken);
	}

	private long RequireUser()
	{
		var user = _authorization.CurrentUser;
		if (user is not { IsActive: true } || user.Id <= 0)
			throw new UnauthorizedAccessException("A signed-in active user is required for workspace productivity preferences.");
		return user.Id;
	}

	private static string NormalizeRoute(string value)
	{
		if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A workspace route is required.", nameof(value));
		var route = value.Trim().ToLowerInvariant();
		if (route.Length > 160) throw new ArgumentException("A workspace route cannot exceed 160 characters.", nameof(value));
		if (route.Any(character => !(char.IsLetterOrDigit(character) || character is '.' or '-' or '_' or ':')))
			throw new ArgumentException("Workspace routes may only contain letters, digits, '.', '-', '_' and ':'.", nameof(value));
		return route;
	}
}

internal static class WorkspaceProductivityRuntime
{
	private static WorkspaceProductivityService? _service;

	public static WorkspaceProductivityService Current => _service ?? throw new InvalidOperationException("Workspace productivity services have not been configured.");

	public static void Configure(WorkspaceProductivityService service) => _service = service ?? throw new ArgumentNullException(nameof(service));

	public static void Clear(WorkspaceProductivityService service)
	{
		if (ReferenceEquals(_service, service)) _service = null;
	}
}
