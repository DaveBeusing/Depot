# Depot Dependency License Review

## Purpose

This document establishes the Phase 1 license-review baseline for third-party dependencies. It is an engineering release gate, not legal advice. License interpretation with material distribution implications must be escalated to qualified legal/compliance review.

## Classification policy

### Allowlist baseline

The following common permissive SPDX licenses may normally proceed through engineering review when package metadata is complete and there are no unusual additional terms:

- MIT
- Apache-2.0
- BSD-2-Clause
- BSD-3-Clause
- ISC

### Manual review required

The following always require explicit review before a production release:

- missing or `UNKNOWN` license metadata,
- custom/non-SPDX licenses,
- dual/multi-license terms where the selected grant is unclear,
- LGPL,
- MPL,
- licenses with attribution/notice obligations not already covered by the release process,
- source-available or field-of-use restrictions,
- dependencies whose repository/package metadata conflicts.

### Block until reviewed

A production release is blocked pending qualified review when a shipped dependency is identified as using or potentially imposing material reciprocal/restrictive terms, including:

- GPL family,
- AGPL family,
- SSPL,
- non-commercial licenses,
- licenses restricting redistribution or commercial use.

`Block until reviewed` does not mean a dependency is necessarily prohibited; it means engineering may not assume compatibility.

## Current direct production dependencies

The direct package inventory in `src/Depot/Depot.csproj` is:

| Package | Version | Review state | Required evidence before production |
| --- | ---: | --- | --- |
| ClosedXML | 0.105.0 | Metadata/license verification required | Confirm SBOM/package SPDX/license metadata and notice obligations |
| Microsoft.Data.Sqlite | 10.0.10 | Metadata/license verification required | Confirm SBOM/package SPDX/license metadata and notice obligations |
| Microsoft.Data.SqlClient | 7.0.2 | Metadata/license verification required | Confirm SBOM/package SPDX/license metadata and notice obligations |
| MySqlConnector | 2.6.1 | Metadata/license verification required | Confirm SBOM/package SPDX/license metadata and notice obligations |
| PDFsharp-WPF | 6.2.4 | Metadata/license verification required | Confirm SBOM/package SPDX/license metadata and notice obligations |
| SQLitePCLRaw.lib.e_sqlite3 | 2.1.12 | Metadata/license verification required | Confirm package plus bundled native-library license/notice obligations |

Test-only dependencies (`Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`) are inventoried by the workflow but do not ship as Depot runtime dependencies unless the packaging model changes.

## Automated evidence

The `Security supply chain` workflow generates:

- CycloneDX SBOM with available license evidence,
- direct/transitive dependency inventory,
- deprecated dependency inventory,
- NuGet vulnerability audit results.

Automated evidence must be inspected before a production release. Missing license metadata is a review finding, not evidence that no obligations exist.

## Review procedure

For every new or changed production dependency:

1. Confirm exact package and version in the lockfile/SBOM.
2. Confirm license identifier/text from authoritative package/project metadata.
3. Classify it as `Allowed`, `Manual review`, or `Block until reviewed`.
4. Record required attribution/NOTICE/source-offer or redistribution obligations.
5. Review bundled native components separately where applicable.
6. Escalate ambiguous/restrictive terms.
7. Record the decision in the dependency-changing PR/release evidence.

## Release gate

Before the first production release, every shipped direct and material transitive dependency must have a recorded license classification. No `UNKNOWN`, unreviewed custom, or blocked license may pass the production release gate without documented approval.

## Phase 1 conclusion

Phase 1 establishes the license inventory mechanism, classification rules, current direct-dependency review queue, and release procedure. Final legal classification is deliberately retained as a pre-production review activity because automated package metadata is not a legal compatibility opinion.
