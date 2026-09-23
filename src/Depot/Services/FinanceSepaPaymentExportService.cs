// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Depot.Data;
using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class FinanceSepaPaymentExportService
{
	public const string MessageVersion = "pain.001.001.09";
	public const string SchemeProfile = "EPC SCT 2025 v1.1 / C2PSP IG 2025 v1.0";
	public const string XmlNamespace = "urn:iso:std:iso:20022:tech:xsd:pain.001.001.09";

	private const int MaximumTransactionsPerExport = 500;
	private static readonly Regex BicPattern = new("^[A-Z]{6}[A-Z0-9]{2}([A-Z0-9]{3})?$", RegexOptions.CultureInvariant);
	private static readonly Regex CountryPattern = new("^[A-Z]{2}$", RegexOptions.CultureInvariant);
	private static readonly IReadOnlySet<string> SepaCountryCodes = new HashSet<string>(StringComparer.Ordinal)
	{
		"AT", "BE", "BG", "HR", "CY", "CZ", "DK", "EE", "FI", "FR", "DE", "GR", "HU", "IE", "IT", "LV", "LT", "LU", "MT", "NL", "PL", "PT", "RO", "SK", "SI", "ES", "SE", "IS", "LI", "NO", "AD", "AL", "CH", "GB", "MD", "MC", "ME", "MK", "RS", "SM", "VA"
	};

	private readonly IDatabaseTransactionRunner _transactions;
	private readonly FinanceBankingRepository _bankingRepository;
	private readonly FinanceSepaPaymentExportRepository _exports;
	private readonly FinanceBankingService _banking;
	private readonly AuditRepository _auditEntries;
	private readonly AuditService _audit;
	private readonly IAuthorizationService _authorization;

	public FinanceSepaPaymentExportService(
		IDatabaseTransactionRunner transactions,
		FinanceBankingRepository bankingRepository,
		FinanceSepaPaymentExportRepository exports,
		FinanceBankingService banking,
		AuditRepository auditEntries,
		AuditService audit,
		IAuthorizationService authorization)
	{
		_transactions = transactions;
		_bankingRepository = bankingRepository;
		_exports = exports;
		_banking = banking;
		_auditEntries = auditEntries;
		_audit = audit;
		_authorization = authorization;
	}

	public bool CanManageProfiles => _authorization.HasPermission(ApplicationPermission.FinanceSepaPaymentProfilesManage);
	public bool CanGenerate => _authorization.HasPermission(ApplicationPermission.FinanceSepaPaymentExportsCreate);
	public bool CanDownload => _authorization.HasPermission(ApplicationPermission.FinanceSepaPaymentExportsExport);
	public bool CanManageStatus => _authorization.HasPermission(ApplicationPermission.FinanceSepaPaymentExportsManage);

	public Task<PageResult<FinanceSepaPaymentExportSummary>> SearchExportsAsync(long? paymentRunId, int pageNumber = 1, int pageSize = 100, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceBankingView);
		if (paymentRunId is <= 0) throw new ArgumentOutOfRangeException(nameof(paymentRunId));
		return _exports.SearchExportsAsync(paymentRunId, pageNumber, pageSize, cancellationToken);
	}

	public Task<FinanceSepaDebtorProfile?> GetDebtorProfileAsync(long bankAccountId, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceBankingView);
		if (bankAccountId <= 0) throw new ArgumentOutOfRangeException(nameof(bankAccountId));
		return _exports.GetDebtorProfileAsync(bankAccountId, cancellationToken);
	}

	public Task<FinanceSepaCreditorProfile?> GetCreditorProfileAsync(long supplierId, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceBankingView);
		if (supplierId <= 0) throw new ArgumentOutOfRangeException(nameof(supplierId));
		return _exports.GetCreditorProfileAsync(supplierId, cancellationToken);
	}

	public async Task<FinanceSepaDebtorProfile> SaveDebtorProfileAsync(FinanceSepaDebtorProfile profile, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(profile);
		_authorization.RequirePermission(ApplicationPermission.FinanceSepaPaymentProfilesManage);
		RequireUser();
		var normalized = Normalize(profile);
		ValidateParty(normalized.Name, normalized.StreetName, normalized.BuildingNumber, normalized.PostalCode, normalized.TownName, normalized.CountrySubdivision, normalized.CountryCode);
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var bank = await _bankingRepository.GetBankAccountAsync(transaction, normalized.BankAccountId, token) ?? throw new InvalidOperationException("Bank account was not found.");
			var before = await _exports.GetDebtorProfileAsync(transaction, normalized.BankAccountId, token);
			if (before is null)
			{
				if (await _exports.InsertDebtorProfileAsync(transaction, normalized, token) != 1) throw new InvalidOperationException("SEPA debtor profile could not be created.");
				var created = normalized with { Version = 1 };
				await _auditEntries.CreateAsync(transaction, _audit.CreateCreatedEntry(bank.Id, created), token);
				return created;
			}
			if (before.Version != normalized.Version) throw new ConcurrencyConflictException("SEPA debtor profile");
			if (await _exports.UpdateDebtorProfileAsync(transaction, normalized, before.Version, token) != 1) throw new ConcurrencyConflictException("SEPA debtor profile");
			var after = normalized with { Version = before.Version + 1 };
			await _auditEntries.CreateAsync(transaction, _audit.CreateUpdatedEntry(bank.Id, before, after), token);
			return after;
		}, cancellationToken);
	}

	public async Task<FinanceSepaCreditorProfile> SaveCreditorProfileAsync(FinanceSepaCreditorProfile profile, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(profile);
		_authorization.RequirePermission(ApplicationPermission.FinanceSepaPaymentProfilesManage);
		RequireUser();
		var normalized = Normalize(profile);
		ValidateParty(normalized.Name, normalized.StreetName, normalized.BuildingNumber, normalized.PostalCode, normalized.TownName, normalized.CountrySubdivision, normalized.CountryCode);
		ValidateSepaIban(normalized.Iban, "Creditor IBAN");
		ValidateBic(normalized.Bic, "Creditor BIC");
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			if (!await _exports.SupplierExistsAsync(transaction, normalized.SupplierId, token)) throw new InvalidOperationException("Active supplier was not found.");
			var before = await _exports.GetCreditorProfilesAsync(transaction, [normalized.SupplierId], token);
			if (!before.TryGetValue(normalized.SupplierId, out var current))
			{
				if (await _exports.InsertCreditorProfileAsync(transaction, normalized, token) != 1) throw new InvalidOperationException("SEPA creditor profile could not be created.");
				var created = normalized with { Version = 1 };
				await _auditEntries.CreateAsync(transaction, _audit.CreateCreatedEntry(normalized.SupplierId, created), token);
				return created;
			}
			if (current.Version != normalized.Version) throw new ConcurrencyConflictException("SEPA creditor profile");
			if (await _exports.UpdateCreditorProfileAsync(transaction, normalized, current.Version, token) != 1) throw new ConcurrencyConflictException("SEPA creditor profile");
			var after = normalized with { Version = current.Version + 1 };
			await _auditEntries.CreateAsync(transaction, _audit.CreateUpdatedEntry(normalized.SupplierId, current, after), token);
			return after;
		}, cancellationToken);
	}

	public async Task<FinanceSepaPaymentExportPreview> PreviewAsync(long paymentRunId, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceSepaPaymentExportsCreate);
		var run = await _banking.GetPaymentRunAsync(paymentRunId, cancellationToken) ?? throw new InvalidOperationException("Payment run was not found.");
		var bank = await _bankingRepository.GetBankAccountAsync(run.BankAccountId, cancellationToken);
		var debtor = bank is null ? null : await _exports.GetDebtorProfileAsync(bank.Id, cancellationToken);
		var creditors = new Dictionary<long, FinanceSepaCreditorProfile>();
		foreach (var supplierId in run.Lines.Select(value => value.SupplierId).Distinct().Take(MaximumTransactionsPerExport))
		{
			var profile = await _exports.GetCreditorProfileAsync(supplierId, cancellationToken);
			if (profile is not null) creditors[supplierId] = profile;
		}
		return Validate(run, bank, debtor, creditors);
	}

	public async Task<FinanceSepaPaymentExport> GenerateAsync(long paymentRunId, bool supersedeExisting = false, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceSepaPaymentExportsCreate);
		var user = RequireUser();
		_ = await _banking.GetPaymentRunAsync(paymentRunId, cancellationToken) ?? throw new InvalidOperationException("Payment run was not found.");

		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var run = await _bankingRepository.LockPaymentRunAsync(transaction, paymentRunId, token) ?? throw new InvalidOperationException("Payment run was not found.");
			var latest = await _exports.GetLatestExportForRunAsync(transaction, paymentRunId, token);
			if (latest is not null && !supersedeExisting) return latest;

			if (latest is not null && latest.CurrentStatus is FinanceSepaPaymentExportStatus.SubmittedExternally or FinanceSepaPaymentExportStatus.Accepted)
				throw new InvalidOperationException("A submitted or accepted payment export cannot be superseded. Record the external outcome first.");

			var bank = await _bankingRepository.GetBankAccountAsync(transaction, run.BankAccountId, token);
			var debtor = bank is null ? null : await _exports.GetDebtorProfileAsync(transaction, bank.Id, token);
			var creditors = await _exports.GetCreditorProfilesAsync(transaction, run.Lines.Select(value => value.SupplierId).Distinct().ToArray(), token);
			var preview = Validate(run, bank, debtor, creditors);
			if (!preview.IsValid) throw new InvalidOperationException("SEPA payment export validation failed: " + string.Join(" ", preview.Errors));

			var sequence = await _exports.GetNextSequenceAsync(transaction, run.Id, token);
			var generatedAt = DateTime.UtcNow;
			var messageId = DeterministicIdentifier("M", run.OperationId, sequence, 0);
			var paymentInformationId = DeterministicIdentifier("P", run.OperationId, sequence, 0);
			var xml = BuildXml(run, bank!, debtor!, creditors, sequence, messageId, paymentInformationId);
			ValidateGeneratedXml(xml, run, preview.ControlSum, preview.TransactionCount);
			var xmlHash = Convert.ToHexString(SHA256.HashData(xml)).ToLowerInvariant();
			var exportKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"Depot|Finance|SEPA-SCT|{run.OperationId:D}|{sequence}"))).ToLowerInvariant();
			var fileName = $"DEP-SCT-{run.PaymentDate:yyyyMMdd}-{messageId}.xml";

			if (latest is not null)
			{
				if (await _exports.UpdateStatusAsync(transaction, latest.Id, latest.CurrentStatus, FinanceSepaPaymentExportStatus.Superseded, token) != 1)
					throw new ConcurrencyConflictException("SEPA payment export");
				await _exports.AddStatusHistoryAsync(transaction, new FinanceSepaPaymentExportStatusHistory
				{
					ExportId = latest.Id,
					Status = FinanceSepaPaymentExportStatus.Superseded,
					RecordedAtUtc = generatedAt,
					RecordedByUserId = user.Id,
					EvidenceNote = $"Superseded by export sequence {sequence}."
				}, token);
				await _auditEntries.CreateAsync(transaction, _audit.CreateActionEntry(latest.Id, "Superseded", ToSummary(latest), ToSummary(latest with { CurrentStatus = FinanceSepaPaymentExportStatus.Superseded })), token);
			}

			var value = new FinanceSepaPaymentExport
			{
				PaymentRunId = run.Id,
				ExportSequence = sequence,
				ExportKey = exportKey,
				MessageId = messageId,
				PaymentInformationId = paymentInformationId,
				FileName = fileName,
				MessageVersion = MessageVersion,
				SchemeProfile = SchemeProfile,
				GeneratedAtUtc = generatedAt,
				GeneratedByUserId = user.Id,
				TransactionCount = preview.TransactionCount,
				ControlSum = preview.ControlSum,
				BankAccountId = bank!.Id,
				XmlSha256 = xmlHash,
				XmlPayload = xml,
				CurrentStatus = FinanceSepaPaymentExportStatus.Generated,
				SupersedesExportId = latest?.Id
			};
			var id = await _exports.CreateExportAsync(transaction, value, token);
			var history = new FinanceSepaPaymentExportStatusHistory
			{
				ExportId = id,
				Status = FinanceSepaPaymentExportStatus.Generated,
				RecordedAtUtc = generatedAt,
				RecordedByUserId = user.Id,
				EvidenceNote = "Deterministic SEPA SCT artifact generated and retained."
			};
			var historyId = await _exports.AddStatusHistoryAsync(transaction, history, token);
			var created = value with { Id = id, StatusHistory = [history with { Id = historyId }] };
			await _auditEntries.CreateAsync(transaction, _audit.CreateCreatedEntry(id, ToSummary(created)), token);
			return created;
		}, cancellationToken);
	}

	public async Task<FinanceSepaPaymentExport> DownloadAsync(long exportId, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceSepaPaymentExportsExport);
		_ = RequireUser();
		var current = await _exports.GetExportAsync(exportId, cancellationToken) ?? throw new InvalidOperationException("SEPA payment export was not found.");
		if (!string.Equals(Convert.ToHexString(SHA256.HashData(current.XmlPayload)).ToLowerInvariant(), current.XmlSha256, StringComparison.Ordinal))
			throw new InvalidDataException("Retained SEPA payment export hash does not match its immutable XML payload.");
		return current;
	}

	public async Task<FinanceSepaPaymentExport> RecordDownloadedAsync(long exportId, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceSepaPaymentExportsExport);
		var user = RequireUser();
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var before = await _exports.GetExportAsync(transaction, exportId, token) ?? throw new InvalidOperationException("SEPA payment export was not found.");
			if (before.CurrentStatus != FinanceSepaPaymentExportStatus.Generated) return before;
			if (await _exports.UpdateStatusAsync(transaction, exportId, FinanceSepaPaymentExportStatus.Generated, FinanceSepaPaymentExportStatus.Downloaded, token) != 1)
				throw new ConcurrencyConflictException("SEPA payment export");
			var now = DateTime.UtcNow;
			await _exports.AddStatusHistoryAsync(transaction, new FinanceSepaPaymentExportStatusHistory
			{
				ExportId=exportId,Status=FinanceSepaPaymentExportStatus.Downloaded,RecordedAtUtc=now,RecordedByUserId=user.Id,EvidenceNote="Exact retained XML artifact downloaded."
			}, token);
			var after = before with { CurrentStatus = FinanceSepaPaymentExportStatus.Downloaded };
			await _auditEntries.CreateAsync(transaction, _audit.CreateActionEntry(exportId, "Downloaded", ToSummary(before), ToSummary(after)), token);
			return after;
		}, cancellationToken);
	}

	public async Task<FinanceSepaPaymentExport> UpdateStatusAsync(long exportId, FinanceSepaPaymentExportStatusUpdate update, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(update);
		_authorization.RequirePermission(ApplicationPermission.FinanceSepaPaymentExportsManage);
		var user = RequireUser();
		if (update.Status is FinanceSepaPaymentExportStatus.Generated or FinanceSepaPaymentExportStatus.Downloaded or FinanceSepaPaymentExportStatus.Superseded)
			throw new ArgumentException("The requested status is controlled by generation, download or supersede workflows.", nameof(update));

		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var before = await _exports.GetExportAsync(transaction, exportId, token) ?? throw new InvalidOperationException("SEPA payment export was not found.");
			ValidateTransition(before.CurrentStatus, update.Status);
			var externalReference = Clean(update.ExternalReference, 200);
			var evidenceNote = Clean(update.EvidenceNote, 500);
			if (update.Status == FinanceSepaPaymentExportStatus.SubmittedExternally && string.IsNullOrWhiteSpace(externalReference))
				throw new ArgumentException("External submission reference is required when recording external submission.", nameof(update));
			if (await _exports.UpdateStatusAsync(transaction, exportId, before.CurrentStatus, update.Status, token) != 1) throw new ConcurrencyConflictException("SEPA payment export");
			var now = DateTime.UtcNow;
			await _exports.AddStatusHistoryAsync(transaction, new FinanceSepaPaymentExportStatusHistory
			{
				ExportId=exportId,Status=update.Status,RecordedAtUtc=now,RecordedByUserId=user.Id,ExternalReference=externalReference,EvidenceNote=evidenceNote
			}, token);
			var after = before with { CurrentStatus = update.Status };
			await _auditEntries.CreateAsync(transaction, _audit.CreateActionEntry(exportId, $"External status: {update.Status}", ToSummary(before), ToSummary(after)), token);
			return after;
		}, cancellationToken);
	}

	public Task<FinanceSepaPaymentExport?> GetExportAsync(long exportId, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceSepaPaymentExportsExport);
		if (exportId <= 0) throw new ArgumentOutOfRangeException(nameof(exportId));
		return _exports.GetExportAsync(exportId, cancellationToken);
	}

	private static FinanceSepaPaymentExportPreview Validate(
		FinancePaymentRun run,
		FinanceBankAccount? bank,
		FinanceSepaDebtorProfile? debtor,
		IReadOnlyDictionary<long, FinanceSepaCreditorProfile> creditors)
	{
		var errors = new List<string>();
		if (run.Status != FinancePaymentRunStatus.Approved) errors.Add("Payment run must be approved and not yet executed.");
		if (run.Lines.Count == 0) errors.Add("Payment run contains no payment instructions.");
		if (run.Lines.Count > MaximumTransactionsPerExport) errors.Add($"Payment run exceeds the supported maximum of {MaximumTransactionsPerExport} transactions per export.");
		if (run.Lines.Any(value => value.Status != FinancePaymentRunLineStatus.Proposed)) errors.Add("All payment-run lines must still be proposed when the file is generated.");
		if (run.Currency.Value != "EUR") errors.Add("Initial SEPA SCT export supports EUR only.");
		if (run.PaymentDate < DateOnly.FromDateTime(DateTime.UtcNow)) errors.Add("Requested execution date cannot be in the past.");
		if (bank is null) errors.Add("Configured bank account was not found.");
		else
		{
			if (!bank.IsActive) errors.Add("Configured bank account is inactive.");
			if (bank.Currency.Value != "EUR") errors.Add("Configured bank account must use EUR.");
			try { ValidateSepaIban(bank.Iban, "Debtor IBAN"); } catch (Exception exception) { errors.Add(exception.Message); }
			try { ValidateBic(bank.Bic, "Debtor BIC"); } catch (Exception exception) { errors.Add(exception.Message); }
		}
		if (debtor is null) errors.Add("A structured SEPA debtor profile is required for the selected bank account.");
		else
		{
			try { ValidateParty(debtor.Name,debtor.StreetName,debtor.BuildingNumber,debtor.PostalCode,debtor.TownName,debtor.CountrySubdivision,debtor.CountryCode); }
			catch (Exception exception) { errors.Add("Debtor profile: " + exception.Message); }
		}
		foreach (var line in run.Lines)
		{
			if (line.Amount <= 0m) errors.Add($"Payment line {line.Id} must have a positive amount.");
			if (decimal.Round(line.Amount, 2, MidpointRounding.ToEven) != line.Amount) errors.Add($"Payment line {line.Id} amount must not exceed two decimal places for EUR.");
			if (!creditors.TryGetValue(line.SupplierId, out var creditor))
			{
				errors.Add($"Supplier {line.SupplierId} has no structured SEPA creditor profile.");
				continue;
			}
			if (!creditor.IsActive) errors.Add($"Supplier {line.SupplierId} SEPA creditor profile is inactive.");
			try { ValidateSepaIban(creditor.Iban, $"Supplier {line.SupplierId} IBAN"); } catch (Exception exception) { errors.Add(exception.Message); }
			try { ValidateBic(creditor.Bic, $"Supplier {line.SupplierId} BIC"); } catch (Exception exception) { errors.Add(exception.Message); }
			try { ValidateParty(creditor.Name,creditor.StreetName,creditor.BuildingNumber,creditor.PostalCode,creditor.TownName,creditor.CountrySubdivision,creditor.CountryCode); }
			catch (Exception exception) { errors.Add($"Supplier {line.SupplierId} profile: {exception.Message}"); }
			if (!string.IsNullOrWhiteSpace(line.Reference) && line.Reference.Trim().Length > 140) errors.Add($"Payment line {line.Id} remittance reference exceeds 140 characters.");
		}
		return new FinanceSepaPaymentExportPreview
		{
			PaymentRunId = run.Id,
			TransactionCount = run.Lines.Count,
			ControlSum = run.Lines.Sum(value => value.Amount),
			Errors = errors.Distinct(StringComparer.Ordinal).ToArray()
		};
	}

	private static byte[] BuildXml(
		FinancePaymentRun run,
		FinanceBankAccount bank,
		FinanceSepaDebtorProfile debtor,
		IReadOnlyDictionary<long, FinanceSepaCreditorProfile> creditors,
		int sequence,
		string messageId,
		string paymentInformationId)
	{
		XNamespace ns = XmlNamespace;
		var controlSum = run.Lines.Sum(value => value.Amount);
		var createdAt = (run.ApprovedAtUtc ?? run.CreatedAtUtc).ToUniversalTime();
		var transactions = run.Lines.OrderBy(value => value.Id).Select(line =>
		{
			var creditor = creditors[line.SupplierId];
			var transaction = new XElement(ns + "CdtTrfTxInf",
				new XElement(ns + "PmtId", new XElement(ns + "EndToEndId", DeterministicIdentifier("E", run.OperationId, sequence, line.Id))),
				new XElement(ns + "Amt", new XElement(ns + "InstdAmt", new XAttribute("Ccy", "EUR"), Amount(line.Amount))));
			if (!string.IsNullOrWhiteSpace(creditor.Bic))
				transaction.Add(new XElement(ns + "CdtrAgt", new XElement(ns + "FinInstnId", new XElement(ns + "BICFI", creditor.Bic))));
			transaction.Add(
				new XElement(ns + "Cdtr",
					new XElement(ns + "Nm", creditor.Name),
					PostalAddress(ns, creditor.StreetName, creditor.BuildingNumber, creditor.PostalCode, creditor.TownName, creditor.CountrySubdivision, creditor.CountryCode)),
				new XElement(ns + "CdtrAcct", new XElement(ns + "Id", new XElement(ns + "IBAN", creditor.Iban))));
			if (!string.IsNullOrWhiteSpace(line.Reference))
				transaction.Add(new XElement(ns + "RmtInf", new XElement(ns + "Ustrd", line.Reference!.Trim())));
			return transaction;
		}).ToArray();

		var debtorAgent = new XElement(ns + "DbtrAgt", new XElement(ns + "FinInstnId"));
		var institution = debtorAgent.Element(ns + "FinInstnId")!;
		if (!string.IsNullOrWhiteSpace(bank.Bic)) institution.Add(new XElement(ns + "BICFI", bank.Bic));
		else institution.Add(new XElement(ns + "Othr", new XElement(ns + "Id", "NOTPROVIDED")));

		var document = new XDocument(
			new XDeclaration("1.0", "utf-8", null),
			new XElement(ns + "Document",
				new XElement(ns + "CstmrCdtTrfInitn",
					new XElement(ns + "GrpHdr",
						new XElement(ns + "MsgId", messageId),
						new XElement(ns + "CreDtTm", createdAt.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)),
						new XElement(ns + "NbOfTxs", run.Lines.Count.ToString(CultureInfo.InvariantCulture)),
						new XElement(ns + "CtrlSum", Amount(controlSum)),
						new XElement(ns + "InitgPty", new XElement(ns + "Nm", debtor.Name))),
					new XElement(ns + "PmtInf",
						new XElement(ns + "PmtInfId", paymentInformationId),
						new XElement(ns + "PmtMtd", "TRF"),
						new XElement(ns + "BtchBookg", "true"),
						new XElement(ns + "NbOfTxs", run.Lines.Count.ToString(CultureInfo.InvariantCulture)),
						new XElement(ns + "CtrlSum", Amount(controlSum)),
						new XElement(ns + "PmtTpInf", new XElement(ns + "SvcLvl", new XElement(ns + "Cd", "SEPA"))),
						new XElement(ns + "ReqdExctnDt", new XElement(ns + "Dt", run.PaymentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))),
						new XElement(ns + "Dbtr",
							new XElement(ns + "Nm", debtor.Name),
							PostalAddress(ns, debtor.StreetName, debtor.BuildingNumber, debtor.PostalCode, debtor.TownName, debtor.CountrySubdivision, debtor.CountryCode)),
						new XElement(ns + "DbtrAcct", new XElement(ns + "Id", new XElement(ns + "IBAN", bank.Iban))),
						debtorAgent,
						new XElement(ns + "ChrgBr", "SLEV"),
						transactions))));

		using var stream = new MemoryStream();
		using (var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false, OmitXmlDeclaration = false, NewLineHandling = NewLineHandling.None }))
			document.Save(writer);
		return stream.ToArray();
	}

	private static XElement PostalAddress(XNamespace ns, string street, string? building, string postalCode, string town, string? subdivision, string country)
	{
		var value = new XElement(ns + "PstlAdr", new XElement(ns + "StrtNm", street));
		if (!string.IsNullOrWhiteSpace(building)) value.Add(new XElement(ns + "BldgNb", building));
		value.Add(new XElement(ns + "PstCd", postalCode), new XElement(ns + "TwnNm", town));
		if (!string.IsNullOrWhiteSpace(subdivision)) value.Add(new XElement(ns + "CtrySubDvsn", subdivision));
		value.Add(new XElement(ns + "Ctry", country));
		return value;
	}

	private static void ValidateGeneratedXml(byte[] payload, FinancePaymentRun run, decimal expectedControlSum, int expectedCount)
	{
		using var stream = new MemoryStream(payload, writable: false);
		var document = XDocument.Load(stream, LoadOptions.None);
		XNamespace ns = XmlNamespace;
		if (document.Root?.Name != ns + "Document") throw new InvalidDataException("Generated payment XML has an unexpected root element or namespace.");
		var init = document.Root.Element(ns + "CstmrCdtTrfInitn") ?? throw new InvalidDataException("Generated payment XML has no Customer Credit Transfer Initiation.");
		var group = init.Element(ns + "GrpHdr") ?? throw new InvalidDataException("Generated payment XML has no group header.");
		if (group.Element(ns + "NbOfTxs")?.Value != expectedCount.ToString(CultureInfo.InvariantCulture)) throw new InvalidDataException("Generated payment XML transaction count is inconsistent.");
		if (group.Element(ns + "CtrlSum")?.Value != Amount(expectedControlSum)) throw new InvalidDataException("Generated payment XML control sum is inconsistent.");
		var payment = init.Element(ns + "PmtInf") ?? throw new InvalidDataException("Generated payment XML has no payment information block.");
		if (payment.Elements(ns + "CdtTrfTxInf").Count() != expectedCount) throw new InvalidDataException("Generated payment XML transaction detail count is inconsistent.");
		if (payment.Descendants(ns + "AdrLine").Any()) throw new InvalidDataException("Generated payment XML must not contain unstructured-only postal address lines.");
		if (payment.Descendants(ns + "InstdAmt").Any(value => (string?)value.Attribute("Ccy") != "EUR")) throw new InvalidDataException("Generated payment XML contains a non-EUR instructed amount.");
		if (run.Lines.OrderBy(value => value.Id).Select(value => value.Amount).Sum() != expectedControlSum) throw new InvalidDataException("Payment source control sum changed during generation.");
	}

	private static void ValidateTransition(FinanceSepaPaymentExportStatus current, FinanceSepaPaymentExportStatus target)
	{
		var allowed = (current, target) switch
		{
			(FinanceSepaPaymentExportStatus.Generated, FinanceSepaPaymentExportStatus.SubmittedExternally) => true,
			(FinanceSepaPaymentExportStatus.Generated, FinanceSepaPaymentExportStatus.Cancelled) => true,
			(FinanceSepaPaymentExportStatus.Downloaded, FinanceSepaPaymentExportStatus.SubmittedExternally) => true,
			(FinanceSepaPaymentExportStatus.Downloaded, FinanceSepaPaymentExportStatus.Cancelled) => true,
			(FinanceSepaPaymentExportStatus.SubmittedExternally, FinanceSepaPaymentExportStatus.Accepted) => true,
			(FinanceSepaPaymentExportStatus.SubmittedExternally, FinanceSepaPaymentExportStatus.Rejected) => true,
			_ => false
		};
		if (!allowed) throw new InvalidOperationException($"SEPA payment export cannot transition from {current} to {target}.");
	}

	private static FinanceSepaDebtorProfile Normalize(FinanceSepaDebtorProfile value) => value with
	{
		Name=Required(value.Name,140),StreetName=Required(value.StreetName,70),BuildingNumber=Clean(value.BuildingNumber,16),PostalCode=Required(value.PostalCode,16),
		TownName=Required(value.TownName,35),CountrySubdivision=Clean(value.CountrySubdivision,35),CountryCode=Required(value.CountryCode,2).ToUpperInvariant()
	};

	private static FinanceSepaCreditorProfile Normalize(FinanceSepaCreditorProfile value) => value with
	{
		Name=Required(value.Name,140),Iban=NormalizeIban(value.Iban),Bic=NormalizeBic(value.Bic),StreetName=Required(value.StreetName,70),BuildingNumber=Clean(value.BuildingNumber,16),
		PostalCode=Required(value.PostalCode,16),TownName=Required(value.TownName,35),CountrySubdivision=Clean(value.CountrySubdivision,35),CountryCode=Required(value.CountryCode,2).ToUpperInvariant()
	};

	private static void ValidateParty(string name, string street, string? building, string postalCode, string town, string? subdivision, string country)
	{
		_ = Required(name,140); _ = Required(street,70); _ = Required(postalCode,16); _ = Required(town,35);
		if (!string.IsNullOrWhiteSpace(building) && building.Trim().Length > 16) throw new ArgumentException("Building number exceeds 16 characters.");
		if (!string.IsNullOrWhiteSpace(subdivision) && subdivision.Trim().Length > 35) throw new ArgumentException("Country subdivision exceeds 35 characters.");
		if (!CountryPattern.IsMatch(country.Trim().ToUpperInvariant())) throw new ArgumentException("Country code must be a two-letter ISO code.");
	}

	private static void ValidateIban(string? value, string field)
	{
		var iban = NormalizeIban(value);
		if (iban.Length is < 15 or > 34 || !char.IsLetter(iban[0]) || !char.IsLetter(iban[1]) || !char.IsDigit(iban[2]) || !char.IsDigit(iban[3]) || iban.Any(character => !char.IsAsciiLetterUpper(character) && !char.IsDigit(character)))
			throw new ArgumentException($"{field} is not a valid IBAN.");
		var reordered = iban[4..] + iban[..4];
		var remainder = 0;
		foreach (var character in reordered)
		{
			if (char.IsDigit(character)) remainder = (remainder * 10 + (character - '0')) % 97;
			else
			{
				var number = character - 'A' + 10;
				remainder = (remainder * 10 + number / 10) % 97;
				remainder = (remainder * 10 + number % 10) % 97;
			}
		}
		if (remainder != 1) throw new ArgumentException($"{field} checksum is invalid.");
	}

	private static void ValidateSepaIban(string? value, string field)
	{
		ValidateIban(value, field);
		var iban = NormalizeIban(value);
		var countryCode = iban[..2];
		if (!SepaCountryCodes.Contains(countryCode)) throw new ArgumentException($"{field} country '{countryCode}' is outside the supported SEPA geographical scope.");
	}

	private static void ValidateBic(string? value, string field)
	{
		var bic = NormalizeBic(value);
		if (bic is not null && !BicPattern.IsMatch(bic)) throw new ArgumentException($"{field} must contain a valid 8- or 11-character BIC.");
	}

	private User RequireUser() => _authorization.CurrentUser ?? throw new UnauthorizedAccessException("An authenticated user is required.");
	private static string Amount(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
	private static string Required(string? value, int maxLength) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A required SEPA text value is missing.") : value.Trim().Length <= maxLength ? value.Trim() : throw new ArgumentException($"SEPA text value exceeds {maxLength} characters.");
	private static string? Clean(string? value, int maxLength) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length <= maxLength ? value.Trim() : throw new ArgumentException($"SEPA text value exceeds {maxLength} characters.");
	private static string NormalizeIban(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
	private static string? NormalizeBic(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();

	private static string DeterministicIdentifier(string prefix, Guid runOperationId, int sequence, long lineId)
	{
		var seed = Encoding.UTF8.GetBytes($"Depot|Finance|SEPA-SCT|{prefix}|{runOperationId:D}|{sequence}|{lineId}");
		var hash = SHA256.HashData(seed);
		return prefix + Convert.ToHexString(hash.AsSpan(0, 17)).ToUpperInvariant();
	}

	private static FinanceSepaPaymentExportSummary ToSummary(FinanceSepaPaymentExport value) => new()
	{
		Id=value.Id,PaymentRunId=value.PaymentRunId,ExportSequence=value.ExportSequence,MessageId=value.MessageId,FileName=value.FileName,MessageVersion=value.MessageVersion,
		SchemeProfile=value.SchemeProfile,GeneratedAtUtc=value.GeneratedAtUtc,TransactionCount=value.TransactionCount,ControlSum=value.ControlSum,XmlSha256=value.XmlSha256,
		CurrentStatus=value.CurrentStatus,SupersedesExportId=value.SupersedesExportId
	};
}
