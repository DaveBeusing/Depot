// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Depot.Tests;

public sealed class FinanceSepaPaymentExportTests
{
	[Fact]
	public async Task ApprovedRunGeneratesDeterministicImmutablePain001Artifact()
	{
		var path=Path.Combine(Path.GetTempPath(),$"depot-sepa-{Guid.NewGuid():N}.db");
		try
		{
			var fixture=await CreateFixtureAsync(new SqliteConnectionFactory(path));
			var preview=await fixture.Service.PreviewAsync(fixture.PaymentRunId);
			Assert.True(preview.IsValid,string.Join(Environment.NewLine,preview.Errors));
			Assert.Equal(2,preview.TransactionCount);
			Assert.Equal(125.55m,preview.ControlSum);

			var first=await fixture.Service.GenerateAsync(fixture.PaymentRunId);
			var retry=await fixture.Service.GenerateAsync(fixture.PaymentRunId);
			Assert.Equal(first.Id,retry.Id);
			Assert.Equal(first.XmlSha256,retry.XmlSha256);
			Assert.Equal(first.XmlPayload,retry.XmlPayload);
			Assert.Equal(FinanceSepaPaymentExportService.MessageVersion,first.MessageVersion);
			Assert.Equal(2,first.TransactionCount);
			Assert.Equal(125.55m,first.ControlSum);
			Assert.Equal(Convert.ToHexString(SHA256.HashData(first.XmlPayload)).ToLowerInvariant(),first.XmlSha256);

			ValidateAgainstPinnedSubset(first.XmlPayload);
			var document=XDocument.Parse(System.Text.Encoding.UTF8.GetString(first.XmlPayload));
			XNamespace ns=FinanceSepaPaymentExportService.XmlNamespace;
			Assert.Equal(ns+"Document",document.Root!.Name);
			Assert.Equal("2",document.Descendants(ns+"GrpHdr").Single().Element(ns+"NbOfTxs")!.Value);
			Assert.Equal("125.55",document.Descendants(ns+"GrpHdr").Single().Element(ns+"CtrlSum")!.Value);
			Assert.Equal(2,document.Descendants(ns+"CdtTrfTxInf").Count());
			Assert.Empty(document.Descendants(ns+"AdrLine"));
			Assert.All(document.Descendants(ns+"InstdAmt"),amount=>Assert.Equal("EUR",(string?)amount.Attribute("Ccy")));
			Assert.Equal(2,document.Descendants(ns+"EndToEndId").Select(value=>value.Value).Distinct(StringComparer.Ordinal).Count());

			var profile=await fixture.Service.GetCreditorProfileAsync(fixture.Supplier1Id);
			await fixture.Service.SaveCreditorProfileAsync(profile! with { Name="Changed supplier name" });
			var downloaded=await fixture.Service.DownloadAsync(first.Id);
			Assert.Equal(first.XmlPayload,downloaded.XmlPayload);
			Assert.Equal(first.XmlSha256,downloaded.XmlSha256);

			var superseding=await fixture.Service.GenerateAsync(fixture.PaymentRunId,true);
			Assert.NotEqual(first.Id,superseding.Id);
			Assert.Equal(2,superseding.ExportSequence);
			Assert.Equal(first.Id,superseding.SupersedesExportId);
			Assert.NotEqual(first.XmlSha256,superseding.XmlSha256);
			var original=await fixture.Service.GetExportAsync(first.Id);
			Assert.Equal(FinanceSepaPaymentExportStatus.Superseded,original!.CurrentStatus);
		}
		finally
		{
			SqliteConnection.ClearAllPools();
			try{File.Delete(path);}catch(IOException){}
		}
	}

	[Fact]
	public async Task ValidationRejectsMissingStructuredCreditorProfileAndNonEurRun()
	{
		var path=Path.Combine(Path.GetTempPath(),$"depot-sepa-invalid-{Guid.NewGuid():N}.db");
		try
		{
			var fixture=await CreateFixtureAsync(new SqliteConnectionFactory(path),saveSecondCreditor:false,currency:"USD");
			var preview=await fixture.Service.PreviewAsync(fixture.PaymentRunId);
			Assert.False(preview.IsValid);
			Assert.Contains(preview.Errors,value=>value.Contains("EUR only",StringComparison.Ordinal));
			Assert.Contains(preview.Errors,value=>value.Contains($"Supplier {fixture.Supplier2Id} has no structured SEPA creditor profile",StringComparison.Ordinal));
			await Assert.ThrowsAsync<InvalidOperationException>(()=>fixture.Service.GenerateAsync(fixture.PaymentRunId));
		}
		finally
		{
			SqliteConnection.ClearAllPools();
			try{File.Delete(path);}catch(IOException){}
		}
	}

	[Fact]
	public async Task ManualExternalStatusFollowsControlledLifecycle()
	{
		var path=Path.Combine(Path.GetTempPath(),$"depot-sepa-status-{Guid.NewGuid():N}.db");
		try
		{
			var fixture=await CreateFixtureAsync(new SqliteConnectionFactory(path));
			var export=await fixture.Service.GenerateAsync(fixture.PaymentRunId);
			await Assert.ThrowsAsync<ArgumentException>(()=>fixture.Service.UpdateStatusAsync(export.Id,new FinanceSepaPaymentExportStatusUpdate{Status=FinanceSepaPaymentExportStatus.SubmittedExternally}));
			var submitted=await fixture.Service.UpdateStatusAsync(export.Id,new FinanceSepaPaymentExportStatusUpdate{Status=FinanceSepaPaymentExportStatus.SubmittedExternally,ExternalReference="BANK-REF-1",EvidenceNote="Uploaded in bank portal"});
			Assert.Equal(FinanceSepaPaymentExportStatus.SubmittedExternally,submitted.CurrentStatus);
			var accepted=await fixture.Service.UpdateStatusAsync(export.Id,new FinanceSepaPaymentExportStatusUpdate{Status=FinanceSepaPaymentExportStatus.Accepted,ExternalReference="BANK-REF-1"});
			Assert.Equal(FinanceSepaPaymentExportStatus.Accepted,accepted.CurrentStatus);
			await Assert.ThrowsAsync<InvalidOperationException>(()=>fixture.Service.GenerateAsync(fixture.PaymentRunId,true));
			var reloaded=await fixture.Service.GetExportAsync(export.Id);
			Assert.Contains(reloaded!.StatusHistory,value=>value.Status==FinanceSepaPaymentExportStatus.SubmittedExternally&&value.ExternalReference=="BANK-REF-1");
			Assert.Contains(reloaded.StatusHistory,value=>value.Status==FinanceSepaPaymentExportStatus.Accepted);
		}
		finally
		{
			SqliteConnection.ClearAllPools();
			try{File.Delete(path);}catch(IOException){}
		}
	}

	[Fact]
	public void FinanceAndTreasuryRolesReceiveSeparateSepaPermissionsButApproverDoesNot()
	{
		var finance=SystemRoleCatalog.Definitions.Single(value=>value.Code==SystemRoleCatalog.FinanceCode);
		var treasury=SystemRoleCatalog.Definitions.Single(value=>value.Code==SystemRoleCatalog.TreasuryCode);
		var approver=SystemRoleCatalog.Definitions.Single(value=>value.Code==SystemRoleCatalog.ApproverCode);
		var permissions=new[]{ApplicationPermission.FinanceSepaPaymentProfilesManage,ApplicationPermission.FinanceSepaPaymentExportsCreate,ApplicationPermission.FinanceSepaPaymentExportsExport,ApplicationPermission.FinanceSepaPaymentExportsManage};
		foreach(var permission in permissions){Assert.Contains(permission,finance.Permissions);Assert.Contains(permission,treasury.Permissions);Assert.DoesNotContain(permission,approver.Permissions);}
		Assert.Equal("FinanceSepaPaymentExports.Create",PermissionCatalog.Code(ApplicationPermission.FinanceSepaPaymentExportsCreate));
		Assert.Equal(BusinessRecordRetentionCategory.AccountingRelevant,BusinessRecordCatalog.Require(nameof(FinanceSepaPaymentExport)).RetentionCategory);
	}

	internal static async Task<SepaFixture> CreateFixtureAsync(IDatabaseConnectionFactory factory,bool saveSecondCreditor=true,string currency="EUR")
	{
		DatabaseProvisioningService.Initialize(factory);
		var database=new DatabaseAccess(factory);
		var transactions=new DatabaseTransactionRunner(database);
		var suffix=Guid.NewGuid().ToString("N");
		var userId=database.Insert("INSERT INTO Users (Email,DisplayName,PasswordHash,IsAdministrator,CanApprovePurchaseOrders,Role,IsActive,CreatedUtc) VALUES ($Email,'SEPA Test','test',0,0,0,1,$Created);",new DatabaseParameter("$Email",$"sepa-{suffix}@depot.test"),new DatabaseParameter("$Created","2026-09-23T10:00:00.0000000Z"));
		var suppliers=new SupplierRepository(database);
		var supplier1=await suppliers.CreateAsync(new Supplier{Name=$"SEPA Supplier A {suffix[..6]}",IsActive=true},CancellationToken.None);
		var supplier2=await suppliers.CreateAsync(new Supplier{Name=$"SEPA Supplier B {suffix[..6]}",IsActive=true},CancellationToken.None);
		var bankId=database.Insert("INSERT INTO FinanceBankAccounts (Version,LegalEntityId,AccountingBookId,GeneralLedgerAccountId,CurrencyCode,Name,BankName,Iban,Bic,LocalAccountNumber,IsActive) VALUES (1,$Entity,$Book,$Gl,$Currency,$Name,'Test Bank','DE89370400440532013000','COBADEFFXXX',NULL,1);",
			new DatabaseParameter("$Entity",Guid.NewGuid().ToString("D")),new DatabaseParameter("$Book",Guid.NewGuid().ToString("D")),new DatabaseParameter("$Gl",Guid.NewGuid().ToString("D")),new DatabaseParameter("$Currency",currency),new DatabaseParameter("$Name",$"SEPA Bank {suffix[..6]}"));
		var operation=Guid.NewGuid();
		var paymentDate=DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2);
		var runId=database.Insert("INSERT INTO FinancePaymentRuns (Version,OperationId,BankAccountId,PaymentDate,CurrencyCode,Description,Status,CreatedAtUtc,CreatedByUserId,ApprovedAtUtc,ApprovedByUserId,ApprovalComment,CompletedAtUtc) VALUES (1,$Operation,$Bank,$Date,$Currency,'SEPA export test',$Status,$Created,$User,$Approved,$User,'Approved for test',NULL);",
			new DatabaseParameter("$Operation",operation.ToString("D")),new DatabaseParameter("$Bank",bankId),new DatabaseParameter("$Date",paymentDate.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture)),new DatabaseParameter("$Currency",currency),new DatabaseParameter("$Status",(int)FinancePaymentRunStatus.Approved),new DatabaseParameter("$Created","2026-09-23T10:00:00.0000000Z"),new DatabaseParameter("$Approved","2026-09-23T10:05:00.0000000Z"),new DatabaseParameter("$User",userId));
		database.Insert("INSERT INTO FinancePaymentRunLines (PaymentRunId,PayableOpenItemId,SupplierId,Amount,Reference,Status,ExecutionOperationId,PayablePaymentId,ExecutedAtUtc,ExecutedByUserId,ExecutionReference) VALUES ($Run,1001,$Supplier,100.25,'INV-1001',$Status,$Operation,NULL,NULL,NULL,NULL);",
			new DatabaseParameter("$Run",runId),new DatabaseParameter("$Supplier",supplier1),new DatabaseParameter("$Status",(int)FinancePaymentRunLineStatus.Proposed),new DatabaseParameter("$Operation",Guid.NewGuid().ToString("D")));
		database.Insert("INSERT INTO FinancePaymentRunLines (PaymentRunId,PayableOpenItemId,SupplierId,Amount,Reference,Status,ExecutionOperationId,PayablePaymentId,ExecutedAtUtc,ExecutedByUserId,ExecutionReference) VALUES ($Run,1002,$Supplier,25.30,'INV-1002',$Status,$Operation,NULL,NULL,NULL,NULL);",
			new DatabaseParameter("$Run",runId),new DatabaseParameter("$Supplier",supplier2),new DatabaseParameter("$Status",(int)FinancePaymentRunLineStatus.Proposed),new DatabaseParameter("$Operation",Guid.NewGuid().ToString("D")));

		var authorization=new AuthorizationService();
		authorization.SignIn(new User{Id=userId,Email=$"sepa-{suffix}@depot.test",DisplayName="SEPA Test",IsActive=true},
		[
			ApplicationPermission.FinanceBankingView,
			ApplicationPermission.FinanceSepaPaymentProfilesManage,
			ApplicationPermission.FinanceSepaPaymentExportsCreate,
			ApplicationPermission.FinanceSepaPaymentExportsExport,
			ApplicationPermission.FinanceSepaPaymentExportsManage
		]);
		var auditRepository=new AuditRepository(database);
		var audit=new AuditService(auditRepository,authorization);
		var gl=new FinanceGeneralLedgerService(transactions,new FinanceGeneralLedgerRepository(database),new FinancePostingProfileRepository(database),auditRepository,audit,authorization);
		var ap=new FinanceAccountsPayableService(transactions,new FinanceAccountsPayableRepository(database),gl,auditRepository,audit,authorization);
		var bankingRepository=new FinanceBankingRepository(database);
		var banking=new FinanceBankingService(transactions,bankingRepository,ap,auditRepository,audit,authorization);
		var service=new FinanceSepaPaymentExportService(transactions,bankingRepository,new FinanceSepaPaymentExportRepository(database),banking,auditRepository,audit,authorization);
		await service.SaveDebtorProfileAsync(new FinanceSepaDebtorProfile{BankAccountId=bankId,Name="Depot SEPA Test",StreetName="Teststrasse",BuildingNumber="1",PostalCode="53111",TownName="Bonn",CountryCode="DE"});
		await service.SaveCreditorProfileAsync(new FinanceSepaCreditorProfile{SupplierId=supplier1,Name="Supplier Alpha",Iban="FR1420041010050500013M02606",Bic="BNPAFRPP",StreetName="Rue de Test",BuildingNumber="2",PostalCode="75001",TownName="Paris",CountryCode="FR"});
		if(saveSecondCreditor)await service.SaveCreditorProfileAsync(new FinanceSepaCreditorProfile{SupplierId=supplier2,Name="Supplier Beta",Iban="GB82WEST12345698765432",Bic="DABAIE2D",StreetName="Test Road",BuildingNumber="3",PostalCode="D02",TownName="Dublin",CountryCode="IE"});
		return new SepaFixture(service,runId,supplier1,supplier2);
	}

	private static void ValidateAgainstPinnedSubset(byte[] payload)
	{
		var schemaPath=Path.Combine(AppContext.BaseDirectory,"Fixtures","Sepa","pain.001.001.09.depot-subset.xsd");
		var schemas=new XmlSchemaSet();
		schemas.Add(FinanceSepaPaymentExportService.XmlNamespace,schemaPath);
		var document=XDocument.Parse(System.Text.Encoding.UTF8.GetString(payload));
		var errors=new List<string>();
		document.Validate(schemas,(_,args)=>errors.Add(args.Message),true);
		Assert.Empty(errors);
	}

	internal sealed record SepaFixture(FinanceSepaPaymentExportService Service,long PaymentRunId,long Supplier1Id,long Supplier2Id);
}
