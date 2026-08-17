// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Text.Json;

using Depot.Models;
using Depot.ViewModels.Administration;
using Depot.ViewModels.Suppliers;
using Depot.ViewModels.Users;

namespace Depot.ViewModels;

public sealed record UnsavedChangesInfo(string Name, Action Discard);

public static class UnsavedChangesGuard
{
	public static bool TryGet(BaseViewModel? viewModel, out UnsavedChangesInfo? info)
	{
		var target = Resolve(viewModel);
		info = target switch
		{
			ItemsViewModel items when HasItemChanges(items) => new("item", () => DiscardItemChanges(items)),
			ProcurementViewModel purchasing when HasPurchaseOrderChanges(purchasing) => new("purchase order", () => DiscardPurchaseOrderChanges(purchasing)),
			SupplierViewModel suppliers when HasSupplierChanges(suppliers) => new("supplier", () => DiscardSupplierChanges(suppliers)),
			RoleViewModel roles when HasRoleChanges(roles) => new("role", () => DiscardRoleChanges(roles)),
			_ => null
		};
		return info is not null;
	}

	public static BaseViewModel? Resolve(BaseViewModel? viewModel) => viewModel switch
	{
		ShellModuleViewModel module => Resolve(module.CurrentViewModel),
		AdministrationViewModel administration => Resolve(administration.CurrentViewModel),
		PurchaseOrdersPageViewModel page => page.Workflow,
		GoodsReceiptsPageViewModel page => page.Workflow,
		_ => viewModel
	};

	private static bool HasItemChanges(ItemsViewModel viewModel)
	{
		var editor = viewModel.Editor;
		var selected = viewModel.SelectedItem;
		if (selected is null)
			return editor.Id != 0 ||
				!string.IsNullOrWhiteSpace(editor.PartNumber) ||
				!string.IsNullOrWhiteSpace(editor.Description) ||
				editor.Manufacturer is not null || editor.Category is not null || editor.UnitOfMeasure is not null || editor.Packaging is not null;

		return editor.Id != selected.Id ||
			!string.Equals(editor.PartNumber, selected.PartNumber, StringComparison.Ordinal) ||
			!string.Equals(editor.Description, selected.Description, StringComparison.Ordinal) ||
			editor.Manufacturer?.Id != selected.ManufacturerId ||
			editor.Category?.Id != selected.CategoryId ||
			editor.UnitOfMeasure?.Id != selected.UnitOfMeasureId ||
			editor.Packaging?.Id != selected.PackagingId;
	}

	private static void DiscardItemChanges(ItemsViewModel viewModel)
	{
		if (viewModel.SelectedItem is not { } selected)
		{
			viewModel.NewItemCommand.Execute(null);
			return;
		}
		viewModel.SelectedItem = null;
		viewModel.SelectedItem = selected;
	}

	private static bool HasPurchaseOrderChanges(ProcurementViewModel viewModel)
	{
		if (!viewModel.IsPurchaseOrdersSection || !viewModel.IsDraft) return false;
		var draft = viewModel.Draft;
		var selected = viewModel.SelectedOrder;
		if (selected is null)
		{
			return draft.SupplierId != 0 ||
				!string.IsNullOrWhiteSpace(draft.Notes) ||
				draft.OrderDate.Date != DateTime.Today ||
				draft.ExpectedDeliveryDate?.Date != DateTime.Today.AddDays(7) ||
				viewModel.Lines.Count > 0;
		}

		return draft.SupplierId != selected.SupplierId ||
			draft.OrderDate != selected.OrderDate ||
			draft.ExpectedDeliveryDate != selected.ExpectedDeliveryDate ||
			!string.Equals(draft.Notes, selected.Notes, StringComparison.Ordinal) ||
			!PurchaseOrderLinesEqual(viewModel.Lines, draft.Lines);
	}

	private static bool PurchaseOrderLinesEqual(IReadOnlyCollection<PurchaseOrderLine> current, IReadOnlyList<PurchaseOrderLine> baseline)
	{
		if (current.Count != baseline.Count) return false;
		return current.Zip(baseline).All(pair =>
			pair.First.ItemId == pair.Second.ItemId &&
			pair.First.Quantity == pair.Second.Quantity &&
			pair.First.UnitPrice == pair.Second.UnitPrice &&
			pair.First.LineNumber == pair.Second.LineNumber);
	}

	private static void DiscardPurchaseOrderChanges(ProcurementViewModel viewModel)
	{
		if (viewModel.SelectedOrder is not { } selected)
		{
			viewModel.NewOrderCommand.Execute(null);
			return;
		}
		viewModel.SelectedOrder = null;
		viewModel.SelectedOrder = selected;
	}

	private static bool HasSupplierChanges(SupplierViewModel viewModel)
	{
		var supplierDirty = viewModel.SelectedSupplier is { } supplier
			? !Equivalent(viewModel.Draft, supplier)
			: !Equivalent(viewModel.Draft, new Supplier());
		if (supplierDirty) return true;

		var itemDraft = viewModel.SupplierItemDraft;
		if (viewModel.SelectedSupplierItem is { } supplierItem)
			return !Equivalent(itemDraft, supplierItem) || (viewModel.SelectedItemOption?.Id ?? itemDraft.ItemId) != supplierItem.ItemId;

		return !Equivalent(itemDraft, new SupplierItem { MinimumOrderQuantity = 1 }) || viewModel.SelectedItemOption is not null;
	}

	private static void DiscardSupplierChanges(SupplierViewModel viewModel)
	{
		var supplierDirty = viewModel.SelectedSupplier is { } supplier
			? !Equivalent(viewModel.Draft, supplier)
			: !Equivalent(viewModel.Draft, new Supplier());
		if (supplierDirty)
		{
			if (viewModel.SelectedSupplier is not { } selected) viewModel.NewSupplierCommand.Execute(null);
			else { viewModel.SelectedSupplier = null; viewModel.SelectedSupplier = selected; }
			return;
		}

		if (viewModel.SelectedSupplierItem is not { } selectedItem) viewModel.NewSupplierItemCommand.Execute(null);
		else { viewModel.SelectedSupplierItem = null; viewModel.SelectedSupplierItem = selectedItem; }
	}

	private static bool HasRoleChanges(RoleViewModel viewModel)
	{
		var selectedPermissions = viewModel.Permissions.Where(permission => permission.IsSelected).Select(permission => permission.Permission).Order().ToArray();
		if (viewModel.SelectedRole is not { } role)
			return !string.IsNullOrWhiteSpace(viewModel.Code) || !string.IsNullOrWhiteSpace(viewModel.Name) || !string.IsNullOrWhiteSpace(viewModel.Description) || selectedPermissions.Length > 0;

		return !string.Equals(viewModel.Code, role.Code, StringComparison.Ordinal) ||
			!string.Equals(viewModel.Name, role.Name, StringComparison.Ordinal) ||
			!string.Equals(viewModel.Description, role.Description, StringComparison.Ordinal) ||
			!selectedPermissions.SequenceEqual(role.Permissions.Order());
	}

	private static void DiscardRoleChanges(RoleViewModel viewModel)
	{
		if (viewModel.SelectedRole is not { } selected)
		{
			viewModel.NewCommand.Execute(null);
			return;
		}
		viewModel.SelectedRole = null;
		viewModel.SelectedRole = selected;
	}

	private static bool Equivalent<T>(T left, T right) =>
		string.Equals(JsonSerializer.Serialize(left), JsonSerializer.Serialize(right), StringComparison.Ordinal);
}
