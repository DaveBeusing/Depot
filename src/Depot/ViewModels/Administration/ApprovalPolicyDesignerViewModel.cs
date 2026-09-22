// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Commands;
using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels.Administration;

public sealed class ApprovalPolicyDesignerViewModel : BaseViewModel, IDisposable
{
	private readonly ApprovalPolicyService _service;
	private readonly IAuthorizationService _authorization;
	private ApprovalPolicy? _selectedPolicy;
	private Guid _id;
	private int _version = 1;
	private string _name = string.Empty;
	private string? _description;
	private ApprovalSubjectKind _subjectKind = ApprovalSubjectKind.PurchaseOrder;
	private int _priority;
	private bool _isActive;
	private DateTime? _effectiveFromUtc;
	private DateTime? _effectiveToUtc;
	private bool _isDirty;
	private bool _applying;
	private ApprovalPolicyConditionEditorViewModel? _selectedCondition;
	private ApprovalPolicyStageEditorViewModel? _selectedStage;
	private ApprovalApproverEditorViewModel? _selectedApprover;
	private decimal? _previewAmount;
	private string? _previewCurrency;
	private Guid? _previewLegalEntityId;
	private Guid? _previewAccountingBookId;
	private ApprovalResolutionPreview? _preview;
	private ApprovalFlowProjection? _previewFlow;

	public ApprovalPolicyDesignerViewModel(ApprovalPolicyService service, IAuthorizationService authorization)
	{
		_service = service;
		_authorization = authorization;
		NewCommand = new RelayCommand(NewPolicy, () => CanManage);
		SaveCommand = new AsyncRelayCommand(SaveAsync, () => CanManage && !string.IsNullOrWhiteSpace(Name));
		ToggleActiveCommand = new AsyncRelayCommand(ToggleActiveAsync, () => CanManage && Id != Guid.Empty);
		PreviewCommand = new AsyncRelayCommand(PreviewAsync, () => !string.IsNullOrWhiteSpace(Name));
		AddConditionCommand = new RelayCommand(AddCondition, () => CanManage);
		RemoveConditionCommand = new RelayCommand(RemoveCondition, () => CanManage && SelectedCondition is not null);
		AddStageCommand = new RelayCommand(AddStage, () => CanManage);
		RemoveStageCommand = new RelayCommand(RemoveStage, () => CanManage && SelectedStage is not null);
		AddApproverCommand = new RelayCommand(AddApprover, () => CanManage && SelectedStage is not null);
		RemoveApproverCommand = new RelayCommand(RemoveApprover, () => CanManage && SelectedStage is not null && SelectedApprover is not null);
		DiscardCommand = new RelayCommand(DiscardUnsavedChanges, () => IsDirty);
	}

	public ObservableCollection<ApprovalPolicy> Policies { get; } = [];
	public ObservableCollection<ApprovalPolicyConditionEditorViewModel> Conditions { get; } = [];
	public ObservableCollection<ApprovalPolicyStageEditorViewModel> Stages { get; } = [];
	public ObservableCollection<ApprovalPolicyValidationIssue> ValidationIssues { get; } = [];
	public IReadOnlyList<ApprovalSubjectKind> SubjectKinds { get; } = Enum.GetValues<ApprovalSubjectKind>();
	public IReadOnlyList<ApprovalConditionKind> ConditionKinds { get; } = Enum.GetValues<ApprovalConditionKind>();
	public IReadOnlyList<ApprovalApproverKind> ApproverKinds { get; } = Enum.GetValues<ApprovalApproverKind>();
	public RelayCommand NewCommand { get; }
	public AsyncRelayCommand SaveCommand { get; }
	public AsyncRelayCommand ToggleActiveCommand { get; }
	public AsyncRelayCommand PreviewCommand { get; }
	public RelayCommand AddConditionCommand { get; }
	public RelayCommand RemoveConditionCommand { get; }
	public RelayCommand AddStageCommand { get; }
	public RelayCommand RemoveStageCommand { get; }
	public RelayCommand AddApproverCommand { get; }
	public RelayCommand RemoveApproverCommand { get; }
	public RelayCommand DiscardCommand { get; }
	public bool CanManage => _authorization.HasPermission(ApplicationPermission.ApprovalPoliciesManage);
	public Guid Id { get => _id; private set { _id=value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNew)); RaiseCommands(); } }
	public bool IsNew => Id == Guid.Empty;
	public string Name { get=>_name; set=>Set(ref _name,value); }
	public string? Description { get=>_description; set=>Set(ref _description,value); }
	public ApprovalSubjectKind SubjectKind { get=>_subjectKind; set=>Set(ref _subjectKind,value); }
	public int Priority { get=>_priority; set=>Set(ref _priority,value); }
	public bool IsActive { get=>_isActive; private set { _isActive=value; OnPropertyChanged(); OnPropertyChanged(nameof(ActivationButtonText)); } }
	public DateTime? EffectiveFromUtc { get=>_effectiveFromUtc; set=>Set(ref _effectiveFromUtc,value); }
	public DateTime? EffectiveToUtc { get=>_effectiveToUtc; set=>Set(ref _effectiveToUtc,value); }
	public bool IsDirty { get=>_isDirty; private set { if(_isDirty==value)return;_isDirty=value;OnPropertyChanged();DiscardCommand.RaiseCanExecuteChanged(); } }
	public string ActivationButtonText => IsActive ? "Deactivate" : "Activate";
	public ApprovalPolicy? SelectedPolicy { get=>_selectedPolicy; set { if(_selectedPolicy==value)return;if(IsDirty&&value is not null&&value.Id!=Id)return;_selectedPolicy=value;OnPropertyChanged();if(value is not null)Apply(value); } }
	public ApprovalPolicyConditionEditorViewModel? SelectedCondition { get=>_selectedCondition; set { if(_selectedCondition==value)return;_selectedCondition=value;OnPropertyChanged();RaiseCommands(); } }
	public ApprovalPolicyStageEditorViewModel? SelectedStage { get=>_selectedStage; set { if(_selectedStage==value)return;_selectedStage=value;SelectedApprover=null;OnPropertyChanged();RaiseCommands(); } }
	public ApprovalApproverEditorViewModel? SelectedApprover { get=>_selectedApprover; set { if(_selectedApprover==value)return;_selectedApprover=value;OnPropertyChanged();RaiseCommands(); } }
	public decimal? PreviewAmount { get=>_previewAmount; set { _previewAmount=value;OnPropertyChanged(); } }
	public string? PreviewCurrency { get=>_previewCurrency; set { _previewCurrency=value;OnPropertyChanged(); } }
	public Guid? PreviewLegalEntityId { get=>_previewLegalEntityId; set { _previewLegalEntityId=value;OnPropertyChanged(); } }
	public Guid? PreviewAccountingBookId { get=>_previewAccountingBookId; set { _previewAccountingBookId=value;OnPropertyChanged(); } }
	public ApprovalResolutionPreview? Preview { get=>_preview; private set { _preview=value;OnPropertyChanged();OnPropertyChanged(nameof(PreviewSummary)); } }
	public ApprovalFlowProjection? PreviewFlow { get=>_previewFlow; private set { _previewFlow=value;OnPropertyChanged(); } }
	public string PreviewSummary => Preview is null ? "Enter bounded sample attributes to resolve a policy without changing business data." : $"Resolved {Preview.Policy.Name} v{Preview.Policy.Version} with {Preview.Snapshot.Stages.Count} stage(s).";

	public async Task LoadAsync(CancellationToken cancellationToken = default)
	{
		BeginOperation("Loading approval policies");
		try
		{
			var policies=await _service.GetPageAsync(null,0,200,cancellationToken);
			CollectionSynchronizer.Replace(Policies,policies);
			if(SelectedPolicy is null&&Policies.Count>0)SelectedPolicy=Policies[0];
			CompleteOperation(Policies.Count==0,$"{Policies.Count:N0} approval policies");
		}
		catch(Exception exception) when(exception is not OperationCanceledException){FailOperation(exception,"Approval policies could not be loaded");}
	}

	private void NewPolicy()
	{
		_selectedPolicy=null;OnPropertyChanged(nameof(SelectedPolicy));_applying=true;
		Id=Guid.Empty;_version=1;_name=string.Empty;_description=null;_subjectKind=ApprovalSubjectKind.PurchaseOrder;_priority=0;IsActive=false;_effectiveFromUtc=null;_effectiveToUtc=null;
		Conditions.Clear();Stages.Clear();Stages.Add(new ApprovalPolicyStageEditorViewModel(new ApprovalPolicyStage{Order=1,Name="Approval 1"},MarkDirty));
		ValidationIssues.Clear();Preview=null;PreviewFlow=null;_applying=false;NotifyEditor();IsDirty=false;
	}

	private async Task SaveAsync(CancellationToken cancellationToken)
	{
		BeginOperation("Saving approval policy");
		try
		{
			var candidate=BuildPolicy();
			var issues=await _service.ValidateAsync(candidate,cancellationToken);
			CollectionSynchronizer.Replace(ValidationIssues,issues);
			if(issues.Count>0)throw new InvalidOperationException("Resolve the listed policy validation issues before saving.");
			var saved=Id==Guid.Empty?await _service.CreateAsync(candidate,cancellationToken):await _service.UpdateAsync(candidate,_version,cancellationToken);
			ReplacePolicy(saved);Apply(saved);CompleteOperation(false,"Approval policy saved");
		}
		catch(Exception exception) when(exception is not OperationCanceledException){FailOperation(exception,"Approval policy could not be saved");}
	}

	private async Task ToggleActiveAsync(CancellationToken cancellationToken)
	{
		BeginOperation(IsActive?"Deactivating approval policy":"Activating approval policy");
		try
		{
			if(IsDirty)throw new InvalidOperationException("Save or discard editor changes before changing activation.");
			var saved=IsActive?await _service.DeactivateAsync(Id,_version,cancellationToken):await _service.ActivateAsync(Id,_version,cancellationToken);
			ReplacePolicy(saved);Apply(saved);CompleteOperation(false,saved.IsActive?"Approval policy active":"Approval policy inactive");
		}
		catch(Exception exception) when(exception is not OperationCanceledException){FailOperation(exception,"Approval policy activation could not be changed");}
	}

	private async Task PreviewAsync(CancellationToken cancellationToken)
	{
		BeginOperation("Resolving approval policy preview");
		try
		{
			CollectionSynchronizer.Replace(ValidationIssues,await _service.ValidateAsync(BuildPolicy(),cancellationToken));
			var preview=await _service.PreviewAsync(new ApprovalSubjectAttributes(SubjectKind,"preview",PreviewAmount,PreviewCurrency,PreviewLegalEntityId,PreviewAccountingBookId),cancellationToken);
			Preview=preview;PreviewFlow=ApprovalFlowProjectionService.ProjectPolicyPreview(preview);CompleteOperation(false,"Approval preview resolved");
		}
		catch(Exception exception) when(exception is not OperationCanceledException){Preview=null;PreviewFlow=null;FailOperation(exception,"Approval preview could not be resolved");}
	}

	private ApprovalPolicy BuildPolicy()=>new(){Id=Id,Version=_version,Name=Name,Description=Description,SubjectKind=SubjectKind,Priority=Priority,IsActive=IsActive,EffectiveFromUtc=EffectiveFromUtc,EffectiveToUtc=EffectiveToUtc,Conditions=Conditions.Select(value=>value.ToModel()).ToList(),Stages=Stages.Select(value=>value.ToModel()).OrderBy(value=>value.Order).ToList()};

	private void Apply(ApprovalPolicy policy)
	{
		_applying=true;Id=policy.Id;_version=policy.Version;_name=policy.Name;_description=policy.Description;_subjectKind=policy.SubjectKind;_priority=policy.Priority;IsActive=policy.IsActive;_effectiveFromUtc=policy.EffectiveFromUtc;_effectiveToUtc=policy.EffectiveToUtc;
		Conditions.Clear();foreach(var condition in policy.Conditions)Conditions.Add(new ApprovalPolicyConditionEditorViewModel(condition,MarkDirty));
		Stages.Clear();foreach(var stage in policy.Stages.OrderBy(value=>value.Order))Stages.Add(new ApprovalPolicyStageEditorViewModel(stage,MarkDirty));
		ValidationIssues.Clear();Preview=null;PreviewFlow=null;_applying=false;NotifyEditor();IsDirty=false;
	}
	private void ReplacePolicy(ApprovalPolicy policy){var existing=Policies.FirstOrDefault(value=>value.Id==policy.Id);if(existing is null)Policies.Insert(0,policy);else Policies[Policies.IndexOf(existing)]=policy;_selectedPolicy=policy;OnPropertyChanged(nameof(SelectedPolicy));}
	private void AddCondition(){Conditions.Add(new ApprovalPolicyConditionEditorViewModel(new ApprovalPolicyCondition{Kind=ApprovalConditionKind.MinimumAmount},MarkDirty));SelectedCondition=Conditions[^1];MarkDirty();}
	private void RemoveCondition(){if(SelectedCondition is null)return;Conditions.Remove(SelectedCondition);SelectedCondition=null;MarkDirty();}
	private void AddStage(){var order=Stages.Count==0?1:Stages.Max(value=>value.Order)+1;Stages.Add(new ApprovalPolicyStageEditorViewModel(new ApprovalPolicyStage{Order=order,Name=$"Approval {order}"},MarkDirty));SelectedStage=Stages[^1];MarkDirty();}
	private void RemoveStage(){if(SelectedStage is null)return;Stages.Remove(SelectedStage);SelectedStage=null;MarkDirty();}
	private void AddApprover(){if(SelectedStage is null)return;SelectedStage.Approvers.Add(new ApprovalApproverEditorViewModel(new ApprovalApproverTarget{Kind=ApprovalApproverKind.Role},MarkDirty));SelectedApprover=SelectedStage.Approvers[^1];MarkDirty();}
	private void RemoveApprover(){if(SelectedStage is null||SelectedApprover is null)return;SelectedStage.Approvers.Remove(SelectedApprover);SelectedApprover=null;MarkDirty();}
	public void DiscardUnsavedChanges(){if(_selectedPolicy is not null)Apply(_selectedPolicy);else NewPolicy();IsDirty=false;}
	private void MarkDirty(){if(!_applying)IsDirty=true;RaiseCommands();}
	private void NotifyEditor(){OnPropertyChanged(nameof(Name));OnPropertyChanged(nameof(Description));OnPropertyChanged(nameof(SubjectKind));OnPropertyChanged(nameof(Priority));OnPropertyChanged(nameof(EffectiveFromUtc));OnPropertyChanged(nameof(EffectiveToUtc));OnPropertyChanged(nameof(IsNew));RaiseCommands();}
	private void RaiseCommands(){NewCommand.RaiseCanExecuteChanged();SaveCommand.RaiseCanExecuteChanged();ToggleActiveCommand.RaiseCanExecuteChanged();PreviewCommand.RaiseCanExecuteChanged();AddConditionCommand.RaiseCanExecuteChanged();RemoveConditionCommand.RaiseCanExecuteChanged();AddStageCommand.RaiseCanExecuteChanged();RemoveStageCommand.RaiseCanExecuteChanged();AddApproverCommand.RaiseCanExecuteChanged();RemoveApproverCommand.RaiseCanExecuteChanged();}
	private void Set<T>(ref T field,T value){if(EqualityComparer<T>.Default.Equals(field,value))return;field=value;OnPropertyChanged();MarkDirty();}
	public void Dispose(){SaveCommand.Dispose();ToggleActiveCommand.Dispose();PreviewCommand.Dispose();}

	public sealed class ApprovalPolicyConditionEditorViewModel : BaseViewModel
	{
		private readonly Action _changed;private ApprovalConditionKind _kind;private decimal? _decimalValue;private string? _stringValue;private Guid? _guidValue;
		public ApprovalPolicyConditionEditorViewModel(ApprovalPolicyCondition value,Action changed){_changed=changed;_kind=value.Kind;_decimalValue=value.DecimalValue;_stringValue=value.StringValue;_guidValue=value.GuidValue;}
		public ApprovalConditionKind Kind{get=>_kind;set=>Set(ref _kind,value);} public decimal? DecimalValue{get=>_decimalValue;set=>Set(ref _decimalValue,value);} public string? StringValue{get=>_stringValue;set=>Set(ref _stringValue,value);} public Guid? GuidValue{get=>_guidValue;set=>Set(ref _guidValue,value);}
		public ApprovalPolicyCondition ToModel()=>new(){Kind=Kind,DecimalValue=DecimalValue,StringValue=StringValue,GuidValue=GuidValue};
		private void Set<T>(ref T field,T value){if(EqualityComparer<T>.Default.Equals(field,value))return;field=value;OnPropertyChanged();_changed();}
	}
	public sealed class ApprovalPolicyStageEditorViewModel : BaseViewModel
	{
		private readonly Action _changed;private int _order;private string _name;
		public ApprovalPolicyStageEditorViewModel(ApprovalPolicyStage value,Action changed){_changed=changed;_order=value.Order;_name=value.Name;foreach(var approver in value.Approvers)Approvers.Add(new ApprovalApproverEditorViewModel(approver,changed));}
		public int Order{get=>_order;set=>Set(ref _order,value);} public string Name{get=>_name;set=>Set(ref _name,value);} public ObservableCollection<ApprovalApproverEditorViewModel> Approvers{get;}=[];
		public ApprovalPolicyStage ToModel()=>new(){Order=Order,Name=Name,Approvers=Approvers.Select(value=>value.ToModel()).ToList()};
		private void Set<T>(ref T field,T value){if(EqualityComparer<T>.Default.Equals(field,value))return;field=value;OnPropertyChanged();_changed();}
	}
	public sealed class ApprovalApproverEditorViewModel : BaseViewModel
	{
		private readonly Action _changed;private ApprovalApproverKind _kind;private string? _roleCode;private long? _userId;
		public ApprovalApproverEditorViewModel(ApprovalApproverTarget value,Action changed){_changed=changed;_kind=value.Kind;_roleCode=value.RoleCode;_userId=value.UserId;}
		public ApprovalApproverKind Kind{get=>_kind;set=>Set(ref _kind,value);} public string? RoleCode{get=>_roleCode;set=>Set(ref _roleCode,value);} public long? UserId{get=>_userId;set=>Set(ref _userId,value);}
		public ApprovalApproverTarget ToModel()=>new(){Kind=Kind,RoleCode=Kind==ApprovalApproverKind.Role?RoleCode:null,UserId=Kind==ApprovalApproverKind.User?UserId:null};
		private void Set<T>(ref T field,T value){if(EqualityComparer<T>.Default.Equals(field,value))return;field=value;OnPropertyChanged();_changed();}
	}
}
