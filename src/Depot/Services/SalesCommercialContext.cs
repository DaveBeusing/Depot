// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Services;

internal static class SalesCommercialContext
{
	public static SalesPricingService Pricing { get; private set; } = null!;
	public static SalesTimelineService Timeline { get; private set; } = null!;
	public static SalesQuoteService Quotes { get; private set; } = null!;
	public static ShipmentPackingService Packing { get; private set; } = null!;
	public static bool IsConfigured { get; private set; }

	public static void Configure(SalesPricingService pricing, SalesTimelineService timeline, SalesQuoteService quotes, ShipmentPackingService packing)
	{
		Pricing = pricing;
		Timeline = timeline;
		Quotes = quotes;
		Packing = packing;
		IsConfigured = true;
	}
}
