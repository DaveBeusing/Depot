# Documentation status

Updated: 2026-08-22

This document identifies the documentation baseline for the current `compliance` branch. User-facing and engineering documentation must distinguish implemented technical controls from production/legal acceptance gates.

## Current baseline

- Application line: `0.14.x-preview`
- Help manifest: `1.6`
- Core database schema: 29
- Security/compliance roadmap: phases 1-7 technically implemented; environment/legal release gates remain where documented
- Phase 8 enterprise readiness remains planned

## Primary user documentation

- `README.md`
- embedded Help Center under `src/Depot/Help`
- `docs/HELP_CENTER.md`

## Primary engineering/release documentation

- `docs/Architecture.md`
- `docs/CodingStandard.md`
- `docs/Roadmap.md`
- `docs/RELEASE_1_0.md`
- `docs/SECURITY_ROADMAP.md`
- `docs/compliance/*`

The Help Center and README must not document removed default credentials or describe compliance controls as legal certification.