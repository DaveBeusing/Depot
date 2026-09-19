// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.DocumentRendering;

public static class DefaultDocumentTemplates
{
	private static readonly Lazy<IReadOnlyList<DocumentTemplate>> DefaultsFactory = new(BuildTemplates);
	private static readonly Lazy<DocumentTemplateRuntimeCatalog> RuntimeFactory = new(() => new DocumentTemplateRuntimeCatalog(DefaultsFactory.Value));

	public static DocumentTemplateCatalog Catalog => RuntimeFactory.Value.Snapshot;
	public static IReadOnlyList<DocumentTemplate> Defaults => DefaultsFactory.Value;
	internal static DocumentTemplateRuntimeCatalog Runtime => RuntimeFactory.Value;

	public static DocumentTemplate GetDefault(DocumentTemplateType type) =>
		DefaultsFactory.Value.Single(template => template.Type == type);

	private static IReadOnlyList<DocumentTemplate> BuildTemplates() =>
	[
		CreateCommercial(
			"sales-quote-default",
			DocumentTemplateType.SalesQuote,
			"QUOTE",
			"Customer",
			"Customer.BillingAddress",
			[
				("Valid until", "Document.ValidUntil"),
				("Contact", "Document.Contact"),
				("Customer reference", "Document.CustomerReference"),
				("Status", "Document.Status")
			],
			FinancialColumns("Total"),
			includeTotals: true),
		CreateCommercial(
			"sales-order-confirmation-default",
			DocumentTemplateType.SalesOrderConfirmation,
			"ORDER CONFIRMATION",
			"Customer",
			"Customer.BillingAddress",
			[
				("Customer reference", "Document.CustomerReference"),
				("Requested delivery", "Document.RequestedDeliveryDate"),
				("Status", "Document.Status"),
				("Currency", "Document.Currency")
			],
			FinancialColumns("Total"),
			includeTotals: true),
		CreateOperational(
			"pick-list-default",
			DocumentTemplateType.PickList,
			"PICK LIST",
			null,
			null,
			[
				("Sales order", "Document.SalesOrderNumber"),
				("Packing status", "Document.PackingStatus"),
				("Carrier", "Document.Carrier"),
				("Tracking", "Document.TrackingNumber")
			],
			ShipmentColumns("Quantity")),
		CreateOperational(
			"packing-slip-default",
			DocumentTemplateType.PackingSlip,
			"PACKING SLIP",
			"Ship to",
			"Customer.ShippingAddress",
			[
				("Sales order", "Document.SalesOrderNumber"),
				("Carrier", "Document.Carrier"),
				("Tracking", "Document.TrackingNumber"),
				("Packed", "Document.PackedAt")
			],
			ShipmentColumns("Quantity")),
		CreateOperational(
			"delivery-note-default",
			DocumentTemplateType.DeliveryNote,
			"DELIVERY NOTE",
			"Ship to",
			"Customer.ShippingAddress",
			[
				("Sales order", "Document.SalesOrderNumber"),
				("Carrier", "Document.Carrier"),
				("Tracking", "Document.TrackingNumber"),
				("Status", "Document.Status")
			],
			ShipmentColumns("Quantity")),
		CreateCommercial(
			"sales-invoice-default",
			DocumentTemplateType.SalesInvoice,
			"INVOICE",
			"Bill to",
			"Customer.BillingAddress",
			[
				("Sales order", "Document.SalesOrderNumber"),
				("Shipment", "Document.ShipmentNumber"),
				("Customer reference", "Document.CustomerReference"),
				("Due date", "Document.DueDate")
			],
			FinancialColumns("Total"),
			includeTotals: true),
		CreateCommercial(
			"credit-note-default",
			DocumentTemplateType.CreditNote,
			"CREDIT NOTE",
			"Customer",
			"Customer.BillingAddress",
			[
				("Invoice", "Document.InvoiceNumber"),
				("Sales order", "Document.SalesOrderNumber"),
				("Status", "Document.Status"),
				("Reason", "Document.Reason")
			],
			FinancialColumns("Credit"),
			includeTotals: true),
		CreateOperational(
			"customer-return-default",
			DocumentTemplateType.CustomerReturn,
			"CUSTOMER RETURN",
			"Customer",
			"Customer.Address",
			[
				("Shipment", "Document.ShipmentNumber"),
				("Sales order", "Document.SalesOrderNumber"),
				("Status", "Document.Status"),
				("Reason", "Document.Reason")
			],
			ShipmentColumns("Returned"))
	];

	private static DocumentTemplate CreateCommercial(
		string id,
		DocumentTemplateType type,
		string title,
		string addressHeading,
		string addressBinding,
		IReadOnlyList<(string Label, string Binding)> metadata,
		IReadOnlyList<DocumentTemplateColumn> columns,
		bool includeTotals)
	{
		var elements = CommonHeader(title).ToList();
		elements.AddRange(Address(addressHeading, addressBinding, 146));
		elements.AddRange(Metadata(metadata, 226));
		elements.Add(new DocumentTemplateElement
		{
			Id = "lines",
			Type = DocumentTemplateElementType.LineTable,
			X = 40,
			Y = 270,
			Width = 515,
			Height = 20,
			FontSize = 9,
			RowHeight = 20,
			Columns = columns
		});
		if (includeTotals)
		{
			elements.Add(new DocumentTemplateElement
			{
				Id = "totals",
				Type = DocumentTemplateElementType.TotalsBlock,
				X = 370,
				Y = 10,
				Width = 185,
				Height = 64,
				Placement = DocumentTemplatePlacement.Flow
			});
		}
		elements.AddRange(CommonFooter());
		return new DocumentTemplate { Id = id, Type = type, Version = 1, IsActive = true, Elements = elements };
	}

	private static DocumentTemplate CreateOperational(
		string id,
		DocumentTemplateType type,
		string title,
		string? addressHeading,
		string? addressBinding,
		IReadOnlyList<(string Label, string Binding)> metadata,
		IReadOnlyList<DocumentTemplateColumn> columns)
	{
		var elements = CommonHeader(title).ToList();
		var metadataY = 150d;
		var tableY = 206d;
		if (!string.IsNullOrWhiteSpace(addressHeading) && !string.IsNullOrWhiteSpace(addressBinding))
		{
			elements.AddRange(Address(addressHeading, addressBinding, 146));
			metadataY = 226;
			tableY = 270;
		}
		elements.AddRange(Metadata(metadata, metadataY));
		elements.Add(new DocumentTemplateElement
		{
			Id = "lines",
			Type = DocumentTemplateElementType.LineTable,
			X = 40,
			Y = tableY,
			Width = 515,
			Height = 20,
			FontSize = 9,
			RowHeight = 20,
			Columns = columns
		});
		elements.AddRange(CommonFooter());
		return new DocumentTemplate { Id = id, Type = type, Version = 1, IsActive = true, Elements = elements };
	}

	private static IEnumerable<DocumentTemplateElement> CommonHeader(string title)
	{
		yield return Bound("company-name", "Company.Name", 40, 34, 260, 18, 11, DocumentTemplateFontWeight.Bold);
		yield return Bound("company-address", "Company.Address", 40, 50, 320, 26, 8);
		yield return new DocumentTemplateElement
		{
			Id = "document-title",
			Type = DocumentTemplateElementType.Text,
			Text = title,
			X = 40,
			Y = 78,
			Width = 300,
			Height = 24,
			FontSize = 18,
			FontWeight = DocumentTemplateFontWeight.Bold
		};
		yield return Bound("document-number", "Document.Number", 40, 103, 220, 16, 11, DocumentTemplateFontWeight.Bold);
		yield return Bound("document-date", "Document.Date", 455, 41, 100, 15, 9, alignment: DocumentTemplateAlignment.Right);
		yield return Bound("customer-name-top", "Customer.Name", 340, 103, 215, 16, 9, alignment: DocumentTemplateAlignment.Right);
		yield return new DocumentTemplateElement
		{
			Id = "header-rule",
			Type = DocumentTemplateElementType.Line,
			X = 40,
			Y = 120,
			Width = 515,
			Height = 0
		};
	}

	private static IEnumerable<DocumentTemplateElement> Address(string heading, string binding, double y)
	{
		yield return new DocumentTemplateElement
		{
			Id = "address-heading",
			Type = DocumentTemplateElementType.Text,
			Text = heading,
			X = 40,
			Y = y,
			Width = 180,
			Height = 16,
			FontSize = 11,
			FontWeight = DocumentTemplateFontWeight.Bold
		};
		yield return Bound("address-customer", "Customer.Name", 40, y + 18, 260, 14, 9);
		yield return Bound("address-value", binding, 40, y + 32, 320, 48, 9);
	}

	private static IEnumerable<DocumentTemplateElement> Metadata(IReadOnlyList<(string Label, string Binding)> metadata, double y)
	{
		for (var index = 0; index < metadata.Count; index++)
		{
			var x = 40 + index * 128d;
			yield return new DocumentTemplateElement
			{
				Id = $"metadata-{index}-label",
				Type = DocumentTemplateElementType.Text,
				Text = metadata[index].Label,
				X = x,
				Y = y,
				Width = 120,
				Height = 12,
				FontSize = 8
			};
			yield return Bound($"metadata-{index}-value", metadata[index].Binding, x, y + 14, 120, 15, 9);
		}
	}

	private static IEnumerable<DocumentTemplateElement> CommonFooter()
	{
		yield return new DocumentTemplateElement
		{
			Id = "footer-rule",
			Type = DocumentTemplateElementType.Line,
			X = 40,
			Y = 790,
			Width = 515,
			Height = 0,
			RepeatOnEveryPage = true
		};
		yield return Bound("footer-legal", "Company.LegalLine", 40, 800, 430, 12, 8, repeat: true);
		yield return Bound("footer-contact", "Company.ContactLine", 40, 811, 430, 12, 8, repeat: true);
		yield return new DocumentTemplateElement
		{
			Id = "page-number",
			Type = DocumentTemplateElementType.PageNumber,
			X = 470,
			Y = 811,
			Width = 85,
			Height = 12,
			FontSize = 8,
			Alignment = DocumentTemplateAlignment.Right,
			Format = "Page {0} of {1}",
			RepeatOnEveryPage = true
		};
	}

	private static DocumentTemplateElement Bound(
		string id,
		string binding,
		double x,
		double y,
		double width,
		double height,
		double fontSize,
		DocumentTemplateFontWeight weight = DocumentTemplateFontWeight.Regular,
		DocumentTemplateAlignment alignment = DocumentTemplateAlignment.Left,
		bool repeat = false) =>
		new()
		{
			Id = id,
			Type = DocumentTemplateElementType.BoundText,
			Binding = binding,
			X = x,
			Y = y,
			Width = width,
			Height = height,
			FontSize = fontSize,
			FontWeight = weight,
			Alignment = alignment,
			RepeatOnEveryPage = repeat
		};

	private static IReadOnlyList<DocumentTemplateColumn> FinancialColumns(string totalHeader) =>
	[
		new("Item", "Line.Item", 85),
		new("Description", "Line.Description", 220),
		new("Qty", "Line.Quantity", 50, DocumentTemplateAlignment.Right),
		new("Unit", "Line.UnitPrice", 70, DocumentTemplateAlignment.Right),
		new("Tax", "Line.TaxRate", 40, DocumentTemplateAlignment.Right),
		new(totalHeader, "Line.Total", 50, DocumentTemplateAlignment.Right)
	];

	private static IReadOnlyList<DocumentTemplateColumn> ShipmentColumns(string quantityHeader) =>
	[
		new("Item", "Line.Item", 110),
		new("Description", "Line.Description", 330),
		new(quantityHeader, "Line.Quantity", 75, DocumentTemplateAlignment.Right)
	];
}
