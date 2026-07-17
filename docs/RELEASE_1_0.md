# Depot Version 1.0 Release Checklist

## Status

- [ ] Ready for Release

---

# 1. Application

## Startup

- [x] Application starts without an existing database.
- [x] Database is created automatically.
- [x] Default administrator exists.
- [x] Login window is shown.
- [x] Main window opens after successful login.
- [x] Logout returns to login window.
- [x] Login as another user works.

---

# 2. Authentication

- [x] Administrator login
- [x] User login
- [x] Administrator sees Administration section.
- [x] User does not see Administration section.
- [x] Current user is shown in the sidebar.
- [x] Logout works.

---

# 3. Items

- [x] Create
- [x] Edit
- [x] Search
- [x] Deactivate

---

# 4. Purposes

- [x] Create
- [x] Edit
- [x] Activate
- [x] Deactivate

---

# 5. Locations

- [x] Create
- [x] Edit
- [x] Activate
- [x] Deactivate

---

# 6. Users

- [x] Create
- [x] Edit
- [x] Activate
- [x] Deactivate
- [x] Change administrator flag
- [x] Current user cannot deactivate himself.

---

# 7. Inventory

- [ ] Inventory is created correctly.
- [ ] Multiple inventories per item work.
- [ ] Purpose assignment works.
- [ ] Location assignment works.

---

# 8. Stock Movements

- [ ] Goods receipt
- [ ] Goods issue
- [ ] Inventory correction
- [ ] Opening balance
- [ ] Average cost calculation
- [ ] Current stock calculation

---

# 9. Import

- [x] Preview
- [x] Validation
- [x] Duplicate detection
- [x] Purpose import
- [x] Location import
- [x] Inventory creation
- [x] Opening balance creation
- [x] Warnings are displayed correctly.

---

# 10. Reports

- [x] Inventory Value
- [x] Stock by Purpose
- [x] Stock by Location
- [x] Stock by Category
- [x] Stock by Manufacturer
- [x] Excel export

---

# 11. Database

- [ ] Backup
- [ ] Restore
- [ ] Compact
- [ ] Integrity Check

---

# 12. User Interface

- [ ] No layout issues
- [ ] No broken bindings
- [ ] No runtime exceptions
- [ ] No startup exceptions
- [ ] No build warnings

---

# 13. Release

- [ ] Build succeeds
- [ ] All manual tests passed
- [ ] Version number updated
- [ ] Release notes written