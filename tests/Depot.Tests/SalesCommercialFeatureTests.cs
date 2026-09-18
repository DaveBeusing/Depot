// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class SalesCommercialFeatureTests : IAsyncLifetime
{
	private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"depot-sales-commercial-{Guid.NewGuid():N}.db");
	private CommercialFixture? _fixture;

	[Fact]
	public async Task CustomerContactsPersistRoleAndPrimaryState()
	{
		var fixture = Fixture;
		var customer = await fixture.Customers.SaveAsync(new Customer { Name = "Contact Customer", Currency = "EUR" });
		var contact = await fixture.Customers.SaveContactAsync(new CustomerContact { CustomerId = customer.Id, Name = "Morgan Buyer", Role = CustomerContactRole.Purchasing, Department = "Procurement", Email = "morgan@example.test", IsPrimary = true, IsActive = true });
		var loaded = await fixture.Customers.GetByIdAsync(customer.Id) ?? throw new InvalidOperationException();
		var saved = Assert.Single(loaded.Contacts);
		Assert.Equal(contact.Id, saved.Id);
		Assert.Equal(CustomerContactRole.Purchasing, saved.Role);
		Assert.True(saved.IsPrimary);
	}

	[Fact]
	public async Task CustomerPricingResolvesAssignedPriceList()
	{
		var fixture = Fixture;
		var customer = await fixture.Customers.SaveAsync(new Customer { Name = "Pricing Customer", Currency = "EUR" });
		var list = await fixture.Pricing.SaveAsync(new SalesPriceList { Code = "B2B-EUR", Name = "B2B EUR", Currency = "EUR", IsActive = false });
		await fixture.Pricing.SaveItemAsync(new SalesPriceListItem { SalesPriceListId = list.Id, ItemId = fixture.ItemId, UnitPrice = 89m, DiscountPercent = 7.5m });
		await fixture.Pricing.AssignCustomerAsync(customer.Id, list.Id);
		list.IsActive = true;
		await fixture.Pricing.SaveAsync(list);
		var price = await fixture.Pricing.ResolveAsync(customer.Id, fixture.ItemId, DateTime.Today);
		Assert.NotNull(price);
		Assert.Equal(89m, price.UnitPrice);
		Assert.Equal(7.5m, price.DiscountPercent);
	}

	[Fact]
	public async Task QuoteCanBeAcceptedAndConvertedToSalesOrderWithSnapshots()
	{
		var fixture = Fixture;
		var customer = await fixture.Customers.SaveAsync(new Customer { Name = "Quote Customer", BillingAddress = "Billing Snapshot", ShippingAddress = "Shipping Snapshot", Currency = "EUR" });
		var contact = await fixture.Customers.SaveContactAsync(new CustomerContact { CustomerId = customer.Id, Name = "Taylor Commercial", Role = CustomerContactRole.Commercial, Email = "taylor@example.test", IsPrimary = true, IsActive = true });
		var quote = await fixture.Quotes.SaveDraftAsync(new SalesQuote { CustomerId = customer.Id, ContactId = contact.Id, ContactName = contact.Name, QuoteDate = DateTime.Today, ValidUntil = DateTime.Today.AddDays(14), Lines = [new SalesQuoteLine { ItemId = fixture.ItemId, PartNumber = fixture.PartNumber, Description = "Commercial item", Quantity = 2, UnitPrice = 120m, DiscountPercent = 5m, TaxRate = 19m }] });
		var sent = await fixture.Quotes.MarkSentAsync(quote.Id, quote.Version);
		var accepted = await fixture.Quotes.AcceptAsync(sent.Id, sent.Version);
		var order = await fixture.Quotes.ConvertToSalesOrderAsync(accepted.Id, accepted.Version);
		Assert.Equal(customer.Id, order.CustomerId);
		Assert.Equal("Billing Snapshot", order.BillingAddress);
		Assert.Equal("Shipping Snapshot", order.ShippingAddress);
		Assert.Equal(114m, Assert.Single(order.Lines).UnitPrice * (1m - Assert.Single(order.Lines).DiscountPercent / 100m));

		var timeline = await fixture.Timeline.ListAsync(order);
		var quoteIndex = Array.FindIndex(timeline.ToArray(), value => value.Kind == WorkflowTimelineKind.SalesQuote);
		var orderIndex = Array.FindIndex(timeline.ToArray(), value => value.Kind == WorkflowTimelineKind.SalesOrder);
		Assert.True(quoteIndex >= 0 && orderIndex > quoteIndex);
	}

	[Fact]
	public async Task TimelineReflectsSalesOrderLifecycleDeterministically()
	{
		var fixture = Fixture;
		var customer = await fixture.Customers.SaveAsync(new Customer { Name = "Timeline Customer", Currency = "EUR" });
		var draft = await fixture.Orders.SaveDraftAsync(new SalesOrder { CustomerId = customer.Id, Lines = [new SalesOrderLine { ItemId = fixture.ItemId, Quantity = 1, UnitPrice = 50m, TaxRate = 19m }] });
		var submitted = await fixture.Orders.SubmitAsync(draft.Id, draft.Version);
		var approved = await fixture.Orders.ApproveAsync(submitted.Id, submitted.Version, "Timeline approval");

		var first = await fixture.Timeline.ListAsync(approved);
		var second = await fixture.Timeline.ListAsync(approved);

		var createdIndex = Array.FindIndex(first.ToArray(), value => value.Kind == WorkflowTimelineKind.SalesOrder && value.Title == "Sales order created");
		var submittedIndex = Array.FindIndex(first.ToArray(), value => value.Kind == WorkflowTimelineKind.SalesApproval && value.Status == "Pending Approval");
		var approvedIndex = Array.FindIndex(first.ToArray(), value => value.Kind == WorkflowTimelineKind.SalesApproval && value.Status == "Approved");
		Assert.True(createdIndex >= 0 && submittedIndex > createdIndex && approvedIndex > submittedIndex);
		Assert.Single(first, value => value.IsCurrent);
		Assert.Equal(
			first.Select(value => (value.Kind, value.EntityId, value.DisplayNumber, value.Title, value.Status, value.OccurredAt, value.IsCorrection, value.IsReversal)),
			second.Select(value => (value.Kind, value.EntityId, value.DisplayNumber, value.Title, value.Status, value.OccurredAt, value.IsCorrection, value.IsReversal)));
	}

	[Fact]
	public async Task TimelineDoesNotRequireOptionalWorkflowSteps()
	{
		var fixture = Fixture;
		var customer = await fixture.Customers.SaveAsync(new Customer { Name = "Minimal Timeline Customer", Currency = "EUR" });
		var draft = await fixture.Orders.SaveDraftAsync(new SalesOrder { CustomerId = customer.Id, Lines = [new SalesOrderLine { ItemId = fixture.ItemId, Quantity = 1, UnitPrice = 25m, TaxRate = 19m }] });

		var events = await fixture.Timeline.ListAsync(draft);

		var current = Assert.Single(events, value => value.IsCurrent);
		Assert.Equal(WorkflowTimelineKind.SalesOrder, current.Kind);
		Assert.DoesNotContain(events, value => value.Kind is WorkflowTimelineKind.Shipment or WorkflowTimelineKind.SalesInvoice);
	}

	[Fact]
	public async Task TimelineMarksCorrectionsAndReversalsSeparately()
	{
		var fixture = Fixture;
		var customer = await fixture.Customers.SaveAsync(new Customer { Name = "Correction Timeline Customer", Currency = "EUR" });
		var order = await fixture.Orders.SaveDraftAsync(new SalesOrder { CustomerId = customer.Id, Lines = [new SalesOrderLine { ItemId = fixture.ItemId, Quantity = 1, UnitPrice = 75m, TaxRate = 19m }] });
		var shipmentId = await fixture.Data.InsertAsync(
			"INSERT INTO Shipments (ShipmentNumber,SalesOrderId,CustomerId,ShipmentDate,Status,CreatedByUserId,PostedByUserId,PostedAtUtc,ReversedAtUtc,ReversedByUserId,ReversalReason,Version) VALUES ($Number,$OrderId,$CustomerId,$Date,$Status,$UserId,$UserId,$At,$At,$UserId,$Reason,1);",
			CancellationToken.None,
			new DatabaseParameter("$Number", $"SH-TL-{Guid.NewGuid():N}"),
			new DatabaseParameter("$OrderId", order.Id),
			new DatabaseParameter("$CustomerId", customer.Id),
			new DatabaseParameter("$Date", DateTime.Today.ToString("yyyy-MM-dd")),
			new DatabaseParameter("$Status", (int)ShipmentStatus.Cancelled),
			new DatabaseParameter("$UserId", fixture.Admin.Id),
			new DatabaseParameter("$At", DateTime.UtcNow.ToString("O")),
			new DatabaseParameter("$Reason", "Timeline reversal"));
		await fixture.Data.InsertAsync(
			"INSERT INTO CustomerReturns (ReturnNumber,ShipmentId,SalesOrderId,CustomerId,ReturnDate,Status,Reason,CreatedByUserId,PostedByUserId,PostedAtUtc,Version) VALUES ($Number,$ShipmentId,$OrderId,$CustomerId,$Date,$Status,$Reason,$UserId,$UserId,$At,1);",
			CancellationToken.None,
			new DatabaseParameter("$Number", $"CR-TL-{Guid.NewGuid():N}"),
			new DatabaseParameter("$ShipmentId", shipmentId),
			new DatabaseParameter("$OrderId", order.Id),
			new DatabaseParameter("$CustomerId", customer.Id),
			new DatabaseParameter("$Date", DateTime.Today.ToString("yyyy-MM-dd")),
			new DatabaseParameter("$Status", (int)CustomerReturnStatus.Posted),
			new DatabaseParameter("$Reason", "Timeline correction"),
			new DatabaseParameter("$UserId", fixture.Admin.Id),
			new DatabaseParameter("$At", DateTime.UtcNow.ToString("O")));

		var events = await fixture.Timeline.ListAsync(order);
		var correction = Assert.Single(events, value => value.Kind == WorkflowTimelineKind.CustomerReturn);
		Assert.True(correction.IsCorrection);
		Assert.False(correction.IsReversal);
		Assert.Equal(WorkflowTimelineSeverity.Warning, correction.Severity);

		var reversal = Assert.Single(events, value => value.Kind == WorkflowTimelineKind.ShipmentReversal);
		Assert.True(reversal.IsReversal);
		Assert.False(reversal.IsCorrection);
		Assert.Equal(WorkflowTimelineSeverity.Error, reversal.Severity);
	}

	[Fact]
	public async Task TimelineFiltersEntitiesWithoutViewPermission()
	{
		var fixture = Fixture;
		var customer = await fixture.Customers.SaveAsync(new Customer { Name = "Restricted Timeline Customer", Currency = "EUR" });
		var quote = await fixture.Quotes.SaveDraftAsync(new SalesQuote { CustomerId = customer.Id, QuoteDate = DateTime.Today, ValidUntil = DateTime.Today.AddDays(7), Lines = [new SalesQuoteLine { ItemId = fixture.ItemId, PartNumber = fixture.PartNumber, Description = "Restricted timeline item", Quantity = 1, UnitPrice = 10m, TaxRate = 19m }] });
		var sent = await fixture.Quotes.MarkSentAsync(quote.Id, quote.Version);
		var accepted = await fixture.Quotes.AcceptAsync(sent.Id, sent.Version);
		var order = await fixture.Quotes.ConvertToSalesOrderAsync(accepted.Id, accepted.Version);
		var restrictedTimeline = fixture.CreateTimeline(ApplicationPermission.SalesOrdersView);

		var events = await restrictedTimeline.ListAsync(order);

		Assert.NotEmpty(events);
		Assert.DoesNotContain(events, value => value.Kind == WorkflowTimelineKind.SalesQuote);
		Assert.All(events, value => Assert.Equal("sales.orders", value.RouteId));
	}

	[Fact]
	public void InvoiceDueStatusTracksDraftDueAndOverdueStates()
	{
		Assert.Equal(SalesInvoiceDueStatus.Draft, new SalesInvoice { Status = SalesInvoiceStatus.Draft, DueDate = DateTime.Today.AddDays(-1) }.DueStatus);
		Assert.Equal(SalesInvoiceDueStatus.NotDue, new SalesInvoice { Status = SalesInvoiceStatus.Posted, DueDate = DateTime.Today.AddDays(5) }.DueStatus);
		Assert.Equal(SalesInvoiceDueStatus.DueToday, new SalesInvoice { Status = SalesInvoiceStatus.Posted, DueDate = DateTime.Today }.DueStatus);
		Assert.Equal(SalesInvoiceDueStatus.Overdue, new SalesInvoice { Status = SalesInvoiceStatus.Posted, DueDate = DateTime.Today.AddDays(-1) }.DueStatus);
		Assert.Equal(SalesInvoiceDueStatus.Cancelled, new SalesInvoice { Status = SalesInvoiceStatus.Cancelled, DueDate = DateTime.Today.AddDays(-1) }.DueStatus);
	}

	public async Task InitializeAsync()
	{
		var factory = new SqliteConnectionFactory(_databasePath);
		new DepotDatabase(factory).Initialize();
		SalesSchemaMigration.Migrate(factory);
		_fixture = await CommercialFixture.CreateAsync(factory);
	}

	public Task DisposeAsync()
	{
		SqliteConnection.ClearAllPools();
		if (File.Exists(_databasePath)) File.Delete(_databasePath);
		return Task.CompletedTask;
	}

	private CommercialFixture Fixture => _fixture ?? throw new InvalidOperationException("Commercial fixture is not initialized.");

	private sealed class CommercialFixture
	{
		private CommercialFixture(CustomerService customers, SalesPricingService pricing, SalesQuoteService quotes, SalesOrderService orders, SalesTimelineService timeline, DatabaseAccess data, User admin, long itemId, string partNumber)
		{
			Customers = customers; Pricing = pricing; Quotes = quotes; Orders = orders; Timeline = timeline; Data = data; Admin = admin; ItemId = itemId; PartNumber = partNumber;
		}
		public CustomerService Customers { get; }
		public SalesPricingService Pricing { get; }
		public SalesQuoteService Quotes { get; }
		public SalesOrderService Orders { get; }
		public SalesTimelineService Timeline { get; }
		public DatabaseAccess Data { get; }
		public User Admin { get; }
		public long ItemId { get; }
		public string PartNumber { get; }

		public static async Task<CommercialFixture> CreateAsync(IDatabaseConnectionFactory factory)
		{
			var data = new DatabaseAccess(factory);
			var authorization = new AuthorizationService();
			var roles = new RoleRepository(data);
			var users = new UserRepository(data);
			var admin = await users.GetByEmailAsync("admin@depot.local", CancellationToken.None) ?? throw new InvalidOperationException("Default administrator missing.");
			admin.Roles = await roles.GetUserRolesAsync(admin.Id, CancellationToken.None); admin.EffectivePermissions = PermissionCatalog.All; authorization.SignIn(admin, PermissionCatalog.All);
			var partNumber = $"COMM-{Guid.NewGuid():N}";
			var itemId = await data.InsertAsync("INSERT INTO Items (PartNumber,Description,IsActive) VALUES ($PartNumber,$Description,1);", CancellationToken.None, new DatabaseParameter("$PartNumber", partNumber), new DatabaseParameter("$Description", "Commercial test item"));
			var auditRepository = new AuditRepository(data); var audit = new AuditService(auditRepository, authorization); var runner = new DatabaseTransactionRunner(data);
			var notifications = new NotificationService(runner, new NotificationRepository(data), authorization);
			var customerRepository = new CustomerRepository(data); var customers = new CustomerService(customerRepository, audit, authorization);
			var orderRepository = new SalesOrderRepository(data);
			var orders = new SalesOrderService(runner, orderRepository, customerRepository, new ItemRepository(data), new InventoryRepository(data), new InventoryReservationRepository(data), new StockMovementRepository(data), auditRepository, audit, authorization, notifications);
			var pricing = new SalesPricingService(runner, new SalesPriceListRepository(data), auditRepository, audit, authorization);
			var quotes = new SalesQuoteService(new SalesQuoteRepository(data), customerRepository, orders, audit, authorization);
			var timeline = new SalesTimelineService(new SalesTimelineRepository(data), authorization);
			return new CommercialFixture(customers, pricing, quotes, orders, timeline, data, admin, itemId, partNumber);
		}

		public SalesTimelineService CreateTimeline(params ApplicationPermission[] permissions)
		{
			var authorization = new AuthorizationService();
			var user = new User
			{
				Id = Admin.Id,
				Email = Admin.Email,
				DisplayName = Admin.DisplayName,
				IsActive = true,
				Roles = Admin.Roles
			};
			authorization.SignIn(user, permissions);
			return new SalesTimelineService(new SalesTimelineRepository(Data), authorization);
		}
	}
}
