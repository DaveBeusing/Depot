// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.DocumentRendering;
using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class SalesDocumentService
{
	private readonly CompanyDocumentIdentityService _issuerService;
	private readonly DocumentIssuerSnapshotService _issuerSnapshots;
	private readonly SalesInvoiceFinalizationService _invoiceFinalizations;
	private readonly DocumentTemplateCatalog _templates;
	private readonly DocumentLayoutRenderer _renderer;

	public SalesDocumentService()
	{
		var settingsService = new SettingsService(new SettingsRepository("depot.settings"));
		var settings = settingsService.LoadOrCreate();
		var dataAccess = new DatabaseAccess(DatabaseProviderFactory.CreateConnectionFactory(settings));
		_issuerService = new CompanyDocumentIdentityService(dataAccess, settings.Provider);
		_issuerSnapshots = new DocumentIssuerSnapshotService(dataAccess);
		_invoiceFinalizations = new SalesInvoiceFinalizationService(dataAccess);
		_templates = DefaultDocumentTemplates.Catalog;
		_renderer = new DocumentLayoutRenderer();
	}

	public SalesDocumentService(
		CompanyDocumentIdentityService issuerService,
		DocumentIssuerSnapshotService issuerSnapshots,
		SalesInvoiceFinalizationService invoiceFinalizations)
		: this(issuerService, issuerSnapshots, invoiceFinalizations, DefaultDocumentTemplates.Catalog, new DocumentLayoutRenderer())
	{
	}

	public SalesDocumentService(
		CompanyDocumentIdentityService issuerService,
		DocumentIssuerSnapshotService issuerSnapshots,
		SalesInvoiceFinalizationService invoiceFinalizations,
		DocumentTemplateCatalog templates,
		DocumentLayoutRenderer renderer)
	{
		_issuerService = issuerService ?? throw new ArgumentNullException(nameof(issuerService));
		_issuerSnapshots = issuerSnapshots ?? throw new ArgumentNullException(nameof(issuerSnapshots));
		_invoiceFinalizations = invoiceFinalizations ?? throw new ArgumentNullException(nameof(invoiceFinalizations));
		_templates = templates ?? throw new ArgumentNullException(nameof(templates));
		_renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
	}

	public void CreateQuote(string path, SalesQuote quote)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		ArgumentNullException.ThrowIfNull(quote);
		var issuer = _issuerService.Load();
		Render(path, DocumentTemplateType.SalesQuote, BusinessDocumentRenderModelFactory.Create(issuer, quote));
	}

	public void CreateOrderConfirmation(string path, SalesOrder order)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		ArgumentNullException.ThrowIfNull(order);
		var issuer = _issuerService.Load();
		Render(path, DocumentTemplateType.SalesOrderConfirmation, BusinessDocumentRenderModelFactory.Create(issuer, order));
	}

	public void CreatePickList(string path, Shipment shipment)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		ArgumentNullException.ThrowIfNull(shipment);
		var issuer = _issuerService.Load();
		Render(path, DocumentTemplateType.PickList, BusinessDocumentRenderModelFactory.CreatePickList(issuer, shipment));
	}

	public void CreatePackingSlip(string path, Shipment shipment)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		ArgumentNullException.ThrowIfNull(shipment);
		var issuer = _issuerService.Load();
		Render(path, DocumentTemplateType.PackingSlip, BusinessDocumentRenderModelFactory.CreatePackingSlip(issuer, shipment));
	}

	public void CreateDeliveryNote(string path, Shipment shipment)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		ArgumentNullException.ThrowIfNull(shipment);
		var issuer = _issuerService.Load();
		Render(path, DocumentTemplateType.DeliveryNote, BusinessDocumentRenderModelFactory.CreateDeliveryNote(issuer, shipment));
	}

	public void CreateInvoice(string path, SalesInvoice invoice)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		ArgumentNullException.ThrowIfNull(invoice);
		var issuer = ResolveInvoiceIssuer(invoice);
		Render(path, DocumentTemplateType.SalesInvoice, BusinessDocumentRenderModelFactory.Create(issuer, invoice));
	}

	public void ExportXRechnung(string path, SalesInvoice invoice)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		ArgumentNullException.ThrowIfNull(invoice);
		if (invoice.Status != SalesInvoiceStatus.Posted)
			throw new InvalidOperationException("Only a posted sales invoice has an issued XRechnung document.");
		_invoiceFinalizations.ExportXRechnung(invoice.Id, path);
	}

	public void CreateCreditNote(string path, SalesCreditNote creditNote, SalesInvoice invoice)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		ArgumentNullException.ThrowIfNull(creditNote);
		ArgumentNullException.ThrowIfNull(invoice);
		var issuer = ResolveCreditNoteIssuer(creditNote);
		Render(path, DocumentTemplateType.CreditNote, BusinessDocumentRenderModelFactory.Create(issuer, creditNote, invoice));
	}

	public void CreateCustomerReturnReceipt(string path, CustomerReturn customerReturn, Shipment shipment)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		ArgumentNullException.ThrowIfNull(customerReturn);
		ArgumentNullException.ThrowIfNull(shipment);
		var issuer = _issuerService.Load();
		Render(path, DocumentTemplateType.CustomerReturn, BusinessDocumentRenderModelFactory.Create(issuer, customerReturn, shipment));
	}

	private void Render(string path, DocumentTemplateType type, DocumentRenderModel model) =>
		_renderer.Render(path, _templates.GetActive(type), model);

	private DocumentIssuerProfile ResolveInvoiceIssuer(SalesInvoice invoice) =>
		invoice.Status == SalesInvoiceStatus.Posted
			? _issuerSnapshots.LoadRequired(DocumentIssuerSnapshotType.SalesInvoice, invoice.Id)
			: _issuerService.Load();

	private DocumentIssuerProfile ResolveCreditNoteIssuer(SalesCreditNote creditNote) =>
		creditNote.Status == SalesCreditNoteStatus.Posted
			? _issuerSnapshots.LoadRequired(DocumentIssuerSnapshotType.SalesCreditNote, creditNote.Id)
			: _issuerService.Load();
}
