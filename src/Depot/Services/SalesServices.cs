// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Services;

public sealed class SalesServices
{
	public SalesServices(
		CustomerService customers,
		SalesPricingService pricing,
		SalesTimelineService timeline,
		SalesOrderService orders,
		SalesQuoteService quotes,
		ShipmentService shipments,
		ShipmentPackingService packing,
		SalesInvoiceService invoices,
		CustomerReturnService customerReturns,
		SalesCreditNoteService creditNotes,
		ItemService items,
		IAuthorizationService authorization,
		SalesDocumentService documents,
		SalesDocumentEmailService email,
		SalesInvoiceFinalizationService invoiceFinalizations,
		ItemCostCalculationService itemCosts,
		PriceListGenerationService priceListGeneration)
	{
		Customers = customers;
		Pricing = pricing;
		Timeline = timeline;
		Orders = orders;
		Quotes = quotes;
		Shipments = shipments;
		Packing = packing;
		Invoices = invoices;
		CustomerReturns = customerReturns;
		CreditNotes = creditNotes;
		Items = items;
		Authorization = authorization;
		Documents = documents;
		Email = email;
		InvoiceFinalizations = invoiceFinalizations;
		ItemCosts = itemCosts;
		PriceListGeneration = priceListGeneration;
	}

	public CustomerService Customers { get; }
	public SalesPricingService Pricing { get; }
	public SalesTimelineService Timeline { get; }
	public SalesOrderService Orders { get; }
	public SalesQuoteService Quotes { get; }
	public ShipmentService Shipments { get; }
	public ShipmentPackingService Packing { get; }
	public SalesInvoiceService Invoices { get; }
	public CustomerReturnService CustomerReturns { get; }
	public SalesCreditNoteService CreditNotes { get; }
	public ItemService Items { get; }
	public IAuthorizationService Authorization { get; }
	public SalesDocumentService Documents { get; }
	public SalesDocumentEmailService Email { get; }
	public SalesInvoiceFinalizationService InvoiceFinalizations { get; }
	public ItemCostCalculationService ItemCosts { get; }
	public PriceListGenerationService PriceListGeneration { get; }
}
