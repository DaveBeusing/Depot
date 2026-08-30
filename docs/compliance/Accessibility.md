# Desktop Accessibility Baseline

Depot targets a WCAG 2.2 AA / EN 301 549 inspired desktop accessibility baseline where the criteria are applicable to a Windows WPF application. This document is an engineering baseline, not a formal accessibility certification.

## Automated requirements

- Keyboard focus visuals must not be disabled through `FocusVisualStyle={x:Null}`.
- Core foreground/background resource pairs must meet a 4.5:1 contrast baseline for normal text.
- Connection and operation state must expose text in addition to color.
- Shared control styles must retain keyboard focus indication.
- Accessibility static checks run in CI through `scripts/quality/test-accessibility.ps1`.

## Manual release matrix

Before a production accessibility claim, execute and record:

| Check | Required matrix |
| --- | --- |
| Keyboard-only navigation | Login, first-run admin, shell/navigation, critical CRUD/workflow dialogs, audit/privacy/export flows |
| Focus order | Logical reading/workflow order; no keyboard traps; focus returns predictably after dialogs |
| Automation names/properties | Inspect interactive unlabeled/icon-only controls with Windows Accessibility Insights or equivalent |
| Screen reader | Narrator baseline for login, navigation, data grids, dialogs, validation/errors and status updates |
| Scaling | 100%, 125%, 150%, 200% Windows display scaling; no clipped required controls or unreachable actions |
| Contrast | Automated resource baseline plus manual inspection of images/icons/selection/disabled states |
| Non-color communication | Errors, warnings, success and connectivity state contain meaningful text/icon semantics |

## Design rules

Use shared theme resources instead of hard-coded colors. Every icon-only interactive control needs an accessible name. Labels and validation messages must identify the affected field. Tab order should follow the visual/logical workflow unless an explicit exception is documented. Do not rely solely on hover behavior. Status changes important to task completion must remain perceivable without color.
