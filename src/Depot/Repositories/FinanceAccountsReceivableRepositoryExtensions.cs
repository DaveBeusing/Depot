// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;

using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

internal static class FinanceAccountsReceivableRepositoryExtensions
{
	internal static Task<FinanceAllocationOperationRecord?> FindAllocationOperationAsync(
		this FinanceAccountsReceivableRepository repository,
		DatabaseTransactionContext transaction,
		Guid operationId,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(repository);
		return transaction.Session.QuerySingleOrDefaultAsync(
			"SELECT OperationId,RequestHash,CreditOpenItemId,CreatedAtUtc,CreatedByUserId FROM FinanceReceivableAllocationOperations WHERE OperationId=$OperationId;",
			reader => new FinanceAllocationOperationRecord(
				Guid.Parse(reader.GetString(0)),
				reader.GetString(1),
				reader.GetInt64(2),
				ReadDateTimeUtc(reader, 3),
				reader.IsDBNull(4) ? null : reader.GetInt64(4)),
			cancellationToken,
			new DatabaseParameter("$OperationId", operationId.ToString("D")));
	}

	internal static Task<int> CreateAllocationOperationAsync(
		this FinanceAccountsReceivableRepository repository,
		DatabaseTransactionContext transaction,
		Guid operationId,
		string requestHash,
		long creditOpenItemId,
		DateTime createdAtUtc,
		long userId,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(repository);
		return transaction.Session.ExecuteAsync(
			"INSERT INTO FinanceReceivableAllocationOperations (OperationId,RequestHash,CreditOpenItemId,CreatedAtUtc,CreatedByUserId) VALUES ($OperationId,$RequestHash,$CreditOpenItemId,$CreatedAtUtc,$CreatedByUserId);",
			cancellationToken,
			new DatabaseParameter("$OperationId", operationId.ToString("D")),
			new DatabaseParameter("$RequestHash", requestHash),
			new DatabaseParameter("$CreditOpenItemId", creditOpenItemId),
			new DatabaseParameter("$CreatedAtUtc", createdAtUtc.ToString("O", CultureInfo.InvariantCulture)),
			new DatabaseParameter("$CreatedByUserId", userId));
	}

	internal static Task<IReadOnlyList<FinanceReceivableAllocation>> GetActiveAllocationsForCreditAsync(
		this FinanceAccountsReceivableRepository repository,
		DatabaseTransactionContext transaction,
		long creditOpenItemId,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(repository);
		return transaction.Session.QueryAsync(
			"SELECT Id,OperationId,DebitOpenItemId,CreditOpenItemId,Amount,AllocationDate,CreatedAtUtc,CreatedByUserId,ReversedAtUtc,ReversedByUserId,ReversalOperationId FROM FinanceReceivableAllocations WHERE CreditOpenItemId=$CreditOpenItemId AND ReversedAtUtc IS NULL ORDER BY Id;",
			ReadAllocation,
			cancellationToken,
			new DatabaseParameter("$CreditOpenItemId", creditOpenItemId));
	}

	internal static Task<int> ReverseAllocationsForCreditAsync(
		this FinanceAccountsReceivableRepository repository,
		DatabaseTransactionContext transaction,
		long creditOpenItemId,
		Guid reversalOperationId,
		DateTime reversedAtUtc,
		long userId,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(repository);
		return transaction.Session.ExecuteAsync(
			"UPDATE FinanceReceivableAllocations SET ReversedAtUtc=$At,ReversedByUserId=$UserId,ReversalOperationId=$ReversalOperationId WHERE CreditOpenItemId=$CreditOpenItemId AND ReversedAtUtc IS NULL;",
			cancellationToken,
			new DatabaseParameter("$At", reversedAtUtc.ToString("O", CultureInfo.InvariantCulture)),
			new DatabaseParameter("$UserId", userId),
			new DatabaseParameter("$ReversalOperationId", reversalOperationId.ToString("D")),
			new DatabaseParameter("$CreditOpenItemId", creditOpenItemId));
	}

	internal static async Task<bool> CustomerExistsAsync(
		this FinanceAccountsReceivableRepository repository,
		DatabaseTransactionContext transaction,
		long customerId,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(repository);
		var value = await transaction.Session.ExecuteScalarAsync(
			"SELECT COUNT(*) FROM Customers WHERE Id=$CustomerId AND IsActive=1;",
			cancellationToken,
			new DatabaseParameter("$CustomerId", customerId));
		return Convert.ToInt64(value ?? 0, CultureInfo.InvariantCulture) == 1;
	}

	internal static Task<FinanceReceivablesConfiguration?> GetAnyConfigurationAsync(
		this FinanceAccountsReceivableRepository repository,
		DatabaseTransactionContext transaction,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(repository);
		return transaction.Session.QuerySingleOrDefaultAsync(
			"SELECT Id,Version,LegalEntityId,FiscalCalendarId,InvoicePostingProfileId,CreditNotePostingProfileId,PaymentPostingProfileId,WriteOffPostingProfileId,IsActive FROM FinanceReceivablesConfigurations ORDER BY Id;",
			reader => new FinanceReceivablesConfiguration
			{
				Id = reader.GetInt64(0),
				Version = Convert.ToInt64(reader.GetValue(1), CultureInfo.InvariantCulture),
				LegalEntityId = Guid.Parse(reader.GetString(2)),
				FiscalCalendarId = Guid.Parse(reader.GetString(3)),
				InvoicePostingProfileId = reader.GetInt64(4),
				CreditNotePostingProfileId = reader.GetInt64(5),
				PaymentPostingProfileId = reader.GetInt64(6),
				WriteOffPostingProfileId = reader.GetInt64(7),
				IsActive = Convert.ToBoolean(reader.GetValue(8), CultureInfo.InvariantCulture)
			},
			cancellationToken);
	}

	private static FinanceReceivableAllocation ReadAllocation(DbDataReader reader) => new()
	{
		Id = reader.GetInt64(0),
		OperationId = Guid.Parse(reader.GetString(1)),
		DebitOpenItemId = reader.GetInt64(2),
		CreditOpenItemId = reader.GetInt64(3),
		Amount = Convert.ToDecimal(reader.GetValue(4), CultureInfo.InvariantCulture),
		AllocationDate = ReadDateOnly(reader, 5),
		CreatedAtUtc = ReadDateTimeUtc(reader, 6),
		CreatedByUserId = reader.IsDBNull(7) ? null : reader.GetInt64(7),
		ReversedAtUtc = reader.IsDBNull(8) ? null : ReadDateTimeUtc(reader, 8),
		ReversedByUserId = reader.IsDBNull(9) ? null : reader.GetInt64(9),
		ReversalOperationId = reader.IsDBNull(10) ? null : Guid.Parse(reader.GetString(10))
	};

	private static DateOnly ReadDateOnly(DbDataReader reader, int ordinal) =>
		reader.GetValue(ordinal) is DateTime dateTime
			? DateOnly.FromDateTime(dateTime)
			: DateOnly.Parse(Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture) ?? string.Empty, CultureInfo.InvariantCulture);

	private static DateTime ReadDateTimeUtc(DbDataReader reader, int ordinal) =>
		reader.GetValue(ordinal) is DateTime dateTime
			? DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)
			: DateTime.Parse(
				Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture) ?? string.Empty,
				CultureInfo.InvariantCulture,
				DateTimeStyles.RoundtripKind).ToUniversalTime();
}

internal sealed record FinanceAllocationOperationRecord(
	Guid OperationId,
	string RequestHash,
	long CreditOpenItemId,
	DateTime CreatedAtUtc,
	long? CreatedByUserId);
