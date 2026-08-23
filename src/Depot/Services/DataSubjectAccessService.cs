// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;
using System.Text.Json;

using Depot.Data;
using Depot.Models;

namespace Depot.Services;

public sealed class DataSubjectAccessService
{
	private const int MaxMatchesPerSource = 200;
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		WriteIndented = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	private readonly DatabaseAccess _database;
	private readonly IAuthorizationService _authorization;

	public DataSubjectAccessService(DatabaseAccess database, IAuthorizationService authorization)
	{
		_database = database;
		_authorization = authorization;
	}

	public async Task<PersonalDataSearchResult> SearchAsync(string query, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.AdministrationView);
		var normalized = NormalizeQuery(query);
		var search = $"%{normalized}%";
		var parameter = new DatabaseParameter("$Search", search);

		var usersTask = _database.QuerySliceAsync(
			"SELECT Id, Email, DisplayName, IsActive, CreatedUtc FROM Users WHERE Email LIKE $Search OR DisplayName LIKE $Search ORDER BY Id",
			ReadUser,
			0,
			MaxMatchesPerSource,
			cancellationToken,
			parameter);
		var customersTask = _database.QuerySliceAsync(
			"SELECT Id, CustomerNumber, Name, BillingAddress, ShippingAddress, ContactName, Email, Phone, TaxId, IsActive FROM Customers WHERE Name LIKE $Search OR ContactName LIKE $Search OR Email LIKE $Search OR Phone LIKE $Search OR BillingAddress LIKE $Search OR ShippingAddress LIKE $Search OR TaxId LIKE $Search ORDER BY Id",
			ReadCustomer,
			0,
			MaxMatchesPerSource,
			cancellationToken,
			parameter);
		var contactsTask = _database.QuerySliceAsync(
			"SELECT Id, CustomerId, Name, Department, Email, Phone, Mobile, IsPrimary, IsActive FROM CustomerContacts WHERE Name LIKE $Search OR Department LIKE $Search OR Email LIKE $Search OR Phone LIKE $Search OR Mobile LIKE $Search ORDER BY Id",
			ReadCustomerContact,
			0,
			MaxMatchesPerSource,
			cancellationToken,
			parameter);
		var suppliersTask = _database.QuerySliceAsync(
			"SELECT Id, AccountNumber, Name, Contact, Email, Phone, Address, Iban, AccountName, SepaMandate, VatNumber, IsActive FROM Suppliers WHERE Name LIKE $Search OR Contact LIKE $Search OR Email LIKE $Search OR Phone LIKE $Search OR Address LIKE $Search OR Iban LIKE $Search OR AccountName LIKE $Search OR SepaMandate LIKE $Search OR VatNumber LIKE $Search ORDER BY Id",
			ReadSupplier,
			0,
			MaxMatchesPerSource,
			cancellationToken,
			parameter);
		var auditTask = _database.QuerySliceAsync(
			"SELECT Id, UserEmail, EntityType, EntityId, Action, TimestampUtc FROM AuditEntries WHERE UserEmail LIKE $Search ORDER BY Id",
			ReadAudit,
			0,
			MaxMatchesPerSource,
			cancellationToken,
			parameter);

		await Task.WhenAll(usersTask, customersTask, contactsTask, suppliersTask, auditTask);
		var records = (await usersTask)
			.Concat(await customersTask)
			.Concat(await contactsTask)
			.Concat(await suppliersTask)
			.Concat(await auditTask)
			.OrderBy(record => record.Category, StringComparer.Ordinal)
			.ThenBy(record => record.Source, StringComparer.Ordinal)
			.ThenBy(record => record.EntityId)
			.ToArray();
		return new PersonalDataSearchResult(normalized, DateTime.UtcNow, records);
	}

	public async Task<string> CreateJsonExportAsync(string query, CancellationToken cancellationToken = default)
	{
		var result = await SearchAsync(query, cancellationToken);
		return JsonSerializer.Serialize(result, JsonOptions);
	}

	public async Task ExportJsonAsync(string query, string filePath, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("An export path is required.", nameof(filePath));
		var json = await CreateJsonExportAsync(query, cancellationToken);
		await File.WriteAllTextAsync(filePath, json, cancellationToken);
	}

	private static string NormalizeQuery(string query)
	{
		var normalized = query?.Trim() ?? string.Empty;
		if (normalized.Length < 2) throw new ArgumentException("Enter at least two characters to search personal data.", nameof(query));
		if (normalized.Length > 250) throw new ArgumentException("The personal-data search term must not exceed 250 characters.", nameof(query));
		return normalized;
	}

	private static PersonalDataRecord ReadUser(DbDataReader reader) => new(
		"User",
		"Users",
		reader.GetInt64(0),
		reader.GetString(2),
		reader.GetString(1),
		Fields(
			("email", reader.GetString(1)),
			("displayName", reader.GetString(2)),
			("isActive", reader.GetBoolean(3).ToString(CultureInfo.InvariantCulture)),
			("createdUtc", reader.GetString(4))));

	private static PersonalDataRecord ReadCustomer(DbDataReader reader) => new(
		"Customer",
		"Customers",
		reader.GetInt64(0),
		reader.GetString(2),
		NullableString(reader, 6),
		Fields(
			("customerNumber", reader.GetString(1)),
			("name", reader.GetString(2)),
			("billingAddress", NullableString(reader, 3)),
			("shippingAddress", NullableString(reader, 4)),
			("contactName", NullableString(reader, 5)),
			("email", NullableString(reader, 6)),
			("phone", NullableString(reader, 7)),
			("taxId", NullableString(reader, 8)),
			("isActive", reader.GetBoolean(9).ToString(CultureInfo.InvariantCulture))));

	private static PersonalDataRecord ReadCustomerContact(DbDataReader reader) => new(
		"Customer contact",
		"CustomerContacts",
		reader.GetInt64(0),
		reader.GetString(2),
		NullableString(reader, 4),
		Fields(
			("customerId", reader.GetInt64(1).ToString(CultureInfo.InvariantCulture)),
			("name", reader.GetString(2)),
			("department", NullableString(reader, 3)),
			("email", NullableString(reader, 4)),
			("phone", NullableString(reader, 5)),
			("mobile", NullableString(reader, 6)),
			("isPrimary", reader.GetBoolean(7).ToString(CultureInfo.InvariantCulture)),
			("isActive", reader.GetBoolean(8).ToString(CultureInfo.InvariantCulture))));

	private static PersonalDataRecord ReadSupplier(DbDataReader reader) => new(
		"Supplier/contact",
		"Suppliers",
		reader.GetInt64(0),
		reader.GetString(2),
		NullableString(reader, 4),
		Fields(
			("accountNumber", reader.GetInt64(1).ToString(CultureInfo.InvariantCulture)),
			("name", reader.GetString(2)),
			("contact", NullableString(reader, 3)),
			("email", NullableString(reader, 4)),
			("phone", NullableString(reader, 5)),
			("address", NullableString(reader, 6)),
			("iban", NullableString(reader, 7)),
			("accountName", NullableString(reader, 8)),
			("sepaMandate", NullableString(reader, 9)),
			("vatNumber", NullableString(reader, 10)),
			("isActive", reader.GetBoolean(11).ToString(CultureInfo.InvariantCulture))));

	private static PersonalDataRecord ReadAudit(DbDataReader reader) => new(
		"Audit attribution",
		"AuditEntries",
		reader.GetInt64(0),
		reader.GetString(1),
		reader.GetString(1),
		Fields(
			("userEmail", reader.GetString(1)),
			("entityType", reader.GetString(2)),
			("entityId", reader.GetInt64(3).ToString(CultureInfo.InvariantCulture)),
			("action", reader.GetString(4)),
			("timestampUtc", reader.GetString(5))));

	private static Dictionary<string, string?> Fields(params (string Name, string? Value)[] values) =>
		values.ToDictionary(value => value.Name, value => value.Value, StringComparer.Ordinal);

	private static string? NullableString(DbDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
}
