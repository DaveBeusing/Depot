// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;

using Xunit;

namespace Depot.Tests;

public sealed class ServiceManagementTests
{
	[Fact]
	public async Task ServiceCaseLifecycleUsesOptimisticConcurrencyAndHistory()
	{
		await using var context=await ProcurementTestContext.CreateSqliteAsync();
		var fixture=CreateFixture(context);
		var customer=await CreateCustomerAsync(context,"Lifecycle Customer");

		var created=await fixture.Service.SaveCaseAsync(new ServiceCase
		{
			CustomerId=customer.Id,
			Subject="Packaging line fault",
			Description="Initial diagnostic request",
			Priority=ServiceCasePriority.High,
			DueAtUtc=DateTime.UtcNow.AddDays(1)
		});
		Assert.StartsWith("SC-",created.CaseNumber,StringComparison.Ordinal);
		Assert.Equal(ServiceCaseStatus.New,created.Status);

		var updated=await fixture.Service.SaveCaseAsync(created with{Description="Diagnostic request updated"});
		Assert.Equal(created.Version+1,updated.Version);
		await Assert.ThrowsAsync<ConcurrencyConflictException>(()=>fixture.Service.SaveCaseAsync(created with{Description="Stale update"}));

		var opened=await fixture.Service.OpenCaseAsync(updated.Id,updated.Version);
		var started=await fixture.Service.StartCaseAsync(opened.Id,opened.Version);
		var waiting=await fixture.Service.WaitCaseAsync(started.Id,started.Version,"Waiting for customer access");
		var resumed=await fixture.Service.StartCaseAsync(waiting.Id,waiting.Version);
		var resolved=await fixture.Service.ResolveCaseAsync(resumed.Id,resumed.Version,"Replaced failed control relay and verified operation.");
		var closed=await fixture.Service.CloseCaseAsync(resolved.Id,resolved.Version,"Customer accepted repair.");

		Assert.Equal(ServiceCaseStatus.Closed,closed.Status);
		Assert.NotNull(closed.ResolvedAtUtc);
		Assert.NotNull(closed.ClosedAtUtc);
		var history=await fixture.Service.ListCaseHistoryAsync(closed.Id);
		Assert.Equal(7,history.Count);
		Assert.Equal(ServiceCaseStatus.New,history[0].Status);
		Assert.Equal(ServiceCaseStatus.Closed,history[^1].Status);
		Assert.Contains(history,value=>value.Note=="Replaced failed control relay and verified operation.");
	}

	[Fact]
	public async Task CompletedServiceOrderIsImmutableAndBillingIsIdempotent()
	{
		await using var context=await ProcurementTestContext.CreateSqliteAsync();
		var fixture=CreateFixture(context);
		var customer=await CreateCustomerAsync(context,"Billing Customer");
		var serviceCase=await fixture.Service.SaveCaseAsync(new ServiceCase
		{
			CustomerId=customer.Id,
			Subject="Commissioning support",
			Priority=ServiceCasePriority.Normal
		});
		var order=await fixture.Service.CreateServiceOrderAsync(serviceCase.Id,null,null,null,null,DateTime.UtcNow,DateTime.UtcNow.AddDays(1));
		await fixture.Service.AddWorkLineAsync(order.Id,"On-site commissioning",2m,100m,true,19m);
		await fixture.Service.AddWorkLineAsync(order.Id,"Goodwill follow-up",1m,50m,false,19m);
		order=await fixture.Service.StartServiceOrderAsync(order.Id,order.Version);
		var completed=await fixture.Service.CompleteServiceOrderAsync(order.Id,order.Version,"Commissioning completed; functional test passed.");

		Assert.Equal(ServiceOrderStatus.Completed,completed.Status);
		await Assert.ThrowsAsync<InvalidOperationException>(()=>fixture.Service.AddWorkLineAsync(completed.Id,"Late mutation",1m,1m,true,19m));
		await Assert.ThrowsAsync<InvalidOperationException>(()=>fixture.Service.CreateServiceOrderAsync(serviceCase.Id,null,null,null,null,null,null));

		var first=await fixture.Service.GenerateInvoiceDraftAsync(completed.Id);
		var second=await fixture.Service.GenerateInvoiceDraftAsync(completed.Id);
		Assert.NotNull(first.Invoice);
		Assert.NotNull(second.Invoice);
		Assert.Equal(first.Invoice!.Id,second.Invoice!.Id);
		var line=Assert.Single(first.Invoice.Lines);
		Assert.Equal("SERVICE",line.PartNumber);
		Assert.Equal(2m,line.Quantity);
		Assert.Equal(100m,line.UnitPrice);
		Assert.Equal(1L,Convert.ToInt64(await context.Data.ExecuteScalarAsync(
			"SELECT COUNT(*) FROM SalesInvoices WHERE CustomerId=$CustomerId;",
			CancellationToken.None,
			new DatabaseParameter("$CustomerId",customer.Id)),CultureInfo.InvariantCulture));
	}

	[Fact]
	public async Task PartsUseStockMovementAuthorityAndCompletionFreezesAttachments()
	{
		await using var context=await ProcurementTestContext.CreateSqliteAsync();
		var fixture=CreateFixture(context);
		var customer=await CreateCustomerAsync(context,"Parts Customer");
		var serviceCase=await fixture.Service.SaveCaseAsync(new ServiceCase
		{
			CustomerId=customer.Id,
			Subject="Replace damaged component",
			Priority=ServiceCasePriority.Critical
		});
		var order=await fixture.Service.CreateServiceOrderAsync(serviceCase.Id,null,context.ItemId,context.InventoryId,null,null,null);
		await fixture.Movements.AddCorrectionAsync(context.InventoryId,5,null,"SERVICE-TEST-STOCK","Service test stock",CancellationToken.None);

		var consumed=await fixture.Service.ConsumePartAsync(order.Id,context.InventoryId,2,null,25m,true,19m);
		var returned=await fixture.Service.ReturnPartAsync(order.Id,context.InventoryId,1,null,25m,19m);
		Assert.NotEqual(consumed.StockMovementId,returned.StockMovementId);
		Assert.Equal(ServicePartMovementKind.Consumption,consumed.MovementKind);
		Assert.Equal(ServicePartMovementKind.Return,returned.MovementKind);
		Assert.Equal(4L,Convert.ToInt64(await context.Data.ExecuteScalarAsync(
			"SELECT COALESCE(SUM(Quantity),0) FROM StockMovements WHERE InventoryId=$InventoryId;",
			CancellationToken.None,
			new DatabaseParameter("$InventoryId",context.InventoryId)),CultureInfo.InvariantCulture));

		await context.BusinessAttachments.AddAsync(
			BusinessAttachmentEntityKind.ServiceOrder,
			order.Id,
			"before-completion.txt",
			"text/plain",
			new MemoryStream("service evidence"u8.ToArray()));

		var current=await fixture.Service.GetOrderAsync(order.Id)??throw new InvalidOperationException("Service order missing.");
		var completed=await fixture.Service.CompleteServiceOrderAsync(current.Id,current.Version,"Component replaced and final test passed.");
		await Assert.ThrowsAsync<InvalidOperationException>(()=>context.BusinessAttachments.AddAsync(
			BusinessAttachmentEntityKind.ServiceOrder,
			completed.Id,
			"after-completion.txt",
			"text/plain",
			new MemoryStream("late evidence"u8.ToArray())));

		var resolved=await fixture.Service.GetCaseAsync(serviceCase.Id)??throw new InvalidOperationException("Service case missing.");
		var closed=await fixture.Service.CloseCaseAsync(resolved.Id,resolved.Version,"Repair accepted.");
		await Assert.ThrowsAsync<InvalidOperationException>(()=>context.BusinessAttachments.AddAsync(
			BusinessAttachmentEntityKind.ServiceCase,
			closed.Id,
			"after-close.txt",
			"text/plain",
			new MemoryStream("late case evidence"u8.ToArray())));
	}

	[Fact]
	public async Task ServiceManagementSchemaAndWorkspaceRespectEstablishedBoundaries()
	{
		await using var context=await ProcurementTestContext.CreateSqliteAsync();
		Assert.Equal(ServiceManagementSchemaMigration.CurrentVersion,Convert.ToInt32(await context.Data.ExecuteScalarAsync(
			"SELECT Version FROM DepotFeatureVersions WHERE Name='ServiceManagement';",CancellationToken.None),CultureInfo.InvariantCulture));
		Assert.Equal(1L,Convert.ToInt64(await context.Data.ExecuteScalarAsync("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='ServiceCases';",CancellationToken.None),CultureInfo.InvariantCulture));
		Assert.Equal(1L,Convert.ToInt64(await context.Data.ExecuteScalarAsync("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='ServiceOrders';",CancellationToken.None),CultureInfo.InvariantCulture));

		var root=FindRepositoryRoot();
		var service=File.ReadAllText(Path.Combine(root,"src","Depot","Services","ServiceManagementService.cs"));
		var viewModel=File.ReadAllText(Path.Combine(root,"src","Depot","ViewModels","ServiceManagementViewModel.cs"));
		var view=File.ReadAllText(Path.Combine(root,"src","Depot","Views","ServiceManagementView.xaml"));
		var providers=File.ReadAllText(Path.Combine(root,"src","Depot","Services","MyWorkProviders.cs"));

		Assert.Contains("_movements.AddWithdrawalAsync",service,StringComparison.Ordinal);
		Assert.Contains("_movements.AddCorrectionAsync",service,StringComparison.Ordinal);
		Assert.Contains("_invoices.CreateDirectDraftAsync",service,StringComparison.Ordinal);
		Assert.DoesNotContain("UPDATE Inventories",service,StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("ServiceManagementRepository",viewModel,StringComparison.Ordinal);
		Assert.DoesNotContain("DatabaseAccess",viewModel,StringComparison.Ordinal);
		Assert.Contains("<controls:PageHeader",view,StringComparison.Ordinal);
		Assert.Contains("<controls:OperationPanel",view,StringComparison.Ordinal);
		Assert.Contains("<controls:BusinessAttachmentPanel",view,StringComparison.Ordinal);
		Assert.Contains("ServiceManagementMyWorkProvider",providers,StringComparison.Ordinal);
		Assert.Contains("MyWorkSectionKind.Exceptions",providers,StringComparison.Ordinal);
	}

	private static ServiceFixture CreateFixture(ProcurementTestContext context)
	{
		var data=context.Data;
		var authorization=context.Authorization;
		var runner=new DatabaseTransactionRunner(data);
		var auditRepository=new AuditRepository(data);
		var audit=new AuditService(auditRepository,authorization);
		var notifications=new NotificationService(runner,new NotificationRepository(data),authorization);
		var inventoryRepository=new InventoryRepository(data);
		var movementRepository=new StockMovementRepository(data);
		var reasonCodes=new ReasonCodeRepository(data);
		var reversals=new StockMovementReversalService(runner,inventoryRepository,movementRepository,reasonCodes,auditRepository,audit);
		var movements=new MovementService(new ItemRepository(data),inventoryRepository,reasonCodes,movementRepository,audit,reversals,runner,auditRepository);
		var customerRepository=new CustomerRepository(data);
		var invoiceRepository=new SalesInvoiceRepository(data);
		var credits=new SalesCreditNoteService(runner,new SalesCreditNoteRepository(data),invoiceRepository,auditRepository,audit,authorization,notifications);
		var invoices=new SalesInvoiceService(runner,invoiceRepository,new ShipmentRepository(data),new SalesOrderRepository(data),customerRepository,auditRepository,audit,authorization,notifications,credits);
		var service=new ServiceManagementService(runner,new ServiceManagementRepository(data),customerRepository,auditRepository,audit,authorization,notifications,movements,invoices);
		return new ServiceFixture(service,movements);
	}

	private static Task<Customer> CreateCustomerAsync(ProcurementTestContext context,string name)=>
		new CustomerRepository(context.Data).SaveAsync(new Customer{Name=name,Currency="EUR",PaymentTermsDays=14,IsActive=true},CancellationToken.None);

	private static string FindRepositoryRoot()
	{
		for(var directory=new DirectoryInfo(AppContext.BaseDirectory);directory is not null;directory=directory.Parent)
			if(File.Exists(Path.Combine(directory.FullName,"Depot.slnx")))return directory.FullName;
		throw new DirectoryNotFoundException("Could not locate the Depot repository root.");
	}

	private sealed record ServiceFixture(ServiceManagementService Service,MovementService Movements);
}
