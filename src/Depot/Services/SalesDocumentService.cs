// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;

using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace Depot.Services;

public sealed class SalesDocumentService
{
	private static readonly XFont TitleFont = new("Segoe UI", 18, XFontStyleEx.Bold);
	private static readonly XFont HeadingFont = new("Segoe UI", 11, XFontStyleEx.Bold);
	private static readonly XFont BodyFont = new("Segoe UI", 9, XFontStyleEx.Regular);
	private static readonly XFont SmallFont = new("Segoe UI", 8, XFontStyleEx.Regular);
	private readonly CompanyDocumentIdentityService _issuerService;

	public SalesDocumentService() : this(CreateIssuerService()) { }
	public SalesDocumentService(CompanyDocumentIdentityService issuerService) => _issuerService = issuerService;

	public void CreateQuote(string path, SalesQuote quote)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path); ArgumentNullException.ThrowIfNull(quote); var issuer=_issuerService.Load();
		var document=CreateDocument(issuer,"Sales Quote",quote.QuoteNumber);var page=document.AddPage();var graphics=XGraphics.FromPdfPage(page);
		var y=DrawHeader(graphics,issuer,"QUOTE",quote.QuoteNumber,quote.CustomerName,quote.QuoteDate);
		y=DrawAddress(graphics,y,"Customer",quote.CustomerName,quote.BillingAddress);
		y=DrawMetadata(graphics,y,[("Valid until",quote.ValidUntil.ToString("d")),("Contact",quote.ContactName??"—"),("Customer reference",quote.CustomerReference??"—"),("Status",quote.Status.ToString())]);
		y=DrawQuoteLines(graphics,page,y,quote.Lines,quote.Currency);DrawTotals(graphics,page,y,quote.NetAmount,quote.TaxAmount,quote.GrossAmount,quote.Currency);DrawIssuerFooter(graphics,page,issuer);Save(document,path);
	}

	public void CreateOrderConfirmation(string path, SalesOrder order)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);ArgumentNullException.ThrowIfNull(order);var issuer=_issuerService.Load();
		var document=CreateDocument(issuer,"Order Confirmation",order.OrderNumber);var page=document.AddPage();var graphics=XGraphics.FromPdfPage(page);
		var y=DrawHeader(graphics,issuer,"ORDER CONFIRMATION",order.OrderNumber,order.CustomerName,order.OrderDate);
		y=DrawAddress(graphics,y,"Customer",order.CustomerName,order.BillingAddress);
		y=DrawMetadata(graphics,y,[("Customer reference",order.CustomerReference??"—"),("Requested delivery",order.RequestedDeliveryDate?.ToString("d")??"—"),("Status",order.Status.ToString()),("Currency",order.Currency)]);
		y=DrawSalesLines(graphics,page,y,order.Lines,order.Currency,true);DrawTotals(graphics,page,y,order.NetAmount,order.TaxAmount,order.GrossAmount,order.Currency);DrawIssuerFooter(graphics,page,issuer);Save(document,path);
	}

	public void CreatePickList(string path, Shipment shipment)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);ArgumentNullException.ThrowIfNull(shipment);var issuer=_issuerService.Load();
		var document=CreateDocument(issuer,"Pick List",shipment.ShipmentNumber);var page=document.AddPage();var graphics=XGraphics.FromPdfPage(page);
		var y=DrawHeader(graphics,issuer,"PICK LIST",shipment.ShipmentNumber,shipment.CustomerName,shipment.ShipmentDate);
		y=DrawMetadata(graphics,y,[("Sales order",shipment.SalesOrderNumber),("Packing status",shipment.PackingStatus.ToString()),("Carrier",shipment.Carrier??"—"),("Tracking",shipment.TrackingNumber??"—")]);
		DrawShipmentLines(graphics,page,y,shipment.Lines);DrawIssuerFooter(graphics,page,issuer);Save(document,path);
	}

	public void CreatePackingSlip(string path, Shipment shipment)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);ArgumentNullException.ThrowIfNull(shipment);var issuer=_issuerService.Load();
		var document=CreateDocument(issuer,"Packing Slip",shipment.ShipmentNumber);var page=document.AddPage();var graphics=XGraphics.FromPdfPage(page);
		var y=DrawHeader(graphics,issuer,"PACKING SLIP",shipment.ShipmentNumber,shipment.CustomerName,shipment.ShipmentDate);
		y=DrawAddress(graphics,y,"Ship to",shipment.CustomerName,shipment.ShippingAddress);
		y=DrawMetadata(graphics,y,[("Sales order",shipment.SalesOrderNumber),("Carrier",shipment.Carrier??"—"),("Tracking",shipment.TrackingNumber??"—"),("Packed",shipment.PackedAtUtc?.ToLocalTime().ToString("g")??"—")]);
		DrawShipmentLines(graphics,page,y,shipment.Lines);DrawIssuerFooter(graphics,page,issuer);Save(document,path);
	}

	public void CreateDeliveryNote(string path, Shipment shipment)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);ArgumentNullException.ThrowIfNull(shipment);var issuer=_issuerService.Load();
		var document=CreateDocument(issuer,"Delivery Note",shipment.ShipmentNumber);var page=document.AddPage();var graphics=XGraphics.FromPdfPage(page);
		var y=DrawHeader(graphics,issuer,"DELIVERY NOTE",shipment.ShipmentNumber,shipment.CustomerName,shipment.ShipmentDate);
		y=DrawAddress(graphics,y,"Ship to",shipment.CustomerName,shipment.ShippingAddress);
		y=DrawMetadata(graphics,y,[("Sales order",shipment.SalesOrderNumber),("Carrier",shipment.Carrier??"—"),("Tracking",shipment.TrackingNumber??"—"),("Status",shipment.Status.ToString())]);
		DrawShipmentLines(graphics,page,y,shipment.Lines);DrawIssuerFooter(graphics,page,issuer);Save(document,path);
	}

	public void CreateInvoice(string path, SalesInvoice invoice)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);ArgumentNullException.ThrowIfNull(invoice);var issuer=_issuerService.Load();
		var document=CreateDocument(issuer,"Sales Invoice",invoice.InvoiceNumber);var page=document.AddPage();var graphics=XGraphics.FromPdfPage(page);
		var y=DrawHeader(graphics,issuer,"INVOICE",invoice.InvoiceNumber,invoice.CustomerName,invoice.InvoiceDate);
		y=DrawAddress(graphics,y,"Bill to",invoice.CustomerName,invoice.BillingAddress);
		y=DrawMetadata(graphics,y,[("Sales order",invoice.SalesOrderNumber),("Shipment",invoice.ShipmentNumber),("Customer reference",invoice.CustomerReference??"—"),("Due date",invoice.DueDate.ToString("d"))]);
		y=DrawInvoiceLines(graphics,page,y,invoice.Lines,invoice.Currency);DrawTotals(graphics,page,y,invoice.NetAmount,invoice.TaxAmount,invoice.GrossAmount,invoice.Currency);DrawIssuerFooter(graphics,page,issuer);Save(document,path);
	}

	public void CreateCreditNote(string path, SalesCreditNote creditNote, SalesInvoice invoice)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);ArgumentNullException.ThrowIfNull(creditNote);ArgumentNullException.ThrowIfNull(invoice);var issuer=_issuerService.Load();
		var document=CreateDocument(issuer,"Credit Note",creditNote.CreditNoteNumber);var page=document.AddPage();var graphics=XGraphics.FromPdfPage(page);
		var y=DrawHeader(graphics,issuer,"CREDIT NOTE",creditNote.CreditNoteNumber,invoice.CustomerName,creditNote.CreditDate);
		y=DrawAddress(graphics,y,"Customer",invoice.CustomerName,invoice.BillingAddress);
		y=DrawMetadata(graphics,y,[("Invoice",invoice.InvoiceNumber),("Sales order",invoice.SalesOrderNumber),("Status",creditNote.Status.ToString()),("Reason",Trim(creditNote.Reason,28))]);
		y=DrawCreditLines(graphics,page,y,creditNote.Lines,invoice.Lines,invoice.Currency);DrawTotals(graphics,page,y,-creditNote.NetAmount,-creditNote.TaxAmount,-creditNote.GrossAmount,invoice.Currency);DrawIssuerFooter(graphics,page,issuer);Save(document,path);
	}

	public void CreateCustomerReturnReceipt(string path, CustomerReturn customerReturn, Shipment shipment)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);ArgumentNullException.ThrowIfNull(customerReturn);ArgumentNullException.ThrowIfNull(shipment);var issuer=_issuerService.Load();
		var document=CreateDocument(issuer,"Customer Return",customerReturn.ReturnNumber);var page=document.AddPage();var graphics=XGraphics.FromPdfPage(page);
		var y=DrawHeader(graphics,issuer,"CUSTOMER RETURN",customerReturn.ReturnNumber,shipment.CustomerName,customerReturn.ReturnDate);
		y=DrawAddress(graphics,y,"Customer",shipment.CustomerName,shipment.ShippingAddress);
		y=DrawMetadata(graphics,y,[("Shipment",shipment.ShipmentNumber),("Sales order",shipment.SalesOrderNumber),("Status",customerReturn.Status.ToString()),("Reason",Trim(customerReturn.Reason,28))]);
		DrawReturnLines(graphics,page,y,customerReturn.Lines,shipment.Lines);DrawIssuerFooter(graphics,page,issuer);Save(document,path);
	}

	private static CompanyDocumentIdentityService CreateIssuerService()
	{
		var settingsService=new SettingsService(new SettingsRepository("depot.settings"));
		var settings=settingsService.LoadOrCreate();
		return new CompanyDocumentIdentityService(new DatabaseAccess(DatabaseProviderFactory.CreateConnectionFactory(settings)),settings.Provider);
	}
	private static PdfDocument CreateDocument(DocumentIssuerProfile issuer,string title,string subject){var document=new PdfDocument();document.Info.Title=$"{issuer.DisplayName} - {title}";document.Info.Subject=subject;document.Info.Creator=issuer.LegalName;document.Info.Author=issuer.LegalName;return document;}
	private static double DrawHeader(XGraphics graphics,DocumentIssuerProfile issuer,string title,string number,string customer,DateTime date){graphics.DrawString(Trim(issuer.DisplayName,50),HeadingFont,XBrushes.Black,new XPoint(40,38));graphics.DrawString(Trim(issuer.PostalAddress,82),SmallFont,XBrushes.Gray,new XPoint(40,54));graphics.DrawString(title,TitleFont,XBrushes.Black,new XPoint(40,82));graphics.DrawString(number,HeadingFont,XBrushes.Black,new XPoint(40,106));graphics.DrawString(date.ToString("d"),BodyFont,XBrushes.Black,new XPoint(470,45));graphics.DrawString(customer,BodyFont,XBrushes.Black,new XPoint(360,106));graphics.DrawLine(XPens.LightGray,40,120,555,120);return 146;}
	private static void DrawIssuerFooter(XGraphics graphics,PdfPage page,DocumentIssuerProfile issuer){var y=page.Height.Point-44;graphics.DrawLine(XPens.LightGray,40,y-8,555,y-8);var legal=string.Join(" · ",new[]{issuer.RegistrationLine,issuer.TaxLine}.Where(value=>!string.IsNullOrWhiteSpace(value)));graphics.DrawString(Trim(legal,110),SmallFont,XBrushes.Gray,new XPoint(40,y));var bank=issuer.BankLine;var contact=string.Join(" · ",new[]{issuer.Email,issuer.Phone,issuer.Website}.Where(value=>!string.IsNullOrWhiteSpace(value)));graphics.DrawString(Trim(string.Join(" · ",new[]{bank,contact}.Where(value=>!string.IsNullOrWhiteSpace(value))),110),SmallFont,XBrushes.Gray,new XPoint(40,y+11));}
	private static double DrawAddress(XGraphics graphics,double y,string heading,string customer,string? address){graphics.DrawString(heading,HeadingFont,XBrushes.Black,new XPoint(40,y));y+=18;graphics.DrawString(customer,BodyFont,XBrushes.Black,new XPoint(40,y));y+=14;foreach(var line in(address??string.Empty).Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries)){graphics.DrawString(line,BodyFont,XBrushes.Black,new XPoint(40,y));y+=13;}return y+12;}
	private static double DrawMetadata(XGraphics graphics,double y,IReadOnlyList<(string Label,string Value)> values){var x=40d;foreach(var value in values){graphics.DrawString(value.Label,SmallFont,XBrushes.Gray,new XPoint(x,y));graphics.DrawString(value.Value,BodyFont,XBrushes.Black,new XPoint(x,y+14));x+=128;}return y+42;}
	private static double DrawSalesLines(XGraphics graphics,PdfPage page,double y,IReadOnlyList<SalesOrderLine> lines,string currency,bool showTax){DrawTableHeader(graphics,y,"Item","Description","Qty","Unit",showTax?"Tax":string.Empty,"Total");y+=22;foreach(var line in lines){y=EnsurePageSpace(ref graphics,page.Owner,ref page,y,32);graphics.DrawString(line.PartNumber,BodyFont,XBrushes.Black,new XPoint(40,y));graphics.DrawString(Trim(line.Description,40),BodyFont,XBrushes.Black,new XPoint(125,y));graphics.DrawString(line.Quantity.ToString("N0"),BodyFont,XBrushes.Black,new XPoint(365,y));graphics.DrawString(Money(line.UnitPrice,currency),BodyFont,XBrushes.Black,new XPoint(405,y));if(showTax)graphics.DrawString($"{line.TaxRate:N0}%",BodyFont,XBrushes.Black,new XPoint(475,y));graphics.DrawString(Money(line.GrossAmount,currency),BodyFont,XBrushes.Black,new XPoint(515,y));y+=20;}return y+8;}
	private static double DrawQuoteLines(XGraphics graphics,PdfPage page,double y,IReadOnlyList<SalesQuoteLine> lines,string currency){DrawTableHeader(graphics,y,"Item","Description","Qty","Unit","Tax","Total");y+=22;foreach(var line in lines){y=EnsurePageSpace(ref graphics,page.Owner,ref page,y,32);graphics.DrawString(line.PartNumber,BodyFont,XBrushes.Black,new XPoint(40,y));graphics.DrawString(Trim(line.Description,40),BodyFont,XBrushes.Black,new XPoint(125,y));graphics.DrawString(line.Quantity.ToString("N0"),BodyFont,XBrushes.Black,new XPoint(365,y));graphics.DrawString(Money(line.UnitPrice,currency),BodyFont,XBrushes.Black,new XPoint(405,y));graphics.DrawString($"{line.TaxRate:N0}%",BodyFont,XBrushes.Black,new XPoint(475,y));graphics.DrawString(Money(line.GrossAmount,currency),BodyFont,XBrushes.Black,new XPoint(515,y));y+=20;}return y+8;}
	private static double DrawInvoiceLines(XGraphics graphics,PdfPage page,double y,IReadOnlyList<SalesInvoiceLine> lines,string currency){DrawTableHeader(graphics,y,"Item","Description","Qty","Unit","Tax","Total");y+=22;foreach(var line in lines){y=EnsurePageSpace(ref graphics,page.Owner,ref page,y,32);graphics.DrawString(line.PartNumber,BodyFont,XBrushes.Black,new XPoint(40,y));graphics.DrawString(Trim(line.Description,40),BodyFont,XBrushes.Black,new XPoint(125,y));graphics.DrawString(line.Quantity.ToString("N0"),BodyFont,XBrushes.Black,new XPoint(365,y));graphics.DrawString(Money(line.UnitPrice,currency),BodyFont,XBrushes.Black,new XPoint(405,y));graphics.DrawString($"{line.TaxRate:N0}%",BodyFont,XBrushes.Black,new XPoint(475,y));graphics.DrawString(Money(line.GrossAmount,currency),BodyFont,XBrushes.Black,new XPoint(515,y));y+=20;}return y+8;}
	private static double DrawCreditLines(XGraphics graphics,PdfPage page,double y,IReadOnlyList<SalesCreditNoteLine> lines,IReadOnlyList<SalesInvoiceLine> invoiceLines,string currency){var invoiceById=invoiceLines.ToDictionary(line=>line.Id);DrawTableHeader(graphics,y,"Item","Description","Qty","Unit","Tax","Credit");y+=22;foreach(var line in lines){y=EnsurePageSpace(ref graphics,page.Owner,ref page,y,32);invoiceById.TryGetValue(line.SalesInvoiceLineId,out var source);graphics.DrawString(source?.PartNumber??$"Line {line.SalesInvoiceLineId}",BodyFont,XBrushes.Black,new XPoint(40,y));graphics.DrawString(Trim(source?.Description??"Credited invoice line",40),BodyFont,XBrushes.Black,new XPoint(125,y));graphics.DrawString(line.Quantity.ToString("N0"),BodyFont,XBrushes.Black,new XPoint(365,y));graphics.DrawString(Money(line.UnitPrice,currency),BodyFont,XBrushes.Black,new XPoint(405,y));graphics.DrawString($"{line.TaxRate:N0}%",BodyFont,XBrushes.Black,new XPoint(475,y));graphics.DrawString(Money(-line.GrossAmount,currency),BodyFont,XBrushes.Black,new XPoint(515,y));y+=20;}return y+8;}
	private static void DrawShipmentLines(XGraphics graphics,PdfPage page,double y,IReadOnlyList<ShipmentLine> lines){graphics.DrawString("Item",HeadingFont,XBrushes.Black,new XPoint(40,y));graphics.DrawString("Description",HeadingFont,XBrushes.Black,new XPoint(150,y));graphics.DrawString("Quantity",HeadingFont,XBrushes.Black,new XPoint(480,y));y+=22;foreach(var line in lines){y=EnsurePageSpace(ref graphics,page.Owner,ref page,y,28);graphics.DrawString(line.PartNumber,BodyFont,XBrushes.Black,new XPoint(40,y));graphics.DrawString(Trim(line.Description,55),BodyFont,XBrushes.Black,new XPoint(150,y));graphics.DrawString(line.Quantity.ToString("N0"),BodyFont,XBrushes.Black,new XPoint(510,y));y+=20;}}
	private static void DrawReturnLines(XGraphics graphics,PdfPage page,double y,IReadOnlyList<CustomerReturnLine> lines,IReadOnlyList<ShipmentLine> shipmentLines){var shipmentById=shipmentLines.ToDictionary(line=>line.Id);graphics.DrawString("Item",HeadingFont,XBrushes.Black,new XPoint(40,y));graphics.DrawString("Description",HeadingFont,XBrushes.Black,new XPoint(150,y));graphics.DrawString("Returned",HeadingFont,XBrushes.Black,new XPoint(480,y));y+=22;foreach(var line in lines){y=EnsurePageSpace(ref graphics,page.Owner,ref page,y,28);shipmentById.TryGetValue(line.ShipmentLineId,out var source);graphics.DrawString(source?.PartNumber??$"Line {line.ShipmentLineId}",BodyFont,XBrushes.Black,new XPoint(40,y));graphics.DrawString(Trim(source?.Description??"Returned shipment line",55),BodyFont,XBrushes.Black,new XPoint(150,y));graphics.DrawString(line.Quantity.ToString("N0"),BodyFont,XBrushes.Black,new XPoint(510,y));y+=20;}}
	private static void DrawTableHeader(XGraphics graphics,double y,string c1,string c2,string c3,string c4,string c5,string c6){graphics.DrawLine(XPens.LightGray,40,y+5,555,y+5);graphics.DrawString(c1,HeadingFont,XBrushes.Black,new XPoint(40,y));graphics.DrawString(c2,HeadingFont,XBrushes.Black,new XPoint(125,y));graphics.DrawString(c3,HeadingFont,XBrushes.Black,new XPoint(365,y));graphics.DrawString(c4,HeadingFont,XBrushes.Black,new XPoint(405,y));graphics.DrawString(c5,HeadingFont,XBrushes.Black,new XPoint(475,y));graphics.DrawString(c6,HeadingFont,XBrushes.Black,new XPoint(515,y));}
	private static void DrawTotals(XGraphics graphics,PdfPage page,double y,decimal net,decimal tax,decimal gross,string currency){y=Math.Min(y+10,page.Height.Point-90);graphics.DrawLine(XPens.LightGray,370,y,555,y);graphics.DrawString("Net",BodyFont,XBrushes.Black,new XPoint(410,y+18));graphics.DrawString(Money(net,currency),BodyFont,XBrushes.Black,new XPoint(500,y+18));graphics.DrawString("Tax",BodyFont,XBrushes.Black,new XPoint(410,y+34));graphics.DrawString(Money(tax,currency),BodyFont,XBrushes.Black,new XPoint(500,y+34));graphics.DrawString("Total",HeadingFont,XBrushes.Black,new XPoint(410,y+54));graphics.DrawString(Money(gross,currency),HeadingFont,XBrushes.Black,new XPoint(500,y+54));}
	private static double EnsurePageSpace(ref XGraphics graphics,PdfDocument document,ref PdfPage page,double y,double required){if(y+required<page.Height.Point-72)return y;graphics.Dispose();page=document.AddPage();graphics=XGraphics.FromPdfPage(page);return 55;}
	private static string Money(decimal value,string currency)=>$"{value:N2} {currency}";
	private static string Trim(string value,int length)=>value.Length<=length?value:value[..(length-1)]+"…";
	private static void Save(PdfDocument document,string path){Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);document.Save(path);}
}