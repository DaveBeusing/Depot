// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Net.Mail;

using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class SupplierService
{
	private readonly SupplierRepository _suppliers;
	private readonly SupplierItemRepository _supplierItems;
	private readonly SupplierCategoryRepository _categories;
	private readonly AuditService _audit;
	private readonly AsyncCache<IReadOnlyList<Supplier>> _activeCache = new(TimeSpan.FromMinutes(5));

	public SupplierService(SupplierRepository suppliers, SupplierItemRepository supplierItems, SupplierCategoryRepository categories, AuditService audit)
	{
		_suppliers = suppliers;
		_supplierItems = supplierItems;
		_categories = categories;
		_audit = audit;
	}

	public Task<IReadOnlyList<Supplier>> SearchAsync(string? searchText, CancellationToken cancellationToken = default) =>
		_suppliers.SearchAsync(searchText, cancellationToken);

	public Task<IReadOnlyList<Supplier>> GetActiveAsync(CancellationToken cancellationToken = default) =>
		_activeCache.GetAsync(_suppliers.ListActiveAsync, cancellationToken);

	public async Task<Supplier> SaveAsync(Supplier draft, CancellationToken cancellationToken = default)
	{
		Normalize(draft);
		Validate(draft);
		var duplicateName = await _suppliers.GetByNameAsync(draft.Name, cancellationToken);
		if (duplicateName is not null && duplicateName.Id != draft.Id)
			throw new InvalidOperationException($"Supplier '{draft.Name}' already exists.");
		if (draft.SupplierCategoryId is not null)
		{
			var category = await _categories.GetByIdAsync(draft.SupplierCategoryId.Value, cancellationToken)
				?? throw new InvalidOperationException("The selected supplier category was not found.");
			if (!category.IsActive) throw new InvalidOperationException($"Category '{category.Name}' is inactive.");
			draft.SupplierCategoryName = category.Name;
		}

		if (draft.Id == 0)
		{
			draft.IsActive = true;
			draft.Id = await _suppliers.CreateAsync(draft, cancellationToken);
			await _audit.RecordCreatedAsync(draft.Id, draft, cancellationToken);
			_activeCache.Invalidate();
			return draft;
		}

		var existing = await _suppliers.GetByIdAsync(draft.Id, cancellationToken)
			?? throw new InvalidOperationException($"Supplier with id '{draft.Id}' was not found.");
		if (existing.Version != draft.Version) throw new ConcurrencyConflictException("supplier");
		var before = Copy(existing);
		draft.AccountNumber = existing.AccountNumber;
		draft.IsActive = existing.IsActive;
		if (!await _suppliers.UpdateAsync(draft, cancellationToken)) throw new ConcurrencyConflictException("supplier");
		draft.Version++;
		await _audit.RecordUpdatedAsync(draft.Id, before, draft, cancellationToken);
		_activeCache.Invalidate();
		return draft;
	}

	public async Task<Supplier> SetActiveAsync(long id, long version, bool isActive, CancellationToken cancellationToken = default)
	{
		var supplier = await _suppliers.GetByIdAsync(id, cancellationToken)
			?? throw new InvalidOperationException($"Supplier with id '{id}' was not found.");
		if (!isActive && await _supplierItems.HasActiveForSupplierAsync(id, cancellationToken))
			throw new InvalidOperationException($"Supplier '{supplier.Name}' has active supplier items and cannot be deactivated.");
		if (supplier.Version != version || !await _suppliers.SetActiveAsync(id, version, isActive, cancellationToken))
			throw new ConcurrencyConflictException("supplier");
		var before = Copy(supplier);
		supplier.IsActive = isActive;
		supplier.Version++;
		await _audit.RecordUpdatedAsync(id, before, supplier, cancellationToken);
		_activeCache.Invalidate();
		return supplier;
	}

	private static void Normalize(Supplier value)
	{
		value.CustomerNumber = NormalizeOptional(value.CustomerNumber);
		value.Name = value.Name.Trim();
		value.Contact = NormalizeOptional(value.Contact);
		value.Email = NormalizeOptional(value.Email)?.ToLowerInvariant();
		value.Phone = NormalizeOptional(value.Phone);
		value.Address = NormalizeOptional(value.Address);
		value.RmaTerms = NormalizeOptional(value.RmaTerms);
		value.Url = NormalizeOptional(value.Url);
		value.PaymentTerm = NormalizeOptional(value.PaymentTerm);
		value.Iban = NormalizeOptional(value.Iban)?.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
		value.AccountName = NormalizeOptional(value.AccountName);
		value.SepaMandate = NormalizeOptional(value.SepaMandate);
		value.VatNumber = NormalizeOptional(value.VatNumber)?.ToUpperInvariant();
		value.Notes = NormalizeOptional(value.Notes);
	}

	private static void Validate(Supplier value)
	{
		Required(value.Name, 200, "Supplier name");
		Max(value.CustomerNumber, 100, "Customer number");
		Max(value.Contact, 200, "Contact"); Max(value.Email, 320, "Email"); Max(value.Phone, 100, "Phone");
		Max(value.Address, 1000, "Address"); Max(value.RmaTerms, 2000, "RMA terms"); Max(value.Url, 500, "URL");
		Max(value.PaymentTerm, 200, "Payment term"); Max(value.Iban, 34, "IBAN"); Max(value.AccountName, 200, "Account name");
		Max(value.SepaMandate, 200, "SEPA mandate"); Max(value.VatNumber, 50, "VAT number"); Max(value.Notes, 4000, "Notes");
		if (value.Loyalty < 0) throw new ArgumentOutOfRangeException(nameof(value.Loyalty), "Loyalty cannot be negative.");
		if (value.Quality < 0) throw new ArgumentOutOfRangeException(nameof(value.Quality), "Quality cannot be negative.");
		if (value.Email is not null)
		{
			try { _ = new MailAddress(value.Email); }
			catch (FormatException exception) { throw new ArgumentException("Email address is invalid.", nameof(value.Email), exception); }
		}
		if (value.Url is not null && (!Uri.TryCreate(value.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")))
			throw new ArgumentException("URL must be an absolute HTTP or HTTPS address.", nameof(value.Url));
		if (value.Iban is not null && !IsValidIban(value.Iban))
			throw new ArgumentException("IBAN format is invalid.", nameof(value.Iban));
	}

	private static bool IsValidIban(string iban)
	{
		if (iban.Length is < 15 or > 34 || iban.Any(character => !char.IsLetterOrDigit(character))) return false;
		var rearranged = iban[4..] + iban[..4];
		var remainder = 0;
		foreach (var character in rearranged)
		{
			if (char.IsDigit(character)) remainder = ((remainder * 10) + (character - '0')) % 97;
			else
			{
				var value = character - 'A' + 10;
				remainder = ((remainder * 100) + value) % 97;
			}
		}
		return remainder == 1;
	}

	private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
	private static void Required(string value, int max, string label) { if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{label} is required."); if (value.Length > max) throw new ArgumentException($"{label} must not exceed {max} characters."); }
	private static void Max(string? value, int max, string label) { if (value?.Length > max) throw new ArgumentException($"{label} must not exceed {max} characters."); }

	private static Supplier Copy(Supplier source) => new()
	{
		Id = source.Id, AccountNumber = source.AccountNumber, CustomerNumber = source.CustomerNumber, Name = source.Name, Contact = source.Contact, Email = source.Email,
		Phone = source.Phone, Address = source.Address, RmaTerms = source.RmaTerms, Url = source.Url, PaymentTerm = source.PaymentTerm,
		Iban = source.Iban, AccountName = source.AccountName, SepaMandate = source.SepaMandate, VatNumber = source.VatNumber, SupplierCategoryId = source.SupplierCategoryId,
		SupplierCategoryName = source.SupplierCategoryName, Loyalty = source.Loyalty, Quality = source.Quality, Notes = source.Notes, IsActive = source.IsActive, Version = source.Version
	};
}
