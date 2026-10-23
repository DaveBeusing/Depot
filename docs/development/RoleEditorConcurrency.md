# Role editor selection and mutation consistency

The Role Editor and Permission Matrix share one selection request generation. A new selection, New/Cancel, a cleared selection, or disposal cancels and invalidates the previous generation. A response must still belong to the current generation and selected role before it can change the editor, direct permissions, effective-user impact, or operation feedback. The generation check remains required even when a data source ignores cancellation.

Selecting an existing role immediately clears the previous editor and disables editing, Save, and activation until that role's current details have been loaded. Command handlers also check this eligibility before constructing a mutation. A failed or cancelled detail request leaves no editable target. Protected system roles remain read-only; `RoleService` continues to enforce authorization and system-role protection independently of the UI.

Save and activation capture the loaded role identity, optimistic version and relevant values before awaiting the service. They prevent overlapping editor mutations. Changing selection while a mutation is running does not cancel the requested persistence operation. Its completion may update the corresponding list record but cannot reselect that role, replace a newer draft, apply old effective-user impact, or display an old error over the current selection.

Permission-matrix refreshes update persisted matrix projections only. They do not apply their snapshots to the mutable editor. List refreshes preserve the current selected object when its ID is still visible, keeping the highlighted row, loaded permission baseline and unsaved draft consistent. A selection outside the filtered page remains an explicit selection without inserting an unrelated row into the search results.

`IRoleEditorService` exposes the existing role-service operations consumed by the view model. `RoleService` remains the production implementation; repository access, authorization and write transactions stay in their existing layers.

## Regression coverage

`RoleSelectionConcurrencyTests` uses controlled task completion rather than timing delays. It covers reversed detail completion order, A → B → A selection, New/clear/dispose, cancellation-ignoring sources, stale errors and impact, pending-command rejection, current-load failure, matrix/list selection interleaving, draft-preserving refreshes, late save/activation completion and failure, and protected system roles. The tests assert the actual outgoing role ID, version and permissions for mutation calls.

The focused selection suite runs under the normal Core regression area. The existing permission-designer contract and projection tests remain applicable. Windows/.NET 10 build and test execution are required to validate the implementation.
