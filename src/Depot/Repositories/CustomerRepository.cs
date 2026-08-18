// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class CustomerRepository : DatabaseRepository
{
	private const string Columns = "Id, CustomerNumber, Name, BillingAddress, ShippingAddress, ContactName, Email, Phone, TaxId, PaymentTermsDays, Currency, IsActive, Version";
	public CustomerRepository(DatabaseAccess database) : base(database) { }

	public Task<PageResult<Customer>> SearchAsync(string? searchText, bool includeInactive, int pageNumber, int pageSize, CancellationToken cancellationToken)
	{
		var filters = new List<string>();
		var parameters = new List<DatabaseParameter>();
		if (!includeInactive) filters.Add("IsActive = 1");
		if (!string.IsNullOrWhiteSpace(searchText))
		{
			filters.Add("(CustomerNumber LIKE $Search OR Name LIKE $Search OR Email LIKE $Search OR TaxId LIKE $Search)");
			parameters.Add(Parameter("$Search", $"%{searchText.Trim()}%"));
		}
		var where = filters.Count == 0 ? string.Empty : $"WHERE {string.Join(" AND ", filters)}";
		return Database.QueryPageAsync($"SELECT {Columns} FROM Customers {where} ORDER BY Name, CustomerNumber", $"SELECT COUNT(*) FROM Customers {where}", Read, pageNumber, pageSize, cancellationToken, parameters.ToArray());
	}

	public Task<Customer?> GetByIdAsync(long id, CancellationToken cancellationToken) => Database.QuerySingleOrDefaultAsync($"SELECT {Columns} FROM Customers WHERE Id = $Id;", Read, cancellationToken, Parameter("$Id", id));

	public Task<IReadOnlyList<Customer>> ListActiveAsync(CancellationToken cancellationToken) => Database.QueryAsync($"SELECT {Columns} FROM Customers WHERE IsActive = 1 ORDER BY Name, CustomerNumber;", Read, cancellationToken);

	public async Task<Customer> SaveAsync(Customer customer, CancellationToken cancellationToken)
	{
		if (customer.Id == 0)
		{
			customer.CustomerNumber = $"PENDING-{Guid.NewGuid():N}";
			customer.Id = await Database.InsertAsync("INSERT INTO Customers (CustomerNumber, Name, BillingAddress, ShippingAddress, ContactName, Email, Phone, TaxId, PaymentTermsDays, Currency, IsActive) VALUES ($CustomerNumber, $Name, $BillingAddress, $ShippingAddress, $ContactName, $Email, $Phone, $TaxId, $PaymentTermsDays, $Currency, $IsActive);", cancellationToken, Parameters(customer));
			customer.CustomerNumber = $"CU-{customer.Id:000000}";
			await Database.ExecuteAsync("UPDATE Customers SET CustomerNumber = $CustomerNumber WHERE Id = $Id;", cancellationToken, Parameter("$CustomerNumber", customer.CustomerNumber), Parameter("$Id", customer.Id));
			return customer;
		}
		var updated = await Database.ExecuteAsync("UPDATE Customers SET Name=$Name, BillingAddress=$BillingAddress, ShippingAddress=$ShippingAddress, ContactName=$ContactName, Email=$Email, Phone=$Phone, TaxId=$TaxId, PaymentTermsDays=$PaymentTermsDays, Currency=$Currency, IsActive=$IsActive, Version=Version+1 WHERE Id=$Id AND Version=$Version;", cancellationToken, Parameters(customer).Concat([Parameter("$Id", customer.Id), Parameter("$Version", customer.Version)]).ToArray());
		if (updated != 1) throw new Services.ConcurrencyConflictException("customer");
		customer.Version++;
		return customer;
	}

	private static DatabaseParameter[] Parameters(Customer c) =>
	[
		new("$CustomerNumber", c.CustomerNumber), new("$Name", c.Name), new("$BillingAddress", c.BillingAddress), new("$ShippingAddress", c.ShippingAddress),
		new("$ContactName", c.ContactName), new("$Email", c.Email), new("$Phone", c.Phone), new("$TaxId", c.TaxId), new("$PaymentTermsDays", c.PaymentTermsDays),
		new("$Currency", c.Currency), new("$IsActive", c.IsActive)
	];

	private static Customer Read(DbDataReader r) => new()
	{
		Id = r.GetInt64(0), CustomerNumber = r.GetString(1), Name = r.GetString(2), BillingAddress = r.IsDBNull(3) ? null : r.GetString(3), ShippingAddress = r.IsDBNull(4) ? null : r.GetString(4),
		ContactName = r.IsDBNull(5) ? null : r.GetString(5), Email = r.IsDBNull(6) ? null : r.GetString(6), Phone = r.IsDBNull(7) ? null : r.GetString(7), TaxId = r.IsDBNull(8) ? null : r.GetString(8),
		PaymentTermsDays = r.GetInt32(9), Currency = r.GetString(10), IsActive = r.GetBoolean(11), Version = r.GetInt64(12)
	};
}
