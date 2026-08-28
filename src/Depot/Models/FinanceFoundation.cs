// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed record CurrencyCode
{
	public CurrencyCode(string value)
	{
		if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Currency code is required.", nameof(value));
		var normalized = value.Trim().ToUpperInvariant();
		if (normalized.Length != 3 || !normalized.All(character => character is >= 'A' and <= 'Z'))
			throw new ArgumentException("Currency code must contain exactly three ASCII letters (ISO 4217 syntax).", nameof(value));
		Value = normalized;
	}

	public string Value { get; }
	public override string ToString() => Value;
}

public sealed record FinanceCurrency
{
	public FinanceCurrency(CurrencyCode code, string name, int minorUnits, bool isActive = true)
	{
		Code = code ?? throw new ArgumentNullException(nameof(code));
		Name = FinanceValidation.Required(name, nameof(name), 200);
		if (minorUnits is < 0 or > 9) throw new ArgumentOutOfRangeException(nameof(minorUnits), "Minor units must be between 0 and 9.");
		MinorUnits = minorUnits;
		IsActive = isActive;
	}

	public CurrencyCode Code { get; }
	public string Name { get; }
	public int MinorUnits { get; }
	public bool IsActive { get; }
}

public sealed record LegalEntity
{
	public LegalEntity(Guid id, string code, string name, string countryCode, CurrencyCode functionalCurrency, bool isActive = true)
	{
		Id = FinanceValidation.Id(id, nameof(id));
		Code = FinanceValidation.Required(code, nameof(code), 50).ToUpperInvariant();
		Name = FinanceValidation.Required(name, nameof(name), 250);
		CountryCode = FinanceValidation.CountryCode(countryCode, nameof(countryCode));
		FunctionalCurrency = functionalCurrency ?? throw new ArgumentNullException(nameof(functionalCurrency));
		IsActive = isActive;
	}

	public Guid Id { get; }
	public string Code { get; }
	public string Name { get; }
	public string CountryCode { get; }
	public CurrencyCode FunctionalCurrency { get; }
	public bool IsActive { get; }
}

public sealed record TaxRegistration
{
	public TaxRegistration(Guid id, Guid legalEntityId, string countryCode, string schemeCode, string registrationNumber, DateOnly? validFrom = null, DateOnly? validTo = null)
	{
		Id = FinanceValidation.Id(id, nameof(id));
		LegalEntityId = FinanceValidation.Id(legalEntityId, nameof(legalEntityId));
		CountryCode = FinanceValidation.CountryCode(countryCode, nameof(countryCode));
		SchemeCode = FinanceValidation.Required(schemeCode, nameof(schemeCode), 50).ToUpperInvariant();
		RegistrationNumber = FinanceValidation.Required(registrationNumber, nameof(registrationNumber), 100);
		FinanceValidation.DateRange(validFrom, validTo, nameof(validFrom), nameof(validTo));
		ValidFrom = validFrom;
		ValidTo = validTo;
	}

	public Guid Id { get; }
	public Guid LegalEntityId { get; }
	public string CountryCode { get; }
	public string SchemeCode { get; }
	public string RegistrationNumber { get; }
	public DateOnly? ValidFrom { get; }
	public DateOnly? ValidTo { get; }
}

public sealed record ExchangeRate
{
	public ExchangeRate(Guid id, CurrencyCode baseCurrency, CurrencyCode quoteCurrency, decimal rate, DateTimeOffset effectiveAtUtc, string sourceCode)
	{
		Id = FinanceValidation.Id(id, nameof(id));
		BaseCurrency = baseCurrency ?? throw new ArgumentNullException(nameof(baseCurrency));
		QuoteCurrency = quoteCurrency ?? throw new ArgumentNullException(nameof(quoteCurrency));
		if (rate <= 0m) throw new ArgumentOutOfRangeException(nameof(rate), "Exchange rate must be positive.");
		if (BaseCurrency == QuoteCurrency && rate != 1m) throw new ArgumentException("An exchange rate for the same currency must equal 1.", nameof(rate));
		Rate = rate;
		EffectiveAtUtc = effectiveAtUtc.ToUniversalTime();
		SourceCode = FinanceValidation.Required(sourceCode, nameof(sourceCode), 100);
	}

	public Guid Id { get; }
	public CurrencyCode BaseCurrency { get; }
	public CurrencyCode QuoteCurrency { get; }
	public decimal Rate { get; }
	public DateTimeOffset EffectiveAtUtc { get; }
	public string SourceCode { get; }
}

public sealed record FiscalCalendar
{
	public FiscalCalendar(Guid id, Guid legalEntityId, string code, string name, bool isActive = true)
	{
		Id = FinanceValidation.Id(id, nameof(id));
		LegalEntityId = FinanceValidation.Id(legalEntityId, nameof(legalEntityId));
		Code = FinanceValidation.Required(code, nameof(code), 50).ToUpperInvariant();
		Name = FinanceValidation.Required(name, nameof(name), 200);
		IsActive = isActive;
	}

	public Guid Id { get; }
	public Guid LegalEntityId { get; }
	public string Code { get; }
	public string Name { get; }
	public bool IsActive { get; }
}

public enum AccountingPeriodStatus
{
	Open,
	Closed
}

public sealed record AccountingPeriod
{
	public AccountingPeriod(Guid id, Guid fiscalCalendarId, string code, DateOnly startDate, DateOnly endDate, AccountingPeriodStatus status = AccountingPeriodStatus.Open)
	{
		Id = FinanceValidation.Id(id, nameof(id));
		FiscalCalendarId = FinanceValidation.Id(fiscalCalendarId, nameof(fiscalCalendarId));
		Code = FinanceValidation.Required(code, nameof(code), 50).ToUpperInvariant();
		if (endDate < startDate) throw new ArgumentException("Accounting period end date must be on or after the start date.", nameof(endDate));
		StartDate = startDate;
		EndDate = endDate;
		Status = status;
	}

	public Guid Id { get; }
	public Guid FiscalCalendarId { get; }
	public string Code { get; }
	public DateOnly StartDate { get; }
	public DateOnly EndDate { get; }
	public AccountingPeriodStatus Status { get; }
}

public sealed record ChartOfAccounts
{
	public ChartOfAccounts(Guid id, string code, string name, bool isActive = true)
	{
		Id = FinanceValidation.Id(id, nameof(id));
		Code = FinanceValidation.Required(code, nameof(code), 50).ToUpperInvariant();
		Name = FinanceValidation.Required(name, nameof(name), 200);
		IsActive = isActive;
	}

	public Guid Id { get; }
	public string Code { get; }
	public string Name { get; }
	public bool IsActive { get; }
}

public enum FinanceAccountType
{
	Asset,
	Liability,
	Equity,
	Revenue,
	Expense,
	Statistical
}

public sealed record FinanceAccount
{
	public FinanceAccount(Guid id, Guid chartOfAccountsId, string number, string name, FinanceAccountType accountType, bool allowDirectPosting = true, bool isActive = true)
	{
		Id = FinanceValidation.Id(id, nameof(id));
		ChartOfAccountsId = FinanceValidation.Id(chartOfAccountsId, nameof(chartOfAccountsId));
		Number = FinanceValidation.Required(number, nameof(number), 50);
		Name = FinanceValidation.Required(name, nameof(name), 200);
		AccountType = accountType;
		AllowDirectPosting = allowDirectPosting;
		IsActive = isActive;
	}

	public Guid Id { get; }
	public Guid ChartOfAccountsId { get; }
	public string Number { get; }
	public string Name { get; }
	public FinanceAccountType AccountType { get; }
	public bool AllowDirectPosting { get; }
	public bool IsActive { get; }
}

public sealed record AccountingBook
{
	public AccountingBook(Guid id, Guid legalEntityId, Guid chartOfAccountsId, string code, string name, CurrencyCode reportingCurrency, string accountingStandardCode, bool isPrimary = false, bool isActive = true)
	{
		Id = FinanceValidation.Id(id, nameof(id));
		LegalEntityId = FinanceValidation.Id(legalEntityId, nameof(legalEntityId));
		ChartOfAccountsId = FinanceValidation.Id(chartOfAccountsId, nameof(chartOfAccountsId));
		Code = FinanceValidation.Required(code, nameof(code), 50).ToUpperInvariant();
		Name = FinanceValidation.Required(name, nameof(name), 200);
		ReportingCurrency = reportingCurrency ?? throw new ArgumentNullException(nameof(reportingCurrency));
		AccountingStandardCode = FinanceValidation.Required(accountingStandardCode, nameof(accountingStandardCode), 100);
		IsPrimary = isPrimary;
		IsActive = isActive;
	}

	public Guid Id { get; }
	public Guid LegalEntityId { get; }
	public Guid ChartOfAccountsId { get; }
	public string Code { get; }
	public string Name { get; }
	public CurrencyCode ReportingCurrency { get; }
	public string AccountingStandardCode { get; }
	public bool IsPrimary { get; }
	public bool IsActive { get; }
}

public sealed record JournalDefinition
{
	public JournalDefinition(Guid id, Guid accountingBookId, string code, string name, bool isActive = true)
	{
		Id = FinanceValidation.Id(id, nameof(id));
		AccountingBookId = FinanceValidation.Id(accountingBookId, nameof(accountingBookId));
		Code = FinanceValidation.Required(code, nameof(code), 50).ToUpperInvariant();
		Name = FinanceValidation.Required(name, nameof(name), 200);
		IsActive = isActive;
	}

	public Guid Id { get; }
	public Guid AccountingBookId { get; }
	public string Code { get; }
	public string Name { get; }
	public bool IsActive { get; }
}

public sealed record AccountingDimension
{
	public AccountingDimension(Guid id, string code, string name, bool isRequired = false, bool isActive = true)
	{
		Id = FinanceValidation.Id(id, nameof(id));
		Code = FinanceValidation.Required(code, nameof(code), 50).ToUpperInvariant();
		Name = FinanceValidation.Required(name, nameof(name), 200);
		IsRequired = isRequired;
		IsActive = isActive;
	}

	public Guid Id { get; }
	public string Code { get; }
	public string Name { get; }
	public bool IsRequired { get; }
	public bool IsActive { get; }
}

public sealed record AccountingDimensionValue
{
	public AccountingDimensionValue(Guid id, Guid dimensionId, string code, string name, bool isActive = true)
	{
		Id = FinanceValidation.Id(id, nameof(id));
		DimensionId = FinanceValidation.Id(dimensionId, nameof(dimensionId));
		Code = FinanceValidation.Required(code, nameof(code), 100);
		Name = FinanceValidation.Required(name, nameof(name), 200);
		IsActive = isActive;
	}

	public Guid Id { get; }
	public Guid DimensionId { get; }
	public string Code { get; }
	public string Name { get; }
	public bool IsActive { get; }
}

public sealed record FinanceNumberSequence
{
	public FinanceNumberSequence(Guid id, Guid legalEntityId, string code, string documentType, string prefix, int numericLength, long nextNumber, bool isActive = true)
	{
		Id = FinanceValidation.Id(id, nameof(id));
		LegalEntityId = FinanceValidation.Id(legalEntityId, nameof(legalEntityId));
		Code = FinanceValidation.Required(code, nameof(code), 50).ToUpperInvariant();
		DocumentType = FinanceValidation.Required(documentType, nameof(documentType), 100);
		Prefix = prefix?.Trim() ?? string.Empty;
		if (Prefix.Length > 50) throw new ArgumentException("Number-sequence prefix cannot exceed 50 characters.", nameof(prefix));
		if (numericLength is < 1 or > 18) throw new ArgumentOutOfRangeException(nameof(numericLength), "Numeric length must be between 1 and 18.");
		if (nextNumber < 1) throw new ArgumentOutOfRangeException(nameof(nextNumber), "Next number must be positive.");
		NumericLength = numericLength;
		NextNumber = nextNumber;
		IsActive = isActive;
	}

	public Guid Id { get; }
	public Guid LegalEntityId { get; }
	public string Code { get; }
	public string DocumentType { get; }
	public string Prefix { get; }
	public int NumericLength { get; }
	public long NextNumber { get; }
	public bool IsActive { get; }
}

internal static class FinanceValidation
{
	public static Guid Id(Guid value, string parameterName)
	{
		if (value == Guid.Empty) throw new ArgumentException("Identifier cannot be empty.", parameterName);
		return value;
	}

	public static string Required(string value, string parameterName, int maximumLength)
	{
		if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Value is required.", parameterName);
		var normalized = value.Trim();
		if (normalized.Length > maximumLength) throw new ArgumentException($"Value cannot exceed {maximumLength} characters.", parameterName);
		return normalized;
	}

	public static string CountryCode(string value, string parameterName)
	{
		var normalized = Required(value, parameterName, 2).ToUpperInvariant();
		if (normalized.Length != 2 || !normalized.All(character => character is >= 'A' and <= 'Z'))
			throw new ArgumentException("Country code must contain exactly two ASCII letters (ISO 3166-1 alpha-2 syntax).", parameterName);
		return normalized;
	}

	public static void DateRange(DateOnly? start, DateOnly? end, string startParameter, string endParameter)
	{
		if (start.HasValue && end.HasValue && end.Value < start.Value)
			throw new ArgumentException($"{endParameter} must be on or after {startParameter}.", endParameter);
	}
}
