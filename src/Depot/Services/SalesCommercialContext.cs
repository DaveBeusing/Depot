// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Services;

internal static class SalesCommercialContext
{
	public static CustomerService Customers { get; private set; } = null!;
	public static SalesPricingService Pricing { get; private set; } = null!;
	public static SalesTimelineService Timeline { get; private set; } = null!;
	public static SalesQuoteService Quotes { get; private set; } = null!;
	public static ShipmentPackingService Packing { get; private set; } = null!;
	public static ItemService Items { get; private set; } = null!;
	public static IFileDialogService FileDialogs { get; private set; } = null!;
	public static SalesDocumentService Documents { get; } = new();
	public static SalesDocumentEmailService Email { get; } = new();
	public static bool DomainConfigured { get; private set; }
	public static bool UiConfigured { get; private set; }

	public static void ConfigureDomain(CustomerService customers, SalesPricingService pricing, SalesTimelineService timeline, SalesQuoteService quotes, ShipmentPackingService packing)
	{
		Customers = customers;
		Pricing = pricing;
		Timeline = timeline;
		Quotes = quotes;
		Packing = packing;
		DomainConfigured = true;
	}

	public static void ConfigureUi(ItemService items, IFileDialogService fileDialogs)
	{
		Items = items;
		FileDialogs = fileDialogs;
		UiConfigured = true;
	}
}
