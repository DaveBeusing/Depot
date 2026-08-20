// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Services;

internal static class SalesCommercialContext
{
	public static SalesPricingService Pricing { get; private set; } = null!;
	public static SalesTimelineService Timeline { get; private set; } = null!;
	public static SalesQuoteService Quotes { get; private set; } = null!;
	public static ShipmentPackingService Packing { get; private set; } = null!;
	public static CustomerService Customers { get; private set; } = null!;
	public static ItemService Items { get; private set; } = null!;
	public static IFileDialogService FileDialogs { get; private set; } = null!;
	public static SalesDocumentEmailService Email { get; private set; } = new();
	public static SalesDocumentService Documents { get; private set; } = new();
	public static bool IsConfigured { get; private set; }
	public static bool IsUiConfigured { get; private set; }

	public static void Configure(
		SalesPricingService pricing,
		SalesTimelineService timeline,
		SalesQuoteService quotes,
		ShipmentPackingService packing,
		SalesDocumentService? documents = null,
		SalesDocumentEmailService? email = null)
	{
		Pricing = pricing;
		Timeline = timeline;
		Quotes = quotes;
		Packing = packing;
		Documents = documents ?? Documents;
		Email = email ?? Email;
		IsConfigured = true;
	}

	public static void ConfigureUi(CustomerService customers, ItemService items, IFileDialogService fileDialogs)
	{
		Customers = customers;
		Items = items;
		FileDialogs = fileDialogs;
		IsUiConfigured = true;
	}
}
