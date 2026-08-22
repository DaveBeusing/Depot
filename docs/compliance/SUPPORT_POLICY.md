# Depot Support Policy

## Status

This is the initial support-lifecycle framework. Concrete production support periods and supported-version windows must be defined before Depot 1.0 is commercially distributed.

## Purpose

Security and compliance require users to know which Depot versions are supported, how long security updates are expected, and when a version reaches end of support.

## Version states

### Preview

Preview builds are development versions. They may contain incomplete migrations, functionality, security controls, and compatibility behavior and are not production-certified.

### Supported production

A supported production release receives maintenance according to the published support window and severity policy.

### End of support

After end of support, a version no longer receives normal fixes/security updates. Users should be informed sufficiently in advance and provided with an upgrade path where feasible.

## Before 1.0

Define and publish:

- [ ] minimum support period for a sold/distributed product,
- [ ] which release lines receive security updates,
- [ ] Windows versions supported,
- [ ] SQL Server versions supported,
- [ ] MySQL/MariaDB versions supported,
- [ ] upgrade paths between supported versions,
- [ ] database schema/migration support policy,
- [ ] end-of-support notification approach,
- [ ] emergency security-update process.

## Security updates

Security updates should be prioritized according to severity, exploitability, exposure, and applicable regulatory obligations. Users must have sufficient information to identify affected versions and obtain the fixed version.

## Dependencies

A Depot release should not remain supported indefinitely on an unsupported runtime or critical dependency. Upstream .NET, database-provider, operating-system, and package lifecycles must be considered when defining Depot support windows.

## Compatibility

Supported configurations must be explicitly documented rather than inferred from what happens to work during development.

## CRA alignment

Before CRA obligations applicable to Depot require it, the support period and security-update commitments must be reviewed against the regulation and included in the product/compliance documentation as necessary.
