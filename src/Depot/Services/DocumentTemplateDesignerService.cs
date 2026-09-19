// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.DocumentRendering;
using Depot.Models;

namespace Depot.Services;

public sealed class DocumentTemplateDesignerService
{
	private static readonly byte[] SampleLogo = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl6Ih8AAAAASUVORK5CYII=");
	private readonly IAuthorizationService _authorization;
	private readonly DocumentTemplateRuntimeCatalog _catalog;
	private readonly DocumentLayoutRenderer _renderer;

	public DocumentTemplateDesignerService(
		IAuthorizationService authorization,
		DocumentTemplateRuntimeCatalog? catalog = null,
		DocumentLayoutRenderer? renderer = null)
	{
		_authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
		_catalog = catalog ?? DefaultDocumentTemplates.Runtime;
		_renderer = renderer ?? new DocumentLayoutRenderer();
	}

	public bool CanView => _authorization.HasPermission(ApplicationPermission.DocumentTemplatesView);
	public bool CanManage => _authorization.HasPermission(ApplicationPermission.DocumentTemplatesManage);
	public IReadOnlyCollection<string> Bindings => DocumentTemplateBindings.All;

	public IReadOnlyList<DocumentTemplate> ListVersions(DocumentTemplateType type)
	{
		_authorization.RequirePermission(ApplicationPermission.DocumentTemplatesView);
		return _catalog.ListVersions(type);
	}

	public DocumentTemplate GetDefault(DocumentTemplateType type)
	{
		_authorization.RequirePermission(ApplicationPermission.DocumentTemplatesView);
		return _catalog.GetDefault(type);
	}

	public DocumentTemplate SaveDraft(DocumentTemplate template)
	{
		_authorization.RequirePermission(ApplicationPermission.DocumentTemplatesManage);
		return _catalog.SaveDraft(template);
	}

	public DocumentTemplate Activate(DocumentTemplateType type, int version)
	{
		_authorization.RequirePermission(ApplicationPermission.DocumentTemplatesManage);
		return _catalog.Activate(type, version);
	}

	public DocumentTemplate ResetToDefault(DocumentTemplateType type)
	{
		_authorization.RequirePermission(ApplicationPermission.DocumentTemplatesManage);
		return _catalog.ResetToDefault(type);
	}

	public byte[] Preview(DocumentTemplate template)
	{
		_authorization.RequirePermission(ApplicationPermission.DocumentTemplatesView);
		DocumentTemplateValidator.ValidateAndThrow(template);
		return _renderer.RenderToBytes(template, CreateSampleModel(template.Type));
	}

	public IReadOnlyList<string> Validate(DocumentTemplate template) => DocumentTemplateValidator.Validate(template);

	private static DocumentRenderModel CreateSampleModel(DocumentTemplateType type)
	{
		var values = new Dictionary<string, object?>(StringComparer.Ordinal)
		{
			["Company.Name"] = "Depot Sample GmbH",
			["Company.Address"] = "Example Street 1\n53111 Bonn",
			["Company.LegalLine"] = "HRB 12345 · VAT DE123456789",
			["Company.ContactLine"] = "office@example.invalid · +49 228 000000",
			["Company.Logo"] = SampleLogo,
			["Document.Title"] = type.ToString(),
			["Document.Number"] = "SAMPLE-0001",
			["Document.Date"] = new DateTime(2026, 9, 19),
			["Document.ValidUntil"] = new DateTime(2026, 10, 19),
			["Document.Contact"] = "Sample Contact",
			["Document.CustomerReference"] = "PO-EXAMPLE",
			["Document.Status"] = "Preview",
			["Document.Currency"] = "EUR",
			["Document.RequestedDeliveryDate"] = new DateTime(2026, 9, 30),
			["Document.SalesOrderNumber"] = "SO-0001",
			["Document.PackingStatus"] = "Ready",
			["Document.Carrier"] = "Sample Carrier",
			["Document.TrackingNumber"] = "TRACK-0001",
			["Document.PackedAt"] = "2026-09-19 10:30",
			["Document.ShipmentNumber"] = "SHIP-0001",
			["Document.DueDate"] = new DateTime(2026, 10, 19),
			["Document.InvoiceNumber"] = "INV-0001",
			["Document.Reason"] = "Sample reason",
			["Document.Net"] = "100.00 EUR",
			["Document.Tax"] = "19.00 EUR",
			["Document.Total"] = "119.00 EUR",
			["Customer.Name"] = "Sample Customer GmbH",
			["Customer.Address"] = "Customer Road 2\n50667 Köln",
			["Customer.BillingAddress"] = "Customer Road 2\n50667 Köln",
			["Customer.ShippingAddress"] = "Warehouse Lane 4\n50670 Köln"
		};
		var lines = Enumerable.Range(1, 5).Select(index =>
			new DocumentRenderRow(new Dictionary<string, object?>(StringComparer.Ordinal)
			{
				["Line.Item"] = $"ITEM-{index:000}",
				["Line.Description"] = $"Sample line {index}",
				["Line.Quantity"] = index.ToString(),
				["Line.UnitPrice"] = "20.00 EUR",
				["Line.TaxRate"] = "19%",
				["Line.Total"] = "23.80 EUR"
			})).ToArray();
		return new DocumentRenderModel("Depot document designer preview", "Safe sample data", "Depot", values, lines);
	}
}
