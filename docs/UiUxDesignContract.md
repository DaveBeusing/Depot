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

Low-frequency actions such as Duplicate, Archive, History and advanced operations may move to overflow menus when that improves clarity. Do not hide actions required for the primary workflow.

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

## Dashboard

The Dashboard is an operational attention surface, not a duplicate module menu. Its hierarchy is:

1. Needs attention;
2. Key metrics and workspaces;
3. Recent activity.

Dashboard cards may navigate to the corresponding established workspace but must not replace the Activity Bar as the primary module navigation model.

## Master-detail and inspector

Use master-detail for workflows where users repeatedly select records and inspect/edit details. Use an inspector for quick read-only or low-complexity detail when opening a full workspace would create unnecessary navigation.

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
