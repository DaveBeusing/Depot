// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;
using Depot.Commands;
using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels;

public sealed class FinanceFixedAssetsViewModel : BaseViewModel, IDisposable
{
	private readonly FinanceFixedAssetService _service;
	private FinanceFixedAsset? _selectedAsset;
	private FinanceAssetClass? _selectedClass,_assetClass;
	private FinanceAssetLegalEntityOption? _assetEntity,_classEntity;
	private FinanceAssetFiscalCalendarOption? _classCalendar;
	private FinanceAssetPostingProfileOption? _capitalizationProfile,_depreciationProfile,_impairmentProfile,_disposalProfile;
	private FinanceAssetPeriodOption? _selectedPeriod;
	private FinanceAssetDepreciationPeriod? _selectedSchedulePeriod;
	private string _searchText=string.Empty,_assetNumber=string.Empty,_description=string.Empty,_location=string.Empty,_custodian=string.Empty,_classCode=string.Empty,_className=string.Empty,_reason=string.Empty;
	private DateTime _acquisitionDate=DateTime.Today,_depreciationStartDate=DateTime.Today;
	private decimal _originalCost,_salvageValue,_adjustmentAmount,_disposalProceeds;
	private int _usefulLifeMonths=60,_classUsefulLifeMonths=60;
	private FinanceDepreciationMethod _method=FinanceDepreciationMethod.StraightLine,_classMethod=FinanceDepreciationMethod.StraightLine;
	private FinanceClosedPeriodPolicy _closedPeriodPolicy=FinanceClosedPeriodPolicy.Fail;
	private bool _disposed;

	public FinanceFixedAssetsViewModel(FinanceFixedAssetService service)
	{
		_service=service;
		RefreshCommand=new AsyncRelayCommand(LoadAsync);
		NewAssetCommand=new AsyncRelayCommand(_=>{ClearAssetDraft();return Task.CompletedTask;});
		SaveAssetCommand=new AsyncRelayCommand(SaveAssetAsync);
		RecalculateScheduleCommand=new AsyncRelayCommand(RecalculateScheduleAsync);
		CapitalizeCommand=new AsyncRelayCommand(CapitalizeAsync);
		PostDepreciationCommand=new AsyncRelayCommand(PostDepreciationAsync);
		RunDepreciationCommand=new AsyncRelayCommand(RunDepreciationAsync);
		ImpairCommand=new AsyncRelayCommand(ImpairAsync);
		TransferCommand=new AsyncRelayCommand(TransferAsync);
		DisposeCommand=new AsyncRelayCommand(DisposeAssetAsync);
		NewClassCommand=new AsyncRelayCommand(_=>{ClearClassDraft();return Task.CompletedTask;});
		SaveClassCommand=new AsyncRelayCommand(SaveClassAsync);
	}

	public ObservableCollection<FinanceFixedAsset> Assets{get;}=[];
	public ObservableCollection<FinanceAssetClass> Classes{get;}=[];
	public ObservableCollection<FinanceAssetDepreciationPeriod> Schedule{get;}=[];
	public ObservableCollection<FinanceAssetTransaction> Transactions{get;}=[];
	public ObservableCollection<FinanceAssetReconciliationRow> Reconciliation{get;}=[];
	public ObservableCollection<FinanceAssetLegalEntityOption> LegalEntities{get;}=[];
	public ObservableCollection<FinanceAssetFiscalCalendarOption> FiscalCalendars{get;}=[];
	public ObservableCollection<FinanceAssetPostingProfileOption> PostingProfiles{get;}=[];
	public ObservableCollection<FinanceAssetPeriodOption> Periods{get;}=[];
	public IReadOnlyList<FinanceDepreciationMethod> Methods{get;}=Enum.GetValues<FinanceDepreciationMethod>();
	public IReadOnlyList<FinanceClosedPeriodPolicy> ClosedPeriodPolicies{get;}=Enum.GetValues<FinanceClosedPeriodPolicy>();

	public AsyncRelayCommand RefreshCommand{get;}
	public AsyncRelayCommand NewAssetCommand{get;}
	public AsyncRelayCommand SaveAssetCommand{get;}
	public AsyncRelayCommand RecalculateScheduleCommand{get;}
	public AsyncRelayCommand CapitalizeCommand{get;}
	public AsyncRelayCommand PostDepreciationCommand{get;}
	public AsyncRelayCommand RunDepreciationCommand{get;}
	public AsyncRelayCommand ImpairCommand{get;}
	public AsyncRelayCommand TransferCommand{get;}
	public AsyncRelayCommand DisposeCommand{get;}
	public AsyncRelayCommand NewClassCommand{get;}
	public AsyncRelayCommand SaveClassCommand{get;}

	public bool CanConfigure=>_service.CanConfigure;
	public bool CanManage=>_service.CanManage;
	public bool CanPostDepreciation=>_service.CanPostDepreciation;
	public string SearchText{get=>_searchText;set=>Set(ref _searchText,value);}
	public FinanceFixedAsset? SelectedAsset{get=>_selectedAsset;set{if(ReferenceEquals(_selectedAsset,value))return;_selectedAsset=value;OnPropertyChanged();if(value is not null){ApplyAsset(value);_ = LoadAssetDetailsAsync(value.Id,CancellationToken.None);}}}
	public FinanceAssetClass? SelectedClass{get=>_selectedClass;set{if(ReferenceEquals(_selectedClass,value))return;_selectedClass=value;OnPropertyChanged();if(value is not null)ApplyClass(value);}}
	public FinanceAssetClass? AssetClass{get=>_assetClass;set{if(ReferenceEquals(_assetClass,value))return;_assetClass=value;OnPropertyChanged();}}
	public FinanceAssetLegalEntityOption? AssetEntity{get=>_assetEntity;set{if(ReferenceEquals(_assetEntity,value))return;_assetEntity=value;OnPropertyChanged();}}
	public FinanceAssetLegalEntityOption? ClassEntity{get=>_classEntity;set{if(ReferenceEquals(_classEntity,value))return;_classEntity=value;OnPropertyChanged();}}
	public FinanceAssetFiscalCalendarOption? ClassCalendar{get=>_classCalendar;set{if(ReferenceEquals(_classCalendar,value))return;_classCalendar=value;OnPropertyChanged();}}
	public FinanceAssetPostingProfileOption? CapitalizationProfile{get=>_capitalizationProfile;set=>SetRef(ref _capitalizationProfile,value);}
	public FinanceAssetPostingProfileOption? DepreciationProfile{get=>_depreciationProfile;set=>SetRef(ref _depreciationProfile,value);}
	public FinanceAssetPostingProfileOption? ImpairmentProfile{get=>_impairmentProfile;set=>SetRef(ref _impairmentProfile,value);}
	public FinanceAssetPostingProfileOption? DisposalProfile{get=>_disposalProfile;set=>SetRef(ref _disposalProfile,value);}
	public FinanceAssetPeriodOption? SelectedPeriod{get=>_selectedPeriod;set=>SetRef(ref _selectedPeriod,value);}
	public FinanceAssetDepreciationPeriod? SelectedSchedulePeriod{get=>_selectedSchedulePeriod;set=>SetRef(ref _selectedSchedulePeriod,value);}
	public string AssetNumber{get=>_assetNumber;set=>Set(ref _assetNumber,value);}
	public string Description{get=>_description;set=>Set(ref _description,value);}
	public string Location{get=>_location;set=>Set(ref _location,value);}
	public string Custodian{get=>_custodian;set=>Set(ref _custodian,value);}
	public DateTime AcquisitionDate{get=>_acquisitionDate;set=>Set(ref _acquisitionDate,value);}
	public DateTime DepreciationStartDate{get=>_depreciationStartDate;set=>Set(ref _depreciationStartDate,value);}
	public decimal OriginalCost{get=>_originalCost;set=>Set(ref _originalCost,value);}
	public decimal SalvageValue{get=>_salvageValue;set=>Set(ref _salvageValue,value);}
	public int UsefulLifeMonths{get=>_usefulLifeMonths;set=>Set(ref _usefulLifeMonths,value);}
	public FinanceDepreciationMethod Method{get=>_method;set=>Set(ref _method,value);}
	public string ClassCode{get=>_classCode;set=>Set(ref _classCode,value);}
	public string ClassName{get=>_className;set=>Set(ref _className,value);}
	public int ClassUsefulLifeMonths{get=>_classUsefulLifeMonths;set=>Set(ref _classUsefulLifeMonths,value);}
	public FinanceDepreciationMethod ClassMethod{get=>_classMethod;set=>Set(ref _classMethod,value);}
	public string Reason{get=>_reason;set=>Set(ref _reason,value);}
	public decimal AdjustmentAmount{get=>_adjustmentAmount;set=>Set(ref _adjustmentAmount,value);}
	public decimal DisposalProceeds{get=>_disposalProceeds;set=>Set(ref _disposalProceeds,value);}
	public FinanceClosedPeriodPolicy ClosedPeriodPolicy{get=>_closedPeriodPolicy;set=>Set(ref _closedPeriodPolicy,value);}

	public async Task LoadAsync(CancellationToken token=default)
	{
		BeginOperation("Loading fixed assets...");
		try
		{
			Replace(LegalEntities,await _service.GetLegalEntitiesAsync(token));
			Replace(FiscalCalendars,await _service.GetFiscalCalendarsAsync(token));
			Replace(PostingProfiles,await _service.GetPostingProfilesAsync(token));
			Replace(Periods,await _service.GetPeriodOptionsAsync(token));
			Replace(Classes,await _service.GetClassesAsync(null,token));
			var page=await _service.SearchAssetsAsync(null,string.IsNullOrWhiteSpace(SearchText)?null:SearchText,null,1,200,token);
			var selectedId=SelectedAsset?.Id;
			Replace(Assets,page.Items);
			SelectedAsset=selectedId.HasValue?Assets.FirstOrDefault(v=>v.Id==selectedId.Value):Assets.FirstOrDefault();
			await LoadReconciliationAsync(token);
			CompleteOperation(false,$"{Assets.Count} fixed assets loaded.");
		}
		catch(OperationCanceledException)when(token.IsCancellationRequested){}
		catch(Exception ex){FailOperation(ex,"Fixed assets could not be loaded.");}
	}

	private async Task SaveAssetAsync(CancellationToken token)
	{
		BeginOperation("Saving asset...");
		try
		{
			var current=SelectedAsset;
			var cls=AssetClass??throw new InvalidOperationException("Select an asset class.");
			var entity=AssetEntity??throw new InvalidOperationException("Select a legal entity.");
			var currency=current?.Currency??new CurrencyCode("EUR");
			var value=new FinanceFixedAsset{Id=current?.Id??0,Version=current?.Version??1,AssetNumber=AssetNumber,LegalEntityId=entity.Id,AssetClassId=cls.Id,Description=Description,AcquisitionDate=DateOnly.FromDateTime(AcquisitionDate),CapitalizationDate=current?.CapitalizationDate,DepreciationStartDate=DateOnly.FromDateTime(DepreciationStartDate),Currency=currency,OriginalCost=OriginalCost,SalvageValue=SalvageValue,UsefulLifeMonths=UsefulLifeMonths,DepreciationMethod=Method,Location=Location,Custodian=Custodian,Status=current?.Status??FinanceAssetStatus.Draft,SourceSupplierDocumentLineId=current?.SourceSupplierDocumentLineId};
			SelectedAsset=await _service.SaveAssetAsync(value,token);
			await LoadAsync(token);
			CompleteOperation(false,"Asset saved.");
		}
		catch(Exception ex){FailOperation(ex,"Asset could not be saved.");}
	}

	private async Task SaveClassAsync(CancellationToken token)
	{
		BeginOperation("Saving asset class...");
		try
		{
			var entity=ClassEntity??throw new InvalidOperationException("Select a legal entity.");
			var calendar=ClassCalendar??throw new InvalidOperationException("Select a fiscal calendar.");
			var cap=CapitalizationProfile??throw new InvalidOperationException("Select a capitalization profile.");
			var dep=DepreciationProfile??throw new InvalidOperationException("Select a depreciation profile.");
			var imp=ImpairmentProfile??throw new InvalidOperationException("Select an impairment profile.");
			var dis=DisposalProfile??throw new InvalidOperationException("Select a disposal profile.");
			var current=SelectedClass;
			SelectedClass=await _service.SaveClassAsync(new FinanceAssetClass{Id=current?.Id??0,Version=current?.Version??1,LegalEntityId=entity.Id,FiscalCalendarId=calendar.Id,Code=ClassCode,Name=ClassName,CapitalizationPostingProfileId=cap.Id,DepreciationPostingProfileId=dep.Id,ImpairmentPostingProfileId=imp.Id,DisposalPostingProfileId=dis.Id,DefaultUsefulLifeMonths=ClassUsefulLifeMonths,DefaultMethod=ClassMethod,IsActive=current?.IsActive??true},token);
			await LoadAsync(token);
			CompleteOperation(false,"Asset class saved.");
		}
		catch(Exception ex){FailOperation(ex,"Asset class could not be saved.");}
	}

	private async Task RecalculateScheduleAsync(CancellationToken token){if(SelectedAsset is null)return;BeginOperation("Recalculating depreciation schedule...");try{Replace(Schedule,await _service.RecalculateScheduleAsync(SelectedAsset.Id,token));CompleteOperation(false,"Depreciation schedule recalculated.");}catch(Exception ex){FailOperation(ex,"Schedule could not be recalculated.");}}
	private async Task CapitalizeAsync(CancellationToken token){if(SelectedAsset is null||SelectedPeriod is null)return;BeginOperation("Capitalizing asset...");try{await _service.CapitalizeAsync(SelectedAsset.Id,Guid.NewGuid(),SelectedPeriod.Id,DateOnly.FromDateTime(DateTime.Today),token);await LoadAsync(token);CompleteOperation(false,"Asset capitalized.");}catch(Exception ex){FailOperation(ex,"Asset capitalization failed.");}}
	private async Task PostDepreciationAsync(CancellationToken token){if(SelectedSchedulePeriod is null)return;BeginOperation("Posting depreciation...");try{await _service.PostDepreciationAsync(SelectedSchedulePeriod.Id,Guid.NewGuid(),ClosedPeriodPolicy,token);await LoadAssetDetailsAsync(SelectedSchedulePeriod.AssetId,token);await LoadReconciliationAsync(token);CompleteOperation(false,"Depreciation posted.");}catch(Exception ex){FailOperation(ex,"Depreciation posting failed.");}}
	private async Task RunDepreciationAsync(CancellationToken token){if(SelectedPeriod is null)return;BeginOperation("Running period depreciation...");try{var result=await _service.RunDepreciationAsync(SelectedPeriod.Id,Guid.NewGuid(),ClosedPeriodPolicy,token);await LoadAsync(token);CompleteOperation(false,$"Posted {result.PostedCount} depreciation entries ({result.PostedAmount:N2}).");}catch(Exception ex){FailOperation(ex,"Depreciation run failed.");}}
	private async Task ImpairAsync(CancellationToken token){if(SelectedAsset is null||SelectedPeriod is null)return;BeginOperation("Posting impairment...");try{await _service.ImpairAsync(SelectedAsset.Id,Guid.NewGuid(),SelectedPeriod.Id,DateOnly.FromDateTime(DateTime.Today),AdjustmentAmount,Reason,token);await LoadAsync(token);CompleteOperation(false,"Impairment posted.");}catch(Exception ex){FailOperation(ex,"Impairment failed.");}}
	private async Task TransferAsync(CancellationToken token){if(SelectedAsset is null)return;BeginOperation("Transferring asset...");try{await _service.TransferAsync(SelectedAsset.Id,Guid.NewGuid(),DateOnly.FromDateTime(DateTime.Today),Location,Custodian,Reason,token);await LoadAsync(token);CompleteOperation(false,"Asset transfer recorded.");}catch(Exception ex){FailOperation(ex,"Asset transfer failed.");}}
	private async Task DisposeAssetAsync(CancellationToken token){if(SelectedAsset is null||SelectedPeriod is null)return;BeginOperation("Disposing asset...");try{await _service.DisposeAsync(SelectedAsset.Id,Guid.NewGuid(),SelectedPeriod.Id,DateOnly.FromDateTime(DateTime.Today),DisposalProceeds,Reason,token);await LoadAsync(token);CompleteOperation(false,"Asset disposed.");}catch(Exception ex){FailOperation(ex,"Asset disposal failed.");}}
	private async Task LoadAssetDetailsAsync(long id,CancellationToken token){Replace(Schedule,await _service.GetScheduleAsync(id,token));var tx=await _service.SearchTransactionsAsync(id,1,200,token);Replace(Transactions,tx.Items);}
	private async Task LoadReconciliationAsync(CancellationToken token){Reconciliation.Clear();foreach(var entity in LegalEntities){var page=await _service.SearchReconciliationAsync(entity.Id,1,200,token);foreach(var row in page.Items)Reconciliation.Add(row);}}

	private void ApplyAsset(FinanceFixedAsset v)
	{
		AssetNumber=v.AssetNumber;AssetEntity=LegalEntities.FirstOrDefault(x=>x.Id==v.LegalEntityId);AssetClass=Classes.FirstOrDefault(x=>x.Id==v.AssetClassId);Description=v.Description;AcquisitionDate=v.AcquisitionDate.ToDateTime(TimeOnly.MinValue);DepreciationStartDate=v.DepreciationStartDate.ToDateTime(TimeOnly.MinValue);OriginalCost=v.OriginalCost;SalvageValue=v.SalvageValue;UsefulLifeMonths=v.UsefulLifeMonths;Method=v.DepreciationMethod;Location=v.Location??string.Empty;Custodian=v.Custodian??string.Empty;
		var cls=AssetClass;SelectedPeriod=cls is null?Periods.FirstOrDefault(p=>p.Status==AccountingPeriodStatus.Open):Periods.FirstOrDefault(p=>p.FiscalCalendarId==cls.FiscalCalendarId&&p.StartDate<=DateOnly.FromDateTime(DateTime.Today)&&p.EndDate>=DateOnly.FromDateTime(DateTime.Today))??Periods.FirstOrDefault(p=>p.FiscalCalendarId==cls.FiscalCalendarId&&p.Status==AccountingPeriodStatus.Open);
	}
	private void ApplyClass(FinanceAssetClass v){ClassEntity=LegalEntities.FirstOrDefault(x=>x.Id==v.LegalEntityId);ClassCalendar=FiscalCalendars.FirstOrDefault(x=>x.Id==v.FiscalCalendarId);ClassCode=v.Code;ClassName=v.Name;ClassUsefulLifeMonths=v.DefaultUsefulLifeMonths;ClassMethod=v.DefaultMethod;CapitalizationProfile=PostingProfiles.FirstOrDefault(x=>x.Id==v.CapitalizationPostingProfileId);DepreciationProfile=PostingProfiles.FirstOrDefault(x=>x.Id==v.DepreciationPostingProfileId);ImpairmentProfile=PostingProfiles.FirstOrDefault(x=>x.Id==v.ImpairmentPostingProfileId);DisposalProfile=PostingProfiles.FirstOrDefault(x=>x.Id==v.DisposalPostingProfileId);}
	private void ClearAssetDraft(){SelectedAsset=null;AssetNumber=string.Empty;Description=string.Empty;AssetEntity=LegalEntities.FirstOrDefault();AssetClass=Classes.FirstOrDefault();AcquisitionDate=DateTime.Today;DepreciationStartDate=DateTime.Today;OriginalCost=0;SalvageValue=0;UsefulLifeMonths=AssetClass?.DefaultUsefulLifeMonths??60;Method=AssetClass?.DefaultMethod??FinanceDepreciationMethod.StraightLine;Location=string.Empty;Custodian=string.Empty;Schedule.Clear();Transactions.Clear();}
	private void ClearClassDraft(){SelectedClass=null;ClassEntity=LegalEntities.FirstOrDefault();ClassCalendar=FiscalCalendars.FirstOrDefault(c=>c.LegalEntityId==ClassEntity?.Id);ClassCode=string.Empty;ClassName=string.Empty;ClassUsefulLifeMonths=60;ClassMethod=FinanceDepreciationMethod.StraightLine;CapitalizationProfile=PostingProfiles.FirstOrDefault(p=>p.SourceEvent==FinanceFixedAssetService.CapitalizationEvent);DepreciationProfile=PostingProfiles.FirstOrDefault(p=>p.SourceEvent==FinanceFixedAssetService.DepreciationEvent);ImpairmentProfile=PostingProfiles.FirstOrDefault(p=>p.SourceEvent==FinanceFixedAssetService.ImpairmentEvent);DisposalProfile=PostingProfiles.FirstOrDefault(p=>p.SourceEvent==FinanceFixedAssetService.DisposalEvent);}
	private void Set<T>(ref T field,T value,[System.Runtime.CompilerServices.CallerMemberName]string? name=null){if(EqualityComparer<T>.Default.Equals(field,value))return;field=value;OnPropertyChanged(name);}
	private void SetRef<T>(ref T? field,T? value,[System.Runtime.CompilerServices.CallerMemberName]string? name=null)where T:class{if(ReferenceEquals(field,value))return;field=value;OnPropertyChanged(name);}
	private static void Replace<T>(ObservableCollection<T> target,IEnumerable<T> values){target.Clear();foreach(var value in values)target.Add(value);}
	public void Dispose(){if(_disposed)return;_disposed=true;foreach(var command in new[]{RefreshCommand,NewAssetCommand,SaveAssetCommand,RecalculateScheduleCommand,CapitalizeCommand,PostDepreciationCommand,RunDepreciationCommand,ImpairCommand,TransferCommand,DisposeCommand,NewClassCommand,SaveClassCommand})command.Dispose();}
}
