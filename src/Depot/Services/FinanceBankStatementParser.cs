// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Depot.Models;

namespace Depot.Services;

public static class FinanceBankStatementParser
{
	public static FinanceParsedBankStatement Parse(FinanceBankStatementImportRequest request, CurrencyCode bankCurrency)
	{
		ArgumentNullException.ThrowIfNull(request);
		if (string.IsNullOrWhiteSpace(request.Content)) throw new ArgumentException("Bank statement content is required.", nameof(request));
		return request.Format switch
		{
			FinanceBankStatementFormat.Csv => ParseCsv(request, bankCurrency),
			FinanceBankStatementFormat.Iso20022Camt053 => ParseCamt053(request, bankCurrency),
			_ => throw new NotSupportedException($"Bank statement format '{request.Format}' is not supported.")
		};
	}

	private static FinanceParsedBankStatement ParseCsv(FinanceBankStatementImportRequest request, CurrencyCode bankCurrency)
	{
		var rows = ReadCsvRows(request.Content);
		if (rows.Count < 2) throw new InvalidDataException("CSV bank statement requires a header and at least one transaction row.");
		var header = rows[0].Select(NormalizeHeader).ToArray();
		var bookingIndex = RequiredColumn(header, "bookingdate", "booking date", "date");
		var amountIndex = RequiredColumn(header, "amount");
		var currencyIndex = OptionalColumn(header, "currency", "currencycode");
		var valueDateIndex = OptionalColumn(header, "valuedate", "value date");
		var externalIndex = OptionalColumn(header, "externalid", "external id", "transactionid", "transaction id");
		var referenceIndex = OptionalColumn(header, "reference", "remittance", "description");
		var counterpartyIndex = OptionalColumn(header, "counterparty", "counterpartyname", "counterparty name");
		var codeIndex = OptionalColumn(header, "banktransactioncode", "transactioncode", "code");
		var lines = new List<FinanceParsedBankStatementLine>();
		for (var rowIndex = 1; rowIndex < rows.Count; rowIndex++)
		{
			var row = rows[rowIndex];
			if (row.All(string.IsNullOrWhiteSpace)) continue;
			var booking = ParseDate(Get(row, bookingIndex), $"CSV row {rowIndex + 1} booking date");
			var amount = ParseDecimal(Get(row, amountIndex), $"CSV row {rowIndex + 1} amount");
			var currency = currencyIndex >= 0 && !string.IsNullOrWhiteSpace(Get(row, currencyIndex)) ? new CurrencyCode(Get(row, currencyIndex)) : bankCurrency;
			if (currency != bankCurrency) throw new InvalidDataException($"CSV row {rowIndex + 1} currency '{currency}' does not match bank-account currency '{bankCurrency}'.");
			var valueDateText = Get(row, valueDateIndex);
			lines.Add(new FinanceParsedBankStatementLine(
				booking,
				string.IsNullOrWhiteSpace(valueDateText) ? null : ParseDate(valueDateText, $"CSV row {rowIndex + 1} value date"),
				amount,
				currency,
				Clean(Get(row, externalIndex), 200),
				Clean(Get(row, referenceIndex), 500),
				Clean(Get(row, counterpartyIndex), 300),
				Clean(Get(row, codeIndex), 100)));
		}
		if (lines.Count == 0) throw new InvalidDataException("CSV bank statement contains no transaction rows.");
		var fromDate = request.FromDate ?? lines.Min(line => line.BookingDate);
		var toDate = request.ToDate ?? lines.Max(line => line.BookingDate);
		if (toDate < fromDate) throw new InvalidDataException("Statement end date precedes start date.");
		var opening = request.OpeningBalance ?? 0m;
		var closing = request.ClosingBalance ?? checked(opening + lines.Sum(line => line.Amount));
		var reference = Clean(request.StatementReference, 200) ?? $"CSV-{fromDate:yyyyMMdd}-{toDate:yyyyMMdd}";
		return new FinanceParsedBankStatement(reference, bankCurrency, fromDate, toDate, opening, closing, lines);
	}

	private static FinanceParsedBankStatement ParseCamt053(FinanceBankStatementImportRequest request, CurrencyCode bankCurrency)
	{
		XDocument document;
		try { document = XDocument.Parse(request.Content, LoadOptions.PreserveWhitespace); }
		catch (Exception exception) when (exception is System.Xml.XmlException or InvalidOperationException) { throw new InvalidDataException("ISO 20022 statement XML is invalid.", exception); }
		var statement = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "Stmt") ?? throw new InvalidDataException("ISO 20022 camt.053 statement does not contain a Stmt element.");
		var id = Value(statement, "Id") ?? request.StatementReference ?? throw new InvalidDataException("ISO 20022 statement ID is missing.");
		var fromDate = ParseIsoDate(Value(statement.Descendants().FirstOrDefault(element => element.Name.LocalName == "FrToDt"), "FrDtTm") ?? Value(statement.Descendants().FirstOrDefault(element => element.Name.LocalName == "FrToDt"), "FrDt"));
		var toDate = ParseIsoDate(Value(statement.Descendants().FirstOrDefault(element => element.Name.LocalName == "FrToDt"), "ToDtTm") ?? Value(statement.Descendants().FirstOrDefault(element => element.Name.LocalName == "FrToDt"), "ToDt"));
		var balances = statement.Elements().Where(element => element.Name.LocalName == "Bal").ToArray();
		var opening = ReadBalance(balances, "OPBD") ?? request.OpeningBalance;
		var closing = ReadBalance(balances, "CLBD") ?? request.ClosingBalance;
		var lines = new List<FinanceParsedBankStatementLine>();
		foreach (var entry in statement.Elements().Where(element => element.Name.LocalName == "Ntry"))
		{
			var amountElement = entry.Elements().FirstOrDefault(element => element.Name.LocalName == "Amt") ?? throw new InvalidDataException("ISO 20022 entry is missing Amt.");
			var currency = new CurrencyCode((string?)amountElement.Attribute("Ccy") ?? bankCurrency.Value);
			if (currency != bankCurrency) throw new InvalidDataException($"ISO 20022 entry currency '{currency}' does not match bank-account currency '{bankCurrency}'.");
			var amount = ParseDecimal(amountElement.Value, "ISO 20022 entry amount");
			var direction = Value(entry, "CdtDbtInd") ?? throw new InvalidDataException("ISO 20022 entry is missing CdtDbtInd.");
			if (string.Equals(direction, "DBIT", StringComparison.OrdinalIgnoreCase)) amount = -amount;
			else if (!string.Equals(direction, "CRDT", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"Unsupported ISO 20022 credit/debit indicator '{direction}'.");
			var booking = ParseIsoDate(DateValue(entry, "BookgDt")) ?? throw new InvalidDataException("ISO 20022 entry is missing booking date.");
			var valueDate = ParseIsoDate(DateValue(entry, "ValDt"));
			var details = entry.Descendants().FirstOrDefault(element => element.Name.LocalName == "TxDtls");
			var externalId = Value(details, "AcctSvcrRef") ?? Value(entry, "AcctSvcrRef") ?? Value(details, "EndToEndId") ?? Value(entry, "NtryRef");
			var reference = Value(details, "Ustrd") ?? Value(entry, "AddtlNtryInf") ?? Value(details, "RmtInf");
			var counterparty = details?.Descendants().FirstOrDefault(element => element.Name.LocalName is "Dbtr" or "Cdtr")?.Descendants().FirstOrDefault(element => element.Name.LocalName == "Nm")?.Value;
			var bankCode = entry.Descendants().FirstOrDefault(element => element.Name.LocalName == "BkTxCd")?.Value;
			lines.Add(new FinanceParsedBankStatementLine(booking.Value, valueDate, amount, currency, Clean(externalId, 200), Clean(reference, 500), Clean(counterparty, 300), Clean(bankCode, 100)));
		}
		if (lines.Count == 0) throw new InvalidDataException("ISO 20022 statement contains no Ntry entries.");
		fromDate ??= request.FromDate ?? lines.Min(line => line.BookingDate);
		toDate ??= request.ToDate ?? lines.Max(line => line.BookingDate);
		opening ??= 0m;
		closing ??= checked(opening.Value + lines.Sum(line => line.Amount));
		if (toDate < fromDate) throw new InvalidDataException("Statement end date precedes start date.");
		return new FinanceParsedBankStatement(Clean(id, 200)!, bankCurrency, fromDate.Value, toDate.Value, opening.Value, closing.Value, lines);
	}

	private static decimal? ReadBalance(IEnumerable<XElement> balances, string code)
	{
		foreach (var balance in balances)
		{
			var balanceCode = balance.Descendants().FirstOrDefault(element => element.Name.LocalName == "Cd")?.Value;
			if (!string.Equals(balanceCode, code, StringComparison.OrdinalIgnoreCase)) continue;
			var amount = balance.Elements().FirstOrDefault(element => element.Name.LocalName == "Amt");
			if (amount is null) continue;
			var value = ParseDecimal(amount.Value, $"ISO 20022 {code} balance");
			var direction = Value(balance, "CdtDbtInd");
			return string.Equals(direction, "DBIT", StringComparison.OrdinalIgnoreCase) ? -value : value;
		}
		return null;
	}

	private static IReadOnlyList<string[]> ReadCsvRows(string content)
	{
		var firstLine = content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
		var delimiter = firstLine.Count(character => character == ';') > firstLine.Count(character => character == ',') ? ';' : ',';
		var result = new List<string[]>();
		var row = new List<string>();
		var field = new StringBuilder();
		var quoted = false;
		for (var index = 0; index <= content.Length; index++)
		{
			var character = index < content.Length ? content[index] : '\n';
			if (character == '"')
			{
				if (quoted && index + 1 < content.Length && content[index + 1] == '"') { field.Append('"'); index++; }
				else quoted = !quoted;
				continue;
			}
			if (!quoted && character == delimiter) { row.Add(field.ToString().Trim()); field.Clear(); continue; }
			if (!quoted && (character == '\r' || character == '\n'))
			{
				if (character == '\r' && index + 1 < content.Length && content[index + 1] == '\n') index++;
				row.Add(field.ToString().Trim()); field.Clear();
				if (row.Any(value => !string.IsNullOrWhiteSpace(value))) result.Add(row.ToArray());
				row.Clear();
				continue;
			}
			field.Append(character);
		}
		if (quoted) throw new InvalidDataException("CSV contains an unterminated quoted field.");
		return result;
	}

	private static int RequiredColumn(IReadOnlyList<string> header, params string[] aliases) => OptionalColumn(header, aliases) is var index && index >= 0 ? index : throw new InvalidDataException($"CSV header is missing required column '{aliases[0]}'.");
	private static int OptionalColumn(IReadOnlyList<string> header, params string[] aliases)
	{
		for (var index = 0; index < header.Count; index++) if (aliases.Contains(header[index], StringComparer.OrdinalIgnoreCase)) return index;
		return -1;
	}
	private static string NormalizeHeader(string value) => value.Trim().Trim('\ufeff').Replace("_", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
	private static string Get(IReadOnlyList<string> row, int index) => index >= 0 && index < row.Count ? row[index] : string.Empty;
	private static string? Clean(string? value, int maxLength) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length <= maxLength ? value.Trim() : value.Trim()[..maxLength];
	private static DateOnly ParseDate(string value, string field)
	{
		if (DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var invariant)) return invariant;
		if (DateOnly.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out var current)) return current;
		throw new InvalidDataException($"{field} '{value}' is not a valid date.");
	}
	private static decimal ParseDecimal(string value, string field)
	{
		if (decimal.TryParse(value, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var invariant)) return invariant;
		if (decimal.TryParse(value, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.CurrentCulture, out var current)) return current;
		throw new InvalidDataException($"{field} '{value}' is not a valid decimal amount.");
	}
	private static DateOnly? ParseIsoDate(string? value)
	{
		if (string.IsNullOrWhiteSpace(value)) return null;
		if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var instant)) return DateOnly.FromDateTime(instant.Date);
		if (DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var date)) return date;
		throw new InvalidDataException($"ISO 20022 date '{value}' is invalid.");
	}
	private static string? DateValue(XElement parent, string name)
	{
		var container = parent.Elements().FirstOrDefault(element => element.Name.LocalName == name);
		return container?.Descendants().FirstOrDefault(element => element.Name.LocalName is "Dt" or "DtTm")?.Value;
	}
	private static string? Value(XElement? parent, string name) => parent?.Descendants().FirstOrDefault(element => element.Name.LocalName == name)?.Value;
}
