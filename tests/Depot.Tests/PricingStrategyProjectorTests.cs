// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

using Xunit;

namespace Depot.Tests;

public sealed class PricingStrategyProjectorTests
{
	[Fact]
	public void ProjectsGlobalRegionAndCustomerAssignmentHierarchyDeterministically()
	{
		var region = new SalesRegion { Id = 7, Code = "DACH", Name = "DACH" };
		var customer = new Customer { Id = 11, CustomerNumber = "C-11", Name = "Acme", SalesRegionId = region.Id, SalesRegionName = region.Name, IsActive = true };
		var global = new SalesPriceList { Id = 1, Code = "GLOBAL", Name = "Global Standard", Scope = SalesPriceListScope.Global, Currency = "EUR" };
		var regional = new SalesPriceList { Id = 2, Code = "DACH", Name = "DACH Standard", Scope = SalesPriceListScope.Region, RegionId = region.Id, RegionName = region.Name, Currency = "EUR" };
		var customerList = new SalesPriceList { Id = 3, Code = "ACME", Name = "Acme Contract", Scope = SalesPriceListScope.Customer, Currency = "EUR" };
		var assignment = new CustomerPriceListAssignment { CustomerId = customer.Id, SalesPriceListId = customerList.Id, PriceListName = customerList.Name, IsActive = true };

		var projection = PricingStrategyProjector.Project([customerList, regional, global], [region], [customer], [assignment]);

		Assert.Equal(3, projection.Roots.Count);
		Assert.Equal(["Global pricing", "Regional pricing", "Customer pricing"], projection.Roots.Select(value => value.Title).ToArray());
		Assert.Equal(global.Id, Assert.Single(projection.Roots[0].ChildNodes).PriceListId);
		var regionNode = Assert.Single(projection.Roots[1].ChildNodes);
		Assert.Equal(region.Id, regionNode.RegionId);
		Assert.Equal(regional.Id, Assert.Single(regionNode.ChildNodes).PriceListId);
		var customerListNode = Assert.Single(projection.Roots[2].ChildNodes);
		Assert.Equal(customerList.Id, customerListNode.PriceListId);
		var customerNode = Assert.Single(customerListNode.ChildNodes);
		Assert.Equal(customer.Id, customerNode.CustomerId);
		Assert.Equal(region.Id, customerNode.RegionId);
		Assert.Equal(new[] { 0, 1, 0, 1, 2, 0, 1, 2 }, projection.Rows.Select(row => row.Depth).ToArray());
	}

	[Fact]
	public void InactiveListsAndAssignmentsRemainVisible()
	{
		var customer = new Customer { Id = 14, CustomerNumber = "C-14", Name = "Inactive assignment", IsActive = true };
		var list = new SalesPriceList { Id = 5, Code = "STAGED", Name = "Staged", Scope = SalesPriceListScope.Customer, Currency = "EUR", IsActive = false };
		var assignment = new CustomerPriceListAssignment { CustomerId = customer.Id, SalesPriceListId = list.Id, PriceListName = list.Name, IsActive = false };

		var projection = PricingStrategyProjector.Project([list], [], [customer], [assignment]);

		var listNode = projection.Rows.Single(row => row.PriceListId == list.Id && row.Kind == PricingStrategyNodeKind.CustomerPriceList);
		var assignmentNode = projection.Rows.Single(row => row.CustomerId == customer.Id);
		Assert.False(listNode.IsActive);
		Assert.False(assignmentNode.IsActive);
		Assert.Equal("Inactive", listNode.State);
		Assert.Equal("Inactive", assignmentNode.State);
	}

	[Fact]
	public void ProjectionDoesNotInventCustomerFallbackEdges()
	{
		var region = new SalesRegion { Id = 2, Code = "EMEA", Name = "EMEA" };
		var customer = new Customer { Id = 9, CustomerNumber = "C-9", Name = "Automatic customer", SalesRegionId = region.Id, SalesRegionName = region.Name, IsActive = true };
		var global = new SalesPriceList { Id = 1, Code = "G", Name = "Global", Scope = SalesPriceListScope.Global, Currency = "EUR" };
		var regional = new SalesPriceList { Id = 2, Code = "R", Name = "Regional", Scope = SalesPriceListScope.Region, RegionId = region.Id, RegionName = region.Name, Currency = "EUR" };

		var projection = PricingStrategyProjector.Project([global, regional], [region], [customer], []);

		Assert.DoesNotContain(projection.Rows, row => row.CustomerId == customer.Id);
		Assert.Empty(projection.Roots[2].ChildNodes);
	}
}
