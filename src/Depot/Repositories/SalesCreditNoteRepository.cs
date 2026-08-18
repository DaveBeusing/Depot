// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;

using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class SalesCreditNoteRepository : DatabaseRepository
{
	private const string Columns = "cn.Id,cn.CreditNoteNumber,cn.SalesInvoiceId,cn.CustomerId,cn.CreditDate,cn.Status,cn.Reason,cn.CreatedByUserId,cn.PostedByUserId,cn.PostedAtUtc,cn.Version";
	public SalesCreditNoteRepository(DatabaseAccess database) : base(database) { }

	public Task<PageResult<SalesCreditNote>> SearchAsync(string? searchText, SalesCreditNoteStatus? status, int pageNumber, int pageSize, CancellationToken token)
	{
		var filters = new List<string>();
		var parameters = new List<DatabaseParameter>();
		if (!string.IsNullOrWhiteSpace(searchText))
		{
			filters.Add("(cn.CreditNoteNumber LIKE $Search OR si.InvoiceNumber LIKE $Search OR c.Name LIKE $Search)");
			parameters.Add(Parameter("$Search", $"%{searchText.Trim()}%"));
		}
		if (status is not null) { filters.Add("cn.Status=$Status"); parameters.Add(Parameter("$Status", (int)status.Value)); }
		var from = "FROM SalesCreditNotes cn INNER JOIN SalesInvoices si ON si.Id=cn.SalesInvoiceId INNER JOIN Customers c ON c.Id=cn.CustomerId";
		var where = filters.Count == 0 ? string.Empty : $"WHERE {string.Join(" AND ", filters)}";
		return Database.QueryPageAsync($"SELECT {Columns} {from} {where} ORDER BY cn.CreditDate DESC,cn.Id DESC", $"SELECT COUNT(*) {from} {where}", Read, pageNumber, pageSize, token, parameters.ToArray());
	}

	public async Task<SalesCreditNote?> GetByIdAsync(long id, CancellationToken token)
	{
		var value = await Database.QuerySingleOrDefaultAsync($"SELECT {Columns} FROM SalesCreditNotes cn WHERE cn.Id=$Id;", Read, token, Parameter("$Id", id));
		if (value is null) return null;
		value.Lines = await Database.QueryAsync(LineSql + " WHERE cnl.SalesCreditNoteId=$Id ORDER BY cnl.Id;", ReadLine, token, Parameter("$Id", id));
		return value;
	}

	public async Task<SalesCreditNote?> GetByInvoiceIdAsync(long invoiceId, CancellationToken token) =>
		await Database.QuerySingleOrDefaultAsync($"SELECT {Columns} FROM SalesCreditNotes cn WHERE cn.SalesInvoiceId=$InvoiceId ORDER BY cn.Id DESC;", Read, token, Parameter("$InvoiceId", invoiceId));

	public async Task<long> CreateAsync(DatabaseTransactionContext tx, SalesCreditNote value, CancellationToken token)
	{
		value.CreditNoteNumber = $"PENDING-{Guid.NewGuid():N}";
		value.Id = await tx.Session.InsertAsync("INSERT INTO SalesCreditNotes (CreditNoteNumber,SalesInvoiceId,CustomerId,CreditDate,Status,Reason,CreatedByUserId) VALUES ($Number,$InvoiceId,$CustomerId,$Date,$Status,$Reason,$UserId);", token,
			Parameter("$Number", value.CreditNoteNumber), Parameter("$InvoiceId", value.SalesInvoiceId), Parameter("$CustomerId", value.CustomerId), Parameter("$Date", value.CreditDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), Parameter("$Status", (int)value.Status), Parameter("$Reason", value.Reason), Parameter("$UserId", value.CreatedByUserId));
		value.CreditNoteNumber = $"CN-{value.Id:000000}";
		await tx.Session.ExecuteAsync("UPDATE SalesCreditNotes SET CreditNoteNumber=$Number WHERE Id=$Id;", token, Parameter("$Number", value.CreditNoteNumber), Parameter("$Id", value.Id));
		foreach (var line in value.Lines)
		{
			line.SalesCreditNoteId = value.Id;
			line.Id = await tx.Session.InsertAsync("INSERT INTO SalesCreditNoteLines (SalesCreditNoteId,SalesInvoiceLineId,Quantity,UnitPrice,DiscountPercent,TaxRate) VALUES ($CreditId,$InvoiceLineId,$Quantity,$Price,$Discount,$Tax);", token,
				Parameter("$CreditId", value.Id), Parameter("$InvoiceLineId", line.SalesInvoiceLineId), Parameter("$Quantity", line.Quantity), Parameter("$Price", line.UnitPrice), Parameter("$Discount", line.DiscountPercent), Parameter("$Tax", line.TaxRate));
		}
		return value.Id;
	}

	public async Task<bool> PostAsync(DatabaseTransactionContext tx, long id, long version, long userId, DateTime postedAtUtc, CancellationToken token) =>
		await tx.Session.ExecuteAsync("UPDATE SalesCreditNotes SET Status=$Posted,PostedByUserId=$UserId,PostedAtUtc=$At,Version=Version+1 WHERE Id=$Id AND Version=$Version AND Status=$Draft;", token,
			Parameter("$Posted", (int)SalesCreditNoteStatus.Posted), Parameter("$UserId", userId), Parameter("$At", postedAtUtc.ToString("O", CultureInfo.InvariantCulture)), Parameter("$Id", id), Parameter("$Version", version), Parameter("$Draft", (int)SalesCreditNoteStatus.Draft)) == 1;

	private static SalesCreditNote Read(DbDataReader r) => new() { Id=r.GetInt64(0), CreditNoteNumber=r.GetString(1), SalesInvoiceId=r.GetInt64(2), CustomerId=r.GetInt64(3), CreditDate=Convert.ToDateTime(r.GetValue(4),CultureInfo.InvariantCulture), Status=(SalesCreditNoteStatus)r.GetInt32(5), Reason=r.GetString(6), CreatedByUserId=r.GetInt64(7), PostedByUserId=r.IsDBNull(8)?null:r.GetInt64(8), PostedAtUtc=r.IsDBNull(9)?null:DateTime.Parse(r.GetString(9),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind), Version=r.GetInt64(10) };
	private const string LineSql = "SELECT cnl.Id,cnl.SalesCreditNoteId,cnl.SalesInvoiceLineId,cnl.Quantity,cnl.UnitPrice,cnl.DiscountPercent,cnl.TaxRate,cnl.Version FROM SalesCreditNoteLines cnl";
	private static SalesCreditNoteLine ReadLine(DbDataReader r) => new() { Id=r.GetInt64(0), SalesCreditNoteId=r.GetInt64(1), SalesInvoiceLineId=r.GetInt64(2), Quantity=r.GetInt32(3), UnitPrice=Convert.ToDecimal(r.GetValue(4),CultureInfo.InvariantCulture), DiscountPercent=Convert.ToDecimal(r.GetValue(5),CultureInfo.InvariantCulture), TaxRate=Convert.ToDecimal(r.GetValue(6),CultureInfo.InvariantCulture), Version=r.GetInt64(7) };
}
