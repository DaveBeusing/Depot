# Accessibility & Desktop Production Acceptance

## Purpose

This document is the Track A H5 production-acceptance procedure for Depot and DepotManager desktop accessibility.

The repository contains automated accessibility engineering controls, but those controls are not a substitute for a human desktop acceptance run. A production accessibility acceptance claim requires evidence from the exact packaged release candidate on the intended Windows desktop baseline.

This is an engineering acceptance boundary, not a formal accessibility certification.

## Acceptance states

Use only these states in retained evidence:

- `MANUAL_REQUIRED` — the check has not yet been executed against the exact release candidate;
- `PASS` — the check was executed and concrete evidence was retained;
- `BLOCKED` — a defect, environment problem or missing evidence prevents acceptance.

Do not convert `MANUAL_REQUIRED` or `BLOCKED` into `PASS` because automated tests are green.

## Automated technical baseline

`Quality Required Gate` includes `Accessibility technical baseline`.

The automated baseline verifies:

- no direct XAML `FocusVisualStyle={x:Null}` suppression is permitted;
- shared style-level focus suppression is only allowed inside `Resources` and is normalized at runtime by `DesktopAccessibilityRuntime`;
- both Depot and DepotManager load the shared `AppKeyboardFocusVisualStyle`;
- cyclic `KeyboardNavigation.TabNavigation="Cycle"` declarations are rejected;
- custom `TextInput` and `PasswordInput` forward automation labels and form semantics to the native focused input;
- `OperationStatus` and `ConnectionStatusIndicator` raise UI Automation notifications for meaningful dynamic status changes;
- Login and first-run administrator inputs expose labels and required-field semantics;
- both executables explicitly declare Per-Monitor-V2 DPI awareness;
- core foreground/background resource pairs retain the existing 4.5:1 contrast baseline;
- important connection and operation states expose meaningful text in addition to color.

The quality job retains `TechnicalAccessibilityEvidence.json`. That artifact proves only the technical/static boundary.

## Production evidence contract

Copy:

```text
operations/AccessibilityAcceptance.example.json
```

to the release evidence location for the candidate being accepted.

Populate the exact:

- Depot release-candidate version;
- source commit SHA;
- tester;
- UTC test timestamp;
- Windows version;
- display resolution;
- graphics adapter;
- screen reader;
- accessibility inspection tool;
- evidence reference for every required scenario.

Validate the structure while preparing evidence:

```powershell
.\scripts\operations\Test-AccessibilityAcceptance.ps1 -Path <evidence.json>
```

A production acceptance is valid only when this succeeds:

```powershell
.\scripts\operations\Test-AccessibilityAcceptance.ps1 -Path <evidence.json> -RequirePass
```

`-RequirePass` requires every scenario and every DPI row to contain concrete evidence and `PASS`. Any blocking finding keeps the candidate blocked.

## Keyboard-only acceptance

Disconnect or stop using the mouse for the scenario being tested. Use Tab, Shift+Tab, arrow keys, Enter, Space, Escape and documented application shortcuts only.

The retained evidence must cover:

1. **Login** — reach email, password and Sign in; submit; receive validation/error feedback; leave the window without a pointer.
2. **First-run administrator** — reach every field and requirement area, submit invalid and valid data, and confirm errors are perceivable without pointer interaction.
3. **Shell, navigation and workspace tabs** — move through primary/secondary navigation, switch and close workspaces, use command/quick-open keyboard shortcuts and reach all exposed actions.
4. **Critical CRUD and workflow dialogs** — create/edit/save/cancel representative records and confirm destructive confirmations are usable by keyboard.
5. **Finance workspaces and approval actions** — execute representative navigation and permitted approval/posting/reconciliation interactions without pointer-only dependencies.
6. **Audit, privacy and export flows** — search/filter, execute export or file-dialog flows and return to the originating control.
7. **DepotManager** — exercise install/update/repair/diagnostics controls and standard file/folder dialogs with keyboard-only navigation.

A scenario is `BLOCKED` if an actionable control cannot be reached, invoked or exited by keyboard.

## Focus acceptance

For the same scenarios verify:

- Tab and Shift+Tab follow logical workflow/read order;
- a visible focus indicator is present for each actionable control;
- keyboard focus never becomes trapped in a region, popup, grid or tab set;
- modal dialogs return focus to a sensible originating control after close;
- disabled/hidden controls do not receive focus;
- grid and list navigation preserves an understandable current item;
- opening and closing standard file/message dialogs does not lose the user's working position.

The shared runtime focus guard exists to preserve focus visibility where legacy/custom styles suppress the framework visual. Manual acceptance must still verify that the resulting indicator is actually visible in the shipped theme.

## Narrator acceptance

Enable Windows Narrator and test the packaged candidate.

Verify at minimum:

- Login and first-run fields announce useful labels and required-field semantics;
- password controls do not expose password content;
- shell navigation and workspace identity are understandable;
- DataGrid/list current items, column context and actions are understandable enough to complete the tested workflow;
- validation and error states are announced when they become relevant;
- `OperationStatus` progress/error/success changes are announced without moving keyboard focus;
- connection-state changes are announced without relying on color;
- dialog purpose and actionable buttons are announced;
- focus after dialog close returns to an understandable location.

Record the tested Narrator version/build with the Windows version.

## Accessibility Insights acceptance

Run Accessibility Insights for Windows against the packaged candidate.

Retain the exported report or equivalent trace/reference and resolve every blocking finding relevant to the tested workflows. The acceptance JSON must list unresolved blocking findings; production acceptance requires that list to be empty.

Automated UIA inspection cannot replace the Narrator workflow above.

## DPI and display-scaling acceptance

Both Depot and DepotManager declare `PerMonitorV2,PerMonitor` in their application manifests. This is a technical prerequisite, not visual acceptance.

Execute the critical desktop scenarios at each Windows display scaling value:

```text
100%
125%
150%
200%
```

At each scale verify:

- required text is readable and not clipped;
- required controls remain visible or reachable by intentional scrolling;
- no primary action is pushed outside an unreachable region;
- dialogs fit or remain operable on the selected display resolution;
- grids retain usable headers, rows, scrollbars and selection state;
- validation/error text remains visible;
- keyboard focus indicators remain visible;
- DepotManager installation/repair UI remains operable;
- moving a Per-Monitor-V2 window between displays with different scaling does not leave the UI unusable.

Capture evidence for every scale independently. Passing one scale does not imply the others pass.

## Failure handling

Set the affected row and the top-level evidence to `BLOCKED` when any of these occurs:

- keyboard-only workflow cannot be completed;
- focus is lost, invisible or trapped;
- a critical field/action has no usable automation name/role/context;
- Narrator misses critical validation/status information;
- Accessibility Insights reports an unresolved blocking issue;
- required content/action is clipped or unreachable at any required scale;
- evidence belongs to a different build or source SHA.

Fix the product or test environment, build a new candidate when product bytes change, and repeat the affected acceptance. Never reuse PASS evidence across different release-candidate bytes.

## Release boundary

Repository implementation of H5 is complete when technical controls, CI evidence and this reproducible evidence contract exist and pass repository gates.

**Desktop production accessibility acceptance remains `MANUAL_REQUIRED` until an exact packaged release candidate has a complete evidence file that passes `Test-AccessibilityAcceptance.ps1 -RequirePass`.**
