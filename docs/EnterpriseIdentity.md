# Enterprise Identity Foundation

Updated: 2026-09-16

## Scope

Depot Enterprise Identity schema **1** provides the persistence and service contracts required for external OpenID Connect and Microsoft Entra ID authentication. This foundation does not itself perform browser-based sign-in, token acquisition or token validation; those protocol/runtime responsibilities are implemented by the subsequent OIDC authentication package.

The feature keeps authentication identity and Depot authorization deliberately separate. An external provider proves who a subject is. The external identity is then resolved to an existing local Depot `User`. Roles and effective permissions are loaded exclusively from Depot's local RBAC tables.

External token roles, groups, application roles and permission claims are not persisted by this feature and never grant Depot permissions directly.

## Provider configuration

`EnterpriseIdentityProviders` stores the non-secret configuration boundary for an external provider:

- stable normalized provider code;
- provider kind (`OpenIdConnect` or `MicrosoftEntraId`);
- display name;
- HTTPS authority;
- public/client application identifier;
- optional tenant boundary;
- enabled state;
- created/updated timestamps and optimistic version.

Provider code and provider kind are immutable after creation. Mutable connection metadata and enabled state use optimistic concurrency. F4A stores no client secret, refresh token, access token, ID token, private key or authorization code.

Provider administration requires the existing `UsersManage` permission. Provider reads require `UsersView`. Mutations are written with Audit evidence in the same database transaction.

## External identity links

`ExternalIdentityLinks` maps an externally validated identity to exactly one existing local Depot user.

The stable identity tuple is:

```text
provider code + issuer + subject
```

`issuer` and `subject` are identity identifiers and are matched exactly. Depot persists the original values plus a SHA-256 key over the UTF-8 tuple separated by NUL delimiters. The hash provides one provider-neutral uniqueness key even when the underlying SQL provider uses a case-insensitive default collation.

A link may also retain optional observed tenant, email and display-name attributes plus `LinkedUtc` and `LastSeenUtc`. These observed values are diagnostic/identity evidence only. They do not overwrite the local Depot user's email/display name and are not authorization inputs.

A linked identity cannot be linked to a second local user. Link and unlink administration requires `UsersManage` and is audited transactionally.

## Resolution contract

`IEnterpriseIdentityResolver` accepts a `ValidatedExternalIdentity`. The word `Validated` is intentional: protocol validation of issuer metadata, signatures, audience, nonce, token lifetime, PKCE/state and related OIDC concerns belongs to the authentication-provider implementation, not this persistence layer.

Resolution fails closed when:

- the configured provider does not exist or is disabled;
- a configured tenant boundary does not match;
- no exact provider/issuer/subject link exists;
- stored link identity material fails its SHA-256/integrity check;
- the linked local Depot user does not exist or is inactive.

On success, roles and effective permissions are reloaded from Depot's own `Roles`, `UserRoles`, `RolePermissions` and `Permissions` data. This means an identity-provider administrator cannot obtain Depot permissions merely by adding an external group or role claim.

`RecordSuccessfulAuthenticationAsync` updates only observed link attributes and `LastSeenUtc`. Authentication Security Events and session creation remain responsibilities of the actual authentication flow in F4B.

## Provider support

Enterprise Identity schema 1 is provider-neutral and part of the database-provider smoke matrix for:

- SQLite;
- SQL Server 2022 / engine 16.x;
- MariaDB 11.8 LTS;
- MySQL 8.4 LTS.

The migration is serialized by the existing database provisioning lock and tracked independently in `DepotFeatureVersions` as `EnterpriseIdentity = 1`.

## Privacy and security boundary

The feature stores the minimum stable mapping needed to associate a validated external account with a local Depot user. It does not store passwords from external providers, protocol tokens, source IP, geolocation, device fingerprints or typed/user-interaction telemetry.

Provider/issuer/subject identifiers and optional observed email/display name may still be personal or security-sensitive data. Existing Depot access control, backup, retention and Audit protections apply.

## Next package

F4B adds the actual OpenID Connect / Microsoft Entra ID authentication flow against this foundation. It must validate protocol evidence before constructing `ValidatedExternalIdentity` and must continue to use the local Depot user/RBAC result returned by `IEnterpriseIdentityResolver`.
