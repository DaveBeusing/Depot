# Depot Architecture

## Overview

Depot follows a layered architecture.

```text
Presentation
────────────────────────────

Views

ViewModels

↓

Business

Services

↓

Persistence

Repositories

↓

SQLite
```

Every layer has a single responsibility.

---

# Presentation Layer

Contains all WPF related code.

Responsibilities:

* Views
* ViewModels
* Commands
* Converters

Rules:

* ViewModels never access repositories.
* ViewModels only communicate with services.
* ViewModels never contain SQL.
* ViewModels never show MessageBoxes.

---

# Business Layer

Contains all business logic.

Responsibilities:

* Item management
* Inventory calculations
* Stock movements
* Import
* Dashboard

Rules:

* Services may use repositories.
* Services never reference ViewModels.
* Services only work with domain models.

---

# Persistence Layer

Contains database access.

Responsibilities:

* CRUD
* SQL
* Mapping

Rules:

* Repositories never reference ViewModels.
* Repositories never reference Services.

---

# Domain Model

Master Data

* Item
* Purpose
* Location

Operational Data

* Inventory
* StockMovement

Reporting

* DashboardSummary
* InventorySummary

Import

* ImportPreview
* ImportResult

---

# Navigation

Shell

* Dashboard
* Inventory
* Items
* Movements
* Administration

Administration

* Import
* Master Data
* Users
* Database
* Settings

---

# Project Rules

Every commit has exactly one purpose.

Always keep the solution buildable.

Never mix refactoring and new functionality in one commit.

Prefer adding new code over modifying existing code.

Refactor only after tests or manual verification.

Business logic belongs into services.

Repositories are only responsible for persistence.

ViewModels prepare data for the UI only.




```
Master Data
Item         CRUD
Purpose      CRUD
Location     CRUD

Operational Data
Inventory       Create + Deactivate
StockMovement   Create only
```
