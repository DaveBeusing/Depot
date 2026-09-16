# Enterprise Identity and OpenID Connect

Updated: 2026-09-16

## Scope

Depot Enterprise Identity schema **1** provides the provider-neutral persistence and identity-resolution foundation for external OpenID Connect and Microsoft Entra ID authentication. F4B adds the interactive desktop authentication flow on top of that schema without changing the persisted contract.

Authentication identity and Depot authorization remain deliberately separate. The external provider proves who a subject is. Depot then resolves the validated provider/issuer/subject tuple to an existing active local `User`. Roles and effective permissions are loaded exclusively from Depot's local RBAC tables.

External token roles, groups, application roles and permission claims are not authorization inputs and never grant Depot permissions directly.

## Provider configuration

`EnterpriseIdentityProviders` stores the non-secret configuration boundary for an external provider:

- stable normalized provider code;
- provider kind (`OpenIdConnect` or `MicrosoftEntraId`);
- display name;
- HTTPS authority;
- public/native client application identifier;
- optional tenant boundary;
- enabled state;
- created/updated timestamps and optimistic version.

Provider code and provider kind are immutable after creation. Mutable connection metadata and enabled state use optimistic concurrency. Depot stores no external client secret, refresh token, access token, ID token, private key, authorization code or external password.

Microsoft Entra ID interactive sign-in is exposed only for tenant-bound provider records. An enabled Entra provider without a configured `TenantId` is not offered on the login screen. Unbound `common` / `organizations` multi-tenant semantics are outside the F4B product boundary.

Provider administration requires the existing `UsersManage` permission. Provider reads in administration require `UsersView`. Mutations are written with Audit evidence in the same database transaction.

## External identity links

`ExternalIdentityLinks` maps an externally validated identity to exactly one existing local Depot user.

The stable identity tuple is:

```text
provider code + issuer + subject
```

`issuer` and `subject` are identity identifiers and are matched exactly. Depot persists the original values plus a SHA-256 key over the UTF-8 tuple separated by NUL delimiters. The hash provides one provider-neutral uniqueness key even when the underlying SQL provider uses a case-insensitive default collation.

A link may also retain optional observed tenant, email and display-name attributes plus `LinkedUtc` and `LastSeenUtc`. These observed values are diagnostic/identity evidence only. They do not overwrite the local Depot user's email/display name and are not authorization inputs.

A linked identity cannot be linked to a second local user. Link and unlink administration requires `UsersManage` and is audited transactionally. F4B does not auto-provision users and does not create identity links during sign-in.

## Interactive OIDC flow

F4B uses the OAuth 2.0 Authorization Code flow with OpenID Connect and PKCE for the native Windows client.

The runtime flow is:

1. read an enabled provider from the existing Enterprise Identity configuration;
2. retrieve the provider's OpenID Connect discovery document over HTTPS;
3. generate cryptographically random `state`, `nonce` and PKCE verifier values;
4. open the system browser with `response_type=code`, `openid profile email` scopes and an S256 PKCE challenge;
5. receive the authorization response on a dynamically allocated loopback `http://localhost:<port>/` callback;
6. require the returned `state` to match the request in fixed time;
7. exchange the authorization code using the public client ID, exact redirect URI and PKCE verifier, without a client secret;
8. validate the ID token signature, issuer, audience, expiration/lifetime, advertised signing algorithm and `nonce`;
9. for Microsoft Entra ID, require the validated tenant claim to match the configured tenant boundary;
10. construct `ValidatedExternalIdentity` only after those checks succeed;
11. resolve that identity through `IEnterpriseIdentityResolver`;
12. start the normal Depot session and sign in using the local user's effective Depot permissions.

Discovery and signing keys are handled through the Microsoft IdentityModel OpenID Connect configuration manager. A signing-key miss triggers one controlled metadata refresh/retry so normal key rollover can recover without disabling signature validation.

## Loopback callback boundary

The desktop callback listener binds only to the local loopback interface on an operating-system-assigned port. The redirect URI uses `localhost` and the callback handler validates the target host/port before accepting the response.

The callback reader is bounded and accepts only one GET response. Duplicate query parameters, malformed request lines, oversized request/header values and mismatched callback targets fail closed. The browser response contains no token or identity details and is returned with `Cache-Control: no-store`.

`access_denied` is treated as user cancellation. Other provider/protocol failures do not reveal raw token content or provider exception messages in the UI.

## Token and secret handling

Depot is a public/native client in this flow:

- no OIDC client secret is configured or sent;
- authorization codes exist only in memory long enough for token exchange;
- access tokens, refresh tokens and ID tokens are not persisted;
- protocol tokens are not written to Audit, Security Events, application logs or UI messages;
- external passwords are entered only at the provider's browser surface and are never visible to Depot.

F4B uses the IdentityModel version already present in Depot's locked dependency graph. It does not combine the authentication feature with an unrelated dependency-version upgrade.

## Resolution and authorization contract

`IEnterpriseIdentityResolver` accepts a `ValidatedExternalIdentity`. Resolution fails closed when:

- the configured provider does not exist or is disabled;
- a configured tenant boundary does not match;
- no exact provider/issuer/subject link exists;
- stored link identity material fails its SHA-256/integrity check;
- the linked local Depot user does not exist or is inactive.

On success, roles and effective permissions are reloaded from Depot's own `Roles`, `UserRoles`, `RolePermissions` and `Permissions` data. An identity-provider administrator therefore cannot obtain Depot permissions merely by adding an external group or role claim.

`RecordSuccessfulAuthenticationAsync` updates only observed link attributes and `LastSeenUtc`. The existing `SessionService`, concurrent-session policy and `AuthorizationService` are reused for enterprise sign-in.

## Security-event boundary

Protocol failures, unavailable providers, unresolved identities and session-limit rejection create controlled authentication-failure evidence without storing token material or raw provider error descriptions. A successful enterprise sign-in uses the existing authentication-success Security Event path and normal session correlation identifiers.

User cancellation does not create a failed-authentication event.

## Provider support

Enterprise Identity schema 1 remains provider-neutral and part of the database-provider smoke matrix for:

- SQLite;
- SQL Server 2022 / engine 16.x;
- MariaDB 11.8 LTS;
- MySQL 8.4 LTS.

F4B adds no persisted schema and therefore does not increment the Enterprise Identity schema version.

## Privacy boundary

The feature stores the minimum stable mapping needed to associate a validated external account with a local Depot user. It does not store external passwords, protocol tokens, source IP, geolocation, device fingerprints or typed/user-interaction telemetry.

Provider/issuer/subject identifiers and optional observed email/display name may still be personal or security-sensitive data. Existing Depot access control, backup, retention and Audit protections apply.

## Remaining work

F4C adds explicit interpretation and evidence for externally satisfied MFA/authentication-method claims plus further identity hardening. Until F4C defines that contract, F4B does not claim or infer that a particular external authentication ceremony satisfied Depot MFA policy merely because an ID token was accepted.
