// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;
using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class FinanceSepaPaymentExportRepository : DatabaseRepository
{
	public FinanceSepaPaymentExportRepository(DatabaseAccess database) : base(database) { }

	public Task<FinanceSepaDebtorProfile?> GetDebtorProfileAsync(long bankAccountId, CancellationToken cancellationToken = default) =>
		Database.QuerySingleOrDefaultAsync(DebtorSelect + " WHERE BankAccountId=$Id;", ReadDebtor, cancellationToken, Parameter("$Id", bankAccountId));

	public Task<FinanceSepaCreditorProfile?> GetCreditorProfileAsync(long supplierId, CancellationToken cancellationToken = default) =>
		Database.QuerySingleOrDefaultAsync(CreditorSelect + " WHERE SupplierId=$Id;", ReadCreditor, cancellationToken, Parameter("$Id", supplierId));

	public Task<PageResult<FinanceSepaPaymentExportSummary>> SearchExportsAsync(long? paymentRunId, int pageNumber = 1, int pageSize = 100, CancellationToken cancellationToken = default)
	{
		var where = paymentRunId.HasValue ? " WHERE PaymentRunId=$Run" : string.Empty;
		DatabaseParameter[] parameters = paymentRunId.HasValue ? [Parameter("$Run", paymentRunId.Value)] : [];
		return Database.QueryPageAsync(
			ExportSummarySelect + where + " ORDER BY GeneratedAtUtc DESC,Id DESC",
			"SELECT COUNT(*) FROM FinanceSepaPaymentExports" + where + ";",
			ReadSummary,
			pageNumber,
			pageSize,
			cancellationToken,
			parameters);
	}

	public async Task<FinanceSepaPaymentExport?> GetExportAsync(long id, CancellationToken cancellationToken = default)
	{
		var value = await Database.QuerySingleOrDefaultAsync(ExportSelect + " WHERE Id=$Id;", ReadExport, cancellationToken, Parameter("$Id", id));
		if (value is null) return null;
		var history = await Database.QueryAsync(HistorySelect + " WHERE ExportId=$Id ORDER BY Id;", ReadHistory, cancellationToken, Parameter("$Id", id));
		return value with { StatusHistory = history };
	}

	internal Task<FinanceSepaDebtorProfile?> GetDebtorProfileAsync(DatabaseTransactionContext transaction, long bankAccountId, CancellationToken cancellationToken) =>
		transaction.Session.QuerySingleOrDefaultAsync(DebtorSelect + " WHERE BankAccountId=$Id;", ReadDebtor, cancellationToken, Parameter("$Id", bankAccountId));

	internal async Task<IReadOnlyDictionary<long, FinanceSepaCreditorProfile>> GetCreditorProfilesAsync(DatabaseTransactionContext transaction, IReadOnlyCollection<long> supplierIds, CancellationToken cancellationToken)
	{
		if (supplierIds.Count == 0) return new Dictionary<long, FinanceSepaCreditorProfile>();
		var ids = supplierIds.Distinct().OrderBy(value => value).ToArray();
		var names = ids.Select((_, index) => $"$Supplier{index}").ToArray();
		var parameters = ids.Select((value, index) => Parameter(names[index], value)).ToArray();
		var rows = await transaction.Session.QueryAsync(CreditorSelect + $" WHERE SupplierId IN ({string.Join(",", names)}) ORDER BY SupplierId;", ReadCreditor, cancellationToken, parameters);
		return rows.ToDictionary(value => value.SupplierId);
	}

	internal Task<bool> SupplierExistsAsync(DatabaseTransactionContext transaction, long supplierId, CancellationToken cancellationToken) =>
		ExistsAsync(transaction, "SELECT COUNT(*) FROM Suppliers WHERE Id=$Id AND IsActive=1;", supplierId, cancellationToken);

	internal Task<int> InsertDebtorProfileAsync(DatabaseTransactionContext transaction, FinanceSepaDebtorProfile value, CancellationToken cancellationToken) =>
		transaction.Session.ExecuteAsync(
			"INSERT INTO FinanceSepaDebtorProfiles (BankAccountId,Version,Name,StreetName,BuildingNumber,PostalCode,TownName,CountrySubdivision,CountryCode) VALUES ($Id,1,$Name,$Street,$Building,$Postal,$Town,$Subdivision,$Country);",
			cancellationToken, DebtorParameters(value));

	internal Task<int> UpdateDebtorProfileAsync(DatabaseTransactionContext transaction, FinanceSepaDebtorProfile value, long expectedVersion, CancellationToken cancellationToken) =>
		transaction.Session.ExecuteAsync(
			"UPDATE FinanceSepaDebtorProfiles SET Version=Version+1,Name=$Name,StreetName=$Street,BuildingNumber=$Building,PostalCode=$Postal,TownName=$Town,CountrySubdivision=$Subdivision,CountryCode=$Country WHERE BankAccountId=$Id AND Version=$Version;",
			cancellationToken, DebtorParameters(value).Append(Parameter("$Version", expectedVersion)).ToArray());

	internal Task<int> InsertCreditorProfileAsync(DatabaseTransactionContext transaction, FinanceSepaCreditorProfile value, CancellationToken cancellationToken) =>
		transaction.Session.ExecuteAsync(
			"INSERT INTO FinanceSepaCreditorProfiles (SupplierId,Version,Name,Iban,Bic,StreetName,BuildingNumber,PostalCode,TownName,CountrySubdivision,CountryCode,IsActive) VALUES ($Id,1,$Name,$Iban,$Bic,$Street,$Building,$Postal,$Town,$Subdivision,$Country,$Active);",
			cancellationToken, CreditorParameters(value));

	internal Task<int> UpdateCreditorProfileAsync(DatabaseTransactionContext transaction, FinanceSepaCreditorProfile value, long expectedVersion, CancellationToken cancellationToken) =>
		transaction.Session.ExecuteAsync(
			"UPDATE FinanceSepaCreditorProfiles SET Version=Version+1,Name=$Name,Iban=$Iban,Bic=$Bic,StreetName=$Street,BuildingNumber=$Building,PostalCode=$Postal,TownName=$Town,CountrySubdivision=$Subdivision,CountryCode=$Country,IsActive=$Active WHERE SupplierId=$Id AND Version=$Version;",
			cancellationToken, CreditorParameters(value).Append(Parameter("$Version", expectedVersion)).ToArray());

	internal Task<FinanceSepaPaymentExport?> GetLatestExportForRunAsync(DatabaseTransactionContext transaction, long paymentRunId, CancellationToken cancellationToken) =>
		transaction.Session.QuerySingleOrDefaultAsync(ExportSelect + " WHERE PaymentRunId=$Run ORDER BY ExportSequence DESC,Id DESC;", ReadExport, cancellationToken, Parameter("$Run", paymentRunId));

	internal async Task<int> GetNextSequenceAsync(DatabaseTransactionContext transaction, long paymentRunId, CancellationToken cancellationToken)
	{
		var value = await transaction.Session.ExecuteScalarAsync("SELECT COALESCE(MAX(ExportSequence),0) FROM FinanceSepaPaymentExports WHERE PaymentRunId=$Run;", cancellationToken, Parameter("$Run", paymentRunId));
		return Convert.ToInt32(value ?? 0, CultureInfo.InvariantCulture) + 1;
	}

	internal Task<long> CreateExportAsync(DatabaseTransactionContext transaction, FinanceSepaPaymentExport value, CancellationToken cancellationToken) =>
		transaction.Session.InsertAsync(
			"INSERT INTO FinanceSepaPaymentExports (PaymentRunId,ExportSequence,ExportKey,MessageId,PaymentInformationId,FileName,MessageVersion,SchemeProfile,GeneratedAtUtc,GeneratedByUserId,TransactionCount,ControlSum,BankAccountId,XmlSha256,XmlPayload,CurrentStatus,SupersedesExportId) VALUES ($Run,$Sequence,$Key,$Message,$PaymentInfo,$File,$Version,$Profile,$Generated,$User,$Count,$Sum,$Bank,$Hash,$Payload,$Status,$Supersedes);",
			cancellationToken,
			Parameter("$Run", value.PaymentRunId), Parameter("$Sequence", value.ExportSequence), Parameter("$Key", value.ExportKey), Parameter("$Message", value.MessageId),
			Parameter("$PaymentInfo", value.PaymentInformationId), Parameter("$File", value.FileName), Parameter("$Version", value.MessageVersion), Parameter("$Profile", value.SchemeProfile),
			Parameter("$Generated", value.GeneratedAtUtc.ToString("O", CultureInfo.InvariantCulture)), Parameter("$User", value.GeneratedByUserId), Parameter("$Count", value.TransactionCount),
			Parameter("$Sum", value.ControlSum), Parameter("$Bank", value.BankAccountId), Parameter("$Hash", value.XmlSha256), Parameter("$Payload", value.XmlPayload),
			Parameter("$Status", (int)value.CurrentStatus), Parameter("$Supersedes", value.SupersedesExportId));

	internal Task<int> UpdateStatusAsync(DatabaseTransactionContext transaction, long exportId, FinanceSepaPaymentExportStatus expectedStatus, FinanceSepaPaymentExportStatus newStatus, CancellationToken cancellationToken) =>
		transaction.Session.ExecuteAsync("UPDATE FinanceSepaPaymentExports SET CurrentStatus=$New WHERE Id=$Id AND CurrentStatus=$Expected;", cancellationToken, Parameter("$New", (int)newStatus), Parameter("$Id", exportId), Parameter("$Expected", (int)expectedStatus));

	internal Task<long> AddStatusHistoryAsync(DatabaseTransactionContext transaction, FinanceSepaPaymentExportStatusHistory value, CancellationToken cancellationToken) =>
		transaction.Session.InsertAsync(
			"INSERT INTO FinanceSepaPaymentExportStatusHistory (ExportId,Status,RecordedAtUtc,RecordedByUserId,ExternalReference,EvidenceNote) VALUES ($Export,$Status,$At,$User,$Reference,$Note);",
			cancellationToken, Parameter("$Export", value.ExportId), Parameter("$Status", (int)value.Status), Parameter("$At", value.RecordedAtUtc.ToString("O", CultureInfo.InvariantCulture)), Parameter("$User", value.RecordedByUserId), Parameter("$Reference", value.ExternalReference), Parameter("$Note", value.EvidenceNote));

	internal Task<FinanceSepaPaymentExport?> GetExportAsync(DatabaseTransactionContext transaction, long id, CancellationToken cancellationToken) =>
		transaction.Session.QuerySingleOrDefaultAsync(ExportSelect + " WHERE Id=$Id;", ReadExport, cancellationToken, Parameter("$Id", id));

	private static async Task<bool> ExistsAsync(DatabaseTransactionContext transaction, string sql, long id, CancellationToken cancellationToken)
	{
		var value = await transaction.Session.ExecuteScalarAsync(sql, cancellationToken, Parameter("$Id", id));
		return Convert.ToInt64(value ?? 0, CultureInfo.InvariantCulture) == 1;
	}

	private static DatabaseParameter[] DebtorParameters(FinanceSepaDebtorProfile value) =>
	[
		Parameter("$Id", value.BankAccountId), Parameter("$Name", value.Name), Parameter("$Street", value.StreetName), Parameter("$Building", value.BuildingNumber),
		Parameter("$Postal", value.PostalCode), Parameter("$Town", value.TownName), Parameter("$Subdivision", value.CountrySubdivision), Parameter("$Country", value.CountryCode)
	];

	private static DatabaseParameter[] CreditorParameters(FinanceSepaCreditorProfile value) =>
	[
		Parameter("$Id", value.SupplierId), Parameter("$Name", value.Name), Parameter("$Iban", value.Iban), Parameter("$Bic", value.Bic), Parameter("$Street", value.StreetName),
		Parameter("$Building", value.BuildingNumber), Parameter("$Postal", value.PostalCode), Parameter("$Town", value.TownName), Parameter("$Subdivision", value.CountrySubdivision),
		Parameter("$Country", value.CountryCode), Parameter("$Active", value.IsActive)
	];

	private const string DebtorSelect = "SELECT BankAccountId,Version,Name,StreetName,BuildingNumber,PostalCode,TownName,CountrySubdivision,CountryCode FROM FinanceSepaDebtorProfiles";
	private const string CreditorSelect = "SELECT SupplierId,Version,Name,Iban,Bic,StreetName,BuildingNumber,PostalCode,TownName,CountrySubdivision,CountryCode,IsActive FROM FinanceSepaCreditorProfiles";
	private const string ExportSelect = "SELECT Id,PaymentRunId,ExportSequence,ExportKey,MessageId,PaymentInformationId,FileName,MessageVersion,SchemeProfile,GeneratedAtUtc,GeneratedByUserId,TransactionCount,ControlSum,BankAccountId,XmlSha256,XmlPayload,CurrentStatus,SupersedesExportId FROM FinanceSepaPaymentExports";
	private const string ExportSummarySelect = "SELECT Id,PaymentRunId,ExportSequence,MessageId,FileName,MessageVersion,SchemeProfile,GeneratedAtUtc,TransactionCount,ControlSum,XmlSha256,CurrentStatus,SupersedesExportId FROM FinanceSepaPaymentExports";
	private const string HistorySelect = "SELECT Id,ExportId,Status,RecordedAtUtc,RecordedByUserId,ExternalReference,EvidenceNote FROM FinanceSepaPaymentExportStatusHistory";

	private static FinanceSepaDebtorProfile ReadDebtor(DbDataReader reader) => new()
	{
		BankAccountId=reader.GetInt64(0),Version=reader.GetInt64(1),Name=reader.GetString(2),StreetName=reader.GetString(3),BuildingNumber=reader.IsDBNull(4)?null:reader.GetString(4),
		PostalCode=reader.GetString(5),TownName=reader.GetString(6),CountrySubdivision=reader.IsDBNull(7)?null:reader.GetString(7),CountryCode=reader.GetString(8)
	};

	private static FinanceSepaCreditorProfile ReadCreditor(DbDataReader reader) => new()
	{
		SupplierId=reader.GetInt64(0),Version=reader.GetInt64(1),Name=reader.GetString(2),Iban=reader.GetString(3),Bic=reader.IsDBNull(4)?null:reader.GetString(4),
		StreetName=reader.GetString(5),BuildingNumber=reader.IsDBNull(6)?null:reader.GetString(6),PostalCode=reader.GetString(7),TownName=reader.GetString(8),
		CountrySubdivision=reader.IsDBNull(9)?null:reader.GetString(9),CountryCode=reader.GetString(10),IsActive=Convert.ToBoolean(reader.GetValue(11),CultureInfo.InvariantCulture)
	};

	private static FinanceSepaPaymentExport ReadExport(DbDataReader reader) => new()
	{
		Id=reader.GetInt64(0),PaymentRunId=reader.GetInt64(1),ExportSequence=Convert.ToInt32(reader.GetValue(2),CultureInfo.InvariantCulture),ExportKey=reader.GetString(3),
		MessageId=reader.GetString(4),PaymentInformationId=reader.GetString(5),FileName=reader.GetString(6),MessageVersion=reader.GetString(7),SchemeProfile=reader.GetString(8),
		GeneratedAtUtc=Convert.ToDateTime(reader.GetValue(9),CultureInfo.InvariantCulture),GeneratedByUserId=reader.GetInt64(10),TransactionCount=Convert.ToInt32(reader.GetValue(11),CultureInfo.InvariantCulture),
		ControlSum=Convert.ToDecimal(reader.GetValue(12),CultureInfo.InvariantCulture),BankAccountId=reader.GetInt64(13),XmlSha256=reader.GetString(14),XmlPayload=(byte[])reader.GetValue(15),
		CurrentStatus=(FinanceSepaPaymentExportStatus)Convert.ToInt32(reader.GetValue(16),CultureInfo.InvariantCulture),SupersedesExportId=reader.IsDBNull(17)?null:reader.GetInt64(17)
	};

	private static FinanceSepaPaymentExportSummary ReadSummary(DbDataReader reader) => new()
	{
		Id=reader.GetInt64(0),PaymentRunId=reader.GetInt64(1),ExportSequence=Convert.ToInt32(reader.GetValue(2),CultureInfo.InvariantCulture),MessageId=reader.GetString(3),
		FileName=reader.GetString(4),MessageVersion=reader.GetString(5),SchemeProfile=reader.GetString(6),GeneratedAtUtc=Convert.ToDateTime(reader.GetValue(7),CultureInfo.InvariantCulture),
		TransactionCount=Convert.ToInt32(reader.GetValue(8),CultureInfo.InvariantCulture),ControlSum=Convert.ToDecimal(reader.GetValue(9),CultureInfo.InvariantCulture),
		XmlSha256=reader.GetString(10),CurrentStatus=(FinanceSepaPaymentExportStatus)Convert.ToInt32(reader.GetValue(11),CultureInfo.InvariantCulture),
		SupersedesExportId=reader.IsDBNull(12)?null:reader.GetInt64(12)
	};

	private static FinanceSepaPaymentExportStatusHistory ReadHistory(DbDataReader reader) => new()
	{
		Id=reader.GetInt64(0),ExportId=reader.GetInt64(1),Status=(FinanceSepaPaymentExportStatus)Convert.ToInt32(reader.GetValue(2),CultureInfo.InvariantCulture),
		RecordedAtUtc=Convert.ToDateTime(reader.GetValue(3),CultureInfo.InvariantCulture),RecordedByUserId=reader.GetInt64(4),
		ExternalReference=reader.IsDBNull(5)?null:reader.GetString(5),EvidenceNote=reader.IsDBNull(6)?null:reader.GetString(6)
	};
}
