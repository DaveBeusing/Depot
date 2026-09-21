# Depot UI/UX Design Contract

This document defines the binding UI/UX grammar for Depot. It applies to new views and to changes made to existing views. The goal is not visual uniformity for its own sake; the goal is predictable interaction across Inventory, Purchasing, Warehouse, Sales, Finance, Administration and future modules.

## Product UI model

Depot uses a VS Code-inspired application shell with ERP-oriented workspaces. The shell provides the Activity Bar, workspace tabs, context navigation and status bar. Business views inside the workspace must follow the patterns defined here rather than inventing module-specific page structures.

The design system is implemented through the central resource dictionaries in `src/Depot/Resources`. Views should compose those resources and existing controls before introducing local styles.

## Foundations

### Spacing

Use the semantic spacing scale from `Spacing.xaml`:

- `Spacing.XS` = 4
- `Spacing.S` = 8
- `Spacing.M` = 12
- `Spacing.L` = 16
- `Spacing.XL` = 24
- `Spacing.XXL` = 32

Prefer semantic workspace tokens such as `Workspace.Header.Margin`, `Workspace.Toolbar.Margin` and `Workspace.Section.Margin` where the purpose is known. Avoid introducing new one-off margins or padding values when an existing token expresses the same purpose.

### Corner radius

Use `Radius.S`, `Radius.M` and `Radius.L`. Larger decorative radii should be exceptional. ERP workspaces should remain visually restrained and information-dense.

### Control heights

Use `ControlHeight.S`, `ControlHeight.M` and `ControlHeight.L` for new common controls. Existing specialized controls may retain their established dimensions until migrated centrally.

### Typography

Use semantic text styles instead of direct font settings where possible:

- `CaptionStyle` for secondary/help text;
- `BodyStyle` for normal content;
- `BodyStrongStyle` for emphasized body content;
- `SectionTitleStyle` for section headings;
- `PageTitleStyle` for page titles;
- `MetricStyle` and `MetricLargeStyle` for dashboard/KPI values.

## Workspace anatomy

A normal workspace should follow this order:

1. `PageHeader` with title, subtitle/context and optional actions;
2. `OperationPanel` when the view exposes busy/error/status feedback;
3. optional search/filter/action toolbar;
4. main content;
5. optional details/inspector area.

Reusable visual patterns are defined in `Workspace.xaml`, including toolbar, section, inspector and dashboard action-card styles.

## Canonical page types

### Collection view

Use for Items, Suppliers, Customers, Purchase Orders, Sales Orders, Warehouses, Users and similar sets.

Expected interaction model:

- clear page header;
- search/filter controls before the result surface;
- one obvious primary creation action;
- standardized grid/list behavior;
- empty/loading/error state;
- pagination or virtualization where required;
- bulk actions only when they are fully implemented and relevant.

Do not add placeholder controls for filtering, export, column selection, saved views or bulk actions.

### Entity view

Use for a single business record such as an Item, Purchase Order, Sales Order, Supplier, Customer or User.

Expected interaction model:

- context/title at the top;
- one dominant primary action when editing/posting/approving;
- secondary navigation only when the record genuinely contains multiple working areas;
- flat, readable sections for normal master data;
- cards reserved for summaries, alerts or self-contained blocks;
- history/audit information separated from editable current state.

### Process view

Use for guided workflows such as creating an order, stock transfer, approval, posting or import.

Expected interaction model:

- process title and current context;
- visible current step/state when the process has multiple steps;
- main form/content area;
- secondary/back/cancel actions separated from the dominant continue/complete/post action;
- failure must remain visible and actionable; do not hide validation or backend errors behind navigation.

## Action hierarchy

### Primary

Use the primary visual treatment for actions such as Create, Save, Post, Approve, Release and Complete. Prefer one dominant primary action per page or process step.

### Secondary

Use secondary treatment for Cancel, Refresh, Export, Print and Close where those actions are implemented.

### Destructive

Delete, Reject, Void and Deactivate must be visually distinct and must not weaken existing confirmation, RBAC or audit behavior.

### Overflow

Low-frequency actions such as Duplicate, Archive, History, document generation, export and similar supporting operations may move to overflow when that improves clarity. Do not hide actions required for the primary workflow. Overflow content must remain fully keyboard-operable and must not expose an empty overflow affordance when no action is currently available.

### Contextual workflow actions

`WorkflowActionBar` provides three presentation areas: secondary actions, optional overflow actions and the current primary action. For the current business context, at most one dominant Primary action may be visible.

The ViewModel or View owns the presentation decision. It must derive action visibility from the already-authoritative workflow state, permissions and command `CanExecute` behavior. `WorkflowActionBar` must not infer business rules, inspect status text or introduce an alternate authorization path.

When multiple commands are valid in the same state, choose one preferred next step as Primary and retain other still-valid workflow actions as Secondary where users must continue to have direct access to them. Supporting low-frequency actions may move to Overflow. Destructive actions remain destructive and never become Primary merely because they are executable.

A workflow state with no appropriate next Primary action should leave the Primary area empty rather than displaying a disabled or fabricated action. Contextual visibility changes should use the shared reduced-motion-aware State transition and must not animate layout dimensions.

Role Center quick actions follow the same hierarchy. Their service/projection order is the explicit priority contract: at most the first permitted action is Primary, the next supporting actions are Secondary, and lower-frequency remainder may move to Overflow. Views must not inspect action labels to infer priority, and reclassification must not change permissions or the existing execution path.

### Filter chips and segmented filters

Use the shared `FilterChipToggleStyle` when a small, finite filter set benefits from immediate switching. Exactly one chip should communicate the selected projection when the filter is mutually exclusive. The selected state must be visible without relying on animation, every chip remains keyboard-operable, and its automation name must identify the filter.

Counts may be shown when they are derived from the already-loaded in-memory projection. A filter chip must not trigger additional repository/provider queries merely to calculate its count. Filtering remains presentation state and must not become a second persistence or authorization model.

## Button interaction and motion

Shared application buttons are defined canonically in `src/Depot/Resources/Buttons.xaml`. Do not redeclare `AppButtonBaseStyle`, `PrimaryButtonStyle`, `SecondaryButtonStyle`, `DangerButtonStyle` or `AppLinkButtonStyle` in later resource dictionaries.

Button state feedback must use the shared motion foundation and preserve the established brush semantics:

- hover may use the established hover brushes plus only a subtle shared render-transform cue;
- press uses the shared pressed-scale token;
- release returns through the shared Fast motion duration rather than snapping from a local transform;
- keyboard focus must remain visible through `AppKeyboardFocusVisualStyle` and the button template border;
- disabled buttons must not run hover or press motion and must retain a clear disabled visual state;
- shell and utility buttons may use the smaller utility pressed-scale token while retaining their own shell visual grammar.

Button motion is non-essential. When Windows client-area animations are disabled, the shared motion behavior collapses the transition duration to zero. Button interactions must not animate `Width`, `Height`, margins, padding or other layout properties; use `RenderTransform` and, where appropriate, `Opacity` only. Do not add permanent, repeating or decorative button animations.

### Shell, tabs and navigation motion

Shell motion uses the same Fast (100 ms), Standard (160 ms) and reduced-motion-aware duration grammar as the rest of Depot. App tabs and workspace tabs communicate selection with opacity plus a centered scale transform on the accent/underline; tab width, height, padding, margin and position are never animated. Keyboard focus always has a static visible border and does not rely on motion alone.

Newly materialized workspace-tab headers may use the shared `State` entrance transition. Closing remains immediate after the existing close/discard guard succeeds; no exit animation may delay removal, disposal, Ctrl+W or Ctrl+Shift+T reopen semantics. Workspace content continues to use its existing `Workspace` transition and must not receive a second page-level animation.

Activity Bar hover and selection may animate the existing accent, icon and halo with brief opacity and minimal render-scale feedback. Running state animations must replace stale animations during rapid navigation. Navigation Toggle, Notifications, Help, Current User and other shell utility buttons use the shared button feedback behavior and utility press scale where appropriate. Notification badges remain one-shot and no shell motion repeats indefinitely.

## Data grids and lists

`AppDataGridStyle` is the standard ERP grid surface. New collection views should use the existing shared grid styles for headers, text, numeric alignment, selection and state behavior. Its standard row/header height is based on the semantic `ControlHeight.L` token rather than per-view magic numbers.

`AppDataGridCompactStyle` is the shared high-density variant. Use it only where a materially denser business table improves productivity and the content remains readable and keyboard-accessible. It inherits the standard selection, focus, scrolling, virtualization and recycling behavior and uses `ControlHeight.M` rows. Do not create view-local compact grid styles or silently use compact density as a substitute for sensible column design.

Guidelines:

- numeric values should use the shared numeric text style;
- dates and identifiers should use stable widths when appropriate;
- descriptions or names normally receive flexible width;
- empty/loading/error states must be explicit;
- double-click and context actions must be predictable and only enabled when implemented;
- tables are productivity surfaces, not decorative cards.

## Forms

Use `FormLabelStyle`, shared input styles and existing validation controls. Keep label placement and spacing consistent within a view and with comparable views.

Required fields must be identifiable by text or another non-color cue. Read-only and disabled states must remain visually distinct. Validation messages should be adjacent to the relevant field or represented through the established operation/validation pattern.

## Cards and sections

Use cards for:

- KPIs and dashboard summaries;
- alerts and attention items;
- clearly self-contained information groups.

Prefer flat workspace sections for ordinary groups of form fields and dense business data. Avoid wrapping every small section in a card.

## Status semantics

`StatusBadgeVariant` provides the common semantic vocabulary:

- `Neutral`: draft, unknown or uncategorized;
- `Primary`: active, open or in progress;
- `Success`: completed, approved, posted, closed or paid;
- `Warning`: pending, review or partially complete;
- `Error`: rejected, failed, blocked, reversed, overdue or error;
- `Muted`: archived, disabled or inactive.

The displayed business status remains the source of truth. The visual mapping must never change domain state.

## Feedback states

Views should use common patterns for busy, success, information, warning, error, empty, disabled and no-permission states. Reuse `OperationPanel`, `OperationStatus`, `WorkflowListState` and `EmptyState` rather than introducing per-view substitutes.

`OperationStatus` may expose one contextual follow-up or recovery CTA through `ActionText`, `ActionCommand` and optional `ActionCommandParameter`. The CTA is interactive only when both meaningful text and a real command are present; never render pseudo-clickable action text. Command `CanExecute` remains authoritative for enabled state and RBAC/business rules must not be duplicated in the status control.

Use recovery actions only when an existing workflow already supports the operation, such as reloading after an optimistic-concurrency conflict. Diagnostic actions such as Copy diagnostics and Open Help remain separate supporting tools and must not be replaced by the recovery CTA.

Document status badges may briefly confirm a real status value change with the shared reduced-motion-aware status-change motion. Initial presentation should not pulse or flash. Status confirmation is one-shot, uses opacity plus a subtle render scale, and must not animate layout dimensions or alter the underlying business status.

Role Center KPI values may use the same one-shot `StatusChange` grammar when the ViewModel can compare a stable KPI identity with a previously presented value in the current view session. Initial load and identical refresh values remain static. Previous KPI values are presentation-only state and are never persisted. Progressive dashboard KPI loading is not treated as a value change unless the surface can reliably distinguish initial materialization from a later refresh.

## Dashboard

The Dashboard is an operational attention surface, not a duplicate module menu. Its hierarchy is:

1. Needs attention;
2. Key metrics and workspaces;
3. Recent activity.

Dashboard cards may navigate to the corresponding established workspace but must not replace the Activity Bar as the primary module navigation model.

## Master-detail and inspector

Use master-detail for workflows where users repeatedly select records and inspect/edit details. Use an inspector for quick read-only or low-complexity detail when opening a full workspace would create unnecessary navigation.

For selection-driven detail content, bind `MasterDetailGrid.DetailTransitionKey` to a stable identity for the currently presented record or editor context. The transition key must change only when the displayed selection/content identity changes; do not bind it to general view-model state or properties that update during scrolling, virtualization, refresh or normal editing.

Selection-detail transitions use the shared `DetailPane` motion grammar: opacity plus a small translate offset, reduced-motion-aware, with running animations replaced on rapid selection changes. Selection motion must not move keyboard focus and must not animate layout dimensions.

Selection-dependent actions must be derived from existing commands and permission/state properties. Collection actions such as New remain available without a selection; record actions such as Activate/Deactivate, Approve/Reject, Open or Continue should appear only when they are meaningful and allowed for the current selection. Do not duplicate RBAC or business-state rules in XAML visibility expressions when an existing command or presentation property already represents that decision.

Selection-driven designer toolbars should keep global editing/navigation controls available while hiding transform groups that are irrelevant to the current selection cardinality. Visibility may use presentation-only selection properties, but command `CanExecute` remains authoritative for whether an operation is permitted. Contextual groups should use the shared reduced-motion-aware `State` transition and must not animate layout dimensions.

When the active record is not otherwise obvious, show a restrained selection context in the detail header. Avoid redundant selection labels when the detail title/status already identifies the record clearly.

Do not force split layouts where available horizontal space would make the workflow materially worse.

## Accessibility

All new interactive controls must support keyboard operation. Do not remove focus visuals unless an equivalent explicit focus treatment is supplied. Provide `AutomationProperties.Name` where visible content does not already give assistive technology a reliable accessible name.

Status, validation and permission information must not rely on color alone.

## Desktop responsiveness

Depot is a desktop ERP application and is not required to support phone layouts. Views must nevertheless degrade safely across common desktop window sizes. Prefer flexible columns, constrained master panes and scrolling over clipped content. Fixed widths should be limited to fields whose content benefits from a stable width.

## Local styles

Local view styles are allowed only when they represent genuinely view-specific behavior or domain presentation. Generic visual styles belong in `src/Depot/Resources`.

For example, password requirement validation in the Users workspace is domain-specific and can remain local. Generic dashboard card styling belongs in the shared workspace resources.

## No mock UI

Never expose a visible control for a capability that is not functional. Search, Filter, Export, Columns, Bulk Actions, Saved Views and similar controls must be backed by real behavior before they appear in production UI.

## Architecture boundary

UI standardization does not change the application architecture:

`Views → ViewModels → Services → Repositories → DatabaseAccess`

Business logic must not move into code-behind as part of UI cleanup. WPF-specific presentation behavior may remain in code-behind when consistent with the existing application pattern.

## Review checklist

Before merging a new or substantially changed view, confirm that:

- it uses one of the canonical page types;
- header/action placement follows the contract;
- central styles/tokens are used where available;
- the primary action is visually unambiguous;
- loading/error/empty states are covered;
- status colors follow semantic variants;
- keyboard focus remains visible;
- no unimplemented controls are exposed;
- business logic and RBAC behavior remain unchanged;
- the view works at supported desktop window sizes.
