// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;

using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class SupplierRepository : DatabaseRepository
{
	private const string Columns =
		"s.Id, s.AccountNumber, s.CustomerNumber, s.Name, s.Contact, s.Email, s.Phone, s.Address, s.RmaTerms, s.Url, s.PaymentTerm, s.Iban, s.AccountName, s.SepaMandate, s.VatNumber, s.SupplierCategoryId, c.Name, s.Loyalty, s.Quality, s.Notes, s.IsActive, s.Version";
	private const string From = "FROM Suppliers s LEFT JOIN SupplierCategories c ON c.Id = s.SupplierCategoryId";

	public SupplierRepository(DatabaseAccess database) : base(database) { }

	public Task<IReadOnlyList<Supplier>> SearchAsync(string? searchText, CancellationToken cancellationToken)
	{
		var search = searchText?.Trim();
		var hasAccountNumber = long.TryParse(search, out var accountNumber);
		var filter = string.IsNullOrWhiteSpace(search)
			? string.Empty
			: $"WHERE {(hasAccountNumber ? "s.AccountNumber = $AccountNumber OR " : string.Empty)}s.CustomerNumber LIKE $Search OR s.Name LIKE $Search OR s.Contact LIKE $Search OR s.Email LIKE $Search OR s.Phone LIKE $Search OR s.VatNumber LIKE $Search OR c.Name LIKE $Search";
		var parameters = string.IsNullOrWhiteSpace(search)
			? []
			: hasAccountNumber
				? new[] { Parameter("$Search", $"%{search}%"), Parameter("$AccountNumber", accountNumber) }
				: new[] { Parameter("$Search", $"%{search}%") };
		return Database.QueryAsync($"SELECT {Columns} {From} {filter} ORDER BY s.IsActive DESC, s.Name;", Read, cancellationToken, parameters);
	}

	public Task<IReadOnlyList<Supplier>> ListActiveAsync(CancellationToken cancellationToken) =>
		Database.QueryAsync($"SELECT {Columns} {From} WHERE s.IsActive = 1 ORDER BY s.Name;", Read, cancellationToken);

	public Task<Supplier?> GetByIdAsync(long id, CancellationToken cancellationToken) =>
		Database.QuerySingleOrDefaultAsync($"SELECT {Columns} {From} WHERE s.Id = $Id;", Read, cancellationToken, Parameter("$Id", id));

	public Task<Supplier?> GetByNameAsync(string name, CancellationToken cancellationToken) =>
		Database.QuerySingleOrDefaultAsync($"SELECT {Columns} {From} WHERE s.Name = $Name;", Read, cancellationToken, Parameter("$Name", name));

	public Task<long> CreateAsync(Supplier supplier, CancellationToken cancellationToken) =>
		Database.ExecuteInWriteTransactionAsync(async (session, token) =>
		{
			var legacyNumber = $"LEGACY-{Guid.NewGuid():N}";
			var temporaryAccountNumber = -DateTime.UtcNow.Ticks;
			var id = await session.InsertAsync(
				"""
				INSERT INTO Suppliers (SupplierNumber, AccountNumber, CustomerNumber, Name, Contact, Email, Phone, Address, RmaTerms, Url, PaymentTerm, Iban, AccountName, SepaMandate, VatNumber, SupplierCategoryId, Loyalty, Quality, Notes, IsActive)
				VALUES ($SupplierNumber, $AccountNumber, $CustomerNumber, $Name, $Contact, $Email, $Phone, $Address, $RmaTerms, $Url, $PaymentTerm, $Iban, $AccountName, $SepaMandate, $VatNumber, $SupplierCategoryId, $Loyalty, $Quality, $Notes, $IsActive);
				""",
				token,
				[.. Parameters(supplier), Parameter("$SupplierNumber", legacyNumber), Parameter("$AccountNumber", temporaryAccountNumber)]);
			supplier.AccountNumber = id;
			await session.ExecuteAsync(
				"UPDATE Suppliers SET AccountNumber = $AccountNumber WHERE Id = $Id;",
				token,
				Parameter("$AccountNumber", supplier.AccountNumber),
				Parameter("$Id", id));
			return id;
		}, cancellationToken);

	public async Task<bool> UpdateAsync(Supplier supplier, CancellationToken cancellationToken) =>
		await Database.ExecuteAsync(
			"""
			UPDATE Suppliers SET CustomerNumber = $CustomerNumber, Name = $Name, Contact = $Contact, Email = $Email,
			Phone = $Phone, Address = $Address, RmaTerms = $RmaTerms, Url = $Url, PaymentTerm = $PaymentTerm,
			Iban = $Iban, AccountName = $AccountName, SepaMandate = $SepaMandate, VatNumber = $VatNumber, SupplierCategoryId = $SupplierCategoryId,
			Loyalty = $Loyalty, Quality = $Quality, Notes = $Notes, Version = Version + 1
			WHERE Id = $Id AND Version = $Version;
			""",
			cancellationToken,
			[.. Parameters(supplier), Parameter("$Id", supplier.Id), Parameter("$Version", supplier.Version)]) == 1;

	public async Task<bool> SetActiveAsync(long id, long version, bool isActive, CancellationToken cancellationToken) =>
		await Database.ExecuteAsync(
			"UPDATE Suppliers SET IsActive = $IsActive, Version = Version + 1 WHERE Id = $Id AND Version = $Version;",
			cancellationToken,
			Parameter("$Id", id), Parameter("$Version", version), Parameter("$IsActive", isActive)) == 1;

	private static Supplier Read(DbDataReader reader) => new()
	{
		Id = reader.GetInt64(0), AccountNumber = reader.GetInt64(1), CustomerNumber = NullableString(reader, 2), Name = reader.GetString(3),
		Contact = NullableString(reader, 4), Email = NullableString(reader, 5), Phone = NullableString(reader, 6),
		Address = NullableString(reader, 7), RmaTerms = NullableString(reader, 8), Url = NullableString(reader, 9),
		PaymentTerm = NullableString(reader, 10), Iban = NullableString(reader, 11), AccountName = NullableString(reader, 12), SepaMandate = NullableString(reader, 13),
		VatNumber = NullableString(reader, 14), SupplierCategoryId = reader.IsDBNull(15) ? null : reader.GetInt64(15),
		SupplierCategoryName = NullableString(reader, 16), Loyalty = reader.GetInt32(17), Quality = reader.GetInt32(18), Notes = NullableString(reader, 19),
		IsActive = reader.GetBoolean(20), Version = reader.GetInt64(21)
	};

	private static string? NullableString(DbDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

	private static DatabaseParameter[] Parameters(Supplier value) =>
	[
		Parameter("$CustomerNumber", value.CustomerNumber), Parameter("$Name", value.Name), Parameter("$Contact", value.Contact),
		Parameter("$Email", value.Email), Parameter("$Phone", value.Phone), Parameter("$Address", value.Address),
		Parameter("$RmaTerms", value.RmaTerms), Parameter("$Url", value.Url), Parameter("$PaymentTerm", value.PaymentTerm),
		Parameter("$Iban", value.Iban), Parameter("$AccountName", value.AccountName), Parameter("$SepaMandate", value.SepaMandate), Parameter("$VatNumber", value.VatNumber),
		Parameter("$SupplierCategoryId", value.SupplierCategoryId), Parameter("$Loyalty", value.Loyalty), Parameter("$Quality", value.Quality), Parameter("$Notes", value.Notes),
		Parameter("$IsActive", value.IsActive)
	];
}
