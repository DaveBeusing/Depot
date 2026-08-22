# Depot Cryptography Standard

## Principles

- Do not design custom cryptographic algorithms or protocols.
- Prefer platform/.NET cryptographic APIs and established authenticated-encryption primitives.
- Passwords must be hashed, never reversibly encrypted for authentication.
- Keys and secrets must not be committed to source control.
- Cryptographic parameters must be reviewable and versioned so they can be upgraded over time.
- Random security tokens/keys must use a cryptographically secure random-number generator.
- Remote database transport should use TLS where supported and required by deployment policy.

## Password hashing

Depot currently uses PBKDF2-SHA256. Parameters must be reviewed before production release against current platform/security guidance. Stored password records should carry enough metadata to permit future parameter upgrades without forced plaintext recovery.

## Secrets at rest

Database credentials and future private keys/tokens require OS-backed or otherwise appropriately protected storage. A production design must define Windows user/machine scope, backup implications, migration behavior, and administrator recovery behavior.

## Backups

If backup encryption is introduced, use authenticated encryption and define key lifecycle/recovery separately from the backup payload.

## Release signing

Production signing keys must be protected outside the repository and normal build workspace. Signing must be timestamped and verifiable by the release process.
