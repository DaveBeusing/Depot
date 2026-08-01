// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public enum ApplicationPermission
{
	PurchaseOrdersCreate,
	PurchaseOrdersEdit,
	PurchaseOrdersSubmit,
	PurchaseOrdersApprove,
	PurchaseOrdersOrder,
	PurchaseOrdersClose,
	MaterialIssuesCreate,
	MaterialIssuesPost,
	MaterialIssuesReverse,
	MaterialReturnsCreate,
	MaterialReturnsPost,
	SupplierReturnsCreate,
	SupplierReturnsPost
}
