# Phase 7 Technical Status — Software Quality and Accessibility

Date: 2026-09-16

## Software quality

The repository quality workflow retains bounded regression, coverage, compatibility, performance and accessibility gates behind the stable `Quality Required Gate` aggregate status.

## Accessibility technical baseline

- [x] WCAG 2.2 AA / EN 301 549 inspired engineering requirements are documented in `Accessibility.md`.
- [x] Static CI rejects direct keyboard-focus suppression and detects Setter-based `FocusVisualStyle={x:Null}` usage.
- [x] Legacy shared-style focus suppression is normalized by the runtime `AppKeyboardFocusVisualStyle` fallback for focusable Tab stops.
- [x] Core foreground/background resource pairs retain the 4.5:1 normal-text contrast baseline.
- [x] Connection and operation states expose text in addition to color.
- [x] Custom text/password controls forward automation metadata to the actual native keyboard focus target.
- [x] Login and first-run administrator inputs expose explicit label and required-field semantics.
- [x] Operation and connection status controls raise UI Automation notifications for meaningful dynamic updates.
- [x] Standard file/message dialog flows preserve a focus-restoration boundary.
- [x] Depot and DepotManager explicitly declare Per-Monitor-V2 DPI awareness.
- [x] CI retains `TechnicalAccessibilityEvidence.json`.
- [x] Exact-release-candidate manual acceptance/evidence contract is version controlled.

## Manual production acceptance

The engineering baseline above does not constitute desktop production accessibility acceptance.

The exact packaged release candidate remains `MANUAL_REQUIRED` until retained evidence proves:

- keyboard-only completion of the mandatory workflow matrix;
- logical focus order, visible focus, no keyboard traps and predictable focus return;
- Narrator operation for inputs, navigation, grids, dialogs, validation/errors and dynamic status;
- Accessibility Insights review with no unresolved blocking findings;
- visual/operational acceptance at 100%, 125%, 150% and 200% display scaling.

The retained evidence must pass:

```powershell
.\scripts\operations\Test-AccessibilityAcceptance.ps1 -Path <evidence.json> -RequirePass
```

See [Accessibility & Desktop Production Acceptance](../AccessibilityProductionAcceptance.md).
