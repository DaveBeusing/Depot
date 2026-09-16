# Desktop Accessibility Baseline

Depot targets a WCAG 2.2 AA / EN 301 549 inspired desktop accessibility engineering baseline where the criteria are applicable to a Windows WPF application. This document is not a formal accessibility certification.

The Track A production-acceptance procedure is [Accessibility & Desktop Production Acceptance](../AccessibilityProductionAcceptance.md).

## Automated technical requirements

`Quality Required Gate` executes `scripts/quality/test-accessibility.ps1` and retains technical evidence.

The automated baseline requires:

- no direct XAML `FocusVisualStyle={x:Null}` suppression;
- legacy shared resource-style focus suppression must be runtime-normalized by `DesktopAccessibilityRuntime` and a shared visible keyboard-focus adorner;
- no cyclic Tab-navigation declarations that can create keyboard traps;
- core foreground/background resource pairs meet a 4.5:1 contrast baseline for normal text;
- connection and operation state expose meaningful text in addition to color;
- custom input controls forward accessibility labels and required-field semantics to the actual native keyboard focus target;
- meaningful dynamic operation/error/connection status changes expose UI Automation notifications;
- Login and first-run administrator inputs expose explicit labels and form-required semantics;
- Depot and DepotManager explicitly declare Per-Monitor-V2 DPI awareness.

CI also validates the structure of `operations/AccessibilityAcceptance.example.json`. It deliberately does not mark manual checks as passed.

## Manual production acceptance

Before a production accessibility acceptance claim, execute and retain evidence from the exact packaged release candidate for:

| Check | Required matrix |
| --- | --- |
| Keyboard-only navigation | Login, first-run admin, shell/navigation/workspace tabs, critical CRUD/workflow dialogs, Finance workflows, audit/privacy/export, DepotManager |
| Focus order | Logical workflow order; visible focus; no keyboard traps; predictable focus return after dialogs |
| Automation names/properties | Accessibility Insights inspection of interactive controls, inputs, grids, dialogs and actions |
| Screen reader | Narrator baseline for labels, required fields, navigation, grids, dialogs, validation/errors and dynamic status updates |
| Scaling | 100%, 125%, 150%, 200% Windows display scaling; no clipped required controls or unreachable actions |
| Contrast | Automated resource baseline plus manual inspection of icons, selection, focus and disabled states |
| Non-color communication | Errors, warnings, success and connectivity state remain understandable without color |

The evidence contract uses `MANUAL_REQUIRED`, `PASS` and `BLOCKED`. Production acceptance requires every mandatory row to be `PASS` with concrete evidence and the exact release-candidate source SHA.

## Design rules

Use shared theme resources instead of hard-coded colors. Every icon-only interactive control needs a reliable accessible name. Labels and validation messages must identify the affected field. Tab order should follow the visual/logical workflow unless an explicit exception is documented. Do not rely solely on hover behavior. Status changes important to task completion must remain perceivable without color and without forcing keyboard focus to move.

Custom controls that transfer keyboard focus to a template child must forward relevant UI Automation metadata to that actual focus target.

## Acceptance boundary

A green static/technical accessibility job proves engineering invariants only.

**Desktop production accessibility acceptance remains `MANUAL_REQUIRED` until the exact packaged release candidate has passed keyboard-only, focus, Narrator, Accessibility Insights and 100/125/150/200% DPI acceptance and the retained evidence passes `scripts/operations/Test-AccessibilityAcceptance.ps1 -RequirePass`.**
