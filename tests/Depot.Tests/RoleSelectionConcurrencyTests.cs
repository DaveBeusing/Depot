// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Windows.Input;

using Depot.Models;
using Depot.Services;
using Depot.ViewModels.Users;

using Xunit;

namespace Depot.Tests;

public sealed class RoleSelectionConcurrencyTests
{
	private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

	[Fact]
	public async Task OlderDetailsCannotReplaceSelectedRoleOrItsMutationTarget()
	{
		var service = new ControlledRoleService();
		var first = service.DelayDetails(1);
		var second = service.DelayDetails(2);
		using var viewModel = new RoleViewModel(service);
		var firstLoad = viewModel.SelectRoleAsync(Role(1));
		var secondLoad = viewModel.SelectRoleAsync(Role(2));

		second.SetResult(Role(2, ApplicationPermission.UsersView));
		await secondLoad.WaitAsync(Timeout);
		first.SetResult(Role(1, ApplicationPermission.RolesManage));
		await firstLoad.WaitAsync(Timeout);

		AssertRole(viewModel, 2, ApplicationPermission.UsersView);
		Assert.Equal(2, Assert.Single(viewModel.EffectivePermissionUsers).UserId);
		Assert.True(service.DetailTokens[1].IsCancellationRequested);
		viewModel.Name = "Reviewed second role";
		viewModel.Permissions.Single(value => value.Permission == ApplicationPermission.RolesView).IsSelected = true;
		await viewModel.SaveCommand.ExecuteAsync();
		var saved = Assert.Single(service.Saves);
		Assert.Equal(2, saved.Id);
		Assert.Equal(12, saved.Version);
		Assert.Contains(ApplicationPermission.UsersView, saved.Permissions);
		Assert.Contains(ApplicationPermission.RolesView, saved.Permissions);
		Assert.DoesNotContain(ApplicationPermission.RolesManage, saved.Permissions);
		await viewModel.ToggleActiveCommand.ExecuteAsync();
		var activation = Assert.Single(service.Activations);
		Assert.Equal(2, activation.Id);
		Assert.Equal(13, activation.Version);
		Assert.False(activation.Active);
	}

	[Fact]
	public async Task ReselectingTheSameRoleStillRejectsItsEarlierRequestGeneration()
	{
		var service = new ControlledRoleService();
		var first = service.DelayDetails(1);
		using var viewModel = new RoleViewModel(service);
		var firstLoad = viewModel.SelectRoleAsync(Role(1));
		await viewModel.SelectRoleAsync(Role(2));
		var latest = service.DelayDetails(1);
		var latestLoad = viewModel.SelectRoleAsync(Role(1));
		latest.SetResult(Role(1, ApplicationPermission.UsersView));
		await latestLoad.WaitAsync(Timeout);
		first.SetResult(Role(1, ApplicationPermission.RolesManage));
		await firstLoad.WaitAsync(Timeout);

		AssertRole(viewModel, 1, ApplicationPermission.UsersView);
	}

	[Fact]
	public async Task PendingSelectionBlocksSaveAndActivationEvenThroughCommandExecute()
	{
		var service = new ControlledRoleService();
		using var viewModel = new RoleViewModel(service);
		await viewModel.SelectRoleAsync(Role(1));
		var delayed = service.DelayDetails(2);
		var load = viewModel.SelectRoleAsync(Role(2));

		Assert.False(viewModel.CanEdit);
		Assert.False(viewModel.SaveCommand.CanExecute(null));
		Assert.False(viewModel.ToggleActiveCommand.CanExecute(null));
		((ICommand)viewModel.SaveCommand).Execute(null);
		((ICommand)viewModel.ToggleActiveCommand).Execute(null);
		await viewModel.SaveCommand.ExecuteAsync();
		await viewModel.ToggleActiveCommand.ExecuteAsync();
		Assert.Empty(service.Saves);
		Assert.Empty(service.Activations);

		delayed.SetResult(Role(2));
		await load.WaitAsync(Timeout);
		Assert.True(viewModel.CanEdit);
		Assert.True(viewModel.SaveCommand.CanExecute(null));
		Assert.True(viewModel.ToggleActiveCommand.CanExecute(null));
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task NewOrClearedSelectionInvalidatesPendingDetails(bool useNewCommand)
	{
		var service = new ControlledRoleService();
		var delayed = service.DelayDetails(1);
		using var viewModel = new RoleViewModel(service);
		var load = viewModel.SelectRoleAsync(Role(1));
		if (useNewCommand) viewModel.NewCommand.Execute(null);
		else viewModel.SelectedRole = null;
		viewModel.Code = "NEW_ROLE";
		viewModel.Name = "New role draft";
		viewModel.Permissions.Single(value => value.Permission == ApplicationPermission.RolesView).IsSelected = true;

		delayed.SetResult(Role(1, ApplicationPermission.RolesManage));
		await load.WaitAsync(Timeout);

		Assert.Null(viewModel.SelectedRole);
		Assert.Equal(0, viewModel.Id);
		Assert.Equal("New role draft", viewModel.Name);
		Assert.Equal(new[] { ApplicationPermission.RolesView }, SelectedPermissions(viewModel));
		Assert.Empty(viewModel.EffectivePermissionUsers);
		Assert.True(viewModel.SaveCommand.CanExecute(null));
		Assert.False(viewModel.ToggleActiveCommand.CanExecute(null));
		await viewModel.SaveCommand.ExecuteAsync();
		Assert.Equal(0, Assert.Single(service.Saves).Id);
		Assert.Equal(99, viewModel.Id);
		Assert.Equal(viewModel.Id, viewModel.SelectedRole?.Id);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task DisposedEditorIgnoresLateDetailsAndErrors(bool fail)
	{
		var service = new ControlledRoleService();
		var delayed = service.DelayDetails(1);
		var viewModel = new RoleViewModel(service);
		var load = viewModel.SelectRoleAsync(Role(1));
		viewModel.Dispose();
		if (fail) delayed.SetException(new InvalidOperationException("old failure"));
		else delayed.SetResult(Role(1));
		await load.WaitAsync(Timeout);

		Assert.Equal(0, viewModel.Id);
		Assert.Empty(viewModel.EffectivePermissionUsers);
		Assert.False(viewModel.CanEdit);
		Assert.False(viewModel.SaveCommand.CanExecute(null));
		Assert.False(viewModel.ToggleActiveCommand.CanExecute(null));
		Assert.False(viewModel.NewCommand.CanExecute(null));
		Assert.False(viewModel.HasOperationError);
		viewModel.NewCommand.Execute(null);
		await viewModel.SaveCommand.ExecuteAsync();
		Assert.Empty(service.Saves);
	}

	[Fact]
	public async Task CancelledCurrentSelectionCannotBecomeEditableWhenTheLookupIgnoresCancellation()
	{
		var service = new ControlledRoleService();
		var delayed = service.DelayDetails(1);
		using var cancellation = new CancellationTokenSource();
		using var viewModel = new RoleViewModel(service);
		var load = viewModel.SelectRoleAsync(Role(1), cancellation.Token);
		cancellation.Cancel();
		delayed.SetResult(Role(1));
		await load.WaitAsync(Timeout);

		Assert.Equal(0, viewModel.Id);
		Assert.False(viewModel.CanEdit);
		Assert.False(viewModel.IsBusy);
		Assert.False(viewModel.HasOperationError);
	}

	[Fact]
	public async Task StaleDetailErrorCannotReplaceTheNewSelectionStatus()
	{
		var service = new ControlledRoleService();
		var delayed = service.DelayDetails(1);
		using var viewModel = new RoleViewModel(service);
		var oldLoad = viewModel.SelectRoleAsync(Role(1));
		await viewModel.SelectRoleAsync(Role(2));
		delayed.SetException(new InvalidOperationException("old failure"));
		await oldLoad.WaitAsync(Timeout);

		AssertRole(viewModel, 2, ApplicationPermission.RolesView);
		Assert.False(viewModel.HasOperationError);
		Assert.Null(viewModel.OperationError);
	}

	[Fact]
	public async Task CurrentDetailFailureLeavesNoEditableMutationTarget()
	{
		var service = new ControlledRoleService();
		var delayed = service.DelayDetails(2);
		using var viewModel = new RoleViewModel(service);
		await viewModel.SelectRoleAsync(Role(1));
		var load = viewModel.SelectRoleAsync(Role(2));
		delayed.SetException(new InvalidOperationException("current failure"));
		await load.WaitAsync(Timeout);

		Assert.Equal(2, viewModel.SelectedRole?.Id);
		Assert.Equal(0, viewModel.Id);
		Assert.False(viewModel.CanEdit);
		Assert.True(viewModel.HasOperationError);
		Assert.Equal("current failure", viewModel.OperationError);
		Assert.Equal("Role unavailable", viewModel.EditorTitle);
		await viewModel.SaveCommand.ExecuteAsync();
		await viewModel.ToggleActiveCommand.ExecuteAsync();
		Assert.Empty(service.Saves);
		Assert.Empty(service.Activations);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task StaleImpactResultsAndErrorsCannotReplaceTheCurrentRole(bool fail)
	{
		var service = new ControlledRoleService();
		var delayed = service.DelayImpact(1);
		using var viewModel = new RoleViewModel(service);
		var oldLoad = viewModel.SelectRoleAsync(Role(1));
		Assert.Equal(1, viewModel.Id);
		await viewModel.SelectRoleAsync(Role(2));
		if (fail) delayed.SetException(new InvalidOperationException("old impact failure"));
		else delayed.SetResult([Impact(1)]);
		await oldLoad.WaitAsync(Timeout);

		AssertRole(viewModel, 2, ApplicationPermission.RolesView);
		Assert.Equal(2, Assert.Single(viewModel.EffectivePermissionUsers).UserId);
		Assert.Equal(2, viewModel.SelectedEffectivePermissionUser?.UserId);
		Assert.False(viewModel.HasOperationError);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task MatrixRefreshPreservesCurrentSelectionReferenceAndUnsavedDraft(bool changeSelection)
	{
		var service = new ControlledRoleService();
		using var viewModel = new RoleViewModel(service);
		await viewModel.SelectRoleAsync(Role(1));
		var matrix = NewCompletion<IReadOnlyList<Role>>();
		service.MatrixResponse = matrix;
		var refresh = viewModel.LoadAsync();
		if (changeSelection) await viewModel.SelectRoleAsync(Role(2));
		viewModel.Name = "Unsaved draft";
		viewModel.Permissions.Single(value => value.Permission == ApplicationPermission.UsersView).IsSelected = true;
		matrix.SetResult([Role(1), Role(2)]);
		await refresh.WaitAsync(Timeout);

		var selectedId = changeSelection ? 2 : 1;
		Assert.Equal(selectedId, viewModel.Id);
		Assert.Equal(selectedId, viewModel.SelectedRole?.Id);
		Assert.Same(viewModel.SelectedRole, viewModel.Roles.Single(value => value.Id == selectedId));
		Assert.Equal("Unsaved draft", viewModel.Name);
		Assert.Contains(ApplicationPermission.UsersView, SelectedPermissions(viewModel));
		Assert.Equal(selectedId, Assert.Single(viewModel.EffectivePermissionUsers).UserId);
	}

	[Theory]
	[InlineData(false, false)]
	[InlineData(true, false)]
	[InlineData(false, true)]
	[InlineData(true, true)]
	public async Task LateMutationCompletionOrFailureCannotReselectItsOldTarget(bool activation, bool fail)
	{
		var response = NewCompletion<Role>();
		var service = new ControlledRoleService { MutationResponse = response };
		using var viewModel = new RoleViewModel(service);
		await viewModel.SelectRoleAsync(Role(1));
		var mutation = activation ? viewModel.ToggleActiveCommand.ExecuteAsync() : viewModel.SaveCommand.ExecuteAsync();
		Assert.False(viewModel.SaveCommand.CanExecute(null));
		Assert.False(viewModel.ToggleActiveCommand.CanExecute(null));
		await viewModel.SelectRoleAsync(Role(2));
		Assert.False(service.MutationToken.IsCancellationRequested);
		if (fail) response.SetException(new InvalidOperationException("old mutation failure"));
		else response.SetResult(Role(1, ApplicationPermission.RolesManage));
		await mutation.WaitAsync(Timeout);

		AssertRole(viewModel, 2, ApplicationPermission.RolesView);
		Assert.Equal(2, Assert.Single(viewModel.EffectivePermissionUsers).UserId);
		Assert.False(viewModel.HasOperationError);
		Assert.True(viewModel.CanEdit);
	}

	[Fact]
	public async Task MutationDesignerRefreshCannotOverwriteANewerSelection()
	{
		var service = new ControlledRoleService();
		using var viewModel = new RoleViewModel(service);
		await viewModel.SelectRoleAsync(Role(1));
		var matrix = NewCompletion<IReadOnlyList<Role>>();
		service.MatrixResponse = matrix;
		var save = viewModel.SaveCommand.ExecuteAsync();
		await viewModel.SelectRoleAsync(Role(2, ApplicationPermission.UsersView));
		viewModel.Name = "Current second role";
		matrix.SetResult([Role(1), Role(2)]);
		await save.WaitAsync(Timeout);

		Assert.Equal(2, viewModel.SelectedRole?.Id);
		Assert.Equal(2, viewModel.Id);
		Assert.Equal("Current second role", viewModel.Name);
		Assert.Equal(2, Assert.Single(viewModel.EffectivePermissionUsers).UserId);
	}

	[Fact]
	public async Task MatrixChoiceUsesTheSameSelectionGenerationAsTheList()
	{
		var service = new ControlledRoleService();
		var delayed = service.DelayDetails(1);
		using var viewModel = new RoleViewModel(service);
		var oldLoad = viewModel.SelectRoleAsync(Role(1));
		await viewModel.SelectMatrixRoleCommand.ExecuteAsync(Role(2));
		delayed.SetResult(Role(1, ApplicationPermission.RolesManage));
		await oldLoad.WaitAsync(Timeout);

		AssertRole(viewModel, 2, ApplicationPermission.RolesView);
		Assert.Equal(2, Assert.Single(viewModel.EffectivePermissionUsers).UserId);
		Assert.True(service.DetailTokens[1].IsCancellationRequested);
	}

	[Fact]
	public async Task SystemRoleRemainsReadOnlyAfterCurrentDetailsAreLoaded()
	{
		var service = new ControlledRoleService { SystemRoleId = 1 };
		using var viewModel = new RoleViewModel(service);
		await viewModel.SelectRoleAsync(Role(1));

		Assert.True(viewModel.IsSystem);
		Assert.False(viewModel.CanEdit);
		((ICommand)viewModel.SaveCommand).Execute(null);
		((ICommand)viewModel.ToggleActiveCommand).Execute(null);
		Assert.Empty(service.Saves);
		Assert.Empty(service.Activations);
	}

	private static void AssertRole(RoleViewModel viewModel, long id, ApplicationPermission permission)
	{
		Assert.Equal(id, viewModel.SelectedRole?.Id);
		Assert.Equal(id, viewModel.Id);
		Assert.Equal($"Role {id}", viewModel.Name);
		Assert.Equal(new[] { permission }, SelectedPermissions(viewModel));
	}

	private static ApplicationPermission[] SelectedPermissions(RoleViewModel viewModel) =>
		viewModel.Permissions.Where(value => value.IsSelected).Select(value => value.Permission).Order().ToArray();

	private static TaskCompletionSource<T> NewCompletion<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);

	private static Role Role(long id, ApplicationPermission permission = ApplicationPermission.RolesView) =>
		new() { Id = id, Code = $"ROLE_{id}", Name = $"Role {id}", Version = id + 10, Permissions = [permission] };

	private static RoleEffectivePermissionUserProjection Impact(long roleId) =>
		new(roleId, $"User {roleId}", $"role{roleId}@example.test", true, [Role(roleId)], new HashSet<ApplicationPermission> { ApplicationPermission.RolesView });

	private sealed class ControlledRoleService : IRoleEditorService
	{
		private readonly Dictionary<long, TaskCompletionSource<Role?>> _details = [];
		private readonly Dictionary<long, TaskCompletionSource<IReadOnlyList<RoleEffectivePermissionUserProjection>>> _impacts = [];
		public Dictionary<long, CancellationToken> DetailTokens { get; } = [];
		public List<Role> Saves { get; } = [];
		public List<(long Id, long Version, bool Active)> Activations { get; } = [];
		public TaskCompletionSource<IReadOnlyList<Role>>? MatrixResponse { get; set; }
		public TaskCompletionSource<Role>? MutationResponse { get; init; }
		public CancellationToken MutationToken { get; private set; }
		public long? SystemRoleId { get; init; }
		public IReadOnlyList<PermissionDefinition> Permissions => PermissionCatalog.Definitions;

		public TaskCompletionSource<Role?> DelayDetails(long id)
		{
			var completion = NewCompletion<Role?>();
			_details[id] = completion;
			return completion;
		}

		public TaskCompletionSource<IReadOnlyList<RoleEffectivePermissionUserProjection>> DelayImpact(long id)
		{
			var completion = NewCompletion<IReadOnlyList<RoleEffectivePermissionUserProjection>>();
			_impacts[id] = completion;
			return completion;
		}

		public Task<Role?> GetByIdAsync(long id, CancellationToken cancellationToken)
		{
			DetailTokens[id] = cancellationToken;
			if (_details.TryGetValue(id, out var completion)) return completion.Task;
			var role = Role(id);
			role.IsSystem = id == SystemRoleId;
			return Task.FromResult<Role?>(role);
		}

		public Task<IReadOnlyList<RoleEffectivePermissionUserProjection>> GetEffectivePermissionUsersAsync(long roleId, CancellationToken cancellationToken) =>
			_impacts.TryGetValue(roleId, out var completion) ? completion.Task : Task.FromResult<IReadOnlyList<RoleEffectivePermissionUserProjection>>([Impact(roleId)]);

		public Task<PageResult<Role>> SearchAsync(string? searchText, int pageNumber, int pageSize, CancellationToken cancellationToken) =>
			Task.FromResult(new PageResult<Role>([Role(1), Role(2)], pageNumber, pageSize, 2));

		public Task<IReadOnlyList<Role>> ListDesignerRolesAsync(CancellationToken cancellationToken) =>
			MatrixResponse?.Task ?? Task.FromResult<IReadOnlyList<Role>>([Role(1), Role(2)]);

		public Task<Role> SaveAsync(Role value, CancellationToken cancellationToken)
		{
			Saves.Add(value);
			MutationToken = cancellationToken;
			return MutationResponse?.Task ?? Task.FromResult(new Role
			{
				Id = value.Id == 0 ? 99 : value.Id,
				Code = value.Code,
				Name = value.Name,
				Description = value.Description,
				IsActive = value.IsActive,
				IsSystem = value.IsSystem,
				Version = value.Version + 1,
				Permissions = value.Permissions.ToArray()
			});
		}

		public Task<Role> SetActiveAsync(long id, long version, bool isActive, CancellationToken cancellationToken)
		{
			Activations.Add((id, version, isActive));
			MutationToken = cancellationToken;
			var saved = Role(id);
			saved.Version = version + 1;
			saved.IsActive = isActive;
			return MutationResponse?.Task ?? Task.FromResult(saved);
		}
	}
}
