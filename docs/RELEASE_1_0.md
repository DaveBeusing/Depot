# Depot Version 1.0 Release Checklist

## Status

- [ ] Ready for Release

---

# 1. Application

## Startup

- [ ] Application starts without an existing database.
- [ ] Database is created automatically.
- [ ] Default administrator exists.
- [ ] Login window is shown.
- [ ] Main window opens after successful login.
- [ ] Logout returns to login window.
- [ ] Login as another user works.

---

# 2. Authentication

- [ ] Administrator login
- [ ] User login
- [ ] Administrator sees Administration section.
- [ ] User does not see Administration section.
- [ ] Current user is shown in the sidebar.
- [ ] Logout works.

---

# 3. Items

- [ ] Create
- [ ] Edit
- [ ] Search
- [ ] Deactivate

---

# 4. Purposes

- [ ] Create
- [ ] Edit
- [ ] Activate
- [ ] Deactivate

---

# 5. Locations

- [ ] Create
- [ ] Edit
- [ ] Activate
- [ ] Deactivate

---

# 6. Users

- [ ] Create
- [ ] Edit
- [ ] Activate
- [ ] Deactivate
- [ ] Change administrator flag
- [ ] User name cannot be changed.
- [ ] Current user cannot deactivate himself.

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

- [ ] Preview
- [ ] Validation
- [ ] Duplicate detection
- [ ] Purpose import
- [ ] Location import
- [ ] Inventory creation
- [ ] Opening balance creation
- [ ] Warnings are displayed correctly.

---

# 10. Reports

- [ ] Inventory Value
- [ ] Stock by Purpose
- [ ] Stock by Location
- [ ] Stock by Category
- [ ] Stock by Manufacturer
- [ ] Excel export

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