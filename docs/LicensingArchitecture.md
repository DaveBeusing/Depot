# Depot Licensing Architecture & Commercial Model

Status: Architecture baseline  
Scope: Depot, DepotManager, internal DepotLicenseManager

## 1. Purpose and principles

Depot uses an offline-capable, environment-based licensing model. Licensing is independent from RBAC:

```text
License
  -> Entitlements / Limits
  -> RBAC
  -> Business operation
```

A license determines what an environment is entitled to use. RBAC determines which user may use an entitled capability. Licensing must be enforced at relevant service/business boundaries, not only by hiding UI.

The model follows these non-negotiable principles:

- customer business data is never deleted, hidden, or made unreadable because of licensing;
- historical financial, audit, inventory, invoice, and compliance evidence remains readable, reportable, and exportable;
- missing or invalid commercial entitlement fails closed for new commercial use;
- access to existing customer data fails safe;
- Depot does not require permanent online activation or a central license server in V1;
- licenses bind to a Depot environment, never to hardware.

## 2. Editions

| Capability | Community | Business | Enterprise |
| --- | ---: | ---: | ---: |
| Commercial use | Yes | Yes | Yes |
| License file required | No | Yes | Yes |
| Named Users | 3 | 10 base | 100 base |
| User expansion | No | +10 packs, max 50 | Individually above 100 |
| Legal Entities | 1 | 5 | Unlimited |
| SQLite | Yes | Yes | Yes |
| SQL Server | No | Yes | Yes |
| MySQL/MariaDB | No | Yes | Yes |
| Production Environments | 1 | 1 | 1 |
| Non-Production Environments | 0 | 1 | 2 |
| ERP Core | Full | Full | Full |

Depot deliberately does not reserve normal ERP modules for Enterprise. Inventory, Warehouse, Purchasing, Sales, Finance, Reporting, and Administration remain part of the common ERP core. Editions differ primarily by scale, database providers, legal entities, and environment count.

Localization and compliance capabilities are separately licensable entitlements and are not Enterprise-only functionality.

## 3. Named User licensing

Depot uses active Named Users, not concurrent-user licensing.

- an active user account consumes one Named User;
- an inactive/deactivated user consumes none;
- multiple devices or sessions for the same user do not consume additional licenses.

Community allows 3 active Named Users. Business includes 10 and may be expanded in 10-user increments up to 50. More than 50 requires Enterprise. Enterprise includes 100; higher limits are individually licensed through a numeric `UserLimit`.

When the limit is reached, existing users remain operational. Creation or activation of additional active users is blocked. Administrators are warned at 80%, 90%, and 100% utilization.

## 4. Legal Entity licensing

Community allows 1 Legal Entity. Business allows 5. Enterprise is Unlimited.

Business cannot extend its Legal Entity limit through add-on packs; a sixth Legal Entity requires Enterprise.

The current usage is shown to administrators. When only one slot remains, Depot warns the administrator. Existing entities and their data are never disabled automatically.

## 5. Over-limit behavior

A later license revision may reduce a previously granted limit. Depot does not automatically deactivate users or Legal Entities.

Example:

```text
UserLimit = 20
ActiveUsers = 27
State = OVER LIMIT
```

Existing users and entities remain usable. Further activation or creation is blocked until usage is back within the licensed limit. Over-limit by itself does not place the whole environment into Restricted Mode.

## 6. DepotEnvironmentId

Every Depot environment receives a permanent `DepotEnvironmentId` at creation time, including Community environments.

The identifier survives:

- upgrades;
- repair;
- backup/restore;
- server migration;
- controlled database-provider migration;
- Community -> Business;
- Business -> Enterprise.

The environment identifier is a core environment identity, not merely a licensing artifact.

## 7. No hardware binding

Licenses must not bind to MAC address, CPU, motherboard, TPM, Windows installation identity, hostname, or an individual client device.

Commercial licenses bind to `DepotEnvironmentId`.

## 8. Environment Request

A commercial license is requested through DepotManager. DepotManager produces a non-secret request file such as:

```text
ExampleCompany.depot-request
```

The request contains at least:

```text
RequestFormatVersion
DepotEnvironmentId
EnvironmentType
CurrentDepotVersion
RequestedEdition
CustomerReference
GeneratedUtc
```

The internal DepotLicenseManager imports the request and issues the signed license.

## 9. Environment types and counts

Supported environment types include at least:

```text
Production
NonProduction
```

Business includes 1 Production and 1 Non-Production environment. Enterprise includes 1 Production and 2 Non-Production environments.

A Non-Production environment has the same Named User limit and entitled ERP functionality as its edition, but Depot permanently labels it `NON-PRODUCTION` in the application shell and license information.

Business documents are not automatically watermarked.

Because customer installations remain offline, environment-count enforcement occurs in the internal DepotLicenseManager at issuance time. The manager refuses issuance beyond the edition allowance by default. An authorized internal override requires an explicit reason and permanent history entry.

## 10. Backup, restore, disaster recovery, and cloning

Backup and restore preserve `DepotEnvironmentId`. A disaster-recovery copy is therefore the same licensed environment.

Two independent environments must not run permanently with the same environment identity. If a clone is intentionally converted into an independent environment, DepotManager must provide a controlled `Create Independent Environment` operation that generates a new `DepotEnvironmentId` and, when required, a new Environment Request.

## 11. License types

Depot supports three license types:

```text
Perpetual
Subscription
Trial
```

The signed format must support all three even if commercial offerings change over time.

## 12. Perpetual and Maintenance

A perpetual license permits the licensed product indefinitely. Update eligibility is controlled by `MaintenanceUntil`.

A release published on or before `MaintenanceUntil` remains permanently installable, reinstallable, and repairable. A later release requires renewed Maintenance.

Maintenance expiry never causes Grace Period, Restricted Mode, feature lock, or loss of access. Maintenance warnings are shown only to license administrators; DepotManager always exposes the Maintenance state.

## 13. Subscription

Subscription licenses contain `ValidFrom` and `ValidUntil`.

After expiry Depot enters a 14-day Grace Period. During Grace, the system remains fully functional and displays appropriate warnings. After Grace, Depot enters Restricted Mode.

## 14. Trial

Trials are manually issued, signed licenses. There is no self-service or automatic trial activation in V1.

A Trial may represent Business or Enterprise and can include selected localization/compliance entitlements. The default Trial duration is 14 days, with an individually configurable duration at issuance.

After Trial expiry, a 14-day Grace Period applies before Restricted Mode.

## 15. Restricted Mode

Restricted Mode preserves access to existing customer data while preventing new or altering operational business activity.

Restricted Mode allows at least:

```text
Read
Search
Reporting
Export
```

New or altering business transactions are blocked. Historical invoices, postings, warehouse movements, audit trails, compliance evidence, and reports remain accessible.

## 16. Community fallback

If no valid commercial Core entitlement exists, Depot evaluates whether the environment can operate as Community.

A compatible SQLite environment within Community limits falls back to Community. If the environment depends on SQL Server or MySQL/MariaDB, or otherwise cannot operate under Community terms, it falls back to Restricted Mode instead.

No automatic data deletion, truncation, or provider conversion occurs.

## 17. Localization and Compliance Packs

Localization and compliance capabilities are independently licensable environment entitlements. Examples include Germany Localization, XRechnung/ZUGFeRD, France, UK, or Canada-specific capabilities.

A pack is licensed per `DepotEnvironmentId`; all Legal Entities in that environment may use it when entitled.

Packs have an independent lifecycle from the Core license and may be perpetual or time-limited. A time-limited pack receives a 14-day Grace Period after expiry. After Grace, new pack-dependent operations are blocked while historical regulated data remains readable, reportable, exportable, and auditable.

## 18. License Bundle

The customer works with one license artifact, normally `DepotLicense.lic`, implemented as a bundle of independently lifecycle-capable entitlements.

Conceptually:

```text
Depot License Bundle
  Core License
    Business / Enterprise
  Localization Entitlement(s)
  Compliance Entitlement(s)
```

A pack can therefore be extended or replaced without forcing the Core entitlement to share its lifecycle.

## 19. Expiry warnings

Subscription, Trial, and time-limited entitlements use warning thresholds at:

```text
30 days
14 days
7 days
1 day
```

License administrators receive the detailed warnings. Normal users are not unnecessarily exposed to commercial administration messages.

## 20. License identity

Each license has:

- `LicenseId`: immutable GUID/UUID used as the technical identity;
- `LicenseNumber`: random human-readable support/customer reference, for example `DEP-7K4M-92PX-R8QF`.

`LicenseNumber` is generated with cryptographically strong randomness, checked for uniqueness, and does not encode customer, edition, year, or validity.

The customer-visible payload contains only `LicensedTo = Company Name` as customer identity. Address, email, phone, and contact information remain only in the internal license database.

A company rename produces a new revision of the same license. `LicenseId` and `LicenseNumber` remain unchanged. A true transfer to a different licensee requires explicit approval and a replacement license with a new identity.

## 21. License revisions and rollback protection

Every material modification creates a new immutable signed revision while preserving normal license identity.

For the same `LicenseId`, Depot accepts only a revision equal to or greater than the highest revision already accepted. Once Revision 5 has been accepted, Revision 1-4 are rejected even if their signatures remain valid.

The highest known revision is stored in both central environment state and a protected local state. Disaster-recovery exceptions are resolved by issuing a higher revision where necessary.

A lost `.lic` can be re-exported or reissued unchanged without creating a new identity or revision.

## 22. License format and cryptography

Depot uses a versioned signed envelope:

```text
DepotLicenseEnvelope
  FormatVersion
  KeyId
  Algorithm
  Payload
  Signature
```

The payload is deterministically/canonically serialized before signing and verification.

V1 uses:

```text
ECDSA P-256
SHA-256
```

The envelope remains algorithm-aware through `Algorithm` and `KeyId` to support key rotation and future cryptographic migration.

The private signing key is never present in Depot, DepotManager, customer installations, or the public repository. V1 stores the private key in Windows protected key storage / Windows Certificate Store with signing access limited to explicitly authorized Windows users.

Customer-side applications contain only trusted public verification keys.

An invalid or manipulated signature grants zero commercial entitlement and receives no Grace Period. Depot then falls back to Community if compatible, otherwise Restricted Mode.

## 23. Offline revocation model

V1 has no mandatory online revocation check. The internal DepotLicenseManager may mark a license `Active`, `Replaced`, or `Revoked`, but a fully offline customer installation cannot be remotely disabled after issuance.

The data model and envelope should remain extensible for a future signed revocation mechanism.

## 24. Offline clock rollback protection

Offline Subscription and Trial enforcement must not rely only on the current system clock.

At minimum Depot evaluates:

```text
SystemUtc
LocalLastKnownUtc
EnvironmentLastKnownUtc
```

Already-known forward time must not be trivially rolled back through system-clock changes, restoration of an older local state, or restoration of an older database state.

Clock anomalies must be handled conservatively. A clock problem must never make existing customer data unreadable.

## 25. Central license state and local cache

The Depot environment is the Source of Truth for the active license state. A license is imported once per environment and becomes available to clients that connect to that environment.

Clients additionally maintain protected local state required for revision rollback and clock rollback protection, including at least the environment identity, highest known revision, and relevant time state.

## 26. Runtime license changes

Positive changes such as renewal, Business -> Enterprise, increased limits, or a newly added pack may become effective automatically in running clients.

Restrictive changes must not terminate an in-progress business transaction abruptly. Restriction becomes effective at controlled service/transaction boundaries.

## 27. RBAC

At minimum the existing RBAC model gains:

```text
Administration.ViewLicense
Administration.ManageLicense
```

Administrators receive them by default, while the existing RBAC system may delegate them. Cryptographic license verification itself remains independent of RBAC.

All users may see basic product information such as edition, `LicensedTo`, environment type, and high-level license state. Detailed identifiers, limits, validity, Maintenance, and entitlement data require `Administration.ViewLicense`.

## 28. DepotManager responsibilities

Customer-side license administration belongs primarily to `DepotManager.exe`, including:

```text
Generate Environment Request
Import License
Replace License
Export License
Remove License
View License
View Environment Identity
View Entitlements
View Maintenance Status
```

Complete license removal is a DepotManager maintenance operation, requires authorization and explicit confirmation, and is audited.

Exporting the currently installed signed license does not change its identity, revision, or validity.

After license removal Depot re-evaluates Community compatibility. Compatible environments become Community; incompatible commercial environments become Restricted Mode. Data remains intact.

## 29. Community to commercial upgrade

License upgrade and database migration are separate workflows.

A Community environment generates a request using its existing `DepotEnvironmentId`, receives the commercial license, and keeps the same environment identity.

A later SQLite -> SQL Server or SQLite -> MySQL/MariaDB migration is a separate controlled DepotManager operation and must not be implicitly triggered by license import.

## 30. Audit

Depot audits meaningful license events, not every validation check.

Examples include:

```text
License imported
License replaced
License removed
License exported
Edition changed
User limit changed
Legal Entity limit changed
Grace entered
License expired
Restricted Mode activated
Compliance Pack added
Compliance Pack expired
Agreement accepted
```

Routine startup or service entitlement evaluation does not generate audit noise.

## 31. Internal DepotLicenseManager

The internal `DepotLicenseManager` is not a customer product and is not distributed publicly. It is responsible for request import, customer/environment records, license issuance, Trial issuance, revisions, renewals, replacements, entitlement packs, environment-count enforcement, signing, and history.

V1 uses its own local issuance database containing at least:

```text
LicenseId
LicenseNumber
Customer
LicensedTo
Edition
EnvironmentId
EnvironmentType
UserLimit
LegalEntityLimit
LicenseType
ValidFrom
ValidUntil
MaintenanceUntil
Entitlements
CompliancePacks
Revision
IssuedUtc
Status
ReplacedBy
PreviousLicense
Notes
History
```

Environment issuance limits are enforced here. An override must be explicit, authorized, reasoned, and historized.

## 32. Source-available commercial model

Future Depot versions move from the existing permissive model to a source-available/proprietary commercial license model.

The goals are:

- source code may remain viewable;
- Community remains free and commercially usable;
- commercial limits cannot simply be legally bypassed by removing enforcement code;
- unauthorized redistribution of commercial variants may be restricted contractually.

Versions already released under MIT retain their existing MIT rights. The license change applies only to future versions released under the new terms.

The concrete software license/EULA requires a separate legal work package and legal review before commercial release.

## 33. Software License Agreement acceptance

A new Depot environment accepts the applicable Software License Agreement once at environment level through an authorized administrator.

Depot records at least:

```text
AgreementVersion
AcceptedUtc
AcceptedBy
```

A materially changed agreement version may require renewed acceptance.

## 34. Effective license state

The implementation must avoid scattering unrelated booleans such as `IsLicensed`, `IsExpired`, or `IsTrial` through the application.

A central effective license model should represent Core state such as:

```text
Community
Active
Grace
Restricted
Invalid
OverLimit
```

Independent dimensions remain separate, including:

```text
Edition
LicenseType
EnvironmentType
MaintenanceState
Entitlements
Limits
```

An environment may therefore legitimately have a combination such as:

```text
Core State = Active
Edition = Business
Maintenance = Expired
User Capacity = OverLimit
Germany Pack = Active
XRechnung Pack = Grace
```

## 35. Enforcement matrix

| Operation | Community | Active Commercial | Grace | Restricted | Over Limit |
| --- | --- | --- | --- | --- | --- |
| Login existing user | Yes | Yes | Yes | Yes | Yes |
| Read/search existing data | Yes | Yes | Yes | Yes | Yes |
| Reporting/export | Yes | Yes | Yes | Yes | Yes |
| Create business transaction | Yes | Yes | Yes | No | Yes |
| Modify operational business data | Yes | Yes | Yes | No | Yes |
| Activate user within limit | Yes | Yes | Yes | No | Yes |
| Activate user above limit | No | No | No | No | No |
| Create Legal Entity within limit | Yes | Yes | Yes | No | Yes |
| Create Legal Entity above limit | No | No | No | No | No |
| Use SQL Server | No | Yes | Yes | Existing-data access only | Entitlement-dependent |
| Use MySQL/MariaDB | No | Yes | Yes | Existing-data access only | Entitlement-dependent |
| Use licensed compliance/localization operation | Entitlement-dependent | Yes | Yes | Historical access only | Yes |
| Install entitled update | Community rules | License/Maintenance rules | License rules | No | Yes |

Exact service-level enforcement points must be defined during implementation design.

## 36. V1 exclusions

V1 does not require:

```text
Permanent License Server
Online Activation
Hardware Fingerprinting
Remote Kill Switch
Automatic Revocation Service
Concurrent User Licensing
Self-Service Trial
Customer Licensing Portal
HSM
Cloud Key Vault
```

The architecture should not prevent these from being added later where appropriate.

## 37. Trust boundary

```text
INTERNAL TRUST ZONE
DepotLicenseManager
  Private Signing Key
      |
      | sign
      v
DepotLicense.lic
-----------------------------
CUSTOMER ZONE
DepotManager / Depot.exe
  Trusted Public Key(s)
      |
      | verify
      v
Effective Entitlements
```

Only the internal issuer may access the private signing key.

## 38. Commercial positioning

The licensing model is designed around customer scale rather than artificial feature removal:

```text
Community
  small business / entry
      -> growth
Business
  professional multi-user / server deployment
      -> scale
Enterprise
  large organization / multi-entity / larger deployments
```

Localization and compliance are independent commercial entitlements layered on top of the shared ERP core.

## 39. Implementation boundary

The intended technical responsibility chain is:

```text
License Envelope / Crypto
  -> License Validation
  -> Effective License State
  -> Entitlement Evaluation
  -> Limit Evaluation
  -> Application Services
  -> RBAC
  -> Business Operation
```

`DepotManager.exe` owns customer-side environment and license administration. `Depot.exe` consumes effective entitlement state and enforces it at relevant application-service boundaries. The internal `DepotLicenseManager` is the sole issuer and the only Depot component with signing-key access.

This document is the architecture baseline for the licensing implementation. Detailed schema, API, service, migration, cryptographic serialization, UI, and test design must be derived from this baseline rather than redefining the commercial model ad hoc during implementation.
