// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Xunit;

namespace Depot.Tests;

public sealed class FinanceBankReconciliationDesignerContractTests
{
	[Fact]
	public void DesignerUsesExistingBankingWorkspaceCustomControlsAndBoundedLists()
	{
		var root = FindRepositoryRoot();
		var view = File.ReadAllText(Path.Combine(root, "src", "Depot", "Views", "FinanceBankingView.xaml"));

		Assert.Contains("Header=\"Reconciliation Designer\"", view, StringComparison.Ordinal);
		Assert.Contains("ReconciliationDesignerLines", view, StringComparison.Ordinal);
		Assert.Contains("ReconciliationCandidates", view, StringComparison.Ordinal);
		Assert.Contains("PreviousReconciliationPageCommand", view, StringComparison.Ordinal);
		Assert.Contains("NextReconciliationPageCommand", view, StringComparison.Ordinal);
		Assert.Contains("VirtualizingPanel.VirtualizationMode=\"Recycling\"", view, StringComparison.Ordinal);
		Assert.Contains("WorkspaceSectionStyle", view, StringComparison.Ordinal);
		Assert.Contains("AppDataGridCompactStyle", view, StringComparison.Ordinal);
		Assert.Contains("controls:TextInput", view, StringComparison.Ordinal);
		Assert.Contains("Use selected target", view, StringComparison.Ordinal);
		Assert.Contains("Content=\"Match\"", view, StringComparison.Ordinal);
	}

	[Fact]
	public void DragAndSelectionOnlyChoosePreviewWhileMatchUsesExistingRequestAndService()
	{
		var root = FindRepositoryRoot();
		var codeBehind = File.ReadAllText(Path.Combine(root, "src", "Depot", "Views", "FinanceBankingView.xaml.cs"));
		var viewModel = File.ReadAllText(Path.Combine(root, "src", "Depot", "ViewModels", "FinanceBankingViewModel.ReconciliationDesigner.cs"));

		Assert.Contains("UseReconciliationCandidate(candidate)", codeBehind, StringComparison.Ordinal);
		Assert.DoesNotContain("ReconcileAsync", codeBehind, StringComparison.Ordinal);
		Assert.DoesNotContain("ReverseReconciliationAsync", codeBehind, StringComparison.Ordinal);

		var selectionStart = viewModel.IndexOf("public bool UseReconciliationCandidate", StringComparison.Ordinal);
		var selectionEnd = viewModel.IndexOf("private async Task MatchReconciliationCandidateAsync", selectionStart, StringComparison.Ordinal);
		var selection = viewModel[selectionStart..selectionEnd];
		Assert.Contains("PreviewReconciliationCandidate = candidate", selection, StringComparison.Ordinal);
		Assert.DoesNotContain("_banking.", selection, StringComparison.Ordinal);

		Assert.Contains("candidate.CreateRequest(Guid.NewGuid(), line.Id)", viewModel, StringComparison.Ordinal);
		Assert.Contains("_banking.ReconcileAsync(request, token)", viewModel, StringComparison.Ordinal);
		Assert.Contains("_banking.ReverseReconciliationAsync", viewModel, StringComparison.Ordinal);
	}

	[Fact]
	public void ReversalHistoryIsVisuallyDistinctAndReadOnly()
	{
		var root = FindRepositoryRoot();
		var view = File.ReadAllText(Path.Combine(root, "src", "Depot", "Views", "FinanceBankingView.xaml"));
		var model = File.ReadAllText(Path.Combine(root, "src", "Depot", "Models", "FinanceBankReconciliationDesigner.cs"));

		Assert.Contains("StrokeDashArray", view, StringComparison.Ordinal);
		Assert.Contains("Binding=\"{Binding IsReversed}\" Value=\"True\"", view, StringComparison.Ordinal);
		Assert.Contains("NavigationAccentBrush", view, StringComparison.Ordinal);
		Assert.Contains("Reverse selected active match", view, StringComparison.Ordinal);
		Assert.Contains("public bool IsReadOnly => IsReversed", model, StringComparison.Ordinal);
	}

	[Fact]
	public void DesignerDoesNotIntroduceConnectivityOrAlternatePersistence()
	{
		var root = FindRepositoryRoot();
		var viewModel = File.ReadAllText(Path.Combine(root, "src", "Depot", "ViewModels", "FinanceBankingViewModel.ReconciliationDesigner.cs"));
		var model = File.ReadAllText(Path.Combine(root, "src", "Depot", "Models", "FinanceBankReconciliationDesigner.cs"));

		Assert.DoesNotContain("DatabaseAccess", viewModel, StringComparison.Ordinal);
		Assert.DoesNotContain("FinanceBankingRepository", viewModel, StringComparison.Ordinal);
		Assert.DoesNotContain("INSERT INTO", viewModel, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("UPDATE ", viewModel, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("PSD2", viewModel, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("PSD2", model, StringComparison.OrdinalIgnoreCase);
	}

	private static string FindRepositoryRoot()
	{
		for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
			if (File.Exists(Path.Combine(directory.FullName, "Depot.slnx"))) return directory.FullName;
		throw new DirectoryNotFoundException("Could not locate the Depot repository root.");
	}
}
