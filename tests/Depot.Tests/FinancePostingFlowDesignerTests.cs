// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

using Xunit;

namespace Depot.Tests;

public sealed class FinancePostingFlowDesignerTests
{
	[Fact]
	public void ProjectionRepresentsExistingProfileAsDeterministicNodesAndEdges()
	{
		var chartId = Guid.NewGuid();
		var legalEntityId = Guid.NewGuid();
		var bookId = Guid.NewGuid();
		var journalId = Guid.NewGuid();
		var debitId = Guid.NewGuid();
		var revenueId = Guid.NewGuid();
		var taxId = Guid.NewGuid();
		var book = new AccountingBook(bookId, legalEntityId, chartId, "PRIMARY", "Primary", new CurrencyCode("USD"), "TEST", true, true);
		var journal = new JournalDefinition(journalId, bookId, "AR", "Accounts Receivable", true);
		var accounts = new[]
		{
			new FinanceAccount(debitId, chartId, "1100", "Receivables", FinanceAccountType.Asset),
			new FinanceAccount(revenueId, chartId, "4000", "Revenue", FinanceAccountType.Revenue),
			new FinanceAccount(taxId, chartId, "2100", "Output tax", FinanceAccountType.Liability)
		};
		var profile = new FinancePostingProfile
		{
			Id = 42,
			Version = 3,
			LegalEntityId = legalEntityId,
			AccountingBookId = bookId,
			JournalId = journalId,
			Code = "AR-INVOICE",
			Name = "AR invoice",
			SourceType = FinanceReceivableSourceTypes.SalesInvoice,
			SourceEvent = "Posted",
			NumberSequenceCode = "GL",
			Lines =
			[
				new FinancePostingProfileLine { Id = 1, PostingProfileId = 42, LineNumber = 1, AccountId = debitId, Direction = FinancePostingDirection.Debit, AmountKey = FinanceReceivablePostingAmountKeys.Gross },
				new FinancePostingProfileLine { Id = 2, PostingProfileId = 42, LineNumber = 2, AccountId = revenueId, Direction = FinancePostingDirection.Credit, AmountKey = FinanceReceivablePostingAmountKeys.Net },
				new FinancePostingProfileLine { Id = 3, PostingProfileId = 42, LineNumber = 3, AccountId = taxId, Direction = FinancePostingDirection.Credit, AmountKey = FinanceReceivablePostingAmountKeys.Tax }
			]
		};
		var context = new FinancePostingFlowContext(book, journal, accounts.ToDictionary(account => account.Id), [journal]);

		var projection = FinancePostingFlowProjector.Project(profile, context);

		Assert.True(projection.IsValid);
		Assert.Equal(11, projection.Nodes.Count);
		Assert.Equal(12, projection.Edges.Count);
		Assert.Single(projection.Nodes, node => node.Kind == FinancePostingFlowNodeKind.BusinessEvent);
		Assert.Equal(3, projection.Nodes.Count(node => node.Kind == FinancePostingFlowNodeKind.AmountKey));
		Assert.Equal(3, projection.Nodes.Count(node => node.Kind == FinancePostingFlowNodeKind.DirectionRule));
		Assert.Equal(3, projection.Nodes.Count(node => node.Kind == FinancePostingFlowNodeKind.Account));
		Assert.Single(projection.Nodes, node => node.Kind == FinancePostingFlowNodeKind.Journal);
		Assert.Contains(projection.Edges, edge => edge.SourceKey == "event" && edge.TargetKey == "amount:Gross");
		Assert.Contains(projection.Edges, edge => edge.SourceKey == "rule:1" && edge.TargetKey == "account:1");
		Assert.Contains(projection.Edges, edge => edge.SourceKey == "account:3" && edge.TargetKey == "journal");
	}

	[Fact]
	public void VisualLineEditProjectsBackToPostingProfileWithoutAlternativeExecutionModel()
	{
		var profile = CreateSimpleProfile();
		var edited = FinancePostingFlowProjector.ApplyLines(
			profile,
			[
				new FinancePostingFlowLineDraft(22, 20, profile.Lines[1].AccountId, FinancePostingDirection.Credit, "NET", 2m, "Credit edit"),
				new FinancePostingFlowLineDraft(11, 10, profile.Lines[0].AccountId, FinancePostingDirection.Debit, "GROSS", 1m, "Debit edit")
			]);

		Assert.IsType<FinancePostingProfile>(edited);
		Assert.Equal([1, 2], edited.Lines.Select(line => line.LineNumber).ToArray());
		Assert.Equal(["GROSS", "NET"], edited.Lines.Select(line => line.AmountKey).ToArray());
		Assert.Equal([profile.Lines[0].AccountId, profile.Lines[1].AccountId], edited.Lines.Select(line => line.AccountId).ToArray());
		Assert.Equal(2m, edited.Lines[1].Multiplier);
	}

	[Fact]
	public void ValidationBlocksMissingRequiredKeysDuplicateRulesAndInvalidAccounts()
	{
		var chartId = Guid.NewGuid();
		var otherChartId = Guid.NewGuid();
		var accountId = Guid.NewGuid();
		var profile = CreateSimpleProfile(
			FinanceReceivableSourceTypes.SalesInvoice,
			"Posted",
			[
				new FinancePostingProfileLine { LineNumber = 1, AccountId = accountId, Direction = FinancePostingDirection.Debit, AmountKey = FinanceReceivablePostingAmountKeys.Gross },
				new FinancePostingProfileLine { LineNumber = 2, AccountId = accountId, Direction = FinancePostingDirection.Debit, AmountKey = FinanceReceivablePostingAmountKeys.Gross }
			]);
		var book = new AccountingBook(profile.AccountingBookId, profile.LegalEntityId, chartId, "PRIMARY", "Primary", new CurrencyCode("USD"), "TEST", true, true);
		var journal = new JournalDefinition(profile.JournalId, profile.AccountingBookId, "AR", "AR", true);
		var invalidAccount = new FinanceAccount(accountId, otherChartId, "9999", "Invalid", FinanceAccountType.Asset, allowDirectPosting: false, isActive: false);
		var context = new FinancePostingFlowContext(book, journal, new Dictionary<Guid, FinanceAccount> { [accountId] = invalidAccount }, [journal]);

		var projection = FinancePostingFlowProjector.Project(profile, context);
		var codes = projection.Issues.Select(issue => issue.Code).ToHashSet(StringComparer.Ordinal);

		Assert.False(projection.IsValid);
		Assert.Contains("amount.required", codes);
		Assert.Contains("direction.credit.missing", codes);
		Assert.Contains("rule.duplicate", codes);
		Assert.Contains("account.inactive", codes);
		Assert.Contains("account.direct-posting", codes);
		Assert.Contains("account.chart", codes);
	}

	[Fact]
	public void DesignerContractUsesServiceBoundaryKeyboardFallbackAndAccessibleCustomCanvas()
	{
		var root = FindRepositoryRoot();
		var viewModel = File.ReadAllText(Path.Combine(root, "src", "Depot", "ViewModels", "FinancePostingFlowDesignerViewModel.cs"));
		var view = File.ReadAllText(Path.Combine(root, "src", "Depot", "Views", "FinancePostingFlowDesignerView.xaml"));
		var codeBehind = File.ReadAllText(Path.Combine(root, "src", "Depot", "Views", "FinancePostingFlowDesignerView.xaml.cs"));
		var resources = File.ReadAllText(Path.Combine(root, "src", "Depot", "Resources", "FinancePostingFlow.xaml"));
		var model = File.ReadAllText(Path.Combine(root, "src", "Depot", "Models", "FinancePostingFlow.cs"));
		var main = File.ReadAllText(Path.Combine(root, "src", "Depot", "ViewModels", "MainViewModel.cs"));

		Assert.Contains("_ledger.SavePostingProfileAsync", viewModel, StringComparison.Ordinal);
		Assert.Contains("_ledger.CanManagePostingProfiles", viewModel, StringComparison.Ordinal);
		Assert.DoesNotContain("FinancePostingProfileRepository", viewModel, StringComparison.Ordinal);
		Assert.DoesNotContain("DatabaseAccess", viewModel, StringComparison.Ordinal);
		Assert.DoesNotContain("INSERT INTO", viewModel, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("FinancePostingFlowCanvas", view, StringComparison.Ordinal);
		Assert.Contains("AllowDrop=\"True\"", view, StringComparison.Ordinal);
		Assert.Contains("Add selected rule", view, StringComparison.Ordinal);
		Assert.Contains("AutomationProperties.Name=\"Posting flow canvas\"", view, StringComparison.Ordinal);
		Assert.Contains("AutomationProperties.Name=\"Posting rule palette\"", view, StringComparison.Ordinal);
		Assert.Contains("TryAddRule(item)", codeBehind, StringComparison.Ordinal);
		Assert.Contains("KeyboardNavigation.DirectionalNavigation", resources, StringComparison.Ordinal);
		Assert.Contains("MotionBehavior.TransitionKind=\"State\"", resources, StringComparison.Ordinal);
		Assert.Contains("public double CanvasLeft =>", model, StringComparison.Ordinal);
		Assert.DoesNotContain("CanvasLeft { get; init;", model, StringComparison.Ordinal);
		Assert.Contains("ApplicationPermission.FinancePostingProfilesView, \"Posting Flow Designer\"", main, StringComparison.Ordinal);
	}

	private static FinancePostingProfile CreateSimpleProfile(
		string sourceType = "TestDocument",
		string sourceEvent = "Posted",
		IReadOnlyList<FinancePostingProfileLine>? lines = null)
	{
		var legalEntityId = Guid.NewGuid();
		var bookId = Guid.NewGuid();
		var journalId = Guid.NewGuid();
		var debitId = Guid.NewGuid();
		var creditId = Guid.NewGuid();
		return new FinancePostingProfile
		{
			Id = 10,
			Version = 2,
			LegalEntityId = legalEntityId,
			AccountingBookId = bookId,
			JournalId = journalId,
			Code = "TEST",
			Name = "Test",
			SourceType = sourceType,
			SourceEvent = sourceEvent,
			NumberSequenceCode = "GL",
			Lines = lines ??
			[
				new FinancePostingProfileLine { Id = 11, PostingProfileId = 10, LineNumber = 1, AccountId = debitId, Direction = FinancePostingDirection.Debit, AmountKey = "TOTAL" },
				new FinancePostingProfileLine { Id = 22, PostingProfileId = 10, LineNumber = 2, AccountId = creditId, Direction = FinancePostingDirection.Credit, AmountKey = "TOTAL" }
			]
		};
	}

	private static string FindRepositoryRoot()
	{
		for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
			if (File.Exists(Path.Combine(directory.FullName, "Depot.slnx"))) return directory.FullName;
		throw new DirectoryNotFoundException("Could not locate the Depot repository root.");
	}
}
