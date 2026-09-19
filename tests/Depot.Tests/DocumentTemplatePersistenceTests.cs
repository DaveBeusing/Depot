// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Xunit;

using Depot.Data;
using Depot.DocumentRendering;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;

namespace Depot.Tests;

public sealed class DocumentTemplatePersistenceTests : IDisposable
{
	private readonly string _path = Path.Combine(Path.GetTempPath(), $"depot-document-templates-{Guid.NewGuid():N}.db");

	[Fact]
	public void MigrationCreatesFeatureSchemaAndIsIdempotent()
	{
		var factory = new SqliteConnectionFactory(_path);
		new DepotDatabase(factory).Initialize();
		DocumentTemplateSchemaMigration.Migrate(factory);
		DocumentTemplateSchemaMigration.Migrate(factory);

		using var connection = factory.CreateConnection();
		connection.Open();
		using var version = connection.CreateCommand();
		version.CommandText = "SELECT Version FROM DepotFeatureVersions WHERE Name='DocumentTemplates';";
		Assert.Equal(DocumentTemplateSchemaMigration.CurrentVersion, Convert.ToInt32(version.ExecuteScalar()));
		using var table = connection.CreateCommand();
		table.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='DocumentTemplates';";
		Assert.Equal(1, Convert.ToInt32(table.ExecuteScalar()));
	}

	[Fact]
	public void DraftActivationAndDefaultResetSurviveCatalogRestart()
	{
		var repository = CreateRepository();
		var first = new DocumentTemplateRuntimeCatalog(DefaultDocumentTemplates.Defaults);
		first.AttachStore(repository);
		var source = first.GetDefault(DocumentTemplateType.SalesInvoice);
		var draft = first.SaveDraft(source with { Id = "sales-invoice-persistent", Version = 0, IsActive = false });

		Assert.Equal(2, draft.Version);
		Assert.False(draft.IsActive);
		Assert.True(first.Activate(DocumentTemplateType.SalesInvoice, draft.Version).IsActive);

		var restarted = new DocumentTemplateRuntimeCatalog(DefaultDocumentTemplates.Defaults);
		restarted.AttachStore(new DocumentTemplateRepository(CreateDatabase()));
		Assert.Equal(2, restarted.Snapshot.GetActive(DocumentTemplateType.SalesInvoice).Version);
		Assert.Equal(2, restarted.ListVersions(DocumentTemplateType.SalesInvoice).Count);

		restarted.ResetToDefault(DocumentTemplateType.SalesInvoice);
		var afterReset = new DocumentTemplateRuntimeCatalog(DefaultDocumentTemplates.Defaults);
		afterReset.AttachStore(new DocumentTemplateRepository(CreateDatabase()));
		Assert.Equal(1, afterReset.Snapshot.GetActive(DocumentTemplateType.SalesInvoice).Version);
		Assert.Equal(2, afterReset.ListVersions(DocumentTemplateType.SalesInvoice).Count);
	}

	[Fact]
	public void EverySupportedDocumentTypeHasPersistentDefaultAndSafePreview()
	{
		var repository = CreateRepository();
		var runtime = new DocumentTemplateRuntimeCatalog(DefaultDocumentTemplates.Defaults);
		runtime.AttachStore(repository);
		var authorization = new AuthorizationService();
		authorization.SignIn(
			new User { Id = 501, Email = "template-test@example.invalid", DisplayName = "Template Test", IsActive = true },
			[ApplicationPermission.DocumentTemplatesView, ApplicationPermission.DocumentTemplatesManage]);
		var designer = new DocumentTemplateDesignerService(authorization, runtime);

		foreach (var type in Enum.GetValues<DocumentTemplateType>())
		{
			var active = designer.ListVersions(type).Single(value => value.IsActive);
			Assert.Equal(1, active.Version);
			var preview = designer.Preview(active);
			Assert.True(preview.Length > 4);
			Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(preview, 0, 4));
		}
	}

	public void Dispose()
	{
		try { if (File.Exists(_path)) File.Delete(_path); }
		catch (IOException) { }
	}

	private DocumentTemplateRepository CreateRepository()
	{
		var factory = new SqliteConnectionFactory(_path);
		new DepotDatabase(factory).Initialize();
		DocumentTemplateSchemaMigration.Migrate(factory);
		return new DocumentTemplateRepository(new DatabaseAccess(factory));
	}

	private DatabaseAccess CreateDatabase() => new(new SqliteConnectionFactory(_path));
}
