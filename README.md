# Depot

Depot is a Windows desktop application for inventory, warehouse, procurement, sales, administration, reporting, and operational workflows. It is built with .NET 10, WPF, MVVM, and a provider-neutral ADO.NET persistence layer.

The project is under active development on the **0.14.x-preview** line and is not yet production-certified. Security/compliance roadmap phases 1-7 have their technically implementable repository/application controls in place; production, legal, provider, signing, accessibility, and environment acceptance gates remain where documented.

## Highlights

- Inventory, warehouse, purchasing, sales, approvals, reporting, and administration workspaces
- SQLite plus SQL Server and MySQL/MariaDB provider implementations
- database-backed multi-role RBAC with service-layer authorization
- first-run administrator bootstrap with no shared production default password
- password policy, login throttling, versioned PBKDF2-HMAC-SHA256 password hashing
- DPAPI-protected persisted database credentials and mandatory encrypted transport for supported remote-provider settings
- immutable/correction-oriented business-record workflows and structured audit evidence
- administrator Audit Log, evidence export, and Privacy Data discovery/export
- backup validation, restore, automatic backup retention, integrity checks, and SQLite compaction
- CycloneDX SBOM, NuGet vulnerability audit, dependency lock verification, CRA evidence generation, and release-integrity workflows
- Administration > Company as the legal seller identity used by generated business documents
- immutable seller and buyer invoice identity with persisted issued XRechnung XML and SHA-256 integrity verification
- EN 16931-oriented electronic invoice model with XRechnung CII generation and pinned KoSIT conformance validation
- ISO/IEC-25010-inspired software-quality gates and automated accessibility baselines

## Application shell

Depot uses a dark workspace-oriented shell with permission-aware activity-bar navigation and closeable workspace tabs. After sign-in, no module or tab is selected automatically: a tabless Welcome page greets the user and exposes navigation shortcuts. Closing the final tab returns to Welcome.

Navigation supports stable routes, `Alt+Left` / `Alt+Right` history, `Ctrl+P` Quick Open, `Ctrl+Shift+P` Command Palette, keyed document tabs for supported records, `Ctrl+W`, `Ctrl+Tab` / `Ctrl+Shift+Tab`, and F1 context Help.

## Workspaces

```text
Dashboard
Inventory
  Overview | Items | Movements
Warehouse
  Transfers | Inventory Counts | Material Issues | Material Returns | Shipping
Purchasing
  Purchase Orders | Goods Receipts | Supplier Returns
Sales
  Overview | Quotes | Pricing | Customers | Sales Orders | Invoices
Approvals
  Purchase Approvals | Sales Approvals
Reports
Administration
```

Administration includes Company master data, users/roles, database configuration, backup/restore, Audit Log, Privacy Data, About/application information, Notification Center, and the offline Help Center.

## Business-record integrity

Depot treats finalized operational records as historical evidence where the workflow requires it. Corrections use explicit reversal, return, cancellation, close, or credit-note transactions rather than silently rewriting finalized history. Audit evidence preserves actor, UTC timestamp, state transitions, and sanitized before/after data for reviewed workflows.

The Audit Log can also produce a structured JSON evidence export for classified business records.

## Privacy

**Administration > Privacy Data** provides an authorized discovery workflow for person-related data and a machine-readable JSON export. Authentication hashes, connection credentials, and protected settings are excluded by design.

Depot deliberately does not provide a universal destructive “GDPR delete” action: deletion, deactivation, anonymization, archival, and retention depend on record type and the operator's legal obligations.

## Electronic invoicing

Depot includes an EN 16931-oriented semantic electronic-invoice model and deterministic UN/CEFACT CII generation targeted at XRechnung 3.0. Representative XML is validated in CI with a pinned KoSIT XRechnung validator/configuration.

Sales-invoice posting is the immutable invoice-identity boundary. In the same transaction Depot captures the historical seller profile, freezes the relevant buyer identity and structured billing/tax data, generates the XRechnung-oriented CII XML, and stores the exact issued XML with a SHA-256 fingerprint. Later customer or company master-data changes therefore cannot change the finalized structured invoice. The Invoice workspace can export the verified persisted XML for posted invoices; it is not regenerated from current master data.

Posting fails closed when mandatory seller/buyer data is incomplete or when the invoice uses a tax scenario that the persisted commercial model cannot represent unambiguously. The current finalization path accepts positive standard-VAT sales-invoice lines. Zero-rated, exempt, and reverse-charge scenarios remain blocked until explicit EN 16931 tax category and exemption/reason semantics are stored on the commercial document. Electronic credit-note buyer/XML finalization remains separate follow-up work.

Runtime posting performs Depot application-level validation and does not invoke the external KoSIT executable. Production deployment still requires organization/recipient-specific routing configuration and validation of every advertised tax/profile/channel scenario against the applicable production XRechnung/KoSIT release. ZUGFeRD/Factur-X is not claimed until a true PDF/A-3 pipeline is implemented and validated.

## Database providers

SQLite is the default provider. Microsoft SQL Server and MySQL/MariaDB implementations are also present. Supported remote-provider settings enforce encrypted transport. Live-server migration, backup/restore, recovery, concurrency, and version-matrix acceptance remain required before a server configuration is advertised as production-supported.

The core database schema is currently **29**. Sales uses the versioned `DepotFeatureVersions` registry; Sales invoice finalization is schema version **8**. Application release versions and database schema versions are independent.

## Offline Help Center

Depot ships an embedded Markdown Help Center rendered natively in WPF. It is permission-filtered, locally searchable, uses stable topic links, and opens as a workspace tab. F1 resolves the current Help context.

Help manifest **1.6** documents first-run administrator creation, current workspace/navigation behavior, hardened database/backup guidance, Audit Log evidence export, Privacy Data, Company identity, and electronic-invoice finalization/export. See `docs/HELP_CENTER.md` for authoring rules.

## Architecture

```text
Views → ViewModels → Services → Repositories → DatabaseAccess
                                      ↓
                    SQLite / SQL Server / MySQL or MariaDB
```

Views contain layout/bindings, ViewModels presentation state/commands, Services business rules and transactions, and Repositories persistence SQL/mapping. Shared UI resources live under `src/Depot/Resources`; reusable WPF controls under `src/Depot/Controls`.

## Technology

- .NET 10 for Windows
- WPF and MVVM
- SQLite via `Microsoft.Data.Sqlite`
- SQL Server via `Microsoft.Data.SqlClient`
- MySQL/MariaDB via `MySqlConnector`
- ClosedXML for Excel import/export
- PDFsharp-WPF for Sales and fulfillment documents
- Nullable reference types enabled

## Getting started

Requirements: Windows 10/11 and the .NET 10 SDK.

```powershell
git clone https://github.com/DaveBeusing/Depot.git
cd Depot
dotnet restore Depot.slnx --locked-mode
dotnet run --project src/Depot/Depot.csproj -c Debug
```

A new installation defaults to local SQLite and creates `depot.db`; protected settings are stored in `depot.settings`. **Administration > Database** configures SQLite, SQL Server, or MySQL/MariaDB. **Administration > Company** configures the legal seller/document identity used by business documents and electronic-invoice finalization.

### First run

Depot no longer uses a shared default administrator login. If the selected database has no usable application user, Depot starts the administrator-bootstrap workflow and requires creation of the initial administrator with an individual email/login and a password that satisfies the current password policy. Existing configured databases proceed to normal sign-in.

## Build and publish

```powershell
dotnet build Depot.slnx -c Debug
dotnet build Depot.slnx -c Release -warnaserror
```

Self-contained single-file publish:

```powershell
dotnet restore src/Depot/Depot.csproj -r win-x64
dotnet publish src/Depot/Depot.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=None -p:DebugSymbols=false
```

Runtime data (`depot.db`, `depot.settings`, logs, backups, PDFs, XML exports, evidence exports) remains external. Do not enable `PublishTrimmed` without dedicated WPF/XAML trimming validation.

## CI and assurance

The repository currently includes:

- normal bounded regression CI
- Security supply-chain workflow with NuGet audit, dependency locks, CycloneDX SBOM/license evidence, security/privacy/integrity tests, and CRA evidence packaging
- Release-integrity workflow with source binding, SHA-256 manifests, and prepared Authenticode/timestamp support
- Electronic-invoice conformance workflow using pinned KoSIT validation
- Software-quality gates on Windows Server 2022 and 2025, including zero-warning build, regression suite, 100,000-record performance baseline, and static accessibility checks

Production Authenticode signing requires the real protected signing identity and remains a release acceptance gate.

## Keyboard navigation

| Shortcut | Action |
| --- | --- |
| `Ctrl+P` | Quick Open |
| `Ctrl+Shift+P` | Command Palette |
| `Ctrl+W` | Close active tab |
| `Ctrl+Tab` | Next tab |
| `Ctrl+Shift+Tab` | Previous tab |
| `Alt+Left` | Navigate backward |
| `Alt+Right` | Navigate forward |
| `F1` | Context-sensitive Help |

## Project structure

```text
src/Depot/
  Controls/       Reusable WPF controls
  Data/           Provider factories, initialization, migrations
  Help/           Embedded offline Help Center
  Models/         Domain, status, report and e-invoice models
  Repositories/   Provider-neutral persistence
  Resources/      Design system and branding
  Services/       Business/application workflows
  ViewModels/     Presentation logic and commands
  Views/          WPF views and windows
tests/Depot.Tests/
  Unit, SQLite integration, security, quality and conformance tests
scripts/
  e-invoice, quality and security/compliance automation
```

## Remaining work before 1.0

Major remaining acceptance work is environment- or production-specific rather than missing generic foundations:

- live SQL Server/MySQL/MariaDB migration, recovery, concurrency, performance, and supported-version matrices
- Windows ACL-denied recovery test
- production code-signing certificate and timestamp validation
- interactive keyboard/focus, Narrator/Accessibility Insights, and 100/125/150/200% DPI acceptance
- representative production sizing/load tests
- explicit EN 16931 tax-category/exemption semantics for zero-rated, exempt, and reverse-charge invoice scenarios
- buyer/XRechnung finalization for electronic credit notes
- production recipient/channel routing and full advertised-scenario validation against the applicable KoSIT/XRechnung release
- PDF/A-3 implementation before any ZUGFeRD/Factur-X claim
- operator/legal acceptance for GDPR, GoBD, CRA classification/conformity, retention periods, and organization-specific procedures
- installer/package, upgrade, rollback, and uninstall acceptance

Barcode scanning/generation, label design/printing, payment collection, accounts receivable, and general-ledger functionality remain outside current scope.

## Documentation

- `docs/Architecture.md`
- `docs/CodingStandard.md`
- `docs/Roadmap.md`
- `docs/RELEASE_1_0.md`
- `docs/SECURITY_ROADMAP.md`
- `docs/HELP_CENTER.md`
- `docs/compliance/`

## License

Depot is licensed under the MIT License.
