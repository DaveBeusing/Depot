# User-facing hardening changes

Updated: 2026-08-22

- New databases no longer use shared default administrator credentials. Depot requires creation of the initial administrator during first-run setup.
- Password policy and login throttling are enforced.
- Remote SQL Server/MySQL/MariaDB configurations require encrypted transport through supported settings.
- Administration includes Audit Log evidence export and Privacy Data discovery/export workflows.
- Automatic backup retention preserves the newest backups and ages older automatic backups according to the configured technical policy.
- Posted/finalized business records use correction/reversal/credit workflows instead of destructive edits.
- Electronic-invoice technical support includes XRechnung CII generation and automated KoSIT conformance validation; production workflow integration remains a release gate.
- Accessibility and software-quality gates now run in CI.