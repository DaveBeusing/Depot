# Depot Manager Packaged E2E Acceptance

## Purpose

The packaged E2E acceptance layer proves lifecycle behavior at the shipped Windows artifact boundary. It is deliberately separate from unit, integration, coverage, performance, accessibility, quality, and security suites.

The authoritative test inputs are Release / `win-x64` / self-contained / single-file publish outputs produced by `scripts/publish-packaged-artifacts.ps1`:

```text
Depot.exe
DepotManager.exe
```

The same publish routine is used by release-integrity CI. Packaged tests do not substitute DLL-only builds, testhost executables, fake PE files, or developer install layouts for the product artifacts.

## Execution boundary

Packaged E2E tests are Windows-only and opt-in. They are skipped unless:

```text
DEPOT_PACKAGED_E2E=1
```

CI supplies isolated current/previous artifact roots and a disposable scenario root. Tests never intentionally target production installations or production databases. File/data lifecycle tests operate only below the supplied temporary root. Windows integration mutation is excluded from the test process; application-file removal is exercised independently from registry/shortcut removal.

Pull requests run the Smoke tier. Pushes to `master` and manual workflow runs execute the Full tier. Failure artifacts and TRX results are retained by CI.

## Artifact authority and signing

The current artifacts are published from the exact workflow checkout. Previous artifacts are published from the immediate base/parent commit through the same publish script. File versions therefore prove the real version transition rather than a fake binary substitute.

Packaged E2E uses a short-lived code-signing certificate created only on the disposable Windows runner. The public certificate is trusted only in that runner's current-user trust stores and is removed after the job. The real `AuthenticodeVerifier` / `WinVerifyTrust` path remains active. Production code contains no unsigned-test bypass.

**Production Authenticode Acceptance: BLOCKED until a production signing identity is available to the tagged release workflow.** The E2E certificate proves the verification mechanics, not the production publisher identity.

## Smoke acceptance

Smoke verifies:

1. current and previous `Depot.exe` / `DepotManager.exe` are real PE files and each publish directory contains exactly one shipped executable;
2. clean local SQLite installation using the previous packaged Depot artifact;
3. real headless administrator provisioning through `Depot.exe --manager-provision`;
4. existing-install version discovery from deployed PE metadata;
5. normal binary update without schema change, rollback backup creation, and post-update `--manager-health-check`;
6. repair of a damaged installed `Depot.exe` while preserving protected settings and administrator data;
7. rejection of corrupt PE input, wrong executable version, wrong published size, wrong SHA-256, missing release manifest, and invalid manifest schema metadata;
8. trusted Authenticode validation for the packaged manager and rejection of a tampered signed manager.

## Full acceptance

Full includes Smoke plus:

1. a supported historical SQLite state at schema 29;
2. SQLite safety backup before migration, with the backup remaining schema 29;
3. real `Depot.exe --manager-migrate` advancement from schema 29 to schema 30;
4. repeated migration/health execution to prove idempotency;
5. fail-closed backup gates for a missing SQLite backup, unconfirmed remote backup, and target-schema downgrade;
6. cancelled SQLite backup without a newly-created backup artifact;
7. persisted executable rollback metadata using real previous packaged `Depot.exe`;
8. exact schema equality for binary rollback and explicit proof that rollback does not downgrade the database;
9. locked-target replacement failure preserving the original binary and removing the `.new` staging file;
10. signed real `DepotManager.exe` self-update through the staged helper/readiness-marker flow;
11. forced updated-manager startup failure before readiness, followed by automatic restoration of the previous signed manager;
12. keep-data and delete-local-data uninstall scopes using the same application-file deletion and local-data deletion production functions without touching Windows integration;
13. repeated removal for idempotency;
14. isolated diagnostics/support-package generation with credential-bearing log lines redacted.

The self-update failure hook is active only when `DEPOT_PACKAGED_E2E=1` is explicitly inherited by the packaged E2E process and only affects `--manager-update-verification`. It does not skip PE, version, hash, or Authenticode verification.

## Compatibility invariants

The acceptance layer must preserve these production rules:

```text
DatabaseVersion.CurrentVersion = 30
rollback schema == current schema
no automatic database downgrade
no target schema older than current schema
migration backup required before schema advancement
manager self-update signature verification remains mandatory
```

Schema 29 -> 30 is used because it is a real supported historical transition in Depot's migration pipeline and does not require inventing a test-only schema version.

## CI evidence

The dedicated workflow performs, on the exact commit under test:

```text
dotnet restore Depot.slnx --locked-mode -p:AuditPipeline=true
dotnet build Depot.slnx --configuration Release --no-restore -warnaserror -p:AuditPipeline=true
dotnet test tests/DepotManager.Tests/DepotManager.Tests.csproj --configuration Release --no-build
Release/win-x64/self-contained/single-file publish for current and previous source
Packaged E2E Smoke or Full execution
```

Existing CI, quality, coverage, performance, accessibility, and security workflows remain separate and continue to provide their prior evidence.
