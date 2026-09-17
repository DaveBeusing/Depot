# Track C acceptance and product status

Reviewed: 2026-09-17

Track C repository implementation is complete through F5B. This document records the implemented product boundary and repository acceptance evidence; it does not replace deployment-specific legal, accounting, accessibility, signing or operational acceptance.

## Overall status

- F1: merged and integrated.
- F2 XRechnung completion/conformance: merged; bounded advertised XML matrix has successful independent repository conformance evidence.
- F3A/F3B ZUGFeRD/Factur-X: merged; bounded advertised hybrid matrix has successful independent PDF/A-3B repository conformance evidence.
- F4A/F4B/F4C Enterprise Identity: merged and acceptance stabilization completed.
- F5A Security Event export foundation: merged.
- F5B Security Event delivery/checkpointing: merged; Security Events feature schema is **3**.
- AP-01 final acceptance repair: merged; the transient DepotManager packaged executable replacement lock is hardened with bounded retry while persistent locks remain fail-closed.

## F2 XRechnung completion and conformance

PR #36 introduced the bounded production path. PRs #37 and #38 repaired provider/migration and packaged-E2E regressions. PR #39 added the independent conformance closure and PR #47 repaired the deterministic exemption/reverse-charge acceptance regressions exposed later in the track.

The bounded XRechnung 3.0 CII issuance matrix covers:

- Standard-rated (`S`) invoices;
- Zero-rated (`Z`) invoices;
- Exempt (`E`) invoices;
- Reverse-charge (`AE`) invoices;
- Standard-rated Sales Credit Note (`381`).

The production generator and retained fixtures are bound to the same issuance rules. The independent electronic-invoice conformance workflow on PR #49 completed successfully for the bounded advertised matrix. This closes the repository KoSIT/conformance boundary for that scope; it is not a jurisdiction-wide tax/legal certification and does not imply support for unimplemented special-tax or channel scenarios.

## F3 ZUGFeRD / Factur-X

The product decision includes ZUGFeRD `2.5.2` / Factur-X `1.09.2` using the German `XRECHNUNG` reference profile.

PR #41 introduced Sales feature schema **14** and the provider-neutral hybrid-artifact store. Depot creates new PDF/A-3B hybrid documents from the immutable finalized XRechnung model, embeds the exact finalized payload as `xrechnung.xml`, writes the Factur-X/XRECHNUNG XMP contract and retains exact PDF bytes plus PDF/XML SHA-256 evidence. Invoice and Sales Credit Note use the same artifact boundary.

PR #42 added the independent PDF/A acceptance path with pinned veraPDF `1.30.2`. PR #49 repaired the final trailer `/ID` and embedded MIME-name defects exposed by veraPDF. The exact PR #49 head then completed the independent electronic-invoice conformance workflow successfully, including the five-document PDF/A-3B matrix.

Repository acceptance therefore covers the bounded advertised hybrid matrix. Arbitrary existing-PDF conversion, other Factur-X profiles and unsupported tax/channel scenarios are outside this scope unless separately implemented and accepted.

## F4 Enterprise Identity and authentication assurance

PR #43 introduced the external-identity foundation. PR #44 added Authorization Code + PKCE, system-browser sign-in, loopback callback handling, discovery/signing-key validation, issuer/audience/lifetime/nonce validation and tenant-bound Microsoft Entra ID support. PR #46 advanced Enterprise Identity feature schema to **2** with provider-bound authentication-assurance requirements.

The current boundary supports optional exact provider requirements for `amr`, `acr` and maximum `auth_time` age, request hints through `acr_values` / `max_age`, and `azp` validation. Assurance is validated before local identity resolution/session creation. Raw protocol tokens and assurance claims are not persisted on identity links.

Local Depot RBAC remains authoritative. External roles, groups and permission claims are not authorization inputs. Depot does not maintain its own TOTP/MFA secret store in this track; Entra Conditional Access and Authentication Strength remain tenant-side controls.

PR #47 is merged and closes the deterministic F4C acceptance regressions that had been tracked in the earlier status document.

## F5A Security Event export foundation

PR #48 is merged. F5A established the immutable source-side export contract while Security Events feature schema remained 2:

- immutable export projection from existing Security Event evidence;
- minimum-severity plus optional event-type filtering;
- canonical SHA-256 filter fingerprint;
- filter-bound checkpoint semantics based on monotonic Security Event ID;
- captured snapshot upper bound before bounded reads;
- deterministic ascending batches with a maximum of 500 events;
- provider-neutral source and sink boundaries;
- SQLite, SQL Server, MariaDB and MySQL provider-smoke coverage;
- source `SecurityEvents` rows remain immutable with respect to export state.

F5A deliberately did not claim reliable delivery or persist sink/retry/checkpoint state.

## F5B Security Event delivery and checkpointing

PR #50 is merged and advances Security Events feature schema from **2** to **3**.

F5B adds:

- persisted export targets separate from source `SecurityEvents`;
- durable checkpoint and in-flight fixed-snapshot state;
- deterministic retry/backoff and suspension evidence;
- short worker leases for delivery ownership;
- checkpoint advancement only after sink success;
- fixed-snapshot at-least-once retry semantics;
- `http-json-v1` HTTPS delivery with deterministic `X-Depot-Delivery-Id`;
- no persistence of endpoint credentials, tokens or response bodies;
- retention protection for events not yet consumed by enabled targets;
- provider persistence coverage for SQLite, SQL Server, MariaDB and MySQL.

Delivery is explicitly **at-least-once**. A crash after remote acceptance but before the local checkpoint commit can duplicate a batch; the deterministic delivery ID is the receiver deduplication boundary. This is intentional and must not be documented as exactly-once delivery.

See [Security Event Export](SecurityEventExport.md).

## Final repository acceptance

AP-01 revalidated the final Track C closure path after F5B. The first repair candidate exposed a pre-existing canonical-documentation schema drift and a transient Windows executable replacement lock. PR #51 is merged with both corrections.

On the final PR #51 head, the defined merge gates completed successfully:

- CI;
- Software quality gates;
- Security supply chain;
- Database Provider Acceptance;
- DepotManager packaged E2E.

The executable replacement path now retries only bounded `IOException` / `UnauthorizedAccessException` failures caused by short-lived Windows locks. Existing persistent-lock behavior remains fail-closed, preserves the original binary and cleans the staged replacement.

## Track C closure

Track C F1 through F5B are complete at the repository implementation/acceptance boundary. There is no remaining Track C implementation package queued after F5B.

The next engineering step is Depot 1.0 technical gap reconciliation against the actual repository, tests and retained evidence. External/manual production gates remain tracked separately in [Release 1.0](Release1.0.md), [Current Project Status](CurrentStatus.md) and Track A acceptance documentation.
