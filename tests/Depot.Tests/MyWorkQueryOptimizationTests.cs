// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Xunit;

namespace Depot.Tests;

public sealed class MyWorkQueryOptimizationTests
{
	[Fact]
	public void PayablesProviderUsesBoundedProjectionWithoutDocumentDetailLoads()
	{
		var root = FindRepositoryRoot();
		var providers = File.ReadAllText(Path.Combine(root, "src", "Depot", "Services", "MyWorkProviders.cs"));
		var repository = File.ReadAllText(Path.Combine(root, "src", "Depot", "Repositories", "FinanceAccountsPayableRepository.cs"));
		var start = providers.IndexOf("internal sealed class PayablesMyWorkProvider", StringComparison.Ordinal);
		var end = providers.IndexOf("internal sealed class BankingMyWorkProvider", start, StringComparison.Ordinal);
		var payablesProvider = providers[start..end];

		Assert.Contains("GetMyWorkDocumentsAsync", payablesProvider, StringComparison.Ordinal);
		Assert.DoesNotContain("GetDocumentAsync", payablesProvider, StringComparison.Ordinal);
		Assert.Contains("QuerySliceAsync", repository, StringComparison.Ordinal);
		Assert.Contains("EXISTS (", repository, StringComparison.Ordinal);
		Assert.Contains("line.MatchStatus = $ExceptionStatus", repository, StringComparison.Ordinal);
	}

	[Fact]
	public void PayablesProjectionContainsOnlyMyWorkFields()
	{
		var root = FindRepositoryRoot();
		var models = File.ReadAllText(Path.Combine(root, "src", "Depot", "Models", "FinanceAccountsPayable.cs"));
		var start = models.IndexOf("public sealed record FinancePayablesMyWorkDocument", StringComparison.Ordinal);
		var end = models.IndexOf("public sealed record FinanceSupplierDocumentLine", start, StringComparison.Ordinal);
		var projection = models[start..end];

		Assert.Contains("HasMatchExceptions", projection, StringComparison.Ordinal);
		Assert.Contains("MatchExceptionApproved", projection, StringComparison.Ordinal);
		Assert.DoesNotContain("Lines", projection, StringComparison.Ordinal);
		Assert.DoesNotContain("ApprovalComment", projection, StringComparison.Ordinal);
		Assert.DoesNotContain("InternalReference", projection, StringComparison.Ordinal);
	}

	private static string FindRepositoryRoot()
	{
		for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
			if (File.Exists(Path.Combine(directory.FullName, "Depot.slnx"))) return directory.FullName;
		throw new DirectoryNotFoundException("Repository root could not be located.");
	}
}
