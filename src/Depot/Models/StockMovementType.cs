// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public enum StockMovementType
{
	OpeningBalance = 1,
	Purchase = 2,
	Withdrawal = 3,
	Correction = 4,
	Transfer = 5,
	TransferOut = 6,
	TransferIn = 7,
	Reversal = 8,
	MaterialReturn = 9,
	SupplierReturn = 10
}
