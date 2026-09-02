// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Net.Mail;

using Depot.Data;
using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class UserService
{
	private readonly IDatabaseTransactionRunner _transactions;
	private readonly UserRepository _users;
	private readonly RoleRepository _roles;
	private readonly AuditRepository _auditEntries;
	private readonly PasswordHasher _passwordHasher;
	private readonly AuthorizationService _authorization;
	private readonly AuditService _audit;
	private SessionService? _session;
	private SecurityEventService? _securityEvents;

	public UserService(IDatabaseTransactionRunner transactions,UserRepository users,RoleRepository roles,AuditRepository auditEntries,PasswordHasher passwordHasher,AuthorizationService authorization,AuditService audit,SessionService? session=null,SecurityEventService? securityEvents=null)
	{_transactions=transactions;_users=users;_roles=roles;_auditEntries=auditEntries;_passwordHasher=passwordHasher;_authorization=authorization;_audit=audit;_session=session;_securityEvents=securityEvents;}

	internal void ConfigureSessionSecurity(SessionService session,SecurityEventService securityEvents)
	{
		ArgumentNullException.ThrowIfNull(session);ArgumentNullException.ThrowIfNull(securityEvents);if(_session is not null||_securityEvents is not null)throw new InvalidOperationException("User session security integration is already configured.");_session=session;_securityEvents=securityEvents;
	}

	public async Task<PageResult<User>> SearchUsersAsync(string? searchText,int pageNumber,int pageSize,CancellationToken cancellationToken)=>await SearchUsersAsync(searchText,null,pageNumber,pageSize,cancellationToken);
	public async Task<PageResult<User>> SearchUsersAsync(string? searchText,bool? isActive,int pageNumber,int pageSize,CancellationToken cancellationToken)
	{
		_authorization.RequirePermission(ApplicationPermission.UsersView);var page=await _users.SearchPageAsync(searchText,isActive,pageNumber,pageSize,cancellationToken);var ids=page.Items.Select(user=>user.Id).ToArray();var roles=await _roles.GetUserRolesAsync(ids,cancellationToken);var permissions=await _roles.GetEffectivePermissionsAsync(ids,cancellationToken);foreach(var user in page.Items){user.Roles=roles.GetValueOrDefault(user.Id)??[];user.EffectivePermissions=permissions.GetValueOrDefault(user.Id)??new HashSet<ApplicationPermission>();}return page;
	}
	public Task<IReadOnlyList<Role>> ListAssignableRolesAsync(CancellationToken cancellationToken){_authorization.RequirePermission(ApplicationPermission.UsersManage);return _roles.ListActiveAsync(cancellationToken);}

	public async Task<User> CreateUserAsync(string email,string displayName,string password,IReadOnlyCollection<long> roleIds,CancellationToken cancellationToken)
	{
		_authorization.RequirePermission(ApplicationPermission.UsersManage);email=NormalizeAndValidateEmail(email);displayName=ValidateDisplayName(displayName);PasswordPolicy.Validate(password,email);var roles=await ValidateRolesAsync(roleIds,cancellationToken);if(await _users.GetByEmailAsync(email,cancellationToken)is not null)throw new InvalidOperationException($"A user with email '{email}' already exists.");var user=new User{Email=email,DisplayName=displayName,Role=UserRole.User,IsAdministrator=false,CanApprovePurchaseOrders=false,IsActive=true,CreatedUtc=DateTime.UtcNow,Roles=roles};user.Id=await _transactions.ExecuteAsync(async(transaction,token)=>{var id=await UserRepository.CreateAsync(transaction,user,_passwordHasher.Hash(password),token);user.Id=id;await RoleRepository.ReplaceUserRolesAsync(transaction,id,roles.Select(role=>role.Id),token);await _auditEntries.CreateAsync(transaction,_audit.CreateCreatedEntry(id,user),token);return id;},cancellationToken);return await HydrateAsync(user,cancellationToken);
	}

	public async Task<User> UpdateUserAsync(long id,long expectedVersion,string email,string displayName,string password,IReadOnlyCollection<long> roleIds,CancellationToken cancellationToken)
	{
		_authorization.RequirePermission(ApplicationPermission.UsersManage);if(id<=0)throw new ArgumentOutOfRangeException(nameof(id));email=NormalizeAndValidateEmail(email);displayName=ValidateDisplayName(displayName);var passwordChanged=!string.IsNullOrEmpty(password);if(passwordChanged)PasswordPolicy.Validate(password,email);var roles=await ValidateRolesAsync(roleIds,cancellationToken);var stored=await _users.GetByIdAsync(id,cancellationToken)??throw new InvalidOperationException("The user was not found.");var before=await HydrateAsync(stored,cancellationToken);if(before.Version!=expectedVersion)throw new ConcurrencyConflictException("user");var duplicate=await _users.GetByEmailAsync(email,cancellationToken);if(duplicate is not null&&duplicate.Id!=id)throw new InvalidOperationException($"A user with email '{email}' already exists.");var after=Copy(before);after.Email=email;after.DisplayName=displayName;after.Roles=roles;
		var committedEvents=await _transactions.ExecuteAsync(async(transaction,token)=>
		{
			var hash=passwordChanged?_passwordHasher.Hash(password):null;if(!await UserRepository.UpdateAsync(transaction,after,hash,token))throw new ConcurrencyConflictException("user");await RoleRepository.ReplaceUserRolesAsync(transaction,id,roles.Select(role=>role.Id),token);after.Version++;await _auditEntries.CreateAsync(transaction,_audit.CreateUpdatedEntry(id,before,after),token);if(!passwordChanged)return Array.Empty<SecurityEvent>();
			var now=DateTime.UtcNow;var ownChange=_authorization.CurrentUser?.Id==id;var currentSessionId=ownChange?_session?.CurrentSessionId:null;var openSessions=await UserSessionRepository.GetOpenSessionsForUserAsync(transaction,id,token);var events=new List<SecurityEvent>();foreach(var openSession in openSessions.Where(value=>currentSessionId is null||value.SessionId!=currentSessionId.Value)){if(!await UserSessionRepository.EndAsync(transaction,openSession.SessionId,now,UserSessionEndReason.CredentialsChanged,token))throw new ConcurrencyConflictException("user session");if(_securityEvents is not null){var endedEvent=_securityEvents.CreateSessionEvent(openSession,SecurityEventType.CredentialsChanged,SecurityEventSeverity.High,"Session invalidated after credentials changed",ownChange?"Another session was invalidated after the user changed credentials.":"The session was invalidated after an administrative credential reset.");endedEvent.Id=await SecurityEventRepository.CreateAsync(transaction,endedEvent,token);events.Add(endedEvent);}}
			if(_securityEvents is not null){var credentialEvent=new SecurityEvent{TimestampUtc=now,EventType=SecurityEventType.CredentialsChanged,Severity=SecurityEventSeverity.High,UserId=id,AccountIdentifier=after.Email,SessionId=currentSessionId,ClientInstanceId=ownChange?_session?.CurrentClientInstanceId:null,MachineName=ownChange?_session?.CurrentMachineName:null,Summary=ownChange?"User credentials changed":"User credentials reset administratively",Details=ownChange?"The current session was retained; other open sessions were invalidated.":"All open sessions for the target user were invalidated."};credentialEvent.Id=await SecurityEventRepository.CreateAsync(transaction,credentialEvent,token);events.Add(credentialEvent);}return events.ToArray();
		},cancellationToken);
		if(_securityEvents is not null)foreach(var securityEvent in committedEvents)await _securityEvents.NotifyPersistedAsync(securityEvent,cancellationToken);after=await HydrateAsync(after,cancellationToken);if(_authorization.CurrentUser?.Id==after.Id)_authorization.SignIn(after,after.EffectivePermissions);return after;
	}

	public async Task<User> SetActiveAsync(long id,bool isActive,long expectedVersion,CancellationToken cancellationToken)
	{
		_authorization.RequirePermission(ApplicationPermission.UsersManage);if(id<=0)throw new ArgumentOutOfRangeException(nameof(id));var stored=await _users.GetByIdAsync(id,cancellationToken)??throw new InvalidOperationException("The user was not found.");var before=await HydrateAsync(stored,cancellationToken);if(before.Version!=expectedVersion)throw new ConcurrencyConflictException("user");if(!isActive&&_authorization.CurrentUser?.Id==id)throw new InvalidOperationException("The currently signed-in user cannot be deactivated.");var after=Copy(before);after.IsActive=isActive;
		var events=await _transactions.ExecuteAsync(async(transaction,token)=>{if(!await UserRepository.SetActiveAsync(transaction,id,isActive,expectedVersion,token))throw new ConcurrencyConflictException("user");var securityEvents=new List<SecurityEvent>();if(!isActive){var openSessions=await UserSessionRepository.GetOpenSessionsForUserAsync(transaction,id,token);var endedUtc=DateTime.UtcNow;var ended=await UserSessionRepository.EndActiveSessionsForUserAsync(transaction,id,endedUtc,UserSessionEndReason.Revoked,token);if(ended!=openSessions.Count)throw new ConcurrencyConflictException("user sessions");if(_securityEvents is not null)foreach(var session in openSessions){var securityEvent=_securityEvents.CreateSessionEvent(session,SecurityEventType.SessionRevoked,SecurityEventSeverity.High,"Session revoked because the user account was deactivated",null);securityEvent.Id=await SecurityEventRepository.CreateAsync(transaction,securityEvent,token);securityEvents.Add(securityEvent);}}after.Version++;await _auditEntries.CreateAsync(transaction,isActive?_audit.CreateUpdatedEntry(id,before,after):CreateDeactivatedEntry(id,before,after),token);return securityEvents.ToArray();},cancellationToken);
		if(_securityEvents is not null)foreach(var securityEvent in events)await _securityEvents.NotifyPersistedAsync(securityEvent,cancellationToken);return after;
	}

	private AuditEntry CreateDeactivatedEntry(long id,User before,User after){var entry=_audit.CreateUpdatedEntry(id,before,after);entry.Action="Deactivated";return entry;}
	private async Task<User> HydrateAsync(User user,CancellationToken cancellationToken){user.Roles=await _roles.GetUserRolesAsync(user.Id,cancellationToken);user.EffectivePermissions=await _roles.GetEffectivePermissionsAsync(user.Id,cancellationToken);return user;}
	private async Task<IReadOnlyList<Role>> ValidateRolesAsync(IEnumerable<long> roleIds,CancellationToken cancellationToken){var ids=roleIds.Distinct().ToArray();if(ids.Length==0)throw new ArgumentException("At least one active role is required.",nameof(roleIds));var roles=await _roles.GetByIdsAsync(ids,cancellationToken);if(roles.Count!=ids.Length||roles.Any(role=>!role.IsActive))throw new InvalidOperationException("Every assigned role must exist and be active.");return roles;}
	private static string NormalizeAndValidateEmail(string email){email=email.Trim().ToLowerInvariant();if(!MailAddress.TryCreate(email,out var parsed)||!string.Equals(parsed.Address,email,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("A valid email address is required.",nameof(email));return email;}
	private static string ValidateDisplayName(string displayName){displayName=displayName.Trim();if(displayName.Length is <1 or >200)throw new ArgumentException("Display name must contain 1-200 characters.",nameof(displayName));return displayName;}
	private static User Copy(User user)=>new(){Id=user.Id,Email=user.Email,DisplayName=user.DisplayName,IsAdministrator=user.IsAdministrator,CanApprovePurchaseOrders=user.CanApprovePurchaseOrders,Role=user.Role,Roles=user.Roles.ToArray(),EffectivePermissions=user.EffectivePermissions.ToHashSet(),IsActive=user.IsActive,CreatedUtc=user.CreatedUtc,Version=user.Version};
}
