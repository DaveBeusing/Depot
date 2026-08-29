// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class FinanceLocalizationService
{
	private readonly IDatabaseTransactionRunner _transactions;
	private readonly FinanceLocalizationRepository _repository;
	private readonly AuditRepository _auditEntries;
	private readonly AuditService _audit;
	private readonly IAuthorizationService _authorization;

	public FinanceLocalizationService(IDatabaseTransactionRunner transactions, FinanceLocalizationRepository repository, AuditRepository auditEntries, AuditService audit, IAuthorizationService authorization)
	{
		_transactions = transactions;
		_repository = repository;
		_auditEntries = auditEntries;
		_audit = audit;
		_authorization = authorization;
	}

	public bool CanView => _authorization.HasPermission(ApplicationPermission.FinanceLocalizationView);
	public bool CanManage => _authorization.HasPermission(ApplicationPermission.FinanceLocalizationManage);

	public Task<IReadOnlyList<LegalEntity>> GetLegalEntitiesAsync(CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceLocalizationView);
		return _repository.GetLegalEntitiesAsync(cancellationToken);
	}

	public Task<IReadOnlyList<FinanceLocalizationPack>> GetPacksAsync(CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceLocalizationView);
		return _repository.GetPacksAsync(cancellationToken);
	}

	public Task<IReadOnlyList<FinanceLocalizationAssignment>> GetAssignmentsAsync(Guid legalEntityId, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceLocalizationView);
		if (legalEntityId == Guid.Empty) throw new ArgumentException("Legal entity is required.", nameof(legalEntityId));
		return _repository.GetAssignmentsAsync(legalEntityId, cancellationToken);
	}

	public async Task<IReadOnlyList<FinanceLocalizationRegistryEntry>> GetRegistryAsync(string? packCode = null, DateOnly? asOfDate = null, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceLocalizationView);
		var values = await _repository.GetRegistryAsync(string.IsNullOrWhiteSpace(packCode) ? null : NormalizeCode(packCode, nameof(packCode), 50), cancellationToken);
		if (!asOfDate.HasValue) return values;
		return values.Where(value => value.IsActive && value.EffectiveFrom <= asOfDate.Value && (!value.EffectiveTo.HasValue || value.EffectiveTo.Value >= asOfDate.Value))
			.GroupBy(value => (value.PackCode, value.RequirementCode))
			.Select(group => group.OrderByDescending(value => value.EffectiveFrom).ThenByDescending(value => value.Id).First())
			.OrderBy(value => value.PackCode).ThenBy(value => value.RequirementCode).ToArray();
	}

	public async Task<FinanceLocalizationProfile> GetEffectiveProfileAsync(Guid legalEntityId, DateOnly asOfDate, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceLocalizationView);
		if (legalEntityId == Guid.Empty) throw new ArgumentException("Legal entity is required.", nameof(legalEntityId));
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var entity = await RequireLegalEntityAsync(transaction, legalEntityId, token);
			var assignment = await _repository.GetEffectiveAssignmentAsync(transaction, legalEntityId, asOfDate, token);
			if (assignment is null)
			{
				return new FinanceLocalizationProfile
				{
					LegalEntityId = entity.Id,
					LegalEntityCode = entity.Code,
					CountryCode = entity.CountryCode,
					AsOfDate = asOfDate,
					Warnings = ["No explicit localization pack is effective for this legal entity. Depot remains jurisdiction-neutral; the legal-entity country does not activate a pack automatically."]
				};
			}

			var packs = await ResolvePackChainAsync(transaction, assignment.PackCode, false, token);
			var root = packs[^1];
			if (!string.IsNullOrWhiteSpace(root.CountryCode) && !string.Equals(root.CountryCode, entity.CountryCode, StringComparison.Ordinal))
				throw new InvalidOperationException($"Localization pack '{root.Code}' targets country '{root.CountryCode}' but legal entity '{entity.Code}' uses country '{entity.CountryCode}'.");

			var requirements = new List<FinanceLocalizationRegistryEntry>();
			foreach (var pack in packs)
			{
				var effective = await _repository.GetEffectiveRegistryAsync(transaction, pack.Code, asOfDate, token);
				requirements.AddRange(effective.GroupBy(value => value.RequirementCode, StringComparer.Ordinal).Select(group => group.OrderByDescending(value => value.EffectiveFrom).ThenByDescending(value => value.Id).First()));
			}
			var warnings = new List<string>();
			var externalCount = requirements.Count(value => value.SupportLevel == FinanceLocalizationSupportLevel.ExternalProcedureRequired);
			var configurationCount = requirements.Count(value => value.SupportLevel == FinanceLocalizationSupportLevel.ConfigurationRequired);
			if (externalCount > 0) warnings.Add($"{externalCount} effective requirement(s) remain external organizational/professional procedures; a pack is not a compliance certification.");
			if (configurationCount > 0) warnings.Add($"{configurationCount} effective requirement(s) require explicit deployment configuration or accounting/tax policy approval.");
			return new FinanceLocalizationProfile { LegalEntityId=entity.Id,LegalEntityCode=entity.Code,CountryCode=entity.CountryCode,AsOfDate=asOfDate,Packs=packs,Requirements=requirements.OrderBy(value=>value.PackCode).ThenBy(value=>value.RequirementCode).ToArray(),Warnings=warnings };
		}, cancellationToken);
	}

	public async Task<FinanceLocalizationPack> SavePackAsync(FinanceLocalizationPack value, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(value);
		_authorization.RequirePermission(ApplicationPermission.FinanceLocalizationManage);
		RequireUser();
		var normalized = NormalizePack(value);
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			FinanceLocalizationPack? parent = null;
			if (normalized.ParentPackCode is not null)
			{
				parent = await _repository.GetPackAsync(transaction, normalized.ParentPackCode, token) ?? throw new InvalidOperationException("Parent localization pack was not found.");
				if (!parent.IsActive) throw new InvalidOperationException("Parent localization pack is inactive.");
				if (parent.Layer >= normalized.Layer) throw new InvalidOperationException("A localization pack parent must belong to a broader layer.");
			}
			ValidateLayer(normalized, parent);
			if (normalized.Id == 0)
			{
				if (await _repository.GetPackAsync(transaction, normalized.Code, token) is not null) throw new InvalidOperationException("A localization pack with this code already exists.");
				var id = await _repository.CreatePackAsync(transaction, normalized, token);
				var created = normalized with { Id=id,Version=1,IsBuiltIn=false };
				await _auditEntries.CreateAsync(transaction, _audit.CreateCreatedEntry(id, created), token);
				return created;
			}
			var before = await _repository.GetPackByIdAsync(transaction, normalized.Id, token) ?? throw new InvalidOperationException("Localization pack was not found.");
			if (before.IsBuiltIn) throw new InvalidOperationException("Built-in localization packs are immutable. Add a custom pack or effective registry entry instead.");
			if (before.Version != normalized.Version) throw new ConcurrencyConflictException("finance localization pack");
			if (!string.Equals(before.Code,normalized.Code,StringComparison.Ordinal) || before.Layer!=normalized.Layer || !string.Equals(before.CountryCode,normalized.CountryCode,StringComparison.Ordinal) || !string.Equals(before.ParentPackCode,normalized.ParentPackCode,StringComparison.Ordinal))
				throw new InvalidOperationException("Localization pack code, layer, country and parent are immutable after creation.");
			if (await _repository.UpdatePackAsync(transaction, normalized, before.Version, token) != 1) throw new ConcurrencyConflictException("finance localization pack");
			var after = normalized with { Version=before.Version+1,IsBuiltIn=false };
			await _auditEntries.CreateAsync(transaction, _audit.CreateUpdatedEntry(after.Id, before, after), token);
			return after;
		}, cancellationToken);
	}

	public async Task<FinanceLocalizationAssignment> SaveAssignmentAsync(FinanceLocalizationAssignment value, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(value);
		_authorization.RequirePermission(ApplicationPermission.FinanceLocalizationManage);
		var user = RequireUser();
		if (value.LegalEntityId == Guid.Empty) throw new ArgumentException("Legal entity is required.", nameof(value));
		var packCode = NormalizeCode(value.PackCode, nameof(value.PackCode), 50);
		ValidateRange(value.EffectiveFrom, value.EffectiveTo);
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var entity = await RequireLegalEntityAsync(transaction, value.LegalEntityId, token);
			var chain = await ResolvePackChainAsync(transaction, packCode, true, token);
			var root = chain[^1];
			if (!string.IsNullOrWhiteSpace(root.CountryCode) && !string.Equals(root.CountryCode, entity.CountryCode, StringComparison.Ordinal))
				throw new InvalidOperationException($"Localization pack '{root.Code}' targets country '{root.CountryCode}' and cannot be assigned to legal entity country '{entity.CountryCode}'.");

			FinanceLocalizationAssignment normalized;
			if (value.Id == 0)
			{
				if (value.IsActive && await _repository.HasOverlappingAssignmentAsync(transaction, value.LegalEntityId, value.EffectiveFrom, value.EffectiveTo, 0, token)) throw new InvalidOperationException("Another localization assignment overlaps this effective date range.");
				normalized = value with { PackCode=packCode,CreatedAtUtc=DateTime.UtcNow,CreatedByUserId=user.Id,Version=1 };
				var id = await _repository.CreateAssignmentAsync(transaction, normalized, token);
				var created = normalized with { Id=id };
				await _auditEntries.CreateAsync(transaction, _audit.CreateCreatedEntry(id, created), token);
				return created;
			}
			var before = await _repository.GetAssignmentAsync(transaction, value.Id, token) ?? throw new InvalidOperationException("Localization assignment was not found.");
			if (before.Version != value.Version) throw new ConcurrencyConflictException("finance localization assignment");
			if (before.LegalEntityId!=value.LegalEntityId || !string.Equals(before.PackCode,packCode,StringComparison.Ordinal) || before.EffectiveFrom!=value.EffectiveFrom) throw new InvalidOperationException("Legal entity, pack and effective-from date are immutable after assignment creation.");
			if (value.IsActive && await _repository.HasOverlappingAssignmentAsync(transaction, value.LegalEntityId, value.EffectiveFrom, value.EffectiveTo, value.Id, token)) throw new InvalidOperationException("Another localization assignment overlaps this effective date range.");
			normalized = value with { PackCode=packCode,CreatedAtUtc=before.CreatedAtUtc,CreatedByUserId=before.CreatedByUserId };
			if (await _repository.UpdateAssignmentAsync(transaction, normalized, before.Version, token) != 1) throw new ConcurrencyConflictException("finance localization assignment");
			var after = normalized with { Version=before.Version+1 };
			await _auditEntries.CreateAsync(transaction, _audit.CreateUpdatedEntry(after.Id, before, after), token);
			return after;
		}, cancellationToken);
	}

	public async Task<FinanceLocalizationRegistryEntry> SaveRegistryEntryAsync(FinanceLocalizationRegistryEntry value, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(value);
		_authorization.RequirePermission(ApplicationPermission.FinanceLocalizationManage);
		RequireUser();
		var normalized = NormalizeRegistry(value);
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			if (await _repository.GetPackAsync(transaction, normalized.PackCode, token) is null) throw new InvalidOperationException("Localization pack was not found.");
			if (normalized.Id == 0)
			{
				var existing = (await _repository.GetEffectiveRegistryAsync(transaction, normalized.PackCode, normalized.EffectiveFrom, token)).FirstOrDefault(entry=>string.Equals(entry.RequirementCode,normalized.RequirementCode,StringComparison.Ordinal));
				if (existing is not null) throw new InvalidOperationException("An effective registry entry for this requirement already covers the selected start date. Close the previous effective range first.");
				var id = await _repository.CreateRegistryEntryAsync(transaction, normalized, token);
				var created = normalized with { Id=id,Version=1,IsBuiltIn=false };
				await _auditEntries.CreateAsync(transaction, _audit.CreateCreatedEntry(id, created), token);
				return created;
			}
			var before = await _repository.GetRegistryEntryAsync(transaction, normalized.Id, token) ?? throw new InvalidOperationException("Localization registry entry was not found.");
			if (before.IsBuiltIn) throw new InvalidOperationException("Built-in registry entries are immutable. Create a new effective custom requirement instead.");
			if (before.Version != normalized.Version) throw new ConcurrencyConflictException("finance localization registry entry");
			if (!string.Equals(before.PackCode,normalized.PackCode,StringComparison.Ordinal) || !string.Equals(before.RequirementCode,normalized.RequirementCode,StringComparison.Ordinal) || before.EffectiveFrom!=normalized.EffectiveFrom) throw new InvalidOperationException("Pack, requirement code and effective-from date are immutable after registry-entry creation.");
			if (await _repository.UpdateRegistryEntryAsync(transaction, normalized, before.Version, token) != 1) throw new ConcurrencyConflictException("finance localization registry entry");
			var after = normalized with { Version=before.Version+1,IsBuiltIn=false };
			await _auditEntries.CreateAsync(transaction, _audit.CreateUpdatedEntry(after.Id, before, after), token);
			return after;
		}, cancellationToken);
	}

	private async Task<IReadOnlyList<FinanceLocalizationPack>> ResolvePackChainAsync(DatabaseTransactionContext transaction, string rootCode, bool requireActive, CancellationToken cancellationToken)
	{
		var chain = new List<FinanceLocalizationPack>();
		var seen = new HashSet<string>(StringComparer.Ordinal);
		var currentCode = rootCode;
		for (var depth=0; depth<16; depth++)
		{
			if (!seen.Add(currentCode)) throw new InvalidOperationException("Localization pack dependency cycle detected.");
			var pack = await _repository.GetPackAsync(transaction,currentCode,cancellationToken) ?? throw new InvalidOperationException($"Localization pack '{currentCode}' was not found.");
			if (requireActive && !pack.IsActive) throw new InvalidOperationException($"Localization pack '{pack.Code}' is inactive.");
			chain.Add(pack);
			if (string.IsNullOrWhiteSpace(pack.ParentPackCode)) { chain.Reverse(); return chain; }
			currentCode = pack.ParentPackCode;
		}
		throw new InvalidOperationException("Localization pack dependency depth exceeds the supported limit.");
	}

	private async Task<LegalEntity> RequireLegalEntityAsync(DatabaseTransactionContext transaction, Guid id, CancellationToken cancellationToken)
	{
		var entity = await _repository.GetLegalEntityAsync(transaction,id,cancellationToken) ?? throw new InvalidOperationException("Legal entity was not found.");
		if (!entity.IsActive) throw new InvalidOperationException("Legal entity is inactive.");
		return entity;
	}

	private static FinanceLocalizationPack NormalizePack(FinanceLocalizationPack value)
	{
		var code=NormalizeCode(value.Code,nameof(value.Code),50);
		var name=Required(value.Name,nameof(value.Name),200);
		var description=Required(value.Description,nameof(value.Description),1000);
		var country=string.IsNullOrWhiteSpace(value.CountryCode)?null:FinanceValidation.CountryCode(value.CountryCode,nameof(value.CountryCode));
		var parent=string.IsNullOrWhiteSpace(value.ParentPackCode)?null:NormalizeCode(value.ParentPackCode,nameof(value.ParentPackCode),50);
		return value with { Code=code,Name=name,Description=description,CountryCode=country,ParentPackCode=parent };
	}

	private static void ValidateLayer(FinanceLocalizationPack value, FinanceLocalizationPack? parent)
	{
		if (value.Layer==FinanceLocalizationLayer.Generic && (value.CountryCode is not null || parent is not null)) throw new InvalidOperationException("Generic localization packs cannot target a country or have a parent.");
		if (value.Layer==FinanceLocalizationLayer.Regional && (value.CountryCode is not null || parent is null)) throw new InvalidOperationException("Regional localization packs require a broader parent and cannot target one country.");
		if (value.Layer==FinanceLocalizationLayer.Country && (value.CountryCode is null || parent is null)) throw new InvalidOperationException("Country localization packs require a country code and a broader parent.");
		if (parent is not null && string.Equals(parent.Code,value.Code,StringComparison.Ordinal)) throw new InvalidOperationException("A localization pack cannot depend on itself.");
	}

	private static FinanceLocalizationRegistryEntry NormalizeRegistry(FinanceLocalizationRegistryEntry value)
	{
		ValidateRange(value.EffectiveFrom,value.EffectiveTo);
		return value with
		{
			PackCode=NormalizeCode(value.PackCode,nameof(value.PackCode),50),
			RequirementCode=NormalizeCode(value.RequirementCode,nameof(value.RequirementCode),100),
			Title=Required(value.Title,nameof(value.Title),250),
			Description=Required(value.Description,nameof(value.Description),2000),
			Reference=(value.Reference??string.Empty).Trim()
		};
	}

	private static void ValidateRange(DateOnly from, DateOnly? to) { if (from==default) throw new ArgumentException("Effective-from date is required."); if (to.HasValue && to.Value<from) throw new ArgumentException("Effective-to date must be on or after effective-from date."); }
	private static string NormalizeCode(string value,string parameterName,int maxLength)=>Required(value,parameterName,maxLength).ToUpperInvariant();
	private static string Required(string value,string parameterName,int maxLength){if(string.IsNullOrWhiteSpace(value))throw new ArgumentException("Value is required.",parameterName);var result=value.Trim();if(result.Length>maxLength)throw new ArgumentException($"Value cannot exceed {maxLength} characters.",parameterName);return result;}
	private User RequireUser()=>_authorization.CurrentUser??throw new UnauthorizedAccessException("An authenticated user is required.");
}
