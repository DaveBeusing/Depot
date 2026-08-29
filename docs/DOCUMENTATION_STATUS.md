# Documentation status

Updated: 2026-08-28

This document identifies the documentation baseline for the current development state. User-facing and engineering documentation must distinguish implemented technical controls from production/legal acceptance gates and must describe finalized financial records from persisted historical evidence rather than mutable current master data.

## Current baseline

- Application: `0.15.42-preview`
- Help manifest: `1.17`
- Core database schema: `29`
- Sales feature schema: `8`
- Finance feature schema: `9`
- Finance foundation, General Ledger, Receivables, Payables, Inventory Accounting, Banking, Financial Reporting and Localization are implemented on `finance`.

## Finance documentation invariants

Documentation must state that:

- the General Ledger is the authoritative immutable accounting ledger;
- Financial Reporting does not maintain a parallel ledger;
- Localization does not post journals;
- `LegalEntity.CountryCode` does not automatically activate localization;
- effective localization requires an explicit effective-dated assignment;
- the built-in Germany reference hierarchy resolves `GENERIC → EU → DE`;
- country packs are validated against Legal Entity country and active assignment ranges cannot overlap;
- built-in pack and registry definitions are immutable;
- custom regional/country packs can be added without another schema change when metadata/configuration is sufficient;
- support levels distinguish software capability, required configuration, external procedure and reference-only information;
- support levels are not legal/compliance status flags;
- Depot does not invent tax rates, statutory charts, filing classifications or accounting-policy choices;
- localization assignments and registry entries are retained Audit evidence;
- provider-neutral schema/code is not live-provider certification.

## Documentation rules

Documentation must not:

- document removed/default credentials;
- reconstruct historical accounting evidence from mutable current master data;
- describe technical compliance controls as legal/statutory certification;
- imply an unconfigured jurisdiction, currency, tax rate, chart/account, accounting standard or reporting classification;
- claim weighted-average, standard cost, LIFO, impairment/NRV or manufacturing costing as implemented;
- claim configurable reports are automatically jurisdiction-specific statutory filings;
- claim assigning a country pack makes a deployment legally/tax/statutorily compliant;
- claim all possible country packs are implemented merely because the framework can host them;
- hide repository failures by attributing them to unrelated Finance changes.

Help manifest **1.17** includes stable topic `finance.localization`, guarded by `FinanceLocalization.View`.
