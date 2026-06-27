# Depot Coding Standard

## General

* All code is written in English.
* UI texts are written in English.
* One class per file.
* Every file starts with the project copyright header.
* Indentation uses tabs only.
* Always use file-scoped namespaces.
* Always use explicit access modifiers.
* Never use regions.

---

# Architecture

View

↓

ViewModel

↓

Service

↓

Repository

↓

SQLite

Rules:

* ViewModels never access repositories.
* Services never access ViewModels.
* Repositories never access services.
* Business logic belongs into services.
* SQL belongs into repositories only.

---

# ViewModels

ViewModels only:

* expose data
* expose commands
* prepare data for the UI

ViewModels never:

* access SQLite
* perform business logic
* show dialogs
* contain SQL

---

# Services

Services contain business logic.

Services may use:

* repositories
* domain models

Services never use:

* ViewModels
* WPF controls

---

# Repositories

Repositories only perform persistence.

Responsibilities:

* SQL
* CRUD
* Mapping

Repositories never:

* validate business rules
* perform calculations

---

# Models

Models represent the business domain.

Models never contain UI logic.

---

# Commits

One commit = one idea.

Examples:

✔ Rename InventoryService to ItemService

✔ Add movement service

✔ Add dashboard

Avoid commits like:

"Refactoring + UI + Import"

---

# Naming

Classes

PascalCase

Methods

PascalCase

Properties

PascalCase

Private fields

_leadingCamelCase

Local variables

camelCase

Constants

PascalCase

---

# Documentation

Every new public class should contain an XML summary.

Example:

/// <summary>
/// Provides business logic for stock movements.
/// </summary>

Every public method should have a summary if its purpose is not immediately obvious.

---

# Nullability

Nullable reference types remain enabled.

Avoid null-forgiving operators unless unavoidable.

Prefer explicit null handling.

---

# Exceptions

Use exceptions only for exceptional situations.

Validation messages should be meaningful and actionable.

---

# UI

Avoid code-behind.

Prefer reusable UserControls.

Avoid hardcoded colors in Views whenever a shared resource is appropriate.

Use application-wide styles whenever possible.

---

# Future

Prefer evolution over rewrites.

Small refactorings are preferred over large redesigns.

Always keep the solution buildable.
