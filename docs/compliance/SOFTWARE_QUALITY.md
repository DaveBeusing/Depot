# Software Quality Baseline

Depot uses ISO/IEC 25010 as a quality-model reference, not as a claim of certification.

## Quality characteristics and evidence

- Functional suitability: critical inventory, warehouse, purchasing, sales, administration, security, backup/recovery and invoicing workflows are covered by automated acceptance/regression tests.
- Performance efficiency: a repeatable SQLite gate creates 100,000 records and verifies a representative paged/indexed query. Baseline limits are 30 seconds for the synthetic bulk insert and 2 seconds for the representative query on GitHub-hosted Windows runners. These limits are regression gates, not end-user SLAs.
- Compatibility: the quality workflow builds and executes gates on Windows Server 2022 and 2025 with .NET 10. Supported desktop Windows editions and remote database server versions require release-environment acceptance before being advertised.
- Interaction capability: shared WPF resources define buttons, inputs, statuses, empty states, workflows and navigation; changes should use these patterns rather than local one-off styling.
- Reliability: existing tests cover transaction rollback, concurrency predicates, backup/recovery failure paths and workflow failures.
- Security: security-supply-chain and release-integrity workflows remain release gates.
- Maintainability: architecture, coding standards, repository/service boundaries, lock files, build/test pipelines and review guidance are version controlled.
- Flexibility: SQLite plus SQL Server/MySQL/MariaDB provider abstractions are retained; real-provider acceptance is required for configurations declared production-supported.
- Safety: final business records use explicit corrections/reversals and atomic audit/business transactions so failures do not silently misrepresent business state.

## Performance interpretation

Performance tests intentionally use generous deterministic limits to catch severe regressions without making CI dependent on runner noise. Production sizing must additionally test representative database providers, network latency, concurrent users, actual report/export workloads and data distributions.
