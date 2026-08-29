// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class FinanceGeneralLedgerTests
{
	[Fact]
	public async Task BalancedPostingIsImmutableAuditedAndIdempotent()
	{
		using var context = FinanceTestContext.Create();
		var service = context.CreateService(ApplicationPermission.FinanceGeneralLedgerView, ApplicationPermission.FinanceGeneralLedgerPost, ApplicationPermission.FinanceManualJournalsPost);
		var operationId = Guid.NewGuid();
		var request = context.Request(operationId, FinanceJournalEntryKind.Manual, 100m, 100m);

		var first = await service.PostAsync(request);
		var retryByOperation = await service.PostAsync(request);
		var retryBySource = await service.PostAsync(request with { OperationId = Guid.NewGuid() });

		Assert.Equal(first.Id, retryByOperation.Id);
		Assert.Equal(first.Id, retryBySource.Id);
		Assert.Equal("GL-0001", first.EntryNumber);
		Assert.Equal(100m, first.Lines.Sum(line => line.TransactionDebit));
		Assert.Equal(first.Lines.Sum(line => line.TransactionDebit), first.Lines.Sum(line => line.TransactionCredit));
		Assert.Equal(1L, context.Scalar("SELECT COUNT(*) FROM FinanceJournalEntries;"));
		Assert.Equal(2L, context.Scalar("SELECT NextNumber FROM FinanceNumberSequences WHERE Id = $Id;", new DatabaseParameter("$Id", context.SequenceId.ToString("D"))));
		Assert.Equal(1L, context.Scalar("SELECT COUNT(*) FROM AuditEntries WHERE EntityType = 'FinanceJournalEntry' AND Action = 'Created';"));
	}

	[Fact]
	public async Task UnbalancedOrClosedPeriodPostingRollsBackWithoutConsumingNumber()
	{
		using var context = FinanceTestContext.Create();
		var service = context.CreateService(ApplicationPermission.FinanceGeneralLedgerPost, ApplicationPermission.FinanceManualJournalsPost);

		await Assert.ThrowsAsync<InvalidOperationException>(() => service.PostAsync(context.Request(Guid.NewGuid(), FinanceJournalEntryKind.Manual, 100m, 99m)));
		Assert.Equal(0L, context.Scalar("SELECT COUNT(*) FROM FinanceJournalEntries;"));
		Assert.Equal(1L, context.Scalar("SELECT NextNumber FROM FinanceNumberSequences WHERE Id = $Id;", new DatabaseParameter("$Id", context.SequenceId.ToString("D"))));

		context.Database.Execute("UPDATE FinanceAccountingPeriods SET Status = 1 WHERE Id = $Id;", new DatabaseParameter("$Id", context.PeriodId.ToString("D")));
		await Assert.ThrowsAsync<InvalidOperationException>(() => service.PostAsync(context.Request(Guid.NewGuid(), FinanceJournalEntryKind.Manual, 100m, 100m)));
		Assert.Equal(0L, context.Scalar("SELECT COUNT(*) FROM FinanceJournalEntries;"));
		Assert.Equal(1L, context.Scalar("SELECT NextNumber FROM FinanceNumberSequences WHERE Id = $Id;", new DatabaseParameter("$Id", context.SequenceId.ToString("D"))));
	}

	[Fact]
	public async Task AuditFailureRollsBackJournalAndNumberSequenceAtomically()
	{
		using var context = FinanceTestContext.Create();
		context.Database.Execute("CREATE TRIGGER RejectFinanceAudit BEFORE INSERT ON AuditEntries WHEN NEW.EntityType = 'FinanceJournalEntry' BEGIN SELECT RAISE(ABORT, 'audit rejected'); END;");
		var service = context.CreateService(ApplicationPermission.FinanceGeneralLedgerPost, ApplicationPermission.FinanceManualJournalsPost);

		await Assert.ThrowsAnyAsync<Exception>(() => service.PostAsync(context.Request(Guid.NewGuid(), FinanceJournalEntryKind.Manual, 25m, 25m)));

		Assert.Equal(0L, context.Scalar("SELECT COUNT(*) FROM FinanceJournalEntries;"));
		Assert.Equal(0L, context.Scalar("SELECT COUNT(*) FROM FinanceJournalEntryLines;"));
		Assert.Equal(1L, context.Scalar("SELECT NextNumber FROM FinanceNumberSequences WHERE Id = $Id;", new DatabaseParameter("$Id", context.SequenceId.ToString("D"))));
	}

	[Fact]
	public async Task PostingProfileCanPostWithoutManualJournalPermission()
	{
		using var context = FinanceTestContext.Create();
		var service = context.CreateService(
			ApplicationPermission.FinancePostingProfilesView,
			ApplicationPermission.FinancePostingProfilesManage,
			ApplicationPermission.FinanceGeneralLedgerPost,
			ApplicationPermission.FinanceGeneralLedgerView);
		var profile = await service.SavePostingProfileAsync(new FinancePostingProfile
		{
			LegalEntityId = context.LegalEntityId,
			AccountingBookId = context.BookId,
			JournalId = context.JournalId,
			Code = "STANDARD",
			Name = "Standard posting",
			SourceType = "TestDocument",
			SourceEvent = "Posted",
			NumberSequenceCode = "GL",
			Lines =
			[
				new FinancePostingProfileLine { LineNumber = 1, AccountId = context.DebitAccountId, Direction = FinancePostingDirection.Debit, AmountKey = "TOTAL" },
				new FinancePostingProfileLine { LineNumber = 2, AccountId = context.CreditAccountId, Direction = FinancePostingDirection.Credit, AmountKey = "TOTAL" }
			]
		});

		var entry = await service.PostFromProfileAsync(new FinanceProfilePostingRequest
		{
			OperationId = Guid.NewGuid(),
			PostingProfileId = profile.Id,
			AccountingPeriodId = context.PeriodId,
			PostingDate = context.PostingDate,
			Description = "Profile posting",
			SourceId = "DOC-2",
			TransactionCurrency = new CurrencyCode("USD"),
			Amounts = new Dictionary<string, decimal> { ["TOTAL"] = 42m }
		});

		Assert.Equal(FinanceJournalEntryKind.Standard, entry.EntryKind);
		Assert.Equal(42m, entry.Lines.Sum(line => line.TransactionDebit));
		Assert.False(service.CanPostManualJournal);
		await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.PostAsync(context.Request(Guid.NewGuid(), FinanceJournalEntryKind.Manual, 10m, 10m) with { SourceId = "DOC-3" }));
	}

	[Fact]
	public async Task ReversalCreatesExactCounterEntryAndNeverMutatesOriginal()
	{
		using var context = FinanceTestContext.Create();
		var service = context.CreateService(ApplicationPermission.FinanceGeneralLedgerPost, ApplicationPermission.FinanceManualJournalsPost, ApplicationPermission.FinanceGeneralLedgerReverse, ApplicationPermission.FinanceGeneralLedgerView);
		var original = await service.PostAsync(context.Request(Guid.NewGuid(), FinanceJournalEntryKind.Manual, 75m, 75m));

		var reversal = await service.ReverseAsync(original.Id, Guid.NewGuid(), context.PeriodId, context.PostingDate, "GL", "Correction");
		var reloadedOriginal = await service.GetByIdAsync(original.Id);

		Assert.NotNull(reloadedOriginal);
		Assert.Equal(FinanceJournalEntryKind.Reversal, reversal.EntryKind);
		Assert.Equal(original.Id, reversal.ReversalOfEntryId);
		Assert.Equal(original.Lines[0].TransactionDebit, reversal.Lines[0].TransactionCredit);
		Assert.Equal(original.Lines[0].ReportingDebit, reversal.Lines[0].ReportingCredit);
		Assert.Equal(original.RequestHash, reloadedOriginal.RequestHash);
		Assert.Equal(2L, context.Scalar("SELECT COUNT(*) FROM FinanceJournalEntries;"));
		Assert.Equal(reversal.Id, context.Scalar("SELECT ReversalEntryId FROM FinanceJournalReversals WHERE OriginalEntryId = $Id;", new DatabaseParameter("$Id", original.Id)));
		await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReverseAsync(original.Id, Guid.NewGuid(), context.PeriodId, context.PostingDate, "GL", "Second reversal"));
	}

	private sealed class FinanceTestContext : IDisposable
	{
		private readonly string _databasePath;
		private readonly SqliteConnectionFactory _factory;

		private FinanceTestContext(string databasePath, SqliteConnectionFactory factory, DatabaseAccess database)
		{
			_databasePath = databasePath;
			_factory = factory;
			Database = database;
		}

		public DatabaseAccess Database { get; }
		public long UserId { get; private set; }
		public Guid LegalEntityId { get; private set; }
		public Guid PeriodId { get; private set; }
		public Guid BookId { get; private set; }
		public Guid JournalId { get; private set; }
		public Guid SequenceId { get; private set; }
		public Guid DebitAccountId { get; private set; }
		public Guid CreditAccountId { get; private set; }
		public DateOnly PostingDate { get; private set; }

		public static FinanceTestContext Create()
		{
			var path = Path.Combine(Path.GetTempPath(), $"depot-finance-gl-{Guid.NewGuid():N}.db");
			var factory = new SqliteConnectionFactory(path);
			new DepotDatabase(factory).Initialize();
			FinanceSchemaMigration.Migrate(factory);
			var context = new FinanceTestContext(path, factory, new DatabaseAccess(factory));
			context.Seed();
			return context;
		}

		public FinanceGeneralLedgerService CreateService(params ApplicationPermission[] permissions)
		{
			var authorization = new AuthorizationService();
			authorization.SignIn(new User { Id = UserId, Email = "finance@depot.test", DisplayName = "Finance", IsActive = true }, permissions);
			var auditRepository = new AuditRepository(Database);
			return new FinanceGeneralLedgerService(
				new DatabaseTransactionRunner(Database),
				new FinanceGeneralLedgerRepository(Database),
				new FinancePostingProfileRepository(Database),
				auditRepository,
				new AuditService(auditRepository, authorization),
				authorization);
		}

		public FinancePostingRequest Request(Guid operationId, FinanceJournalEntryKind kind, decimal debit, decimal credit) => new()
		{
			OperationId = operationId,
			AccountingBookId = BookId,
			JournalId = JournalId,
			AccountingPeriodId = PeriodId,
			NumberSequenceCode = "GL",
			PostingDate = PostingDate,
			Description = "Test journal",
			SourceType = "TestDocument",
			SourceId = "DOC-1",
			SourceEvent = "Posted",
			TransactionCurrency = new CurrencyCode("USD"),
			EntryKind = kind,
			Lines =
			[
				new FinancePostingLine { AccountId = DebitAccountId, Debit = debit },
				new FinancePostingLine { AccountId = CreditAccountId, Credit = credit }
			]
		};

		public long Scalar(string sql, params DatabaseParameter[] parameters) => Convert.ToInt64(Database.ExecuteScalarAsync(sql, CancellationToken.None, parameters).GetAwaiter().GetResult(), System.Globalization.CultureInfo.InvariantCulture);

		private void Seed()
		{
			PostingDate = new DateOnly(2026, 8, 28);
			LegalEntityId = Guid.NewGuid();
			var calendarId = Guid.NewGuid();
			PeriodId = Guid.NewGuid();
			var chartId = Guid.NewGuid();
			DebitAccountId = Guid.NewGuid();
			CreditAccountId = Guid.NewGuid();
			BookId = Guid.NewGuid();
			JournalId = Guid.NewGuid();
			SequenceId = Guid.NewGuid();
			UserId = Database.Insert("INSERT INTO Users (Email, DisplayName, PasswordHash, IsAdministrator, CanApprovePurchaseOrders, Role, IsActive, CreatedUtc) VALUES ('finance@depot.test', 'Finance', 'test', 0, 0, 0, 1, '2026-08-28T00:00:00.0000000Z');");
			Database.Execute("INSERT INTO FinanceCurrencies (Code, Name, MinorUnits, IsActive) VALUES ('USD', 'US Dollar', 2, 1);");
			Database.Execute("INSERT INTO FinanceLegalEntities (Id, Code, Name, CountryCode, FunctionalCurrencyCode, IsActive) VALUES ($Id, 'TEST', 'Test Entity', 'US', 'USD', 1);", new DatabaseParameter("$Id", LegalEntityId.ToString("D")));
			Database.Execute("INSERT INTO FinanceFiscalCalendars (Id, LegalEntityId, Code, Name, IsActive) VALUES ($Id, $LegalEntityId, 'CAL', 'Calendar', 1);", new DatabaseParameter("$Id", calendarId.ToString("D")), new DatabaseParameter("$LegalEntityId", LegalEntityId.ToString("D")));
			Database.Execute("INSERT INTO FinanceAccountingPeriods (Id, FiscalCalendarId, Code, StartDate, EndDate, Status) VALUES ($Id, $CalendarId, '2026-08', '2026-08-01', '2026-08-31', 0);", new DatabaseParameter("$Id", PeriodId.ToString("D")), new DatabaseParameter("$CalendarId", calendarId.ToString("D")));
			Database.Execute("INSERT INTO FinanceChartsOfAccounts (Id, Code, Name, IsActive) VALUES ($Id, 'COA', 'Chart', 1);", new DatabaseParameter("$Id", chartId.ToString("D")));
			Database.Execute("INSERT INTO FinanceAccounts (Id, ChartOfAccountsId, Number, Name, AccountType, AllowDirectPosting, IsActive) VALUES ($Id, $ChartId, '1000', 'Debit account', 0, 1, 1);", new DatabaseParameter("$Id", DebitAccountId.ToString("D")), new DatabaseParameter("$ChartId", chartId.ToString("D")));
			Database.Execute("INSERT INTO FinanceAccounts (Id, ChartOfAccountsId, Number, Name, AccountType, AllowDirectPosting, IsActive) VALUES ($Id, $ChartId, '2000', 'Credit account', 1, 1, 1);", new DatabaseParameter("$Id", CreditAccountId.ToString("D")), new DatabaseParameter("$ChartId", chartId.ToString("D")));
			Database.Execute("INSERT INTO FinanceAccountingBooks (Id, LegalEntityId, ChartOfAccountsId, Code, Name, ReportingCurrencyCode, AccountingStandardCode, IsPrimary, IsActive) VALUES ($Id, $LegalEntityId, $ChartId, 'PRIMARY', 'Primary book', 'USD', 'TEST', 1, 1);", new DatabaseParameter("$Id", BookId.ToString("D")), new DatabaseParameter("$LegalEntityId", LegalEntityId.ToString("D")), new DatabaseParameter("$ChartId", chartId.ToString("D")));
			Database.Execute("INSERT INTO FinanceJournals (Id, AccountingBookId, Code, Name, IsActive) VALUES ($Id, $BookId, 'GJ', 'General Journal', 1);", new DatabaseParameter("$Id", JournalId.ToString("D")), new DatabaseParameter("$BookId", BookId.ToString("D")));
			Database.Execute("INSERT INTO FinanceNumberSequences (Id, LegalEntityId, Code, DocumentType, Prefix, NumericLength, NextNumber, IsActive) VALUES ($Id, $LegalEntityId, 'GL', $DocumentType, 'GL-', 4, 1, 1);", new DatabaseParameter("$Id", SequenceId.ToString("D")), new DatabaseParameter("$LegalEntityId", LegalEntityId.ToString("D")), new DatabaseParameter("$DocumentType", FinanceNumberSequenceDocumentTypes.GeneralLedger));
		}

		public void Dispose()
		{
			SqliteConnection.ClearAllPools();
			if (File.Exists(_databasePath)) File.Delete(_databasePath);
		}
	}
}
