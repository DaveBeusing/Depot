// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class CustomerRepository : DatabaseRepository
{
	private const string Columns = "Id,CustomerNumber,Name,BillingAddress,ShippingAddress,ContactName,Email,Phone,TaxId,VatId,BuyerReference,EInvoiceEndpoint,EInvoiceEndpointScheme,BillingStreet,BillingAddressLine2,BillingPostalCode,BillingCity,BillingCountryCode,SalesRegionId,(SELECT Name FROM SalesRegions WHERE Id=Customers.SalesRegionId),PaymentTermsDays,Currency,IsActive,Version";
	public CustomerRepository(DatabaseAccess database) : base(database) { }

	public Task<PageResult<Customer>> SearchAsync(string? searchText, bool includeInactive, int pageNumber, int pageSize, CancellationToken cancellationToken)
	{
		var filters = new List<string>(); var parameters = new List<DatabaseParameter>();
		if (!includeInactive) filters.Add("IsActive=1");
		if (!string.IsNullOrWhiteSpace(searchText)) { filters.Add("(CustomerNumber LIKE $Search OR Name LIKE $Search OR Email LIKE $Search OR TaxId LIKE $Search OR VatId LIKE $Search)"); parameters.Add(Parameter("$Search", $"%{searchText.Trim()}%")); }
		var where = filters.Count == 0 ? string.Empty : $"WHERE {string.Join(" AND ", filters)}";
		return Database.QueryPageAsync($"SELECT {Columns} FROM Customers {where} ORDER BY Name,CustomerNumber", $"SELECT COUNT(*) FROM Customers {where}", Read, pageNumber, pageSize, cancellationToken, parameters.ToArray());
	}

	public async Task<Customer?> GetByIdAsync(long id, CancellationToken cancellationToken)
	{
		var customer = await Database.QuerySingleOrDefaultAsync($"SELECT {Columns} FROM Customers WHERE Id=$Id;", Read, cancellationToken, Parameter("$Id", id));
		if (customer is null) return null;
		customer.Addresses = await ListAddressesAsync(id, cancellationToken); customer.Contacts = await ListContactsAsync(id, cancellationToken); return customer;
	}

	public Task<IReadOnlyList<Customer>> ListActiveAsync(CancellationToken cancellationToken) => Database.QueryAsync($"SELECT {Columns} FROM Customers WHERE IsActive=1 ORDER BY Name,CustomerNumber;", Read, cancellationToken);
	public Task<SalesRegion?> GetSalesRegionAsync(long id, CancellationToken cancellationToken) => Database.QuerySingleOrDefaultAsync("SELECT Id,Code,Name,IsActive,Version FROM SalesRegions WHERE Id=$Id;", ReadRegion, cancellationToken, Parameter("$Id", id));
	public Task<IReadOnlyList<CustomerAddress>> ListAddressesAsync(long customerId, CancellationToken cancellationToken) => Database.QueryAsync("SELECT Id,CustomerId,Type,Name,Address,IsDefault,IsActive,Version FROM CustomerAddresses WHERE CustomerId=$CustomerId AND IsActive=1 ORDER BY Type,IsDefault DESC,Id;", ReadAddress, cancellationToken, Parameter("$CustomerId", customerId));
	public Task<IReadOnlyList<CustomerContact>> ListContactsAsync(long customerId, CancellationToken cancellationToken) => Database.QueryAsync("SELECT Id,CustomerId,Name,Role,Department,Email,Phone,Mobile,IsPrimary,IsActive,Version FROM CustomerContacts WHERE CustomerId=$CustomerId AND IsActive=1 ORDER BY IsPrimary DESC,Name;", ReadContact, cancellationToken, Parameter("$CustomerId", customerId));

	public async Task<Customer> SaveAsync(Customer customer, CancellationToken cancellationToken)
	{
		if (customer.Id == 0)
		{
			customer.CustomerNumber = $"PENDING-{Guid.NewGuid():N}";
			customer.Id = await Database.InsertAsync("INSERT INTO Customers (CustomerNumber,Name,BillingAddress,ShippingAddress,ContactName,Email,Phone,TaxId,VatId,BuyerReference,EInvoiceEndpoint,EInvoiceEndpointScheme,BillingStreet,BillingAddressLine2,BillingPostalCode,BillingCity,BillingCountryCode,SalesRegionId,PaymentTermsDays,Currency,IsActive) VALUES ($CustomerNumber,$Name,$BillingAddress,$ShippingAddress,$ContactName,$Email,$Phone,$TaxId,$VatId,$BuyerReference,$EInvoiceEndpoint,$EInvoiceEndpointScheme,$BillingStreet,$BillingAddressLine2,$BillingPostalCode,$BillingCity,$BillingCountryCode,$SalesRegionId,$PaymentTermsDays,$Currency,$IsActive);", cancellationToken, Parameters(customer));
			customer.CustomerNumber = $"CU-{customer.Id:000000}";
			await Database.ExecuteAsync("UPDATE Customers SET CustomerNumber=$CustomerNumber WHERE Id=$Id;", cancellationToken, Parameter("$CustomerNumber", customer.CustomerNumber), Parameter("$Id", customer.Id));
			await SyncDefaultAddressAsync(customer.Id, CustomerAddressType.Billing, customer.BillingAddress, cancellationToken); await SyncDefaultAddressAsync(customer.Id, CustomerAddressType.Shipping, customer.ShippingAddress, cancellationToken);
			customer.Addresses = await ListAddressesAsync(customer.Id, cancellationToken); customer.Contacts = await ListContactsAsync(customer.Id, cancellationToken); return customer;
		}
		var updated = await Database.ExecuteAsync("UPDATE Customers SET Name=$Name,BillingAddress=$BillingAddress,ShippingAddress=$ShippingAddress,ContactName=$ContactName,Email=$Email,Phone=$Phone,TaxId=$TaxId,VatId=$VatId,BuyerReference=$BuyerReference,EInvoiceEndpoint=$EInvoiceEndpoint,EInvoiceEndpointScheme=$EInvoiceEndpointScheme,BillingStreet=$BillingStreet,BillingAddressLine2=$BillingAddressLine2,BillingPostalCode=$BillingPostalCode,BillingCity=$BillingCity,BillingCountryCode=$BillingCountryCode,SalesRegionId=$SalesRegionId,PaymentTermsDays=$PaymentTermsDays,Currency=$Currency,IsActive=$IsActive,Version=Version+1 WHERE Id=$Id AND Version=$Version;", cancellationToken, Parameters(customer).Concat([Parameter("$Id", customer.Id), Parameter("$Version", customer.Version)]).ToArray());
		if (updated != 1) throw new Services.ConcurrencyConflictException("customer"); customer.Version++;
		await SyncDefaultAddressAsync(customer.Id, CustomerAddressType.Billing, customer.BillingAddress, cancellationToken); await SyncDefaultAddressAsync(customer.Id, CustomerAddressType.Shipping, customer.ShippingAddress, cancellationToken);
		customer.Addresses = await ListAddressesAsync(customer.Id, cancellationToken); customer.Contacts = await ListContactsAsync(customer.Id, cancellationToken); return customer;
	}

	public async Task<CustomerAddress> SaveAddressAsync(CustomerAddress address, CancellationToken cancellationToken)
	{
		if (address.CustomerId <= 0) throw new ArgumentException("A customer is required.", nameof(address)); if (string.IsNullOrWhiteSpace(address.Address)) throw new ArgumentException("An address is required.", nameof(address));
		if (address.IsDefault) await Database.ExecuteAsync("UPDATE CustomerAddresses SET IsDefault=0,Version=Version+1 WHERE CustomerId=$CustomerId AND Type=$Type AND IsDefault=1;", cancellationToken, Parameter("$CustomerId", address.CustomerId), Parameter("$Type", (int)address.Type));
		if (address.Id == 0) { address.Id = await Database.InsertAsync("INSERT INTO CustomerAddresses (CustomerId,Type,Name,Address,IsDefault,IsActive) VALUES ($CustomerId,$Type,$Name,$Address,$IsDefault,$IsActive);", cancellationToken, Parameter("$CustomerId", address.CustomerId), Parameter("$Type", (int)address.Type), Parameter("$Name", address.Name), Parameter("$Address", address.Address.Trim()), Parameter("$IsDefault", address.IsDefault), Parameter("$IsActive", address.IsActive)); return address; }
		var updated = await Database.ExecuteAsync("UPDATE CustomerAddresses SET Type=$Type,Name=$Name,Address=$Address,IsDefault=$IsDefault,IsActive=$IsActive,Version=Version+1 WHERE Id=$Id AND Version=$Version;", cancellationToken, Parameter("$Type", (int)address.Type), Parameter("$Name", address.Name), Parameter("$Address", address.Address.Trim()), Parameter("$IsDefault", address.IsDefault), Parameter("$IsActive", address.IsActive), Parameter("$Id", address.Id), Parameter("$Version", address.Version));
		if (updated != 1) throw new Services.ConcurrencyConflictException("customer address"); address.Version++; return address;
	}

	public async Task<CustomerContact> SaveContactAsync(CustomerContact contact, CancellationToken cancellationToken)
	{
		if (contact.IsPrimary) await Database.ExecuteAsync("UPDATE CustomerContacts SET IsPrimary=0,Version=Version+1 WHERE CustomerId=$CustomerId AND IsPrimary=1;", cancellationToken, Parameter("$CustomerId", contact.CustomerId));
		if (contact.Id == 0) { contact.Id = await Database.InsertAsync("INSERT INTO CustomerContacts (CustomerId,Name,Role,Department,Email,Phone,Mobile,IsPrimary,IsActive) VALUES ($CustomerId,$Name,$Role,$Department,$Email,$Phone,$Mobile,$IsPrimary,$IsActive);", cancellationToken, Parameter("$CustomerId", contact.CustomerId), Parameter("$Name", contact.Name), Parameter("$Role", (int)contact.Role), Parameter("$Department", contact.Department), Parameter("$Email", contact.Email), Parameter("$Phone", contact.Phone), Parameter("$Mobile", contact.Mobile), Parameter("$IsPrimary", contact.IsPrimary), Parameter("$IsActive", contact.IsActive)); return contact; }
		var updated = await Database.ExecuteAsync("UPDATE CustomerContacts SET Name=$Name,Role=$Role,Department=$Department,Email=$Email,Phone=$Phone,Mobile=$Mobile,IsPrimary=$IsPrimary,IsActive=$IsActive,Version=Version+1 WHERE Id=$Id AND Version=$Version;", cancellationToken, Parameter("$Name", contact.Name), Parameter("$Role", (int)contact.Role), Parameter("$Department", contact.Department), Parameter("$Email", contact.Email), Parameter("$Phone", contact.Phone), Parameter("$Mobile", contact.Mobile), Parameter("$IsPrimary", contact.IsPrimary), Parameter("$IsActive", contact.IsActive), Parameter("$Id", contact.Id), Parameter("$Version", contact.Version));
		if (updated != 1) throw new Services.ConcurrencyConflictException("customer contact"); contact.Version++; return contact;
	}

	private async Task SyncDefaultAddressAsync(long customerId, CustomerAddressType type, string? value, CancellationToken cancellationToken)
	{
		var current = await Database.QuerySingleOrDefaultAsync("SELECT Id,CustomerId,Type,Name,Address,IsDefault,IsActive,Version FROM CustomerAddresses WHERE CustomerId=$CustomerId AND Type=$Type AND IsDefault=1 ORDER BY Id LIMIT 1;", ReadAddress, cancellationToken, Parameter("$CustomerId", customerId), Parameter("$Type", (int)type));
		if (string.IsNullOrWhiteSpace(value)) { if (current is not null) await Database.ExecuteAsync("UPDATE CustomerAddresses SET IsActive=0,Version=Version+1 WHERE Id=$Id AND Version=$Version;", cancellationToken, Parameter("$Id", current.Id), Parameter("$Version", current.Version)); return; }
		if (current is null) { await SaveAddressAsync(new CustomerAddress { CustomerId = customerId, Type = type, Address = value.Trim(), IsDefault = true, IsActive = true }, cancellationToken); return; }
		if (string.Equals(current.Address, value.Trim(), StringComparison.Ordinal) && current.IsActive) return; current.Address = value.Trim(); current.IsActive = true; current.IsDefault = true; await SaveAddressAsync(current, cancellationToken);
	}

	private static DatabaseParameter[] Parameters(Customer c) =>
	[
		new("$CustomerNumber",c.CustomerNumber),new("$Name",c.Name),new("$BillingAddress",c.BillingAddress),new("$ShippingAddress",c.ShippingAddress),new("$ContactName",c.ContactName),new("$Email",c.Email),new("$Phone",c.Phone),new("$TaxId",c.TaxId),new("$VatId",c.VatId),new("$BuyerReference",c.BuyerReference),new("$EInvoiceEndpoint",c.EInvoiceEndpoint),new("$EInvoiceEndpointScheme",c.EInvoiceEndpointScheme),new("$BillingStreet",c.BillingStreet),new("$BillingAddressLine2",c.BillingAddressLine2),new("$BillingPostalCode",c.BillingPostalCode),new("$BillingCity",c.BillingCity),new("$BillingCountryCode",c.BillingCountryCode),new("$SalesRegionId",c.SalesRegionId),new("$PaymentTermsDays",c.PaymentTermsDays),new("$Currency",c.Currency),new("$IsActive",c.IsActive)
	];

	private static Customer Read(DbDataReader r) => new()
	{
		Id=r.GetInt64(0),CustomerNumber=r.GetString(1),Name=r.GetString(2),BillingAddress=r.IsDBNull(3)?null:r.GetString(3),ShippingAddress=r.IsDBNull(4)?null:r.GetString(4),ContactName=r.IsDBNull(5)?null:r.GetString(5),Email=r.IsDBNull(6)?null:r.GetString(6),Phone=r.IsDBNull(7)?null:r.GetString(7),TaxId=r.IsDBNull(8)?null:r.GetString(8),VatId=r.IsDBNull(9)?null:r.GetString(9),BuyerReference=r.IsDBNull(10)?null:r.GetString(10),EInvoiceEndpoint=r.IsDBNull(11)?null:r.GetString(11),EInvoiceEndpointScheme=r.IsDBNull(12)?null:r.GetString(12),BillingStreet=r.IsDBNull(13)?null:r.GetString(13),BillingAddressLine2=r.IsDBNull(14)?null:r.GetString(14),BillingPostalCode=r.IsDBNull(15)?null:r.GetString(15),BillingCity=r.IsDBNull(16)?null:r.GetString(16),BillingCountryCode=r.IsDBNull(17)?null:r.GetString(17),SalesRegionId=r.IsDBNull(18)?null:r.GetInt64(18),SalesRegionName=r.IsDBNull(19)?null:r.GetString(19),PaymentTermsDays=r.GetInt32(20),Currency=r.GetString(21),IsActive=r.GetBoolean(22),Version=r.GetInt64(23)
	};
	private static SalesRegion ReadRegion(DbDataReader r)=>new(){Id=r.GetInt64(0),Code=r.GetString(1),Name=r.GetString(2),IsActive=r.GetBoolean(3),Version=r.GetInt64(4)};
	private static CustomerAddress ReadAddress(DbDataReader r)=>new(){Id=r.GetInt64(0),CustomerId=r.GetInt64(1),Type=(CustomerAddressType)r.GetInt32(2),Name=r.IsDBNull(3)?null:r.GetString(3),Address=r.GetString(4),IsDefault=r.GetBoolean(5),IsActive=r.GetBoolean(6),Version=r.GetInt64(7)};
	private static CustomerContact ReadContact(DbDataReader r)=>new(){Id=r.GetInt64(0),CustomerId=r.GetInt64(1),Name=r.GetString(2),Role=(CustomerContactRole)r.GetInt32(3),Department=r.IsDBNull(4)?null:r.GetString(4),Email=r.IsDBNull(5)?null:r.GetString(5),Phone=r.IsDBNull(6)?null:r.GetString(6),Mobile=r.IsDBNull(7)?null:r.GetString(7),IsPrimary=r.GetBoolean(8),IsActive=r.GetBoolean(9),Version=r.GetInt64(10)};
}
