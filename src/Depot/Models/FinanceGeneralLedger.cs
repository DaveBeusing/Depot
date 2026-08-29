// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public enum FinanceJournalEntryKind
{
	Standard,
	Manual,
	Reversal
}

public enum FinancePostingDirection
{
	Debit,
	Credit
}

public static class FinanceNumberSequenceDocumentTypes
{
	public const string GeneralLedger = "Finance.GeneralLedger";
}

public sealed record FinanceJournalLineDimension(Guid DimensionId, Guid DimensionValueId);

public sealed record FinancePostingLine
{
	public required Guid AccountId { get; init; }
	public string? Description { get; init; }
	public decimal Debit { get; init; }
	public decimal Credit { get; init; }
	public IReadOnlyList<FinanceJournalLineDimension> Dimensions { get; init; } = [];
}

public sealed record FinancePostingRequest
{
	public required Guid OperationId { get; init; }
	public required Guid AccountingBookId { get; init; }
	public required Guid JournalId { get; init; }
	public required Guid AccountingPeriodId { get; init; }
	public required string NumberSequenceCode { get; init; }
	public required DateOnly PostingDate { get; init; }
	public required string Description { get; init; }
	public required string SourceType { get; init; }
	public required string SourceId { get; init; }
	public required string SourceEvent { get; init; }
	public string? SourceReference { get; init; }
	public required CurrencyCode TransactionCurrency { get; init; }
	public Guid? ExchangeRateId { get; init; }
	public FinanceJournalEntryKind EntryKind { get; init; } = FinanceJournalEntryKind.Standard;
	public IReadOnlyList<FinancePostingLine> Lines { get; init; } = [];
}

public sealed record FinanceProfilePostingRequest
{
	public required Guid OperationId { get; init; }
	public required long PostingProfileId { get; init; }
	public required Guid AccountingPeriodId { get; init; }
	public required DateOnly PostingDate { get; init; }
	public required string Description { get; init; }
	public required string SourceId { get; init; }
	public string? SourceReference { get; init; }
	public required CurrencyCode TransactionCurrency { get; init; }
	public Guid? ExchangeRateId { get; init; }
	public IReadOnlyDictionary<string, decimal> Amounts { get; init; } = new Dictionary<string, decimal>(StringComparer.Ordinal);
	public IReadOnlyList<FinanceJournalLineDimension> Dimensions { get; init; } = [];
}

public sealed record FinanceJournalEntry
{
	public long Id { get; init; }
	public required string EntryNumber { get; init; }
	public required Guid OperationId { get; init; }
	public required string RequestHash { get; init; }
	public required Guid AccountingBookId { get; init; }
	public required Guid JournalId { get; init; }
	public required Guid AccountingPeriodId { get; init; }
	public required DateOnly PostingDate { get; init; }
	public required DateTime PostedAtUtc { get; init; }
	public long? PostedByUserId { get; init; }
	public required string Description { get; init; }
	public required string SourceType { get; init; }
	public required string SourceId { get; init; }
	public required string SourceEvent { get; init; }
	public string? SourceReference { get; init; }
	public required CurrencyCode TransactionCurrency { get; init; }
	public required CurrencyCode ReportingCurrency { get; init; }
	public Guid? ExchangeRateId { get; init; }
	public decimal ExchangeRate { get; init; }
	public FinanceJournalEntryKind EntryKind { get; init; }
	public long? ReversalOfEntryId { get; init; }
	public IReadOnlyList<FinanceJournalEntryLine> Lines { get; init; } = [];
}

public sealed record FinanceJournalEntryLine
{
	public long Id { get; init; }
	public long JournalEntryId { get; init; }
	public int LineNumber { get; init; }
	public required Guid AccountId { get; init; }
	public string? Description { get; init; }
	public decimal TransactionDebit { get; init; }
	public decimal TransactionCredit { get; init; }
	public decimal ReportingDebit { get; init; }
	public decimal ReportingCredit { get; init; }
	public IReadOnlyList<FinanceJournalLineDimension> Dimensions { get; init; } = [];
}

public sealed record FinanceJournalEntrySummary(
	long Id,
	string EntryNumber,
	DateOnly PostingDate,
	DateTime PostedAtUtc,
	string Description,
	string SourceType,
	string SourceId,
	string SourceEvent,
	string? SourceReference,
	CurrencyCode ReportingCurrency,
	FinanceJournalEntryKind EntryKind,
	long? ReversalOfEntryId);

public sealed record FinancePostingProfile
{
	public long Id { get; init; }
	public long Version { get; init; }
	public required Guid LegalEntityId { get; init; }
	public required Guid AccountingBookId { get; init; }
	public required Guid JournalId { get; init; }
	public required string Code { get; init; }
	public required string Name { get; init; }
	public required string SourceType { get; init; }
	public required string SourceEvent { get; init; }
	public required string NumberSequenceCode { get; init; }
	public bool IsActive { get; init; } = true;
	public IReadOnlyList<FinancePostingProfileLine> Lines { get; init; } = [];
}

public sealed record FinancePostingProfileLine
{
	public long Id { get; init; }
	public long PostingProfileId { get; init; }
	public int LineNumber { get; init; }
	public required Guid AccountId { get; init; }
	public FinancePostingDirection Direction { get; init; }
	public required string AmountKey { get; init; }
	public decimal Multiplier { get; init; } = 1m;
	public string? Description { get; init; }
}
