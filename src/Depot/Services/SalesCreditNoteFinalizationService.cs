// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Depot.Data;
using Depot.Models;

namespace Depot.Services;

public sealed record SalesCreditNoteFinalization(long SalesCreditNoteId,DocumentBuyerProfile Buyer,string XRechnungXml,string XRechnungSha256,DateTime FinalizedAtUtc);

public sealed class SalesCreditNoteFinalizationService
{
	private static readonly JsonSerializerOptions JsonOptions=new(){PropertyNameCaseInsensitive=true};
	private readonly DatabaseAccess _dataAccess;
	public SalesCreditNoteFinalizationService(DatabaseAccess dataAccess){_dataAccess=dataAccess;}
	public SalesCreditNoteFinalization? TryLoad(long id){var rows=_dataAccess.Query("SELECT BuyerPayload,XRechnungXml,XRechnungSha256,FinalizedAtUtc FROM SalesCreditNoteFinalizations WHERE SalesCreditNoteId=$Id;",r=>new SalesCreditNoteFinalization(id,JsonSerializer.Deserialize<DocumentBuyerProfile>(r.GetString(0),JsonOptions)??throw new InvalidOperationException("Stored buyer snapshot could not be read."),r.GetString(1),r.GetString(2),DateTime.Parse(r.GetString(3),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind)),new DatabaseParameter("$Id",id));if(rows.Count==0)return null;VerifyHash(rows[0].XRechnungXml,rows[0].XRechnungSha256);return rows[0];}
	public SalesCreditNoteFinalization LoadRequired(long id)=>TryLoad(id)??throw new InvalidOperationException($"Posted sales credit note {id} has no finalized buyer/XRechnung record. Legacy records are not reconstructed from mutable master data.");
	public void ExportXRechnung(long id,string path){ArgumentException.ThrowIfNullOrWhiteSpace(path);var value=LoadRequired(id);Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);File.WriteAllText(path,value.XRechnungXml,new UTF8Encoding(false));}
	public static async Task<SalesCreditNoteFinalization> FinalizeAsync(DatabaseTransactionContext transaction,SalesCreditNote creditNote,SalesInvoice sourceInvoice,DocumentIssuerProfile issuer,DateTime finalizedAtUtc,CancellationToken token)
	{
		ArgumentNullException.ThrowIfNull(transaction);ArgumentNullException.ThrowIfNull(creditNote);ArgumentNullException.ThrowIfNull(sourceInvoice);ArgumentNullException.ThrowIfNull(issuer);if(creditNote.Lines.Count==0)throw new InvalidOperationException("A finalized credit note requires at least one line.");
		var buyerRows=await transaction.Session.QueryAsync("SELECT BuyerPayload FROM SalesInvoiceFinalizations WHERE SalesInvoiceId=$Id;",r=>r.GetString(0),token,new DatabaseParameter("$Id",sourceInvoice.Id));if(buyerRows.Count==0)throw new InvalidOperationException("The source invoice has no immutable buyer finalization. Credit-note finalization fails closed.");var buyer=JsonSerializer.Deserialize<DocumentBuyerProfile>(buyerRows[0],JsonOptions)??throw new InvalidOperationException("Source invoice buyer snapshot could not be read.");
		var sourceLines=sourceInvoice.Lines.ToDictionary(x=>x.Id);var electronic=new ElectronicInvoice{InvoiceNumber=creditNote.CreditNoteNumber,TypeCode=ElectronicInvoiceTypeCode.CreditNote,IssueDate=DateOnly.FromDateTime(creditNote.CreditDate),Currency=sourceInvoice.Currency,BuyerReference=buyer.BuyerReference,PurchaseOrderReference=sourceInvoice.InvoiceNumber,Seller=CompanyDocumentIdentityService.ToElectronicInvoiceSeller(issuer),Buyer=SalesInvoiceFinalizationService.ToElectronicInvoiceBuyer(buyer),Payment=new ElectronicInvoicePayment{MeansCode=string.Empty},Lines=creditNote.Lines.Select((line,index)=>{if(!sourceLines.TryGetValue(line.SalesInvoiceLineId,out var source))throw new InvalidOperationException("Credit-note line does not belong to the source invoice.");return new ElectronicInvoiceLine{Id=(index+1).ToString(CultureInfo.InvariantCulture),Name=source.Description,Description=source.Description,Quantity=line.Quantity,UnitCode="C62",UnitPrice=line.UnitPrice,DiscountPercent=line.DiscountPercent,TaxRate=line.TaxRate,TaxCategoryCode=line.TaxCategoryCode,TaxExemptionReasonCode=line.TaxExemptionReasonCode,TaxExemptionReason=line.TaxExemptionReason,SellerItemIdentifier=source.PartNumber};}).ToArray(),Note=creditNote.Reason};
		var xml=new ElectronicInvoiceService().CreateXRechnungXml(electronic);var hash=ComputeHash(xml);var payload=JsonSerializer.Serialize(buyer,JsonOptions);await transaction.Session.ExecuteAsync("INSERT INTO SalesCreditNoteFinalizations (SalesCreditNoteId,BuyerPayload,XRechnungXml,XRechnungSha256,FinalizedAtUtc) VALUES ($Id,$Buyer,$Xml,$Hash,$At);",token,new DatabaseParameter("$Id",creditNote.Id),new DatabaseParameter("$Buyer",payload),new DatabaseParameter("$Xml",xml),new DatabaseParameter("$Hash",hash),new DatabaseParameter("$At",finalizedAtUtc.ToString("O",CultureInfo.InvariantCulture)));await InsertRoutingEvidenceAsync(transaction,"CreditNote",creditNote.Id,buyer,finalizedAtUtc,token);return new(creditNote.Id,buyer,xml,hash,finalizedAtUtc);
	}
	internal static Task<int> InsertRoutingEvidenceAsync(DatabaseTransactionContext transaction,string documentType,long documentId,DocumentBuyerProfile buyer,DateTime at,CancellationToken token)=>transaction.Session.ExecuteAsync("INSERT INTO SalesElectronicInvoiceEvidence (DocumentType,DocumentId,RecipientIdentifier,RecipientScheme,RoutingChannel,GuidelineId,ValidatorProfile,FinalizedAtUtc) VALUES ($Type,$Id,$Recipient,$Scheme,$Channel,$Guideline,$Validator,$At);",token,new DatabaseParameter("$Type",documentType),new DatabaseParameter("$Id",documentId),new DatabaseParameter("$Recipient",buyer.ElectronicAddress),new DatabaseParameter("$Scheme",buyer.ElectronicAddressScheme),new DatabaseParameter("$Channel",ElectronicInvoiceConformanceMatrix.RoutingChannel),new DatabaseParameter("$Guideline",ElectronicInvoiceConformanceMatrix.GuidelineId),new DatabaseParameter("$Validator",ElectronicInvoiceConformanceMatrix.ValidatorProfile),new DatabaseParameter("$At",at.ToString("O",CultureInfo.InvariantCulture)));
	private static string ComputeHash(string xml)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(xml))).ToLowerInvariant();private static void VerifyHash(string xml,string expected){if(!string.Equals(ComputeHash(xml),expected,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Stored XRechnung XML failed its SHA-256 integrity check.");}
}
