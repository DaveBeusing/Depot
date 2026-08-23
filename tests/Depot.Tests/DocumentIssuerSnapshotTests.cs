// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Services;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class DocumentIssuerSnapshotTests : IDisposable
{
	private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"depot-issuer-{Guid.NewGuid():N}.db");
	private readonly SqliteConnectionFactory _factory;
	private readonly DatabaseAccess _database;

	public DocumentIssuerSnapshotTests()
	{
		_factory = new SqliteConnectionFactory(_databasePath);
		new DepotDatabase(_factory).Initialize();
		SalesDocumentIssuerSnapshotSchema.Ensure(_factory);
		_database = new DatabaseAccess(_factory);
	}

	[Fact]
	public async Task SnapshotPreservesIssuerAfterCompanyProfileChanges()
	{
		var authorization = Authorized(ApplicationPermission.SettingsView, ApplicationPermission.SettingsManage);
		var company = new CompanyProfileService(_database, DatabaseProvider.Local, authorization);
		var profile = CompleteProfile("Original Legal GmbH", "DE111111111");
		await company.SaveAsync(profile);

		var runner = new DatabaseTransactionRunner(_database);
		await runner.ExecuteAsync(async (transaction, token) =>
		{
			await DocumentIssuerSnapshotService.CaptureCurrentAsync(transaction, DocumentIssuerSnapshotType.SalesInvoice, 42, new DateTime(2026, 8, 23, 18, 0, 0, DateTimeKind.Utc), token);
			return 0;
		}, CancellationToken.None);

		profile.LegalName = "Renamed Legal GmbH";
		profile.VatId = "DE999999999";
		await company.SaveAsync(profile);

		var snapshots = new DocumentIssuerSnapshotService(_database);
		var issuer = snapshots.LoadRequired(DocumentIssuerSnapshotType.SalesInvoice, 42);
		Assert.Equal("Original Legal GmbH", issuer.LegalName);
		Assert.Contains("DE111111111", issuer.TaxLine, StringComparison.Ordinal);
		Assert.DoesNotContain("DE999999999", issuer.TaxLine, StringComparison.Ordinal);
	}

	[Fact]
	public async Task ExistingSnapshotCannotBeReplaced()
	{
		var authorization = Authorized(ApplicationPermission.SettingsView, ApplicationPermission.SettingsManage);
		var company = new CompanyProfileService(_database, DatabaseProvider.Local, authorization);
		await company.SaveAsync(CompleteProfile("Immutable GmbH", "DE111111111"));
		var runner = new DatabaseTransactionRunner(_database);

		await runner.ExecuteAsync(async (transaction, token) =>
		{
			await DocumentIssuerSnapshotService.CaptureCurrentAsync(transaction, DocumentIssuerSnapshotType.SalesCreditNote, 7, DateTime.UtcNow, token);
			return 0;
		}, CancellationToken.None);

		await Assert.ThrowsAsync<InvalidOperationException>(() => runner.ExecuteAsync(async (transaction, token) =>
		{
			await DocumentIssuerSnapshotService.CaptureCurrentAsync(transaction, DocumentIssuerSnapshotType.SalesCreditNote, 7, DateTime.UtcNow, token);
			return 0;
		}, CancellationToken.None));
	}

	[Fact]
	public void MissingHistoricalSnapshotFailsClosed()
	{
		var snapshots = new DocumentIssuerSnapshotService(_database);
		var exception = Assert.Throws<InvalidOperationException>(() => snapshots.LoadRequired(DocumentIssuerSnapshotType.SalesInvoice, 404));
		Assert.Contains("cannot be regenerated from current company master data", exception.Message, StringComparison.OrdinalIgnoreCase);
	}

	private static AuthorizationService Authorized(params ApplicationPermission[] permissions)
	{
		var authorization = new AuthorizationService();
		authorization.SignIn(new User { Id = 1, Email = "admin@depot.test", DisplayName = "Admin", IsActive = true }, permissions);
		return authorization;
	}

	private static CompanyProfile CompleteProfile(string legalName, string vatId) => new()
	{
		LegalName = legalName,
		LegalForm = "GmbH",
		Street = "Example Street 1",
		PostalCode = "53111",
		City = "Bonn",
		CountryCode = "DE",
		TaxResidenceCountryCode = "DE",
		RegisteredOffice = "Bonn",
		IsRegisteredEntity = true,
		RegisterCourt = "Amtsgericht Bonn",
		RegisterType = "HRB",
		RegisterNumber = "12345",
		ManagingDirectors = "Jane Example",
		VatId = vatId,
		Email = "office@example.test",
		InvoiceEmail = "invoice@example.test",
		Phone = "+49 228 123456",
		DefaultCurrency = "EUR",
		PaymentTermsDays = 14
	};

	public void Dispose()
	{
		SqliteConnection.ClearAllPools();
		if (File.Exists(_databasePath)) File.Delete(_databasePath);
	}
}
