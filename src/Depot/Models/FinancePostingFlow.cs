// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public enum FinancePostingFlowNodeKind
{
	BusinessEvent,
	AmountKey,
	DirectionRule,
	Account,
	Journal
}

public enum FinancePostingFlowIssueSeverity
{
	Information,
	Warning,
	Error
}

public sealed record FinancePostingFlowNode(
	string Key,
	FinancePostingFlowNodeKind Kind,
	string Title,
	string? Subtitle,
	int Column,
	int Row,
	Guid? EntityId = null,
	int? LineNumber = null)
{
	public double CanvasLeft => 24d + (Column * 210d);
	public double CanvasTop => 36d + (Row * 88d);
	public double CanvasWidth => 178d;
	public double CanvasHeight => 62d;
}

public sealed record FinancePostingFlowEdge(string SourceKey, string TargetKey);

public sealed record FinancePostingFlowIssue(
	FinancePostingFlowIssueSeverity Severity,
	string Code,
	string Message,
	int? LineNumber = null);

public sealed record FinancePostingFlowLineDraft(
	long Id,
	int LineNumber,
	Guid AccountId,
	FinancePostingDirection Direction,
	string AmountKey,
	decimal Multiplier,
	string? Description);

public sealed record FinancePostingFlowContext(
	AccountingBook? AccountingBook,
	JournalDefinition? Journal,
	IReadOnlyDictionary<Guid, FinanceAccount> Accounts,
	IReadOnlyList<JournalDefinition> Journals)
{
	public static FinancePostingFlowContext Empty { get; } =
		new(null, null, new Dictionary<Guid, FinanceAccount>(), []);
}

public sealed record FinancePostingFlowProjection(
	FinancePostingProfile Profile,
	IReadOnlyList<FinancePostingFlowNode> Nodes,
	IReadOnlyList<FinancePostingFlowEdge> Edges,
	IReadOnlyList<FinancePostingFlowIssue> Issues)
{
	public bool IsValid => Issues.All(issue => issue.Severity != FinancePostingFlowIssueSeverity.Error);
}

public static class FinancePostingFlowProjector
{
	public static FinancePostingFlowProjection Project(
		FinancePostingProfile profile,
		FinancePostingFlowContext? context = null)
	{
		ArgumentNullException.ThrowIfNull(profile);
		context ??= FinancePostingFlowContext.Empty;

		var orderedLines = profile.Lines.OrderBy(line => line.LineNumber).ToArray();
		var amountKeys = orderedLines
			.Select(line => line.AmountKey?.Trim() ?? string.Empty)
			.Where(value => value.Length > 0)
			.Distinct(StringComparer.Ordinal)
			.OrderBy(value => value, StringComparer.Ordinal)
			.ToArray();

		var nodes = new List<FinancePostingFlowNode>
		{
			new(
				"event",
				FinancePostingFlowNodeKind.BusinessEvent,
				$"{profile.SourceType} / {profile.SourceEvent}",
				profile.Code,
				0,
				0)
		};
		var edges = new List<FinancePostingFlowEdge>();

		for (var index = 0; index < amountKeys.Length; index++)
		{
			var amountKey = amountKeys[index];
			var amountNodeKey = $"amount:{amountKey}";
			nodes.Add(new FinancePostingFlowNode(amountNodeKey, FinancePostingFlowNodeKind.AmountKey, amountKey, "Amount key", 1, index));
			edges.Add(new FinancePostingFlowEdge("event", amountNodeKey));
		}

		for (var index = 0; index < orderedLines.Length; index++)
		{
			var line = orderedLines[index];
			var ruleKey = $"rule:{line.LineNumber}";
			var accountKey = $"account:{line.LineNumber}";
			var amountKey = line.AmountKey?.Trim() ?? string.Empty;
			var amountNodeKey = $"amount:{amountKey}";
			var accountTitle = context.Accounts.TryGetValue(line.AccountId, out var account)
				? $"{account.Number} · {account.Name}"
				: line.AccountId.ToString("D");
			var ruleTitle = line.Direction == FinancePostingDirection.Debit ? "Debit" : "Credit";
			var ruleSubtitle = line.Multiplier == 1m ? null : $"× {line.Multiplier:G29}";

			nodes.Add(new FinancePostingFlowNode(ruleKey, FinancePostingFlowNodeKind.DirectionRule, ruleTitle, ruleSubtitle, 2, index, LineNumber: line.LineNumber));
			nodes.Add(new FinancePostingFlowNode(accountKey, FinancePostingFlowNodeKind.Account, accountTitle, line.Description, 3, index, line.AccountId, line.LineNumber));
			if (amountKey.Length > 0) edges.Add(new FinancePostingFlowEdge(amountNodeKey, ruleKey));
			edges.Add(new FinancePostingFlowEdge(ruleKey, accountKey));
		}

		var journalTitle = context.Journal is null
			? profile.JournalId.ToString("D")
			: $"{context.Journal.Code} · {context.Journal.Name}";
		nodes.Add(new FinancePostingFlowNode("journal", FinancePostingFlowNodeKind.Journal, journalTitle, "Journal", 4, 0, profile.JournalId));
		foreach (var line in orderedLines)
			edges.Add(new FinancePostingFlowEdge($"account:{line.LineNumber}", "journal"));

		return new FinancePostingFlowProjection(profile, nodes, edges, Validate(profile, context));
	}

	public static FinancePostingProfile ApplyLines(
		FinancePostingProfile profile,
		IEnumerable<FinancePostingFlowLineDraft> lines)
	{
		ArgumentNullException.ThrowIfNull(profile);
		ArgumentNullException.ThrowIfNull(lines);

		var projected = lines
			.OrderBy(line => line.LineNumber)
			.Select((line, index) => new FinancePostingProfileLine
			{
				Id = line.Id,
				PostingProfileId = profile.Id,
				LineNumber = index + 1,
				AccountId = line.AccountId,
				Direction = line.Direction,
				AmountKey = line.AmountKey.Trim(),
				Multiplier = line.Multiplier,
				Description = string.IsNullOrWhiteSpace(line.Description) ? null : line.Description.Trim()
			})
			.ToArray();
		return profile with { Lines = projected };
	}

	public static IReadOnlyList<string> RequiredAmountKeys(string sourceType, string sourceEvent)
	{
		if (!string.Equals(sourceEvent, "Posted", StringComparison.Ordinal) &&
			!string.Equals(sourceType, FinanceInventoryAccountingEvents.SourceType, StringComparison.Ordinal))
			return [];

		if (string.Equals(sourceType, FinanceReceivableSourceTypes.SalesInvoice, StringComparison.Ordinal) ||
			string.Equals(sourceType, FinanceReceivableSourceTypes.SalesCreditNote, StringComparison.Ordinal))
			return [FinanceReceivablePostingAmountKeys.Gross, FinanceReceivablePostingAmountKeys.Net, FinanceReceivablePostingAmountKeys.Tax];
		if (string.Equals(sourceType, FinanceReceivableSourceTypes.Payment, StringComparison.Ordinal))
			return [FinanceReceivablePostingAmountKeys.Payment];
		if (string.Equals(sourceType, FinanceReceivableSourceTypes.WriteOff, StringComparison.Ordinal))
			return [FinanceReceivablePostingAmountKeys.WriteOff];

		if (string.Equals(sourceType, FinancePayableSourceTypes.SupplierInvoice, StringComparison.Ordinal) ||
			string.Equals(sourceType, FinancePayableSourceTypes.SupplierCreditNote, StringComparison.Ordinal))
			return [FinancePayablePostingAmountKeys.Gross, FinancePayablePostingAmountKeys.Net, FinancePayablePostingAmountKeys.Tax];
		if (string.Equals(sourceType, FinancePayableSourceTypes.SupplierPayment, StringComparison.Ordinal))
			return [FinancePayablePostingAmountKeys.Payment];

		if (!string.Equals(sourceType, FinanceInventoryAccountingEvents.SourceType, StringComparison.Ordinal))
			return [];
		return sourceEvent switch
		{
			FinanceInventoryAccountingEvents.GoodsReceipt or FinanceInventoryAccountingEvents.SalesShipment or FinanceInventoryAccountingEvents.LandedCost =>
				[FinanceInventoryAccountingAmountKeys.Cost],
			FinanceInventoryAccountingEvents.InventoryAdjustment =>
				[FinanceInventoryAccountingAmountKeys.AdjustmentDebit, FinanceInventoryAccountingAmountKeys.AdjustmentCredit],
			FinanceInventoryAccountingEvents.PurchaseVariance =>
				[FinanceInventoryAccountingAmountKeys.VarianceDebit, FinanceInventoryAccountingAmountKeys.VarianceCredit],
			_ => []
		};
	}

	private static IReadOnlyList<FinancePostingFlowIssue> Validate(
		FinancePostingProfile profile,
		FinancePostingFlowContext context)
	{
		var issues = new List<FinancePostingFlowIssue>();
		var lines = profile.Lines.OrderBy(line => line.LineNumber).ToArray();
		if (lines.Length < 2)
			issues.Add(Error("profile.lines.minimum", "A posting profile requires at least two rules."));

		foreach (var required in RequiredAmountKeys(profile.SourceType, profile.SourceEvent))
		{
			if (!lines.Any(line => string.Equals(line.AmountKey?.Trim(), required, StringComparison.Ordinal)))
				issues.Add(Error("amount.required", $"Required amount key '{required}' is missing."));
		}

		if (lines.Length > 0 && !lines.Any(line => line.Direction == FinancePostingDirection.Debit))
			issues.Add(Error("direction.debit.missing", "At least one debit rule is required."));
		if (lines.Length > 0 && !lines.Any(line => line.Direction == FinancePostingDirection.Credit))
			issues.Add(Error("direction.credit.missing", "At least one credit rule is required."));

		foreach (var line in lines)
		{
			if (string.IsNullOrWhiteSpace(line.AmountKey))
				issues.Add(Error("amount.empty", "Amount key is required.", line.LineNumber));
			if (line.AccountId == Guid.Empty)
				issues.Add(Error("account.empty", "Account is required.", line.LineNumber));
			if (!Enum.IsDefined(line.Direction))
				issues.Add(Error("direction.invalid", "Debit/Credit direction is invalid.", line.LineNumber));
			if (line.Multiplier <= 0m)
				issues.Add(Error("multiplier.invalid", "Multiplier must be greater than zero.", line.LineNumber));

			if (context.Accounts.Count == 0 || line.AccountId == Guid.Empty) continue;
			if (!context.Accounts.TryGetValue(line.AccountId, out var account))
			{
				issues.Add(Error("account.missing", "Selected account is not available for the accounting book.", line.LineNumber));
				continue;
			}
			if (!account.IsActive)
				issues.Add(Error("account.inactive", $"Account '{account.Number}' is inactive.", line.LineNumber));
			if (!account.AllowDirectPosting)
				issues.Add(Error("account.direct-posting", $"Account '{account.Number}' does not allow direct posting.", line.LineNumber));
			if (context.AccountingBook is not null && account.ChartOfAccountsId != context.AccountingBook.ChartOfAccountsId)
				issues.Add(Error("account.chart", $"Account '{account.Number}' is not compatible with the accounting book.", line.LineNumber));
		}

		foreach (var duplicate in lines.GroupBy(
			line => (AmountKey: line.AmountKey?.Trim() ?? string.Empty, line.Direction, line.AccountId, line.Multiplier))
			.Where(group => group.Count() > 1))
		{
			issues.Add(Error(
				"rule.duplicate",
				$"Duplicate {duplicate.Key.Direction} rule for amount key '{duplicate.Key.AmountKey}' and account '{duplicate.Key.AccountId:D}'.",
				duplicate.First().LineNumber));
		}

		if (context.AccountingBook is not null && !context.AccountingBook.IsActive)
			issues.Add(Error("book.inactive", "The accounting book is inactive."));
		if (context.Journal is not null)
		{
			if (!context.Journal.IsActive) issues.Add(Error("journal.inactive", "The journal is inactive."));
			if (context.Journal.AccountingBookId != profile.AccountingBookId)
				issues.Add(Error("journal.book", "The journal does not belong to the selected accounting book."));
		}
		return issues;
	}

	private static FinancePostingFlowIssue Error(string code, string message, int? lineNumber = null) =>
		new(FinancePostingFlowIssueSeverity.Error, code, message, lineNumber);
}
