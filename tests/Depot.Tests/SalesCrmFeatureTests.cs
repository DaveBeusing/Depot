// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class SalesCrmFeatureTests : IAsyncLifetime
{
	private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"depot-sales-crm-{Guid.NewGuid():N}.db");
	private CrmFixture? _fixture;

	[Fact]
	public async Task LeadConversionCreatesCustomerAndOpportunityAndRejectsDuplicateConversion()
	{
		var fixture = Fixture;
		var lead = await fixture.Crm.SaveLeadAsync(new SalesLead
		{
			CompanyName = "CRM Prospect GmbH",
			PersonName = "Morgan Prospect",
			Email = "morgan@example.test",
			Phone = "+49 228 1000",
			Source = "Referral"
		});

		var converted = await fixture.Crm.ConvertLeadAsync(
			lead.Id,
			lead.Version,
			null,
			new SalesOpportunity
			{
				ExpectedAmount = 25000m,
				ProbabilityPercent = 35,
				ExpectedCloseDate = DateTime.Today.AddDays(30),
				Currency = "EUR"
			});

		Assert.Equal(SalesLeadStatus.Converted, converted.Lead.Status);
		Assert.Equal(converted.Customer.Id, converted.Lead.ConvertedCustomerId);
		Assert.Equal(converted.Opportunity.Id, converted.Lead.ConvertedOpportunityId);
		Assert.Equal(converted.Customer.Id, converted.Opportunity.CustomerId);
		Assert.Equal(converted.Lead.Id, converted.Opportunity.LeadId);

		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			fixture.Crm.ConvertLeadAsync(
				converted.Lead.Id,
				converted.Lead.Version,
				converted.Customer.Id,
				new SalesOpportunity { Currency = "EUR" }));
	}

	[Fact]
	public async Task LeadUpdateUsesOptimisticConcurrency()
	{
		var fixture = Fixture;
		var lead = await fixture.Crm.SaveLeadAsync(new SalesLead { CompanyName = "Concurrency Prospect" });
		var stale = Copy(lead);
		lead.NotesSummary = "First update";
		await fixture.Crm.SaveLeadAsync(lead);

		stale.NotesSummary = "Stale update";
		await Assert.ThrowsAsync<ConcurrencyConflictException>(() => fixture.Crm.SaveLeadAsync(stale));
	}

	[Fact]
	public async Task OpportunityLifecycleUpdatesPipelineAndRequiresCloseReason()
	{
		var fixture = Fixture;
		var customer = await fixture.Customers.SaveAsync(new Customer { Name = "Pipeline Customer", Currency = "EUR" });
		var opportunity = await fixture.Crm.SaveOpportunityAsync(new SalesOpportunity
		{
			CustomerId = customer.Id,
			Currency = "EUR",
			ExpectedAmount = 10000m,
			ProbabilityPercent = 40,
			ExpectedCloseDate = DateTime.Today.AddDays(14)
		});

		var pipeline = await fixture.Crm.GetPipelineSummaryAsync(fixture.Admin.Id);
		Assert.Equal(1, pipeline.Sum(value => value.OpportunityCount));
		Assert.Equal(10000m, pipeline.Sum(value => value.ExpectedAmount));
		Assert.Equal(4000m, pipeline.Sum(value => value.WeightedAmount));

		await Assert.ThrowsAsync<ArgumentException>(() =>
			fixture.Crm.CloseOpportunityAsync(opportunity.Id, opportunity.Version, SalesOpportunityOutcome.Won, string.Empty));

		var closed = await fixture.Crm.CloseOpportunityAsync(
			opportunity.Id,
			opportunity.Version,
			SalesOpportunityOutcome.Won,
			"Customer accepted proposal");

		Assert.Equal(SalesOpportunityOutcome.Won, closed.Outcome);
		Assert.Equal("Customer accepted proposal", closed.CloseReason);
		Assert.NotNull(closed.ClosedAtUtc);
		Assert.Equal(0, (await fixture.Crm.GetPipelineSummaryAsync(fixture.Admin.Id)).Sum(value => value.OpportunityCount));
	}

	[Fact]
	public async Task OpportunityCreatesQuoteAndPreservesSourceLink()
	{
		var fixture = Fixture;
		var customer = await fixture.Customers.SaveAsync(new Customer { Name = "Quote Prospect", Currency = "EUR" });
		var opportunity = await fixture.Crm.SaveOpportunityAsync(new SalesOpportunity
		{
			CustomerId = customer.Id,
			Currency = "EUR",
			ExpectedAmount = 5000m,
			ProbabilityPercent = 50
		});

		var quote = await fixture.Crm.CreateQuoteAsync(opportunity.Id, opportunity.Version);
		var linked = await fixture.Crm.GetOpportunityAsync(opportunity.Id) ?? throw new InvalidOperationException();

		Assert.Equal(quote.Id, linked.LinkedSalesQuoteId);
		Assert.Equal(opportunity.OpportunityNumber, quote.CustomerReference);
		await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Crm.CreateQuoteAsync(linked.Id, linked.Version));
	}

	[Fact]
	public async Task ActivityCompletionRemovesOwnedDueWork()
	{
		var fixture = Fixture;
		var lead = await fixture.Crm.SaveLeadAsync(new SalesLead { CompanyName = "Activity Prospect" });
		var activity = await fixture.Crm.SaveActivityAsync(new SalesActivity
		{
			LeadId = lead.Id,
			Type = SalesActivityType.Call,
			DueAtUtc = DateTime.UtcNow.AddMinutes(-10),
			Subject = "Call prospect"
		});

		var provider = new SalesCrmMyWorkProvider(fixture.Crm);
		var before = await provider.GetAsync(new MyWorkQuery(fixture.Admin.Id, DateTime.UtcNow, 50), CancellationToken.None);
		var work = Assert.Single(before, value => value.Kind == MyWorkItemKind.SalesLeadActivity);
		Assert.Equal(lead.Id, work.EntityId);
		Assert.Equal(MyWorkSectionKind.Exceptions, work.Section);

		await fixture.Crm.CompleteActivityAsync(activity.Id, activity.Version);

		var after = await provider.GetAsync(new MyWorkQuery(fixture.Admin.Id, DateTime.UtcNow, 50), CancellationToken.None);
		Assert.DoesNotContain(after, value => value.Kind == MyWorkItemKind.SalesLeadActivity && value.EntityId == lead.Id);
	}

	[Fact]
	public async Task SearchIsBoundedAndFindsLeadAndOpportunity()
	{
		var fixture = Fixture;
		var lead = await fixture.Crm.SaveLeadAsync(new SalesLead
		{
			CompanyName = "Searchable Prospect",
			Email = "searchable@example.test"
		});
		var customer = await fixture.Customers.SaveAsync(new Customer { Name = "Searchable Customer", Currency = "EUR" });
		var opportunity = await fixture.Crm.SaveOpportunityAsync(new SalesOpportunity
		{
			CustomerId = customer.Id,
			Currency = "EUR",
			ExpectedAmount = 1200m
		});

		var leads = await fixture.Crm.SearchLeadsAsync("Searchable", pageSize: 1);
		var opportunities = await fixture.Crm.SearchOpportunitiesAsync("Searchable", pageSize: 1);

		Assert.Single(leads.Items);
		Assert.Equal(lead.Id, leads.Items[0].Id);
		Assert.Single(opportunities.Items);
		Assert.Equal(opportunity.Id, opportunities.Items[0].Id);
	}

	[Fact]
	public async Task RecordManagementPermissionIsEnforcedAtServiceBoundary()
	{
		var fixture = Fixture;
		var restricted = fixture.CreateCrm(ApplicationPermission.SalesCrmView);

		await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
			restricted.SaveLeadAsync(new SalesLead { CompanyName = "Restricted Prospect" }));
	}

	public async Task InitializeAsync()
	{
		var factory = new SqliteConnectionFactory(_databasePath);
		new DepotDatabase(factory).Initialize();
		SalesSchemaMigration.Migrate(factory);
		_fixture = await CrmFixture.CreateAsync(factory);
	}

	public Task DisposeAsync()
	{
		SqliteConnection.ClearAllPools();
		if (File.Exists(_databasePath)) File.Delete(_databasePath);
		return Task.CompletedTask;
	}

	private CrmFixture Fixture => _fixture ?? throw new InvalidOperationException("CRM fixture is not initialized.");

	private static SalesLead Copy(SalesLead value) => new()
	{
		Id = value.Id,
		LeadNumber = value.LeadNumber,
		CompanyName = value.CompanyName,
		PersonName = value.PersonName,
		Email = value.Email,
		Phone = value.Phone,
		Source = value.Source,
		OwnerUserId = value.OwnerUserId,
		Status = value.Status,
		NotesSummary = value.NotesSummary,
		CreatedAtUtc = value.CreatedAtUtc,
		UpdatedAtUtc = value.UpdatedAtUtc,
		ConvertedCustomerId = value.ConvertedCustomerId,
		ConvertedOpportunityId = value.ConvertedOpportunityId,
		ConvertedAtUtc = value.ConvertedAtUtc,
		Version = value.Version
	};

	private sealed class CrmFixture
	{
		private readonly DatabaseAccess _data;
		private readonly CustomerRepository _customerRepository;
		private readonly AuditRepository _auditRepository;

		private CrmFixture(
			DatabaseAccess data,
			AuthorizationService authorization,
			CustomerRepository customerRepository,
			AuditRepository auditRepository,
			CustomerService customers,
			SalesCrmService crm,
			User admin)
		{
			_data = data;
			Authorization = authorization;
			_customerRepository = customerRepository;
			_auditRepository = auditRepository;
			Customers = customers;
			Crm = crm;
			Admin = admin;
		}

		public AuthorizationService Authorization { get; }
		public CustomerService Customers { get; }
		public SalesCrmService Crm { get; }
		public User Admin { get; }

		public static async Task<CrmFixture> CreateAsync(IDatabaseConnectionFactory factory)
		{
			var data = new DatabaseAccess(factory);
			var authorization = new AuthorizationService();
			var roles = new RoleRepository(data);
			var users = new UserRepository(data);
			var admin = await users.GetByEmailAsync("admin@depot.local", CancellationToken.None) ?? throw new InvalidOperationException("Default administrator missing.");
			admin.Roles = await roles.GetUserRolesAsync(admin.Id, CancellationToken.None);
			admin.EffectivePermissions = PermissionCatalog.All;
			authorization.SignIn(admin, PermissionCatalog.All);

			var auditRepository = new AuditRepository(data);
			var audit = new AuditService(auditRepository, authorization);
			var runner = new DatabaseTransactionRunner(data);
			var notifications = new NotificationService(runner, new NotificationRepository(data), authorization);
			var customerRepository = new CustomerRepository(data);
			var customers = new CustomerService(customerRepository, audit, authorization);
			var orders = new SalesOrderService(
				runner,
				new SalesOrderRepository(data),
				customerRepository,
				new ItemRepository(data),
				new InventoryRepository(data),
				new InventoryReservationRepository(data),
				new StockMovementRepository(data),
				auditRepository,
				audit,
				authorization,
				notifications);
			var quotes = new SalesQuoteService(new SalesQuoteRepository(data), customerRepository, orders, audit, authorization);
			var crm = new SalesCrmService(
				runner,
				new SalesCrmRepository(data),
				customerRepository,
				auditRepository,
				audit,
				customers,
				quotes,
				authorization);
			return new CrmFixture(data, authorization, customerRepository, auditRepository, customers, crm, admin);
		}

		public SalesCrmService CreateCrm(params ApplicationPermission[] permissions)
		{
			var authorization = new AuthorizationService();
			authorization.SignIn(Admin, permissions);
			var audit = new AuditService(_auditRepository, authorization);
			var runner = new DatabaseTransactionRunner(_data);
			var notifications = new NotificationService(runner, new NotificationRepository(_data), authorization);
			var customers = new CustomerService(_customerRepository, audit, authorization);
			var orders = new SalesOrderService(
				runner,
				new SalesOrderRepository(_data),
				_customerRepository,
				new ItemRepository(_data),
				new InventoryRepository(_data),
				new InventoryReservationRepository(_data),
				new StockMovementRepository(_data),
				_auditRepository,
				audit,
				authorization,
				notifications);
			var quotes = new SalesQuoteService(new SalesQuoteRepository(_data), _customerRepository, orders, audit, authorization);
			return new SalesCrmService(
				runner,
				new SalesCrmRepository(_data),
				_customerRepository,
				_auditRepository,
				audit,
				customers,
				quotes,
				authorization);
		}
	}
}
