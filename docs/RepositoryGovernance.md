# Repository Governance

Updated: 2026-09-20

## Purpose

Depot protects `master` through a small set of stable aggregate GitHub Actions checks instead of binding repository rules to every individual matrix job. The aggregate jobs are the only status checks that the `master` ruleset requires.

This keeps the branch policy stable when internal test matrices, runner versions or individual job names change.

## Current live status

H1 repository governance is currently **BLOCKED by live-ruleset drift**.

GitHub ruleset **23590604**, `Depot master governance`, is active and still targets exactly `refs/heads/master`. It continues to require pull-request delivery, blocks deletion/non-fast-forward updates and exposes no bypass actor. However, the live ruleset currently omits the `required_status_checks` rule after the temporary development-phase CI relaxation. The source-controlled template `.github/rulesets/MasterGovernance.json` still defines the intended five aggregate checks.

Because live enforcement no longer matches the versioned contract, H1 is reopened until the five required checks are restored in GitHub and `scripts/operations/Test-RepositoryGovernance.ps1 -RequireActiveRuleset` passes again. Repository documentation and Track A evidence must not report H1 as `PASS` while this drift exists.

## Required status checks

The following job names are the repository governance contract and must remain stable unless the ruleset is changed in the same controlled rollout:

| Workflow | Required job name | What it aggregates |
| --- | --- | --- |
| `.github/workflows/ci.yml` | `CI Required Gate` | Release build/publish plus all bounded regression areas |
| `.github/workflows/quality-gates.yml` | `Quality Required Gate` | Windows compatibility, coverage, performance and static accessibility |
| `.github/workflows/security-supply-chain.yml` | `Security Required Gate` | Security tests, risk acceptance policy, vulnerability audit, lock files and SBOM/CRA evidence |
| `.github/workflows/depot-manager-e2e.yml` | `Packaged E2E Required Gate` | Packaged DepotManager acceptance; pull requests execute the Smoke tier |
| `.github/workflows/database-provider-acceptance.yml` | `Database Provider Required Gate` | SQLite acceptance plus SQL Server/MariaDB/MySQL provider acceptance; pull requests execute the remote Smoke tier |

Each aggregate job uses `if: always()` and explicitly fails unless every dependency reports `success`. A cancelled, skipped or failed dependency therefore cannot turn into a green required gate.

`DepotManager packaged E2E` intentionally runs on every pull request targeting `master`. Its previous pull-request path filter was removed because a path-filtered workflow cannot safely be configured as a repository-wide required status check: an unrelated pull request could otherwise wait forever for a status that will never be reported. The push path filter remains in place for `master` pushes.

## `master` ruleset contract

Target policy:

- target exactly `refs/heads/master`;
- enforcement: `active`;
- require changes through a pull request;
- required approving review count: `0` while Depot is maintained as a one-person project;
- no mandatory Code Owner review;
- no mandatory approval of the last push by another person;
- require the five aggregate checks listed above;
- do not require the branch to be up to date with `master` before merging (`strict_required_status_checks_policy=false`);
- block branch deletion;
- block force pushes/non-fast-forward updates;
- no permanent bypass actor.

The live ruleset additionally reports `require_extra_approval_for_unattributed_changes=true`. This does not weaken the Depot contract and does not create a bypass path.

The pull-request rule with zero required approvals blocks normal direct pushes to `master` without creating an artificial second-person dependency. Repository administrators can still edit or disable the ruleset in GitHub settings if a genuine governance incident makes recovery necessary; that administrative recovery action is not part of the normal development path.

## Automated governance contract verification

`scripts/operations/Test-RepositoryGovernance.ps1` is the authoritative repository-side H1 validator.

The default mode validates source-controlled evidence. It fails when the ruleset template drifts from the target policy or when one of the five workflow aggregate job names no longer matches the required-check contract:

```powershell
.\scripts\operations\Test-RepositoryGovernance.ps1 `
    -EvidencePath artifacts\operations\RepositoryGovernance.validation.json
```

CI executes this mode and retains the generated evidence artifact. This proves that the repository contains a coherent governance contract.

The live mode additionally queries the GitHub repository rulesets API, retrieves each active branch ruleset and requires one ruleset to match the complete Depot policy:

```powershell
.\scripts\operations\Test-RepositoryGovernance.ps1 `
    -Repository DaveBeusing/Depot `
    -RequireActiveRuleset `
    -EvidencePath <h1-governance-evidence.json>
```

`GITHUB_TOKEN` or `GH_TOKEN` is used when available; public ruleset metadata can otherwise be queried anonymously where GitHub permits it. The evidence contains the active ruleset ID, required check names and validation result, but never stores an authentication token.

`-RequireActiveRuleset` is deliberately fail-closed. Missing rulesets, inactive rulesets, wrong branch targeting, bypass actors, changed review policy, changed status-check policy or a GitHub API failure all prevent H1 from being represented as `PASS`.

## Why branch-up-to-date is not required

The required status checks validate the exact pull-request head SHA. For the current one-person workflow, additionally requiring every branch to be updated with the latest `master` would force expensive packaged-E2E and real-provider smoke reruns whenever another change lands first, without adding a second reviewer or merge queue.

If Depot moves to parallel multi-developer delivery or a merge queue, reassess `strict_required_status_checks_policy` and enable it together with the corresponding merge workflow.

## Historical activation evidence and current drift

Ruleset ID `23590604` was originally activated on 2026-09-17 with the complete H1 contract and was valid evidence for H1 at that time. A later temporary development-phase relaxation removed the live required-status-check rule while leaving the source-controlled template unchanged.

Current target state:

- ruleset ID: `23590604`;
- name: `Depot master governance`;
- target exactly `refs/heads/master`;
- pull-request delivery required;
- deletion and non-fast-forward updates blocked;
- no bypass actors;
- **restore** `CI Required Gate`, `Quality Required Gate`, `Security Required Gate`, `Packaged E2E Required Gate` and `Database Provider Required Gate` as required status checks;
- keep `strict_required_status_checks_policy=false` for the current one-person workflow.

After restoration, rerun the live validator and only then return H1 and the controlled Track A evidence to `PASS`.

## Safe change procedure
## Safe change procedure

Required-check names are an external repository contract. Do not rename or remove an aggregate job in isolation.

For any future gate rename:

1. add the new aggregate check while the old one still exists;
2. let the new check report successfully on a pull request;
3. update the GitHub ruleset to require the new name;
4. only then remove the old aggregate check in a later change.

This ordering prevents a ruleset from waiting for a check name that no workflow can produce.

## Recovery

If GitHub Actions itself is unavailable or a required check is structurally broken, diagnose and repair the workflow first. Temporarily weakening the ruleset is a break-glass administrative action and should be limited to restoring the governance path, then reversed immediately. A failing product/security/provider check is not a reason to bypass the rule.
