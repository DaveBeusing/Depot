# Documentation status

Updated: 2026-08-23

This document identifies the documentation baseline for the current development state. User-facing and engineering documentation must distinguish implemented technical controls from production/legal acceptance gates and must describe finalized financial documents from their persisted historical identity rather than mutable current master data.

## Current baseline

- Application line: `0.14.x-preview`
- Current branch version: `0.14.124-preview`
- Help manifest: `1.6`
- Core database schema: 29
- Sales invoice-finalization schema: 8
- Security/compliance roadmap: phases 1-7 technically implemented; environment/legal release gates remain where documented
- Sales Invoice finalization: immutable seller + Buyer identity, exact persisted XRechnung XML, SHA-256 verification, posted-invoice XML export
- Electronic-invoice limitations: explicit special-tax semantics, electronic credit-note finalization, recipient/channel acceptance and full production-scenario validation remain open
- Phase 8 enterprise readiness remains planned

## Primary user documentation

- `README.md`
- embedded Help Center under `src/Depot/Help`
- `docs/USER_FACING_CHANGES.md`
- `docs/HELP_CENTER.md`

## Primary engineering/release documentation

- `docs/Architecture.md`
- `docs/CodingStandard.md`
- `docs/Roadmap.md`
- `docs/RELEASE_1_0.md`
- `docs/SECURITY_ROADMAP.md`
- `docs/compliance/*`

The Help Center and README must not document removed default credentials, reconstruct historical financial identity from current master data, or describe technical compliance controls as legal certification.
