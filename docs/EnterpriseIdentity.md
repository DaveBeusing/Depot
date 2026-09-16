# Enterprise Identity and OpenID Connect

Updated: 2026-09-16

## Scope

Enterprise Identity schema **2** is Depot's provider-neutral persistence, identity-resolution and external-authentication-assurance boundary for OpenID Connect and Microsoft Entra ID. F4B provides the interactive Authorization Code + PKCE flow; F4C adds explicit provider-bound assurance policy and token-evidence hardening.

Authentication identity and Depot authorization remain deliberately separate. The external provider proves who a subject is and, when configured, supplies authentication-assurance evidence. Depot then resolves the validated provider/issuer/subject tuple to an existing active local `User`. Roles and effective permissions are loaded exclusively from Depot's local RBAC tables.

External token roles, groups, application roles and permission claims are not authorization inputs and never grant Depot permissions directly.

## Provider configuration

`EnterpriseIdentityProviders` stores the non-secret configuration boundary for an external provider:

- stable normalized provider code;
- provider kind (`OpenIdConnect` or `MicrosoftEntraId`);
- display name;
- HTTPS authority;
- public/native client application identifier;
- optional tenant boundary;
- optional required exact `amr` value;
- optional required exact `acr` value;
- optional maximum authentication age in minutes;
- enabled state;
- created/updated timestamps and optimistic version.

Provider code and provider kind are immutable after creation. Mutable connection metadata, assurance policy and enabled state use optimistic concurrency. Depot stores no external client secret, refresh token, access token, ID token, private key, authorization code, external password or raw authentication-method claim history.

Microsoft Entra ID interactive sign-in is exposed only for tenant-bound provider records. An enabled Entra provider without a configured `TenantId` is not offered on the login screen. Unbound `common` / `organizations` multi-tenant semantics are outside the product boundary.

Provider administration requires the existing `UsersManage` permission. Provider reads in administration require `UsersView`. Provider and assurance-policy mutations are written with Audit evidence in the same database transaction.

## External identity links

`ExternalIdentityLinks` maps an externally validated identity to exactly one existing local Depot user.

The stable identity tuple is:

```text
provider code + issuer + subject
```

`issuer` and `subject` are identity identifiers and are matched exactly. Depot persists the original values plus a SHA-256 key over the UTF-8 tuple separated by NUL delimiters. The hash provides one provider-neutral uniqueness key even when the underlying SQL provider uses a case-insensitive default collation.

A link may also retain optional observed tenant, email and display-name attributes plus `LinkedUtc` and `LastSeenUtc`. These observed values are diagnostic/identity evidence only. They do not overwrite the local Depot user's email/display name and are not authorization inputs.

Authentication-assurance claims are deliberately not stored on the link. `amr`, `acr`, `auth_time` and `azp` describe an individual authentication ceremony and are evaluated only at sign-in time.

A linked identity cannot be linked to a second local user. Link and unlink administration requires `UsersManage` and is audited transactionally. Depot does not auto-provision users and does not create identity links during sign-in.

## Interactive OIDC flow

Depot uses the OAuth 2.0 Authorization Code flow with OpenID Connect and PKCE for the native Windows client.

The runtime flow is:

1. read an enabled provider from Enterprise Identity configuration;
2. validate the persisted assurance-policy shape before protocol execution;
3. retrieve the provider's OpenID Connect discovery document over HTTPS;
4. generate cryptographically random `state`, `nonce` and PKCE verifier values;
5. open the system browser with `response_type=code`, `openid profile email` scopes and an S256 PKCE challenge;
6. when configured, request the exact `acr` through `acr_values` and freshness through `max_age`;
7. receive the authorization response on a dynamically allocated `http://localhost:<port>/` callback;
8. require the returned `state` to match the request in fixed time;
9. exchange the authorization code using the public client ID, exact redirect URI and PKCE verifier, without a client secret;
10. validate the ID token signature, issuer, audience, expiration/lifetime, advertised signing algorithm and `nonce`;
11. for Microsoft Entra ID, require the validated tenant claim to match the configured tenant boundary;
12. evaluate configured `amr`, `acr`, `auth_time` and `azp` assurance requirements fail-closed;
13. construct `ValidatedExternalIdentity` only after protocol and assurance checks succeed;
14. resolve that identity through `IEnterpriseIdentityResolver`;
15. start the normal Depot session and sign in using the local user's effective Depot permissions.

Discovery and signing keys are handled through the Microsoft IdentityModel OpenID Connect configuration manager. A signing-key miss triggers one controlled metadata refresh/retry so normal key rollover can recover without disabling signature validation.

## External authentication assurance

F4C deliberately treats external MFA as provider evidence rather than a locally implemented second factor.

A provider may require one exact `amr` value and/or one exact `acr` value. Depot does not assign universal meaning to arbitrary provider strings; the configured values are an explicit trust contract with that provider. Matching is ordinal and case-sensitive.

A provider may additionally set `MaximumAuthenticationAgeMinutes` from 1 through 1440. If configured, the token must contain a parseable `auth_time`, must not be materially in the future, and must fall within the configured maximum age. Depot requests the corresponding `max_age` value from the provider but still verifies the returned evidence itself.

`azp` is validated when present, and a token with multiple audiences is rejected when it does not provide an authorized-party value. An `azp` value that does not equal the configured Depot client ID is rejected.

Missing, ambiguous, malformed or mismatching required assurance evidence fails before identity resolution, link observation updates, session creation or local authorization. Failure evidence uses controlled boundary codes and does not store raw token contents.

For Microsoft Entra deployments, Conditional Access and Authentication Strength remain the primary tenant-side control for which authentication methods are allowed. Depot's assurance policy is an application-side, explicitly configured verification boundary; it does not attempt to replace tenant policy or infer a universal MFA level from an arbitrary claim string.

Depot does not implement TOTP, SMS codes, push approval, FIDO enrollment, recovery codes or MFA secrets in this package.

## Loopback callback boundary

The desktop callback listener binds only to the local loopback interface on an operating-system-assigned port. The redirect URI uses `localhost` and the callback handler validates the target host/port before accepting the response.

The callback reader is bounded and accepts only one GET response. Duplicate query parameters, malformed request lines, oversized request/header values and mismatched callback targets fail closed. The browser response contains no token or identity details and is returned with `Cache-Control: no-store`.

`access_denied` is treated as user cancellation. Other provider/protocol failures do not reveal raw token content or provider exception messages in the UI.

## Token and secret handling

Depot is a public/native client in this flow:

- no OIDC client secret is configured or sent;
- authorization codes exist only in memory long enough for token exchange;
- access tokens, refresh tokens and ID tokens are not persisted;
- `amr`, `acr`, `auth_time` and `azp` evidence is not persisted;
- protocol tokens are not written to Audit, Security Events, application logs or UI messages;
- external passwords are entered only at the provider's browser surface and are never visible to Depot.

## Resolution and authorization contract

`IEnterpriseIdentityResolver` accepts a `ValidatedExternalIdentity`. Resolution fails closed when:

- the configured provider does not exist or is disabled;
- a configured tenant boundary does not match;
- no exact provider/issuer/subject link exists;
- stored link identity material fails its SHA-256/integrity check;
- the linked local Depot user does not exist or is inactive.

The OIDC client reaches this resolver only after configured assurance requirements pass. On success, roles and effective permissions are reloaded from Depot's own `Roles`, `UserRoles`, `RolePermissions` and `Permissions` data. An identity-provider administrator therefore cannot obtain Depot permissions merely by adding an external group or role claim.

`RecordSuccessfulAuthenticationAsync` updates only observed link attributes and `LastSeenUtc`. The existing `SessionService`, concurrent-session policy and `AuthorizationService` are reused for enterprise sign-in.

## Security-event boundary

Protocol failures, assurance-policy failures, unavailable providers, unresolved identities and session-limit rejection create controlled authentication-failure evidence without storing token material or raw provider error descriptions. A successful enterprise sign-in uses the existing authentication-success Security Event path and normal session correlation identifiers.

User cancellation does not create a failed-authentication event.

## Provider support and migration

Enterprise Identity schema 2 remains provider-neutral and part of the database-provider acceptance boundary for:

- SQLite;
- SQL Server 2022 / engine 16.x;
- MariaDB 11.8 LTS;
- MySQL 8.4 LTS.

Schema 2 adds nullable provider-level `RequiredAmr`, `RequiredAcr` and `MaximumAuthenticationAgeMinutes` columns. Existing schema-1 provider/link identity data is preserved and migrated with null assurance requirements, so an existing provider does not silently acquire a new MFA requirement during upgrade.

## Privacy boundary

The feature stores the minimum stable mapping needed to associate a validated external account with a local Depot user plus the administrator-selected assurance policy. It does not store external passwords, protocol tokens, raw authentication-method history, source IP, geolocation, device fingerprints or typed/user-interaction telemetry.

Provider/issuer/subject identifiers and optional observed email/display name may still be personal or security-sensitive data. Existing Depot access control, backup, retention and Audit protections apply.

## Remaining work

F4C completes the planned Track C enterprise-identity authentication/assurance package. Deployment-specific Entra Conditional Access / Authentication Strength and any generic OIDC `amr`/`acr` value contract still require administrator configuration and acceptance with the actual identity provider.

The next Track C implementation package is F5A Security Event Export Contract.
