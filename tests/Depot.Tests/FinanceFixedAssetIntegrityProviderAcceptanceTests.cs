// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

[Collection("Provider database")]
[Trait("Acceptance", "DatabaseProvider")]
[Trait("AcceptanceLevel", "Smoke")]
public sealed class FinanceFixedAssetIntegrityProviderAcceptanceTests
{
	[Fact]
	[Trait("Provider", "SQLite")]
	public async Task SQLiteFixedAssetIntegrityContract()
	{
		var path = Path.Combine(Path.GetTempPath(), $"depot-asset-integrity-{Guid.NewGuid():N}.db");
		try { await VerifyAsync(new SqliteConnectionFactory(path)); }
		finally
		{
			SqliteConnection.ClearAllPools();
			if (File.Exists(path)) File.Delete(path);
		}
	}

	[SqlServerProcurementFact]
	[Trait("Provider", "SqlServer")]
	public Task SqlServerFixedAssetIntegrityContract() =>
		VerifyAsync(new SqlServerConnectionFactory(ProcurementProviderConfiguration.GetSqlServerSettings()));

	[MariaDbProcurementFact]
	[Trait("Provider", "MariaDB")]
	public Task MariaDbFixedAssetIntegrityContract() =>
		VerifyAsync(new MySqlConnectionFactory(ProcurementProviderConfiguration.GetMariaDbSettings()));

	[MySqlProcurementFact]
	[Trait("Provider", "MySQL")]
	public Task MySqlFixedAssetIntegrityContract() =>
		VerifyAsync(new MySqlConnectionFactory(ProcurementProviderConfiguration.GetMySqlSettings()));

	private static async Task VerifyAsync(IDatabaseConnectionFactory factory)
	{
		DatabaseProvisioningService.Initialize(factory);
		var first = await FixedAssetProviderFixture.CreateAsync(factory);
		var other = await FixedAssetProviderFixture.CreateAsync(factory);
		var service = first.Service;
		var cls = first.AssetClass;

		var scheduled = await first.CreateAssetAsync(1000m, 100m, 3);
		var schedule = await service.RecalculateScheduleAsync(scheduled.Id);
		await service.CapitalizeAsync(scheduled.Id, Guid.NewGuid(), first.Periods[0], new DateOnly(2026, 1, 5));
		var impairment = await service.ImpairAsync(scheduled.Id, Guid.NewGuid(), first.Periods[0], new DateOnly(2026, 1, 10), 600m, "Provider impairment");
		Assert.Equal(100m, (await service.PostDepreciationAsync(schedule[0].Id, Guid.NewGuid())).Amount);
		await service.CorrectImpairmentAsync(impairment.Id, Guid.NewGuid(), first.Periods[1], new DateOnly(2026, 2, 1), "Provider correction");
		var second = await service.PostDepreciationAsync(schedule[1].Id, Guid.NewGuid());
		var third = await service.PostDepreciationAsync(schedule[2].Id, Guid.NewGuid());
		Assert.Equal(400m, second.Amount);
		Assert.Equal(400m, third.Amount);
		Assert.NotEqual(second.JournalEntryId, third.JournalEntryId);
		Assert.Equal(second.Id, (await service.PostDepreciationAsync(schedule[1].Id, Guid.NewGuid())).Id);
		await first.AssertReconciledAsync(scheduled.Id, 100m);

		var adjusted = await first.CreateAssetAsync(1000m, 0m, 1);
		await service.CapitalizeAsync(adjusted.Id, Guid.NewGuid(), first.Periods[0], new DateOnly(2026, 1, 5));
		var operation = Guid.NewGuid();
		var initial = await service.ImpairAsync(adjusted.Id, operation, first.Periods[0], new DateOnly(2026, 1, 10), 100m, "First adjustment");
		var following = await service.ImpairAsync(adjusted.Id, Guid.NewGuid(), first.Periods[0], new DateOnly(2026, 1, 11), 150m, "Second adjustment");
		Assert.NotEqual(initial.JournalEntryId, following.JournalEntryId);
		Assert.Equal(initial.Id, (await service.ImpairAsync(adjusted.Id, operation, first.Periods[0], new DateOnly(2026, 1, 10), 100m, "First adjustment")).Id);
		await Assert.ThrowsAsync<InvalidOperationException>(() => service.ImpairAsync(adjusted.Id, operation, first.Periods[0], new DateOnly(2026, 1, 10), 101m, "First adjustment"));
		await Assert.ThrowsAsync<InvalidOperationException>(() => service.ImpairAsync(adjusted.Id, Guid.NewGuid(), first.Periods[0], new DateOnly(2026, 1, 12), 751m, "Excess adjustment"));
		await first.AssertReconciledAsync(adjusted.Id, 750m);

		var overlapping = await first.CreateAssetAsync(1000m, 0m, 1);
		await service.CapitalizeAsync(overlapping.Id, Guid.NewGuid(), first.Periods[0], new DateOnly(2026, 1, 5));
		var conflict = await ProviderWriteOverlap.RunAsync(first.Factory, first.Transactions,
			() => service.ImpairAsync(overlapping.Id, Guid.NewGuid(), first.Periods[0], new DateOnly(2026, 1, 10), 600m, "First concurrent adjustment"),
			() => service.ImpairAsync(overlapping.Id, Guid.NewGuid(), first.Periods[0], new DateOnly(2026, 1, 10), 600m, "Competing adjustment"));
		Assert.IsType<InvalidOperationException>(conflict);
		Assert.Equal(2, (await service.SearchTransactionsAsync(overlapping.Id)).TotalCount);
		await first.AssertReconciledAsync(overlapping.Id, 400m);

		var depreciationRace = await first.CreateAssetAsync(900m, 0m, 3);
		var raceSchedule = await service.RecalculateScheduleAsync(depreciationRace.Id);
		await service.CapitalizeAsync(depreciationRace.Id, Guid.NewGuid(), first.Periods[0], new DateOnly(2026, 1, 5));
		var depreciationConflict = await ProviderWriteOverlap.RunAsync(first.Factory, first.Transactions,
			() => service.PostDepreciationAsync(raceSchedule[0].Id, Guid.NewGuid()),
			() => service.ImpairAsync(depreciationRace.Id, Guid.NewGuid(), first.Periods[0], new DateOnly(2026, 1, 31), 700m, "Concurrent depreciation consumes basis"));
		Assert.IsType<InvalidOperationException>(depreciationConflict);
		await first.AssertReconciledAsync(depreciationRace.Id, 600m);

		var rollbackOperation = Guid.NewGuid();
		first.Transactions.FailNextCommit(new InvalidOperationException("Injected asset commit failure"));
		await Assert.ThrowsAsync<InvalidOperationException>(() => service.ImpairAsync(overlapping.Id, rollbackOperation, first.Periods[0], new DateOnly(2026, 1, 11), 100m, "Atomic adjustment"));
		Assert.Equal(0L, await first.ScalarAsync("SELECT COUNT(*) FROM FinanceJournalEntries WHERE OperationId=$Operation;", new DatabaseParameter("$Operation", rollbackOperation.ToString("D"))));
		Assert.Equal(2, (await service.SearchTransactionsAsync(overlapping.Id)).TotalCount);
		await first.AssertReconciledAsync(overlapping.Id, 400m);
		await service.ImpairAsync(overlapping.Id, rollbackOperation, first.Periods[0], new DateOnly(2026, 1, 11), 100m, "Atomic adjustment");
		await first.AssertReconciledAsync(overlapping.Id, 300m);

		var reassignment = other.AssetClass with { Id = cls.Id, Version = cls.Version, Code = cls.Code };
		await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveClassAsync(reassignment));
		var edited = await service.SaveClassAsync(cls with { Name = "Updated provider asset class" });
		Assert.Equal(first.EntityId, edited.LegalEntityId);

		await first.Data.ExecuteAsync("UPDATE FinanceFixedAssets SET AssetClassId=$Class WHERE Id=$Asset;", CancellationToken.None,
			new DatabaseParameter("$Class", other.AssetClass.Id), new DatabaseParameter("$Asset", adjusted.Id));
		var wrongEntityOperation = Guid.NewGuid();
		await Assert.ThrowsAsync<InvalidOperationException>(() => service.DisposeAsync(adjusted.Id, wrongEntityOperation, other.Periods[0], new DateOnly(2026, 1, 20), 750m, "Wrong entity disposal"));
		Assert.Equal(0L, await first.ScalarAsync("SELECT COUNT(*) FROM FinanceJournalEntries WHERE OperationId=$Operation;", new DatabaseParameter("$Operation", wrongEntityOperation.ToString("D"))));
		await first.Data.ExecuteAsync("UPDATE FinanceFixedAssets SET AssetClassId=$Class WHERE Id=$Asset;", CancellationToken.None,
			new DatabaseParameter("$Class", cls.Id), new DatabaseParameter("$Asset", adjusted.Id));
		var disposalOperation = Guid.NewGuid();
		var disposal = await service.DisposeAsync(adjusted.Id, disposalOperation, first.Periods[0], new DateOnly(2026, 1, 20), 750m, "Provider disposal");
		Assert.Equal(disposal.Id, (await service.DisposeAsync(adjusted.Id, disposalOperation, first.Periods[0], new DateOnly(2026, 1, 20), 750.00m, "Provider disposal")).Id);
		await Assert.ThrowsAsync<InvalidOperationException>(() => service.DisposeAsync(adjusted.Id, disposalOperation, first.Periods[0], new DateOnly(2026, 1, 20), 751m, "Provider disposal"));
		Assert.Equal(1L, await first.ScalarAsync("SELECT COUNT(*) FROM FinanceAssetTransactions WHERE OperationId=$Operation;", new DatabaseParameter("$Operation", disposalOperation.ToString("D"))));
		Assert.Equal(1L, await first.ScalarAsync("SELECT COUNT(*) FROM FinanceJournalEntries WHERE OperationId=$Operation;", new DatabaseParameter("$Operation", disposalOperation.ToString("D"))));
		await first.AssertReconciledAsync(adjusted.Id, 0m);
	}
}

internal sealed class FixedAssetProviderFixture
{
	private FixedAssetProviderFixture(DatabaseAccess data, ObservedProviderConnectionFactory factory, ProviderTransactionGate transactions,
		FinanceFixedAssetService service, Guid entityId, Guid[] periods, FinanceAssetClass assetClass)
	{
		Data = data;
		Factory = factory;
		Transactions = transactions;
		Service = service;
		EntityId = entityId;
		Periods = periods;
		AssetClass = assetClass;
	}

	public DatabaseAccess Data { get; }
	public ObservedProviderConnectionFactory Factory { get; }
	public ProviderTransactionGate Transactions { get; }
	public FinanceFixedAssetService Service { get; }
	public Guid EntityId { get; }
	public Guid[] Periods { get; }
	public FinanceAssetClass AssetClass { get; }

	public static async Task<FixedAssetProviderFixture> CreateAsync(IDatabaseConnectionFactory inner)
	{
		var factory = new ObservedProviderConnectionFactory(inner);
		var data = new DatabaseAccess(factory);
		var transactions = new ProviderTransactionGate(new DatabaseTransactionRunner(data));
		var authorization = new AuthorizationService();
		var admin = await new UserRepository(data).GetByEmailAsync("admin@depot.local", CancellationToken.None)
			?? throw new InvalidOperationException("Default administrator missing.");
		authorization.SignIn(admin, PermissionCatalog.All);
		var auditEntries = new AuditRepository(data);
		var audit = new AuditService(auditEntries, authorization);
		var ledger = new FinanceGeneralLedgerService(transactions, new FinanceGeneralLedgerRepository(data), new FinancePostingProfileRepository(data), auditEntries, audit, authorization);
		var service = new FinanceFixedAssetService(transactions, new FinanceFixedAssetRepository(data), ledger, auditEntries, audit, authorization);
		var suffix = Guid.NewGuid().ToString("N")[..10];
		var entity = Guid.NewGuid();
		var calendar = Guid.NewGuid();
		var book = Guid.NewGuid();
		var chart = Guid.NewGuid();
		var journal = Guid.NewGuid();
		var sequence = $"ASSET-{suffix}";
		async Task ExecuteAsync(string sql, params DatabaseParameter[] parameters) => await data.ExecuteAsync(sql, CancellationToken.None, parameters);
		if (Convert.ToInt64(await data.ExecuteScalarAsync("SELECT COUNT(*) FROM FinanceCurrencies WHERE Code='XTS';", CancellationToken.None), CultureInfo.InvariantCulture) == 0)
			await ExecuteAsync("INSERT INTO FinanceCurrencies (Code,Name,MinorUnits,IsActive) VALUES ('XTS','Testing currency',2,1);");
		await ExecuteAsync("INSERT INTO FinanceLegalEntities (Id,Code,Name,CountryCode,FunctionalCurrencyCode,IsActive) VALUES ($Id,$Code,'Asset provider entity','DE','XTS',1);", new DatabaseParameter("$Id", entity.ToString("D")), new DatabaseParameter("$Code", sequence));
		await ExecuteAsync("INSERT INTO FinanceFiscalCalendars (Id,LegalEntityId,Code,Name,IsActive) VALUES ($Id,$Entity,$Code,'Asset provider calendar',1);", new DatabaseParameter("$Id", calendar.ToString("D")), new DatabaseParameter("$Entity", entity.ToString("D")), new DatabaseParameter("$Code", sequence));
		var periods = new Guid[3];
		for (var i = 0; i < periods.Length; i++)
		{
			periods[i] = Guid.NewGuid();
			var start = new DateOnly(2026, i + 1, 1);
			await ExecuteAsync("INSERT INTO FinanceAccountingPeriods (Id,FiscalCalendarId,Code,StartDate,EndDate,Status) VALUES ($Id,$Calendar,$Code,$Start,$End,0);",
				new DatabaseParameter("$Id", periods[i].ToString("D")), new DatabaseParameter("$Calendar", calendar.ToString("D")), new DatabaseParameter("$Code", $"2026-{i + 1:00}"),
				new DatabaseParameter("$Start", start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), new DatabaseParameter("$End", start.AddMonths(1).AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
		}
		await ExecuteAsync("INSERT INTO FinanceChartsOfAccounts (Id,Code,Name,IsActive) VALUES ($Id,$Code,'Asset provider chart',1);", new DatabaseParameter("$Id", chart.ToString("D")), new DatabaseParameter("$Code", sequence));
		async Task<Guid> AccountAsync(string number, FinanceAccountType type)
		{
			var id = Guid.NewGuid();
			await ExecuteAsync("INSERT INTO FinanceAccounts (Id,ChartOfAccountsId,Number,Name,AccountType,AllowDirectPosting,IsActive) VALUES ($Id,$Chart,$Number,$Number,$Type,1,1);",
				new DatabaseParameter("$Id", id.ToString("D")), new DatabaseParameter("$Chart", chart.ToString("D")), new DatabaseParameter("$Number", number), new DatabaseParameter("$Type", (int)type));
			return id;
		}
		var cost = await AccountAsync("1500", FinanceAccountType.Asset);
		var depreciation = await AccountAsync("1590", FinanceAccountType.Asset);
		var impairment = await AccountAsync("1595", FinanceAccountType.Asset);
		var offset = await AccountAsync("2000", FinanceAccountType.Liability);
		var depreciationExpense = await AccountAsync("6500", FinanceAccountType.Expense);
		var impairmentExpense = await AccountAsync("6510", FinanceAccountType.Expense);
		var cash = await AccountAsync("1000", FinanceAccountType.Asset);
		var gain = await AccountAsync("4700", FinanceAccountType.Revenue);
		var loss = await AccountAsync("6520", FinanceAccountType.Expense);
		await ExecuteAsync("INSERT INTO FinanceAccountingBooks (Id,LegalEntityId,ChartOfAccountsId,Code,Name,ReportingCurrencyCode,AccountingStandardCode,IsPrimary,IsActive) VALUES ($Id,$Entity,$Chart,$Code,'Asset provider book','XTS','TEST',1,1);", new DatabaseParameter("$Id", book.ToString("D")), new DatabaseParameter("$Entity", entity.ToString("D")), new DatabaseParameter("$Chart", chart.ToString("D")), new DatabaseParameter("$Code", sequence));
		await ExecuteAsync("INSERT INTO FinanceJournals (Id,AccountingBookId,Code,Name,IsActive) VALUES ($Id,$Book,$Code,'Asset provider journal',1);", new DatabaseParameter("$Id", journal.ToString("D")), new DatabaseParameter("$Book", book.ToString("D")), new DatabaseParameter("$Code", sequence));
		await ExecuteAsync("INSERT INTO FinanceNumberSequences (Id,LegalEntityId,Code,DocumentType,Prefix,NumericLength,NextNumber,IsActive) VALUES ($Id,$Entity,$Code,$Type,$Prefix,6,1,1);", new DatabaseParameter("$Id", Guid.NewGuid().ToString("D")), new DatabaseParameter("$Entity", entity.ToString("D")), new DatabaseParameter("$Code", sequence), new DatabaseParameter("$Type", FinanceNumberSequenceDocumentTypes.GeneralLedger), new DatabaseParameter("$Prefix", $"{sequence}-"));
		async Task<long> ProfileAsync(string eventName, params (Guid Account, FinancePostingDirection Direction, string Key)[] lines)
		{
			var profileId = await data.InsertAsync("INSERT INTO FinancePostingProfiles (Version,LegalEntityId,AccountingBookId,JournalId,Code,Name,SourceType,SourceEvent,NumberSequenceCode,IsActive) VALUES (1,$Entity,$Book,$Journal,$Code,$Code,'FixedAssets',$Event,$Sequence,1);", CancellationToken.None,
				new DatabaseParameter("$Entity", entity.ToString("D")), new DatabaseParameter("$Book", book.ToString("D")), new DatabaseParameter("$Journal", journal.ToString("D")), new DatabaseParameter("$Code", $"{suffix}-{eventName}"), new DatabaseParameter("$Event", eventName), new DatabaseParameter("$Sequence", sequence));
			for (var i = 0; i < lines.Length; i++)
				await ExecuteAsync("INSERT INTO FinancePostingProfileLines (PostingProfileId,LineNumber,AccountId,Direction,AmountKey,Multiplier,Description) VALUES ($Profile,$Line,$Account,$Direction,$Key,1,NULL);",
					new DatabaseParameter("$Profile", profileId), new DatabaseParameter("$Line", i + 1), new DatabaseParameter("$Account", lines[i].Account.ToString("D")), new DatabaseParameter("$Direction", (int)lines[i].Direction), new DatabaseParameter("$Key", lines[i].Key));
			return profileId;
		}
		var capitalizationProfile = await ProfileAsync("Capitalization", (cost, FinancePostingDirection.Debit, "AssetCost"), (offset, FinancePostingDirection.Credit, "AssetCost"));
		var depreciationProfile = await ProfileAsync("Depreciation", (depreciationExpense, FinancePostingDirection.Debit, "Depreciation"), (depreciation, FinancePostingDirection.Credit, "Depreciation"));
		var impairmentProfile = await ProfileAsync("Impairment", (impairmentExpense, FinancePostingDirection.Debit, "Impairment"), (impairment, FinancePostingDirection.Credit, "Impairment"));
		var disposalProfile = await ProfileAsync("Disposal", (depreciation, FinancePostingDirection.Debit, "AccumulatedDepreciation"), (impairment, FinancePostingDirection.Debit, "Impairment"), (cash, FinancePostingDirection.Debit, "Proceeds"), (loss, FinancePostingDirection.Debit, "Loss"), (cost, FinancePostingDirection.Credit, "AssetCost"), (gain, FinancePostingDirection.Credit, "Gain"));
		var assetClass = await service.SaveClassAsync(new FinanceAssetClass
		{
			LegalEntityId = entity, FiscalCalendarId = calendar, Code = sequence, Name = "Provider asset class", DefaultMethod = FinanceDepreciationMethod.StraightLine, DefaultUsefulLifeMonths = 3,
			CapitalizationPostingProfileId = capitalizationProfile, DepreciationPostingProfileId = depreciationProfile, ImpairmentPostingProfileId = impairmentProfile, DisposalPostingProfileId = disposalProfile
		});
		return new FixedAssetProviderFixture(data, factory, transactions, service, entity, periods, assetClass);
	}

	public Task<FinanceFixedAsset> CreateAssetAsync(decimal cost, decimal salvage, int life) => Service.SaveAssetAsync(new FinanceFixedAsset
	{
		AssetNumber = $"PROVIDER-{Guid.NewGuid():N}", LegalEntityId = EntityId, AssetClassId = AssetClass.Id,
		Description = "Provider fixed asset", AcquisitionDate = new DateOnly(2026, 1, 1), DepreciationStartDate = new DateOnly(2026, 1, 1),
		Currency = new CurrencyCode("XTS"), OriginalCost = cost, SalvageValue = salvage, UsefulLifeMonths = life, DepreciationMethod = FinanceDepreciationMethod.StraightLine
	});

	public async Task<long> ScalarAsync(string sql, params DatabaseParameter[] parameters) =>
		Convert.ToInt64(await Data.ExecuteScalarAsync(sql, CancellationToken.None, parameters), CultureInfo.InvariantCulture);

	public async Task AssertReconciledAsync(long assetId, decimal carrying)
	{
		var row = Assert.Single((await Service.SearchReconciliationAsync(EntityId)).Items, row => row.AssetId == assetId);
		Assert.Equal(carrying, row.SubledgerCarryingValue);
		Assert.Equal(carrying, row.GeneralLedgerCarryingValue);
	}
}
