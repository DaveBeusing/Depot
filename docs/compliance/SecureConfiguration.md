# Depot Secure Configuration

## Protected settings

Depot stores `depot.settings` under the current Windows user's LocalAppData profile. The settings payload is protected with Windows DPAPI using `DataProtectionScope.CurrentUser`. Database passwords therefore are not stored as readable plaintext and cannot normally be decrypted by another Windows account. Moving the encrypted settings file to another Windows identity is not a supported configuration-transfer mechanism.

## Remote database transport

Depot requires encrypted transport for remote database providers:

- SQL Server: `Encrypt=true` is required. Certificate validation remains enabled by default (`TrustServerCertificate=false`). Trusting a server certificate should be limited to controlled environments using an explicitly accepted certificate strategy.
- MySQL/MariaDB: TLS is required (`SslMode=Required` or stronger provider behavior).
- SQLite is local and does not use network transport.

The settings validation layer rejects disabling SQL Server encryption or MySQL/MariaDB TLS.

## Database least privilege

Production database identities should be dedicated to Depot and restricted to the target Depot database/schema. They should not receive server-administrator, operating-system, cross-database, database-creation, or unrelated schema privileges unless a documented deployment operation specifically requires them. Schema migration privileges should be separated from steady-state runtime privileges when an enterprise deployment supports that split.

## Certificates

Prefer certificates chaining to a trusted enterprise/public CA. `TrustServerCertificate=true` weakens server-identity validation and is not the production default. Do not store private keys or certificate passwords in repository files or application logs.

## Secret redaction

Audit presentation redacts password, credential, connection string, token, API-key, private-key, encryption-key, and related sensitive fields. New integrations must add their secret field names to the sanitizer or otherwise guarantee that secrets cannot enter audit/log payloads.

## Windows account model

Because protected settings are scoped to `CurrentUser`, scheduled tasks, service accounts, shared workstation accounts, and administrator elevation under a different identity must be treated as distinct deployments. A future machine-wide/service deployment requires an explicit secret-store design rather than changing the DPAPI scope implicitly.
