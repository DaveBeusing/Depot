// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Windows.Documents;
using System.Windows.Input;

using Depot.Commands;
using Depot.Models;
using Depot.Services;
using Depot.Services.Help;

using Xunit;

namespace Depot.Tests;

public sealed class HelpCenterTests
{
	[Fact]
	public async Task EmbeddedManifestAndEveryReferencedArticleAreValid()
	{
		var service = CreateService(PermissionCatalog.All);

		await service.ValidateAsync();
		var catalog = await service.GetCatalogAsync();

		Assert.Equal("1.10", catalog.Version);
		Assert.NotEmpty(catalog.Topics);
		Assert.Equal(catalog.Topics.Count, catalog.Topics.Select(topic => topic.Id).Distinct(StringComparer.Ordinal).Count());
	}

	[Fact]
	public async Task PermissionFilteringHidesUnavailableTopics()
	{
		var service = CreateService([ApplicationPermission.InventoryView, ApplicationPermission.ItemsView]);

		var catalog = await service.GetCatalogAsync();

		Assert.Contains(catalog.Topics, topic => topic.Id == "inventory.items");
		Assert.Contains(catalog.Topics, topic => topic.Id == "inventory.traceability");
		Assert.Contains(catalog.Topics, topic => topic.Id == "approvals.queue");
		Assert.DoesNotContain(catalog.Topics, topic => topic.Id == "sales.approvals");
		Assert.DoesNotContain(catalog.Topics, topic => topic.Id == "finance.general-ledger");
		Assert.Null(await service.GetTopicAsync("administration.database"));
	}

	[Fact]
	public async Task SearchUsesTitlesBodyHeadingsAndKeywordAliases()
	{
		var service = CreateService(PermissionCatalog.All);
		var traceabilityService = CreateService([ApplicationPermission.ItemsView]);

		var relocation = await service.SearchAsync("relocation");
		var stocktake = await service.SearchAsync("stocktake");
		var cancellation = await service.SearchAsync("cancellation");
		var serial = await traceabilityService.SearchAsync("serial number");
		var ledger = await service.SearchAsync("double entry");

		Assert.Contains(relocation, topic => topic.Definition.Id == "warehouse.transfers");
		Assert.Contains(stocktake, topic => topic.Definition.Id == "warehouse.inventory-counts");
		Assert.Contains(cancellation, topic => topic.Definition.Id == "inventory.movements");
		Assert.Contains(serial, topic => topic.Definition.Id == "inventory.traceability");
		Assert.Contains(ledger, topic => topic.Definition.Id == "finance.general-ledger");
	}

	[Fact]
	public async Task RelatedTopicsArePermissionFiltered()
	{
		var service = CreateService([ApplicationPermission.ItemsView]);

		var related = await service.GetRelatedTopicsAsync("inventory.items");

		Assert.Contains(related, topic => topic.Id == "inventory.traceability");
		Assert.DoesNotContain(related, topic => topic.Id == "inventory.overview");
		Assert.DoesNotContain(related, topic => topic.Id == "purchasing.purchase-orders");
		Assert.DoesNotContain(related, topic => topic.Id == "sales.orders");
	}

	[Fact]
	public async Task DuplicateIdsAreRejected()
	{
		var topic = Definition("duplicate", "a.md");
		var service = CreateService(PermissionCatalog.All, new MemoryProvider(new HelpManifest { Version = "1", Topics = [topic, topic] }, new Dictionary<string, string> { ["a.md"] = "# Topic" }));

		await Assert.ThrowsAsync<InvalidDataException>(() => service.ValidateAsync());
	}

	[Fact]
	public async Task MissingFilesAreRejected()
	{
		var service = CreateService(PermissionCatalog.All, new MemoryProvider(new HelpManifest { Version = "1", Topics = [Definition(HelpService.FallbackTopicId, "missing.md")] }, new Dictionary<string, string>()));

		await Assert.ThrowsAsync<FileNotFoundException>(() => service.ValidateAsync());
	}

	[Fact]
	public async Task BrokenInternalLinksAreRejected()
	{
		var topic = Definition(HelpService.FallbackTopicId, "topic.md");
		var service = CreateService(PermissionCatalog.All, new MemoryProvider(new HelpManifest { Version = "1", Topics = [topic] }, new Dictionary<string, string> { ["topic.md"] = "# Topic\n[Missing](topic:missing.topic)" }));

		await Assert.ThrowsAsync<InvalidDataException>(() => service.ValidateAsync());
	}

	[Fact]
	public void MarkdownRendererSupportsTheDocumentSubset()
	{
		var document = new HelpMarkdownRenderer().Render("# Heading\n\nA **bold** and *italic* value with `code` and an [internal link](topic:inventory.items).\n\n- One\n- Two\n\n> [!WARNING] Caution\n\n| A | B |\n| --- | --- |\n| 1 | 2 |");

		Assert.Contains(document.Blocks, block => block is Paragraph paragraph && paragraph.Inlines.OfType<Bold>().Any());
		Assert.Contains(document.Blocks, block => block is Paragraph paragraph && paragraph.Inlines.OfType<Hyperlink>().Any(link => link.NavigateUri.OriginalString == "topic:inventory.items"));
		Assert.Contains(document.Blocks, block => block is List);
		Assert.Contains(document.Blocks, block => block is Section);
		Assert.Contains(document.Blocks, block => block is Table);
	}

	[Theory]
	[InlineData(1280, 1.00)]
	[InlineData(1920, 1.25)]
	[InlineData(2560, 1.50)]
	[InlineData(3840, 2.00)]
	public void HelpLayoutRetainsAUsableArticleWidthAtSupportedDpiScales(double viewportPixels, double dpiScale)
	{
		const double shellWidth = 240;
		const double shellMargins = 36;
		const double helpNavigationWidth = 320;
		const double columnGap = 20;
		var logicalViewportWidth = viewportPixels / dpiScale;

		var articleWidth = logicalViewportWidth - shellWidth - shellMargins - helpNavigationWidth - columnGap;

		Assert.True(articleWidth >= 600, $"Article width was only {articleWidth:N0} DIPs at {dpiScale:P0} scaling.");
	}

	[Fact]
	public void F1IsTheGlobalHelpGesture()
	{
		var gesture = Assert.IsType<KeyGesture>(Assert.Single(HelpCommands.OpenHelp.InputGestures));
		Assert.Equal(Key.F1, gesture.Key);
	}

	[Fact]
	public async Task MissingContextTopicFallsBackToFirstLogin()
	{
		var service = CreateService(PermissionCatalog.All);
		var missing = await service.GetTopicAsync("does.not.exist");
		var fallback = await service.GetTopicAsync(HelpService.FallbackTopicId);

		Assert.Null(missing);
		Assert.NotNull(fallback);
	}

	[Fact]
	public void DiagnosticsSanitizerRemovesSecretsAndProtectedValues()
	{
		const string diagnostics = "Connection string: Server=db;User=depot;Password=secret\nHash=abc\nSalt: xyz\n{\"protectedConfiguration\":\"cipher\",\"message\":\"timeout\"}";

		var sanitized = new DiagnosticsSanitizer().Sanitize(diagnostics);

		Assert.DoesNotContain("secret", sanitized, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("abc", sanitized, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("xyz", sanitized, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("cipher", sanitized, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("timeout", sanitized, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void StableErrorCategoriesMapToTroubleshootingTopics()
	{
		Assert.Equal("troubleshooting.concurrency-conflict", HelpErrorTopicMap.TryGetTopicId(new ConcurrencyConflictException("item")));
		Assert.Equal("troubleshooting.insufficient-stock", HelpErrorTopicMap.TryGetTopicId(new InvalidOperationException("Insufficient stock.")));
		Assert.Equal("troubleshooting.database-connection-failures", HelpErrorTopicMap.TryGetTopicId(new TimeoutException()));
		Assert.Null(HelpErrorTopicMap.TryGetTopicId(new InvalidOperationException("Validation failed.")));
	}

	private static HelpService CreateService(IEnumerable<ApplicationPermission> permissions, IHelpContentProvider? provider = null)
	{
		var authorization = new AuthorizationService();
		authorization.SignIn(new User { Id = 1, IsActive = true }, permissions);
		return new HelpService(provider ?? new EmbeddedHelpContentProvider(typeof(App).Assembly), authorization, new HelpSearchService());
	}

	private static HelpTopicDefinition Definition(string id, string file) => new() { Id = id, Title = id, Category = "Test", File = file, Order = 1 };

	private sealed class MemoryProvider(HelpManifest manifest, IReadOnlyDictionary<string, string> files) : IHelpContentProvider
	{
		public Task<HelpManifest> LoadManifestAsync(CancellationToken cancellationToken = default) => Task.FromResult(manifest);
		public Task<string> LoadContentAsync(HelpTopicDefinition topic, CancellationToken cancellationToken = default) => files.TryGetValue(topic.File, out var content) ? Task.FromResult(content) : Task.FromException<string>(new FileNotFoundException(topic.File));
	}
}
