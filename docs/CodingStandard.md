# Depot Coding Standard

## General

- Code and UI text are written in English.
- Nullable reference types remain enabled.
- Use file-scoped namespaces, explicit access modifiers, tabs, and the existing copyright header.
- Prefer `sealed`, `readonly`, and immutable projections where appropriate.
- Do not suppress nullable warnings with the null-forgiving operator; fix the source of the warning.
- Keep every commit focused on one feature, fix, refactoring, or documentation change.
- Keep the solution buildable with zero warnings.

## Architecture

```text
View
  |
ViewModel
  |
Service
  |
Repository
  |
DatabaseAccess
  |
SQLite / Microsoft SQL Server / MySQL or MariaDB
```

- Views contain layout and bindings only.
- ViewModels contain presentation state and commands.
- Services contain validation and business workflows.
- Repositories contain SQL, persistence, and mapping only.
- `App.xaml.cs` is the composition root.

Do not bypass a layer. ViewModels must not access repositories, connections, or SQL. Services must not reference ViewModels or WPF controls. Repositories must not reference ViewModels and should not contain business policy.

## Data access

- All repositories use the shared `DatabaseAccess` layer.
- Repository SQL must remain compatible with SQLite, SQL Server, and MySQL/MariaDB or use an explicit provider abstraction already present in `Data/`.
- Do not invent provider APIs in repositories.
- Prefer asynchronous repository methods with `CancellationToken`.
- Use server-side paging, filtering, aggregation, or streaming for potentially large data sets.
- Do not introduce new unbounded `GetAll()` calls in interactive or report paths.
- Multi-record business changes use one write transaction.
- Concurrent stock or receipt changes must acquire the provider-specific lock through the connection factory.
- Mutable records use version-based optimistic concurrency where stale writes are possible.
- Never log connection strings, passwords, encrypted payloads, or other credentials.

## ViewModels

ViewModels may:

- expose observable state and commands;
- coordinate services;
- maintain loading, saving, empty, and error states;
- debounce search requests;
- cancel superseded work;
- use UI abstractions such as `IFileDialogService`.

ViewModels must not:

- contain SQL or open database connections;
- implement domain validation that belongs in a service;
- construct provider-specific objects;
- directly create WPF message boxes or native dialogs.

## Services

- Validate required values, ranges, references, activation state, and legal status transitions.
- Coordinate repositories and application workflows.
- Preserve atomicity when a workflow updates multiple records.
- Produce clear, actionable errors.
- Create audit information for relevant business changes.

## Repositories

- Own SQL and database-to-model mapping.
- Use parameterized SQL exclusively.
- Keep provider-neutral SQL portable through the existing normalizing connection wrappers.
- Return bounded pages or streams for large collections.
- Do not perform UI decisions or domain-specific presentation formatting.

## Models

- Models represent domain data and status.
- Relationships use identifiers and explicit models rather than duplicated free-text fields when normalized master data exists.
- Master data is deactivated, not hard-deleted.
- Child records that only belong to an editable draft may be removed when the business workflow permits it.

## UI and design system

- Reuse resources from `src/Depot/Resources` and controls from `src/Depot/Controls`.
- Do not hardcode feature-specific colors, spacing, radii, shadows, or typography in views.
- Prefer generic reusable controls over feature-specific duplication.
- Preserve keyboard focus, Tab navigation, loading feedback, and accessible labels.
- Code-behind is limited to `InitializeComponent` and view-only behavior that cannot reasonably be expressed through bindings.

## Tests

- Add tests for migrations, validation, atomic workflows, and concurrency-sensitive behavior.
- SQLite integration tests are required for provider-neutral persistence changes.
- Provider-specific SQL normalization should have unit coverage.
- Changes claimed as production-ready for SQL Server or MySQL/MariaDB require live-server acceptance evidence in addition to compilation and unit tests.
- A successful build must have zero compiler, nullable, XAML, and analyzer warnings.

## Documentation

- Keep application version and database schema version distinct.
- Update all provider and schema references when database capabilities change.
- Distinguish implemented code, partial implementation, unstarted work, and release verification.
- Do not describe a feature as production-ready without code, automated tests where practical, and required environment-specific acceptance testing.
