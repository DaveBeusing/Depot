# Documentation status

Updated: 2026-08-28

This document identifies the documentation baseline for the current development state. User-facing and engineering documentation must distinguish implemented technical controls from production/legal acceptance gates and must describe finalized financial records from persisted historical evidence rather than mutable current master data.

## Current baseline

- Application: `0.15.40-preview`
- Help manifest: `1.16`
- Core database schema: `29`
- Sales feature schema: `8`
- Finance feature schema: `9`
- Finance F0-F7: complete

## F7 synchronization

The F7 documentation baseline synchronizes README, Roadmap, Current Status, Finance Architecture, Finance Compliance, Finance Localization, User-facing Changes, Help Center, embedded `finance.localization` Help and the Help manifest.

Help manifest **1.16** adds stable topic `finance.localization`, guarded by `FinanceLocalization.View`.

## F7 documentation invariants

Documentation must state that:

- F1 remains the authoritative immutable General Ledger;
- F7 does not post journals or maintain a parallel ledger;
- `LegalEntity.CountryCode` does not automatically activate localization;
- effective localization requires an explicit effective-dated assignment;
- the built-in Germany reference hierarchy resolves `GENERIC → EU → DE`;
- country packs are validated against the legal-entity country;
- active assignments for one legal entity may not overlap;
- built-in pack and registry definitions are immutable;
- custom regional/country packs can be added without another schema change;
- support levels distinguish software capability, required configuration, external procedure and reference-only information;
- support levels are not legal/compliance status flags;
- F7 does not invent tax rates, statutory charts, filing classifications or accounting-policy choices;
- `FinanceLocalizationAssignment` and `FinanceLocalizationRegistryEntry` are retained AuditEvidence;
- Finance schema 9 is provider-neutral code for SQLite, SQL Server and MySQL/MariaDB, not live-provider certification.

## Documentation rules

Documentation must not:

- document removed/default credentials;
- reconstruct historical accounting evidence from mutable current master data;
- describe technical compliance controls as legal/statutory certification;
- imply an unconfigured jurisdiction, currency, tax rate, chart/account, accounting standard or reporting classification;
- claim weighted-average, standard cost, LIFO, impairment/NRV or manufacturing costing as implemented;
- claim that F6 reports are jurisdiction-specific statutory filings;
- claim that assigning a country pack makes a deployment legally/tax/statutorily compliant;
- claim all possible country packs are implemented merely because the F7 framework can host them;
- hide repository failures by attributing them to unrelated Finance changes.
