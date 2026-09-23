// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public enum FinanceAssetStatus { Draft=1, Active=2, Disposed=3, Retired=4 }
public enum FinanceDepreciationMethod { StraightLine=1, NoDepreciation=2 }
public enum FinanceAssetTransactionKind { Capitalization=1, Depreciation=2, Impairment=3, Correction=4, Transfer=5, Disposal=6 }
public enum FinanceClosedPeriodPolicy { Fail=1, NextOpenPeriod=2 }

public sealed record FinanceAssetClass
{
	public long Id { get; init; }
	public long Version { get; init; } = 1;
	public required Guid LegalEntityId { get; init; }
	public required string Code { get; init; }
	public required string Name { get; init; }
	public required Guid FiscalCalendarId { get; init; }
	public long CapitalizationPostingProfileId { get; init; }
	public long DepreciationPostingProfileId { get; init; }
	public long ImpairmentPostingProfileId { get; init; }
	public long DisposalPostingProfileId { get; init; }
	public int DefaultUsefulLifeMonths { get; init; }
	public FinanceDepreciationMethod DefaultMethod { get; init; } = FinanceDepreciationMethod.StraightLine;
	public bool IsActive { get; init; } = true;
}

public sealed record FinanceFixedAsset
{
	public long Id { get; init; }
	public long Version { get; init; } = 1;
	public required string AssetNumber { get; init; }
	public required Guid LegalEntityId { get; init; }
	public long AssetClassId { get; init; }
	public required string Description { get; init; }
	public DateOnly AcquisitionDate { get; init; }
	public DateOnly? CapitalizationDate { get; init; }
	public DateOnly DepreciationStartDate { get; init; }
	public required CurrencyCode Currency { get; init; }
	public decimal OriginalCost { get; init; }
	public decimal SalvageValue { get; init; }
	public int UsefulLifeMonths { get; init; }
	public FinanceDepreciationMethod DepreciationMethod { get; init; }
	public string? Location { get; init; }
	public string? Custodian { get; init; }
	public FinanceAssetStatus Status { get; init; } = FinanceAssetStatus.Draft;
	public long? SourceSupplierDocumentLineId { get; init; }
}

public sealed record FinanceAssetDepreciationPeriod
{
	public long Id { get; init; }
	public long AssetId { get; init; }
	public required Guid AccountingPeriodId { get; init; }
	public DateOnly PeriodStart { get; init; }
	public DateOnly PeriodEnd { get; init; }
	public decimal PlannedAmount { get; init; }
	public decimal PostedAmount { get; init; }
	public long? JournalEntryId { get; init; }
	public Guid? OperationId { get; init; }
	public bool IsPosted => JournalEntryId.HasValue;
}

public sealed record FinanceAssetTransaction
{
	public long Id { get; init; }
	public long AssetId { get; init; }
	public FinanceAssetTransactionKind Kind { get; init; }
	public required Guid OperationId { get; init; }
	public DateOnly TransactionDate { get; init; }
	public decimal Amount { get; init; }
	public long? JournalEntryId { get; init; }
	public string? Reason { get; init; }
	public string? Evidence { get; init; }
	public DateTime CreatedAtUtc { get; init; }
	public long CreatedByUserId { get; init; }
}

public sealed record FinanceAssetReconciliationRow(long AssetId,string AssetNumber,decimal SubledgerCarryingValue,decimal GeneralLedgerCarryingValue)
{
	public decimal Difference => SubledgerCarryingValue-GeneralLedgerCarryingValue;
}

public sealed record FinanceDepreciationRunResult(
	Guid RunOperationId,
	Guid AccountingPeriodId,
	int CandidateCount,
	int PostedCount,
	decimal PostedAmount,
	IReadOnlyList<FinanceAssetTransaction> Transactions);

public sealed record FinanceAssetLegalEntityOption(Guid Id,string Code,string Name);
public sealed record FinanceAssetFiscalCalendarOption(Guid Id,Guid LegalEntityId,string Code,string Name,bool IsActive);
public sealed record FinanceAssetPostingProfileOption(long Id,Guid LegalEntityId,string Code,string Name,string SourceEvent,bool IsActive);
public sealed record FinanceAssetPeriodOption(Guid Id,Guid FiscalCalendarId,string Code,DateOnly StartDate,DateOnly EndDate,AccountingPeriodStatus Status);
