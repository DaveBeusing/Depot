// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;
using Depot.Commands;
using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels;

public sealed class FinanceLocalizationViewModel : BaseViewModel, IDisposable
{
	private readonly FinanceLocalizationService _localization;
	private LegalEntity? _selectedLegalEntity;
	private FinanceLocalizationPack? _selectedAssignmentPack,_selectedCatalogPack,_selectedRegistryPack;
	private FinanceLocalizationAssignment? _selectedAssignment;
	private FinanceLocalizationRegistryEntry? _selectedRegistryEntry;
	private FinanceLocalizationProfile? _profile;
	private DateTime _asOfDate=DateTime.Today,_assignmentFrom=DateTime.Today,_registryFrom=DateTime.Today;
	private DateTime? _assignmentTo,_registryTo;
	private bool _assignmentActive=true,_packActive=true,_registryActive=true;
	private string _packCode=string.Empty,_packName=string.Empty,_packCountryCode=string.Empty,_packParentCode=string.Empty,_packDescription=string.Empty;
	private FinanceLocalizationLayer _packLayer=FinanceLocalizationLayer.Country;
	private string _requirementCode=string.Empty,_registryTitle=string.Empty,_registryDescription=string.Empty,_registryReference=string.Empty,_warningText=string.Empty;
	private FinanceLocalizationRequirementCategory _registryCategory;
	private FinanceLocalizationSupportLevel _registrySupportLevel;
	private bool _disposed;

	public FinanceLocalizationViewModel(FinanceLocalizationService localization)
	{
		_localization=localization;
		RefreshCommand=new AsyncRelayCommand(LoadAsync);
		LoadProfileCommand=new AsyncRelayCommand(LoadProfileAsync);
		NewAssignmentCommand=new AsyncRelayCommand(_=>{ClearAssignment();return Task.CompletedTask;});
		SaveAssignmentCommand=new AsyncRelayCommand(SaveAssignmentAsync);
		NewPackCommand=new AsyncRelayCommand(_=>{ClearPack();return Task.CompletedTask;});
		SavePackCommand=new AsyncRelayCommand(SavePackAsync);
		NewRegistryEntryCommand=new AsyncRelayCommand(_=>{ClearRegistry();return Task.CompletedTask;});
		SaveRegistryEntryCommand=new AsyncRelayCommand(SaveRegistryEntryAsync);
	}

	public ObservableCollection<LegalEntity> LegalEntities { get; }=[];
	public ObservableCollection<FinanceLocalizationPack> Packs { get; }=[];
	public ObservableCollection<FinanceLocalizationAssignment> Assignments { get; }=[];
	public ObservableCollection<FinanceLocalizationPack> EffectivePacks { get; }=[];
	public ObservableCollection<FinanceLocalizationRegistryEntry> Requirements { get; }=[];
	public ObservableCollection<FinanceLocalizationRegistryEntry> RegistryEntries { get; }=[];
	public IReadOnlyList<FinanceLocalizationLayer> Layers { get; }=Enum.GetValues<FinanceLocalizationLayer>();
	public IReadOnlyList<FinanceLocalizationRequirementCategory> RequirementCategories { get; }=Enum.GetValues<FinanceLocalizationRequirementCategory>();
	public IReadOnlyList<FinanceLocalizationSupportLevel> SupportLevels { get; }=Enum.GetValues<FinanceLocalizationSupportLevel>();

	public AsyncRelayCommand RefreshCommand { get; }
	public AsyncRelayCommand LoadProfileCommand { get; }
	public AsyncRelayCommand NewAssignmentCommand { get; }
	public AsyncRelayCommand SaveAssignmentCommand { get; }
	public AsyncRelayCommand NewPackCommand { get; }
	public AsyncRelayCommand SavePackCommand { get; }
	public AsyncRelayCommand NewRegistryEntryCommand { get; }
	public AsyncRelayCommand SaveRegistryEntryCommand { get; }
	public bool CanManage=>_localization.CanManage;

	public LegalEntity? SelectedLegalEntity { get=>_selectedLegalEntity; set{if(ReferenceEquals(_selectedLegalEntity,value))return;_selectedLegalEntity=value;OnPropertyChanged();} }
	public FinanceLocalizationPack? SelectedAssignmentPack { get=>_selectedAssignmentPack; set{if(ReferenceEquals(_selectedAssignmentPack,value))return;_selectedAssignmentPack=value;OnPropertyChanged();} }
	public FinanceLocalizationAssignment? SelectedAssignment { get=>_selectedAssignment; set{if(ReferenceEquals(_selectedAssignment,value))return;_selectedAssignment=value;OnPropertyChanged();if(value is not null)ApplyAssignment(value);} }
	public FinanceLocalizationProfile? Profile { get=>_profile; private set{if(ReferenceEquals(_profile,value))return;_profile=value;OnPropertyChanged();} }
	public DateTime AsOfDate { get=>_asOfDate; set=>SetDate(ref _asOfDate,value); }
	public DateTime AssignmentFrom { get=>_assignmentFrom; set=>SetDate(ref _assignmentFrom,value); }
	public DateTime? AssignmentTo { get=>_assignmentTo; set{if(_assignmentTo==value)return;_assignmentTo=value;OnPropertyChanged();} }
	public bool AssignmentActive { get=>_assignmentActive; set=>SetBool(ref _assignmentActive,value); }
	public string WarningText { get=>_warningText; private set=>Set(ref _warningText,value); }

	public FinanceLocalizationPack? SelectedCatalogPack { get=>_selectedCatalogPack; set{if(ReferenceEquals(_selectedCatalogPack,value))return;_selectedCatalogPack=value;OnPropertyChanged();if(value is not null)ApplyPack(value);} }
	public string PackCode { get=>_packCode; set=>Set(ref _packCode,value); }
	public string PackName { get=>_packName; set=>Set(ref _packName,value); }
	public FinanceLocalizationLayer PackLayer { get=>_packLayer; set{if(_packLayer==value)return;_packLayer=value;OnPropertyChanged();} }
	public string PackCountryCode { get=>_packCountryCode; set=>Set(ref _packCountryCode,value); }
	public string PackParentCode { get=>_packParentCode; set=>Set(ref _packParentCode,value); }
	public string PackDescription { get=>_packDescription; set=>Set(ref _packDescription,value); }
	public bool PackActive { get=>_packActive; set=>SetBool(ref _packActive,value); }

	public FinanceLocalizationRegistryEntry? SelectedRegistryEntry { get=>_selectedRegistryEntry; set{if(ReferenceEquals(_selectedRegistryEntry,value))return;_selectedRegistryEntry=value;OnPropertyChanged();if(value is not null)ApplyRegistry(value);} }
	public FinanceLocalizationPack? SelectedRegistryPack { get=>_selectedRegistryPack; set{if(ReferenceEquals(_selectedRegistryPack,value))return;_selectedRegistryPack=value;OnPropertyChanged();} }
	public string RequirementCode { get=>_requirementCode; set=>Set(ref _requirementCode,value); }
	public FinanceLocalizationRequirementCategory RegistryCategory { get=>_registryCategory; set{if(_registryCategory==value)return;_registryCategory=value;OnPropertyChanged();} }
	public FinanceLocalizationSupportLevel RegistrySupportLevel { get=>_registrySupportLevel; set{if(_registrySupportLevel==value)return;_registrySupportLevel=value;OnPropertyChanged();} }
	public DateTime RegistryFrom { get=>_registryFrom; set=>SetDate(ref _registryFrom,value); }
	public DateTime? RegistryTo { get=>_registryTo; set{if(_registryTo==value)return;_registryTo=value;OnPropertyChanged();} }
	public string RegistryTitle { get=>_registryTitle; set=>Set(ref _registryTitle,value); }
	public string RegistryDescription { get=>_registryDescription; set=>Set(ref _registryDescription,value); }
	public string RegistryReference { get=>_registryReference; set=>Set(ref _registryReference,value); }
	public bool RegistryActive { get=>_registryActive; set=>SetBool(ref _registryActive,value); }

	public async Task LoadAsync(CancellationToken cancellationToken=default)
	{
		BeginOperation("Loading Finance Localization...");
		try
		{
			var selectedEntityId=SelectedLegalEntity?.Id;
			Replace(LegalEntities,await _localization.GetLegalEntitiesAsync(cancellationToken));
			SelectedLegalEntity=LegalEntities.FirstOrDefault(value=>value.Id==selectedEntityId)??LegalEntities.FirstOrDefault(value=>value.IsActive);
			var selectedPackCode=SelectedAssignmentPack?.Code;
			Replace(Packs,await _localization.GetPacksAsync(cancellationToken));
			SelectedAssignmentPack=Packs.FirstOrDefault(value=>value.Code==selectedPackCode)??Packs.FirstOrDefault(value=>value.Code==FinanceLocalizationPackCodes.Generic);
			Replace(RegistryEntries,await _localization.GetRegistryAsync(null,null,cancellationToken));
			if(SelectedLegalEntity is not null)
			{
				Replace(Assignments,await _localization.GetAssignmentsAsync(SelectedLegalEntity.Id,cancellationToken));
				await LoadProfileCoreAsync(cancellationToken);
			}
			else { Assignments.Clear(); EffectivePacks.Clear(); Requirements.Clear(); Profile=null; WarningText="No legal entity is configured."; }
			CompleteOperation(false,"Finance Localization loaded.");
		}
		catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested){}
		catch(Exception exception){FailOperation(exception,"Finance Localization could not be loaded.");}
	}

	private async Task LoadProfileAsync(CancellationToken cancellationToken)
	{
		BeginOperation("Resolving effective localization profile...");
		try
		{
			if(SelectedLegalEntity is null)throw new InvalidOperationException("Select a legal entity.");
			Replace(Assignments,await _localization.GetAssignmentsAsync(SelectedLegalEntity.Id,cancellationToken));
			await LoadProfileCoreAsync(cancellationToken);
			CompleteOperation(false,"Effective localization profile resolved.");
		}
		catch(Exception exception){FailOperation(exception,"Localization profile could not be resolved.");}
	}

	private async Task LoadProfileCoreAsync(CancellationToken cancellationToken)
	{
		if(SelectedLegalEntity is null)return;
		Profile=await _localization.GetEffectiveProfileAsync(SelectedLegalEntity.Id,DateOnly.FromDateTime(AsOfDate),cancellationToken);
		Replace(EffectivePacks,Profile.Packs);Replace(Requirements,Profile.Requirements);WarningText=string.Join(Environment.NewLine,Profile.Warnings);
	}

	private async Task SaveAssignmentAsync(CancellationToken token)
	{
		BeginOperation("Saving localization assignment...");
		try
		{
			var entity=SelectedLegalEntity??throw new InvalidOperationException("Select a legal entity.");
			var pack=SelectedAssignmentPack??throw new InvalidOperationException("Select a localization pack.");
			var current=SelectedAssignment;
			var value=new FinanceLocalizationAssignment{Id=current?.Id??0,Version=current?.Version??1,LegalEntityId=entity.Id,PackCode=pack.Code,EffectiveFrom=DateOnly.FromDateTime(AssignmentFrom),EffectiveTo=AssignmentTo.HasValue?DateOnly.FromDateTime(AssignmentTo.Value):null,IsActive=AssignmentActive,CreatedAtUtc=current?.CreatedAtUtc??default,CreatedByUserId=current?.CreatedByUserId??0};
			SelectedAssignment=await _localization.SaveAssignmentAsync(value,token);await LoadAsync(token);CompleteOperation(false,"Localization assignment saved.");
		}
		catch(Exception exception){FailOperation(exception,"Localization assignment could not be saved.");}
	}

	private async Task SavePackAsync(CancellationToken token)
	{
		BeginOperation("Saving localization pack...");
		try
		{
			var current=SelectedCatalogPack;
			var value=new FinanceLocalizationPack{Id=current?.Id??0,Version=current?.Version??1,Code=PackCode,Name=PackName,Layer=PackLayer,CountryCode=string.IsNullOrWhiteSpace(PackCountryCode)?null:PackCountryCode,ParentPackCode=string.IsNullOrWhiteSpace(PackParentCode)?null:PackParentCode,Description=PackDescription,IsBuiltIn=current?.IsBuiltIn??false,IsActive=PackActive};
			SelectedCatalogPack=await _localization.SavePackAsync(value,token);await LoadAsync(token);CompleteOperation(false,"Localization pack saved.");
		}
		catch(Exception exception){FailOperation(exception,"Localization pack could not be saved.");}
	}

	private async Task SaveRegistryEntryAsync(CancellationToken token)
	{
		BeginOperation("Saving localization registry entry...");
		try
		{
			var pack=SelectedRegistryPack??throw new InvalidOperationException("Select a localization pack.");
			var current=SelectedRegistryEntry;
			var value=new FinanceLocalizationRegistryEntry{Id=current?.Id??0,Version=current?.Version??1,PackCode=pack.Code,RequirementCode=RequirementCode,Category=RegistryCategory,SupportLevel=RegistrySupportLevel,EffectiveFrom=DateOnly.FromDateTime(RegistryFrom),EffectiveTo=RegistryTo.HasValue?DateOnly.FromDateTime(RegistryTo.Value):null,Title=RegistryTitle,Description=RegistryDescription,Reference=RegistryReference,IsBuiltIn=current?.IsBuiltIn??false,IsActive=RegistryActive};
			SelectedRegistryEntry=await _localization.SaveRegistryEntryAsync(value,token);await LoadAsync(token);CompleteOperation(false,"Localization registry entry saved.");
		}
		catch(Exception exception){FailOperation(exception,"Localization registry entry could not be saved.");}
	}

	private void ApplyAssignment(FinanceLocalizationAssignment value){SelectedAssignmentPack=Packs.FirstOrDefault(pack=>pack.Code==value.PackCode);AssignmentFrom=value.EffectiveFrom.ToDateTime(TimeOnly.MinValue);AssignmentTo=value.EffectiveTo?.ToDateTime(TimeOnly.MinValue);AssignmentActive=value.IsActive;}
	private void ClearAssignment(){SelectedAssignment=null;SelectedAssignmentPack=Packs.FirstOrDefault(pack=>pack.Code==FinanceLocalizationPackCodes.Generic);AssignmentFrom=DateTime.Today;AssignmentTo=null;AssignmentActive=true;}
	private void ApplyPack(FinanceLocalizationPack value){PackCode=value.Code;PackName=value.Name;PackLayer=value.Layer;PackCountryCode=value.CountryCode??string.Empty;PackParentCode=value.ParentPackCode??string.Empty;PackDescription=value.Description;PackActive=value.IsActive;}
	private void ClearPack(){SelectedCatalogPack=null;PackCode=string.Empty;PackName=string.Empty;PackLayer=FinanceLocalizationLayer.Country;PackCountryCode=string.Empty;PackParentCode=FinanceLocalizationPackCodes.EuropeanUnion;PackDescription=string.Empty;PackActive=true;}
	private void ApplyRegistry(FinanceLocalizationRegistryEntry value){SelectedRegistryPack=Packs.FirstOrDefault(pack=>pack.Code==value.PackCode);RequirementCode=value.RequirementCode;RegistryCategory=value.Category;RegistrySupportLevel=value.SupportLevel;RegistryFrom=value.EffectiveFrom.ToDateTime(TimeOnly.MinValue);RegistryTo=value.EffectiveTo?.ToDateTime(TimeOnly.MinValue);RegistryTitle=value.Title;RegistryDescription=value.Description;RegistryReference=value.Reference;RegistryActive=value.IsActive;}
	private void ClearRegistry(){SelectedRegistryEntry=null;SelectedRegistryPack=Packs.FirstOrDefault();RequirementCode=string.Empty;RegistryCategory=FinanceLocalizationRequirementCategory.Accounting;RegistrySupportLevel=FinanceLocalizationSupportLevel.ReferenceOnly;RegistryFrom=DateTime.Today;RegistryTo=null;RegistryTitle=string.Empty;RegistryDescription=string.Empty;RegistryReference=string.Empty;RegistryActive=true;}
	private void Set(ref string field,string value,[System.Runtime.CompilerServices.CallerMemberName]string? name=null){if(field==value)return;field=value;OnPropertyChanged(name);}
	private void SetDate(ref DateTime field,DateTime value,[System.Runtime.CompilerServices.CallerMemberName]string? name=null){if(field==value)return;field=value;OnPropertyChanged(name);}
	private void SetBool(ref bool field,bool value,[System.Runtime.CompilerServices.CallerMemberName]string? name=null){if(field==value)return;field=value;OnPropertyChanged(name);}
	private static void Replace<T>(ObservableCollection<T> target,IEnumerable<T> values){target.Clear();foreach(var value in values)target.Add(value);}
	public void Dispose(){if(_disposed)return;_disposed=true;RefreshCommand.Dispose();LoadProfileCommand.Dispose();NewAssignmentCommand.Dispose();SaveAssignmentCommand.Dispose();NewPackCommand.Dispose();SavePackCommand.Dispose();NewRegistryEntryCommand.Dispose();SaveRegistryEntryCommand.Dispose();}
}
