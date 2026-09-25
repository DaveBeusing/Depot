// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class BusinessAttachmentTests : IAsyncLifetime
{
	private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"depot-attachments-{Guid.NewGuid():N}.db");
	private AttachmentFixture? _fixture;

	[Fact]
	public async Task FeatureSchemaCreatesVersionedAttachmentTables()
	{
		Assert.Equal(BusinessAttachmentSchemaMigration.CurrentVersion, await Fixture.ScalarAsync("SELECT Version FROM DepotFeatureVersions WHERE Name='BusinessAttachments';"));
		Assert.Equal(1L, await Fixture.ScalarAsync("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='BusinessAttachments';"));
		Assert.Equal(1L, await Fixture.ScalarAsync("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='BusinessAttachmentRevisions';"));
		Assert.Equal(1L, await Fixture.ScalarAsync("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='BusinessAttachmentContents';"));
	}

	[Fact]
	public async Task VersionOneSchemaMigratesExpandedEntityKindsWithoutDataLoss()
	{
		var path = Path.Combine(Path.GetTempPath(), $"depot-attachments-v1-{Guid.NewGuid():N}.db");
		try
		{
			var factory = new SqliteConnectionFactory(path);
			var database = new DatabaseAccess(factory);
			var existingId = Guid.NewGuid().ToString("D");
			await database.ExecuteAsync(
				"""
				CREATE TABLE DepotFeatureVersions (Name TEXT PRIMARY KEY, Version INTEGER NOT NULL);
				INSERT INTO DepotFeatureVersions (Name,Version) VALUES ('BusinessAttachments',1);
				CREATE TABLE BusinessAttachments
				(
					Id TEXT NOT NULL PRIMARY KEY,
					EntityKind INTEGER NOT NULL,
					EntityId INTEGER NOT NULL,
					FileName TEXT NOT NULL,
					MediaType TEXT NOT NULL,
					ByteLength INTEGER NOT NULL,
					Sha256 TEXT NOT NULL,
					Description TEXT NULL,
					Category TEXT NULL,
					CreatedByUserId INTEGER NULL,
					CreatedAtUtc TEXT NOT NULL,
					CurrentRevision INTEGER NOT NULL,
					Status INTEGER NOT NULL,
					Version INTEGER NOT NULL,
					CHECK (EntityKind BETWEEN 1 AND 9),
					CHECK (ByteLength >= 0),
					CHECK (CurrentRevision > 0),
					CHECK (Status IN (1,2)),
					CHECK (Version > 0)
				);
				CREATE TABLE BusinessAttachmentRevisions
				(
					AttachmentId TEXT NOT NULL,
					Revision INTEGER NOT NULL,
					PRIMARY KEY (AttachmentId,Revision),
					FOREIGN KEY (AttachmentId) REFERENCES BusinessAttachments(Id) ON DELETE RESTRICT
				);
				INSERT INTO BusinessAttachments
					(Id,EntityKind,EntityId,FileName,MediaType,ByteLength,Sha256,CreatedAtUtc,CurrentRevision,Status,Version)
				VALUES
					($Id,1,1,'existing.txt','text/plain',1,$Hash,'2026-09-24T12:00:00Z',1,1,1);
				INSERT INTO BusinessAttachmentRevisions (AttachmentId,Revision) VALUES ($Id,1);
				""",
				CancellationToken.None,
				new DatabaseParameter("$Id", existingId),
				new DatabaseParameter("$Hash", new string('A', 64)));

			BusinessAttachmentSchemaMigration.Migrate(factory);

			Assert.Equal(BusinessAttachmentSchemaMigration.CurrentVersion, Convert.ToInt32(await database.ExecuteScalarAsync("SELECT Version FROM DepotFeatureVersions WHERE Name='BusinessAttachments';", CancellationToken.None)));
			Assert.Equal(1L, Convert.ToInt64(await database.ExecuteScalarAsync("SELECT COUNT(*) FROM BusinessAttachments WHERE Id=$Id;", CancellationToken.None, new DatabaseParameter("$Id", existingId))));
			Assert.Equal(0L, Convert.ToInt64(await database.ExecuteScalarAsync("SELECT COUNT(*) FROM pragma_foreign_key_check;", CancellationToken.None)));

			await database.ExecuteAsync(
				"""
				INSERT INTO BusinessAttachments
					(Id,EntityKind,EntityId,FileName,MediaType,ByteLength,Sha256,CreatedAtUtc,CurrentRevision,Status,Version)
				VALUES
					($Id,$EntityKind,42,'project.txt','text/plain',1,$Hash,'2026-09-24T12:00:00Z',1,1,1);
				""",
				CancellationToken.None,
				new DatabaseParameter("$Id", Guid.NewGuid().ToString("D")),
				new DatabaseParameter("$EntityKind", (int)BusinessAttachmentEntityKind.SubscriptionContract),
				new DatabaseParameter("$Hash", new string('B', 64)));
		}
		finally
		{
			SqliteConnection.ClearAllPools();
			if (File.Exists(path)) File.Delete(path);
		}
	}

	[Fact]
	public async Task UploadDownloadAndReplacePreserveImmutableRevisionContent()
	{
		var customer = await Fixture.CreateCustomerAsync("Attachment Customer");
		var firstBytes = "first attachment content"u8.ToArray();
		var attachment = await Fixture.Service.AddAsync(
			BusinessAttachmentEntityKind.Customer,
			customer.Id,
			"evidence.txt",
			"text/plain",
			new MemoryStream(firstBytes),
			"Initial evidence",
			"Evidence");

		var opened = await Fixture.Service.OpenAsync(attachment.Id);
		using (opened.Content)
		using (var copy = new MemoryStream())
		{
			await opened.Content.CopyToAsync(copy);
			Assert.Equal(firstBytes, copy.ToArray());
		}

		var secondBytes = "replacement attachment content"u8.ToArray();
		var replaced = await Fixture.Service.ReplaceAsync(
			attachment.Id,
			attachment.Version,
			"evidence-v2.txt",
			"text/plain",
			new MemoryStream(secondBytes));

		Assert.Equal(2, replaced.CurrentRevision);
		Assert.Equal(2, replaced.Version);
		var revisions = await Fixture.Service.ListRevisionsAsync(attachment.Id);
		Assert.Equal(2, revisions.Count);
		Assert.Contains(revisions, value => value.Revision == 1 && value.Sha256 == attachment.Sha256);
		Assert.Contains(revisions, value => value.Revision == 2 && value.Sha256 == replaced.Sha256);

		var original = await Fixture.Service.OpenAsync(attachment.Id, 1);
		using (original.Content)
		using (var originalCopy = new MemoryStream())
		{
			await original.Content.CopyToAsync(originalCopy);
			Assert.Equal(firstBytes, originalCopy.ToArray());
		}

		var current = await Fixture.Service.OpenAsync(attachment.Id);
		using (current.Content)
		using (var currentCopy = new MemoryStream())
		{
			await current.Content.CopyToAsync(currentCopy);
			Assert.Equal(secondBytes, currentCopy.ToArray());
		}
	}

	[Fact]
	public async Task UploadRejectsOversizedBlockedAndMissingEntityInputs()
	{
		var customer = await Fixture.CreateCustomerAsync("Validation Customer");

		await Assert.ThrowsAsync<InvalidDataException>(() =>
			Fixture.Service.AddAsync(
				BusinessAttachmentEntityKind.Customer,
				customer.Id,
				"blocked.exe",
				"application/octet-stream",
				new MemoryStream([1, 2, 3])));

		await Assert.ThrowsAsync<InvalidDataException>(() =>
			Fixture.Service.AddAsync(
				BusinessAttachmentEntityKind.Customer,
				customer.Id,
				"large.bin",
				"application/octet-stream",
				new MemoryStream(new byte[BusinessAttachmentService.MaxAttachmentBytes + 1])));

		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			Fixture.Service.AddAsync(
				BusinessAttachmentEntityKind.Customer,
				long.MaxValue,
				"missing.txt",
				"text/plain",
				new MemoryStream([1])));
	}

	[Fact]
	public async Task MetadataAndReplacementUseOptimisticConcurrency()
	{
		var customer = await Fixture.CreateCustomerAsync("Concurrency Customer");
		var attachment = await Fixture.Service.AddAsync(
			BusinessAttachmentEntityKind.Customer,
			customer.Id,
			"contract.pdf",
			"application/pdf",
			new MemoryStream([1, 2, 3, 4]));

		var updated = await Fixture.Service.UpdateMetadataAsync(
			attachment.Id,
			new BusinessAttachmentMetadataUpdate("Signed contract", "Contract", attachment.Version));
		Assert.Equal(2, updated.Version);

		await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
			Fixture.Service.UpdateMetadataAsync(
				attachment.Id,
				new BusinessAttachmentMetadataUpdate("Stale", null, attachment.Version)));

		await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
			Fixture.Service.ReplaceAsync(
				attachment.Id,
				attachment.Version,
				"stale.pdf",
				"application/pdf",
				new MemoryStream([9])));
	}

	[Fact]
	public async Task RetireAndPermissionsAreEnforcedAtServiceBoundary()
	{
		var customer = await Fixture.CreateCustomerAsync("Permission Customer");
		var attachment = await Fixture.Service.AddAsync(
			BusinessAttachmentEntityKind.Customer,
			customer.Id,
			"note.txt",
			"text/plain",
			new MemoryStream([1, 2]));

		var readOnly = Fixture.CreateService(ApplicationPermission.CustomersView);
		Assert.Single(await readOnly.ListAsync(BusinessAttachmentEntityKind.Customer, customer.Id));
		await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
			readOnly.ReplaceAsync(
				attachment.Id,
				attachment.Version,
				"new.txt",
				"text/plain",
				new MemoryStream([3])));

		var retired = await Fixture.Service.RetireAsync(attachment.Id, attachment.Version);
		Assert.Equal(BusinessAttachmentStatus.Retired, retired.Status);
		Assert.Empty(await Fixture.Service.ListAsync(BusinessAttachmentEntityKind.Customer, customer.Id));
		Assert.Single(await Fixture.Service.ListAsync(BusinessAttachmentEntityKind.Customer, customer.Id, includeRetired: true));
	}

	[Fact]
	public async Task AttachmentMutationsProduceAuditEvidence()
	{
		var customer = await Fixture.CreateCustomerAsync("Audit Customer");
		var attachment = await Fixture.Service.AddAsync(
			BusinessAttachmentEntityKind.Customer,
			customer.Id,
			"audit.txt",
			"text/plain",
			new MemoryStream([1]));
		var updated = await Fixture.Service.UpdateMetadataAsync(
			attachment.Id,
			new BusinessAttachmentMetadataUpdate("Reviewed", "Audit", attachment.Version));
		await Fixture.Service.RetireAsync(updated.Id, updated.Version);

		var history = await Fixture.Audit.GetEntityHistoryAsync(nameof(BusinessAttachment), customer.Id, 20, CancellationToken.None);

		Assert.Contains(history, entry => entry.Action == "AttachmentAdded");
		Assert.Contains(history, entry => entry.Action == "AttachmentMetadataUpdated");
		Assert.Contains(history, entry => entry.Action == "AttachmentRetired");
	}

	public async Task InitializeAsync()
	{
		var factory = new SqliteConnectionFactory(_databasePath);
		new DepotDatabase(factory).Initialize();
		SalesSchemaMigration.Migrate(factory);
		BusinessAttachmentSchemaMigration.Migrate(factory);
		_fixture = await AttachmentFixture.CreateAsync(factory);
	}

	public Task DisposeAsync()
	{
		SqliteConnection.ClearAllPools();
		if (File.Exists(_databasePath)) File.Delete(_databasePath);
		return Task.CompletedTask;
	}

	private AttachmentFixture Fixture =>
		_fixture ?? throw new InvalidOperationException("Attachment fixture is not initialized.");

	private sealed class AttachmentFixture
	{
		private readonly DatabaseAccess _data;
		private readonly DatabaseBusinessAttachmentContentStore _contentStore;
		private readonly BusinessAttachmentRepository _attachments;
		private readonly User _admin;

		private AttachmentFixture(
			DatabaseAccess data,
			DatabaseBusinessAttachmentContentStore contentStore,
			BusinessAttachmentRepository attachments,
			AuditRepository audit,
			BusinessAttachmentService service,
			CustomerRepository customers,
			User admin)
		{
			_data = data;
			_contentStore = contentStore;
			_attachments = attachments;
			Audit = audit;
			Service = service;
			Customers = customers;
			_admin = admin;
		}

		public AuditRepository Audit { get; }
		public BusinessAttachmentService Service { get; }
		public CustomerRepository Customers { get; }

		public static async Task<AttachmentFixture> CreateAsync(IDatabaseConnectionFactory factory)
		{
			var data = new DatabaseAccess(factory);
			var users = new UserRepository(data);
			var admin = await users.GetByEmailAsync("admin@depot.local", CancellationToken.None)
				?? throw new InvalidOperationException("Default administrator missing.");
			var authorization = new AuthorizationService();
			authorization.SignIn(admin, PermissionCatalog.All);
			var auditRepository = new AuditRepository(data);
			var audit = new AuditService(auditRepository, authorization);
			var contentStore = new DatabaseBusinessAttachmentContentStore(data);
			var attachments = new BusinessAttachmentRepository(data, contentStore);
			var service = new BusinessAttachmentService(attachments, contentStore, audit, authorization);
			return new AttachmentFixture(
				data,
				contentStore,
				attachments,
				auditRepository,
				service,
				new CustomerRepository(data),
				admin);
		}

		public Task<Customer> CreateCustomerAsync(string name) =>
			Customers.SaveAsync(new Customer { Name = name, Currency = "EUR" }, CancellationToken.None);

		public BusinessAttachmentService CreateService(params ApplicationPermission[] permissions)
		{
			var authorization = new AuthorizationService();
			authorization.SignIn(_admin, permissions);
			return new BusinessAttachmentService(
				_attachments,
				_contentStore,
				new AuditService(Audit, authorization),
				authorization);
		}

		public async Task<long> ScalarAsync(string sql)
		{
			var value = await _data.ExecuteScalarAsync(sql, CancellationToken.None);
			return Convert.ToInt64(value);
		}
	}
}
