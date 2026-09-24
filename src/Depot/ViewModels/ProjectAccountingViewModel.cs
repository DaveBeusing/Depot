// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;
using System.Globalization;

using Depot.Commands;
using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels;

public sealed class ProjectAccountingViewModel : BaseViewModel
{
	private readonly ProjectAccountingService _service;
	private ProjectRecord? _selectedProject;
	private ProjectPhase? _selectedPhase;
	private string _projectCode = string.Empty;
	private string _projectName = string.Empty;
	private string _legalEntityId = string.Empty;
	private string _ownerUserId = string.Empty;
	private string _customerId = string.Empty;
	private string _projectDescription = string.Empty;
	private DateTime? _plannedStartDate;
	private DateTime? _plannedEndDate;
	private string _phaseCode = string.Empty;
	private string _phaseName = string.Empty;
	private string _phaseDescription = string.Empty;
	private DateTime? _phasePlannedStartDate;
	private DateTime? _phasePlannedEndDate;
	private ProjectFinancialSummary? _summary;
	private ProjectAttribution? _selectedAttribution;
	private ProjectAttributionEntityKind _selectedAttributionKind = ProjectAttributionEntityKind.PurchaseOrder;
	private string _attributionEntityId = string.Empty;
	private ProjectBudgetLineOption? _selectedBudgetLineOption;
	private ProjectBudgetLink? _selectedBudgetLink;
	private string _budgetCategoryCode = string.Empty;

	public ProjectAccountingViewModel(
		ProjectAccountingService service,
		BusinessAttachmentService attachmentService,
		IFileDialogService fileDialogs)
	{
		_service = service ?? throw new ArgumentNullException(nameof(service));
		Attachments = new BusinessAttachmentPanelViewModel(
			attachmentService ?? throw new ArgumentNullException(nameof(attachmentService)),
			fileDialogs ?? throw new ArgumentNullException(nameof(fileDialogs)));
		RefreshCommand = new AsyncRelayCommand(LoadAsync);
		NewProjectCommand = new RelayCommand(NewProject, () => CanManage);
		SaveProjectCommand = new AsyncRelayCommand(SaveProjectAsync, () => CanManage);
		ActivateCommand = new AsyncRelayCommand(token => TransitionAsync(ProjectStatus.Active, token), () => CanManage && SelectedProject?.Status is ProjectStatus.Draft or ProjectStatus.OnHold);
		HoldCommand = new AsyncRelayCommand(token => TransitionAsync(ProjectStatus.OnHold, token), () => CanManage && SelectedProject?.Status == ProjectStatus.Active);
		CloseCommand = new AsyncRelayCommand(token => TransitionAsync(ProjectStatus.Closed, token), () => CanManage && SelectedProject?.Status is ProjectStatus.Active or ProjectStatus.OnHold);
		CancelCommand = new AsyncRelayCommand(token => TransitionAsync(ProjectStatus.Cancelled, token), () => CanManage && SelectedProject?.Status is ProjectStatus.Draft or ProjectStatus.Active or ProjectStatus.OnHold);
		NewPhaseCommand = new RelayCommand(NewPhase, () => CanManage && SelectedProject?.AcceptsOperationalActivity == true);
		SavePhaseCommand = new AsyncRelayCommand(SavePhaseAsync, () => CanManage && SelectedProject?.AcceptsOperationalActivity == true);
		ActivatePhaseCommand = new AsyncRelayCommand(token => TransitionPhaseAsync(ProjectPhaseStatus.Active, token), () => CanManage && SelectedProject?.AcceptsOperationalActivity == true && SelectedPhase?.Status == ProjectPhaseStatus.Planned);
		CompletePhaseCommand = new AsyncRelayCommand(token => TransitionPhaseAsync(ProjectPhaseStatus.Completed, token), () => CanManage && SelectedProject?.AcceptsOperationalActivity == true && SelectedPhase?.Status == ProjectPhaseStatus.Active);
		CancelPhaseCommand = new AsyncRelayCommand(token => TransitionPhaseAsync(ProjectPhaseStatus.Cancelled, token), () => CanManage && SelectedProject?.AcceptsOperationalActivity == true && SelectedPhase?.Status is ProjectPhaseStatus.Planned or ProjectPhaseStatus.Active);
		AttributeCommand = new AsyncRelayCommand(AttributeAsync, () => CanManageAttributions && SelectedProject?.AcceptsOperationalActivity == true);
		RemoveAttributionCommand = new AsyncRelayCommand(RemoveAttributionAsync, () => CanManageAttributions && SelectedAttribution is { IsImmutable: false });
		LinkBudgetLineCommand = new AsyncRelayCommand(LinkBudgetLineAsync, () => CanManageBudgetLinks && SelectedProject?.AcceptsOperationalActivity == true && SelectedBudgetLineOption is not null);
		UnlinkBudgetLineCommand = new AsyncRelayCommand(UnlinkBudgetLineAsync, () => CanManageBudgetLinks && SelectedProject?.AcceptsOperationalActivity == true && SelectedBudgetLink is not null);
	}

	public ObservableCollection<ProjectRecord> Projects { get; } = [];
	public ObservableCollection<ProjectPhase> Phases { get; } = [];
	public ObservableCollection<ProjectActualRow> Actuals { get; } = [];
	public ObservableCollection<ProjectCommitmentRow> Commitments { get; } = [];
	public ObservableCollection<ProjectBudgetVarianceRow> VarianceRows { get; } = [];
	public ObservableCollection<ProjectAttribution> Attributions { get; } = [];
	public ObservableCollection<ProjectBudgetLink> BudgetLinks { get; } = [];
	public ObservableCollection<ProjectBudgetLineOption> BudgetLineOptions { get; } = [];
	public BusinessAttachmentPanelViewModel Attachments { get; }
	public IReadOnlyList<ProjectAttributionEntityKind> AttributionKinds { get; } = Enum.GetValues<ProjectAttributionEntityKind>();

	public AsyncRelayCommand RefreshCommand { get; }
	public RelayCommand NewProjectCommand { get; }
	public AsyncRelayCommand SaveProjectCommand { get; }
	public AsyncRelayCommand ActivateCommand { get; }
	public AsyncRelayCommand HoldCommand { get; }
	public AsyncRelayCommand CloseCommand { get; }
	public AsyncRelayCommand CancelCommand { get; }
	public RelayCommand NewPhaseCommand { get; }
	public AsyncRelayCommand SavePhaseCommand { get; }
	public AsyncRelayCommand ActivatePhaseCommand { get; }
	public AsyncRelayCommand CompletePhaseCommand { get; }
	public AsyncRelayCommand CancelPhaseCommand { get; }
	public AsyncRelayCommand AttributeCommand { get; }
	public AsyncRelayCommand RemoveAttributionCommand { get; }
	public AsyncRelayCommand LinkBudgetLineCommand { get; }
	public AsyncRelayCommand UnlinkBudgetLineCommand { get; }

	public bool CanManage => _service.CanManage;
	public bool CanViewFinancials => _service.CanViewFinancials;
	public bool CanManageAttributions => _service.CanManageAttributions;
	public bool CanManageBudgetLinks => _service.CanManageBudgetLinks;

	public ProjectRecord? SelectedProject
	{
		get => _selectedProject;
		set
		{
			if (ReferenceEquals(_selectedProject, value)) return;
			_selectedProject = value;
			OnPropertyChanged();
			LoadProjectEditor(value);
			RaiseCommands();
			_ = Attachments.SetTargetAsync(BusinessAttachmentEntityKind.Project, value?.Id);
			if (value is not null) _ = LoadSelectedAsync(value.Id, CancellationToken.None);
		}
	}

	public ProjectAttribution? SelectedAttribution
	{
		get => _selectedAttribution;
		set { if (ReferenceEquals(_selectedAttribution, value)) return; _selectedAttribution = value; OnPropertyChanged(); RemoveAttributionCommand.RaiseCanExecuteChanged(); }
	}

	public ProjectAttributionEntityKind SelectedAttributionKind { get => _selectedAttributionKind; set => Set(ref _selectedAttributionKind, value); }
	public string AttributionEntityId { get => _attributionEntityId; set => Set(ref _attributionEntityId, value); }

	public ProjectBudgetLineOption? SelectedBudgetLineOption
	{
		get => _selectedBudgetLineOption;
		set { if (ReferenceEquals(_selectedBudgetLineOption, value)) return; _selectedBudgetLineOption = value; OnPropertyChanged(); LinkBudgetLineCommand.RaiseCanExecuteChanged(); }
	}

	public ProjectBudgetLink? SelectedBudgetLink
	{
		get => _selectedBudgetLink;
		set { if (ReferenceEquals(_selectedBudgetLink, value)) return; _selectedBudgetLink = value; OnPropertyChanged(); UnlinkBudgetLineCommand.RaiseCanExecuteChanged(); }
	}

	public string BudgetCategoryCode { get => _budgetCategoryCode; set => Set(ref _budgetCategoryCode, value); }

	public ProjectPhase? SelectedPhase
	{
		get => _selectedPhase;
		set
		{
			if (ReferenceEquals(_selectedPhase, value)) return;
			_selectedPhase = value;
			OnPropertyChanged();
			_phaseCode = value?.Code ?? string.Empty;
			_phaseName = value?.Name ?? string.Empty;
			_phaseDescription = value?.Description ?? string.Empty;
			_phasePlannedStartDate = value?.PlannedStartDate?.ToDateTime(TimeOnly.MinValue);
			_phasePlannedEndDate = value?.PlannedEndDate?.ToDateTime(TimeOnly.MinValue);
			OnPropertyChanged(nameof(PhaseCode));
			OnPropertyChanged(nameof(PhaseName));
			OnPropertyChanged(nameof(PhaseDescription));
			OnPropertyChanged(nameof(PhasePlannedStartDate));
			OnPropertyChanged(nameof(PhasePlannedEndDate));
			RaiseCommands();
		}
	}

	public string ProjectCode { get => _projectCode; set => Set(ref _projectCode, value); }
	public string ProjectName { get => _projectName; set => Set(ref _projectName, value); }
	public string LegalEntityId { get => _legalEntityId; set => Set(ref _legalEntityId, value); }
	public string OwnerUserId { get => _ownerUserId; set => Set(ref _ownerUserId, value); }
	public string CustomerId { get => _customerId; set => Set(ref _customerId, value); }
	public string ProjectDescription { get => _projectDescription; set => Set(ref _projectDescription, value); }
	public DateTime? PlannedStartDate { get => _plannedStartDate; set => Set(ref _plannedStartDate, value); }
	public DateTime? PlannedEndDate { get => _plannedEndDate; set => Set(ref _plannedEndDate, value); }
	public string PhaseCode { get => _phaseCode; set => Set(ref _phaseCode, value); }
	public string PhaseName { get => _phaseName; set => Set(ref _phaseName, value); }
	public string PhaseDescription { get => _phaseDescription; set => Set(ref _phaseDescription, value); }
	public DateTime? PhasePlannedStartDate { get => _phasePlannedStartDate; set => Set(ref _phasePlannedStartDate, value); }
	public DateTime? PhasePlannedEndDate { get => _phasePlannedEndDate; set => Set(ref _phasePlannedEndDate, value); }
	public ProjectFinancialSummary? Summary { get => _summary; private set { _summary=value; OnPropertyChanged(); OnPropertyChanged(nameof(FinancialSummaryText)); } }

	public string FinancialSummaryText => Summary is null
		? "Select a project to load GL actuals, open purchasing commitments and budget variance."
		: $"{Summary.ActualEntryCount:N0} actual entries · {Summary.CommitmentLineCount:N0} open commitment lines · {Summary.OpenPurchaseCommitments:N2} open purchasing commitment";

	public async Task LoadAsync(CancellationToken cancellationToken = default)
	{
		BeginOperation("Loading projects...");
		try
		{
			var selectedId = SelectedProject?.Id;
			var page = await _service.SearchAsync(new ProjectListFilter(), 1, 250, cancellationToken);
			Replace(Projects, page.Items);
			SelectedProject = selectedId is long id ? Projects.FirstOrDefault(value => value.Id == id) ?? Projects.FirstOrDefault() : Projects.FirstOrDefault();
			CompleteOperation(Projects.Count == 0, Projects.Count == 0 ? "No projects are available." : $"{Projects.Count:N0} project(s) loaded.");
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
		catch (Exception exception) { FailOperation(exception, "Projects could not be loaded."); }
	}


	public async Task OpenProjectAsync(long projectId, CancellationToken cancellationToken = default)
	{
		if (Projects.Count == 0) await LoadAsync(cancellationToken);
		var project = Projects.FirstOrDefault(value => value.Id == projectId) ?? await _service.GetAsync(projectId, cancellationToken);
		if (Projects.All(value => value.Id != project.Id)) Projects.Add(project);
		SelectedProject = project;
		await LoadSelectedAsync(project.Id, cancellationToken);
	}

	private async Task LoadSelectedAsync(long projectId, CancellationToken cancellationToken)
	{
		try
		{
			var phasesTask = _service.ListPhasesAsync(projectId, cancellationToken);
			var attributionsTask = _service.ListAttributionsAsync(projectId, cancellationToken);
			Task<IReadOnlyList<ProjectBudgetLink>> budgetLinksTask = CanViewFinancials ? _service.ListBudgetLinksAsync(projectId, cancellationToken) : Task.FromResult<IReadOnlyList<ProjectBudgetLink>>([]);
			Task<IReadOnlyList<ProjectBudgetLineOption>> budgetOptionsTask = CanViewFinancials ? _service.ListBudgetLineOptionsAsync(projectId, cancellationToken: cancellationToken) : Task.FromResult<IReadOnlyList<ProjectBudgetLineOption>>([]);
			Task<IReadOnlyList<ProjectActualRow>> actualsTask = CanViewFinancials ? _service.ListActualsAsync(projectId, cancellationToken: cancellationToken) : Task.FromResult<IReadOnlyList<ProjectActualRow>>([]);
			Task<IReadOnlyList<ProjectCommitmentRow>> commitmentsTask = CanViewFinancials ? _service.ListCommitmentsAsync(projectId, cancellationToken: cancellationToken) : Task.FromResult<IReadOnlyList<ProjectCommitmentRow>>([]);
			Task<IReadOnlyList<ProjectBudgetVarianceRow>> varianceTask = CanViewFinancials ? _service.GetBudgetVarianceAsync(projectId, cancellationToken: cancellationToken) : Task.FromResult<IReadOnlyList<ProjectBudgetVarianceRow>>([]);
			Task<ProjectFinancialSummary?> summaryTask = CanViewFinancials ? LoadSummaryAsync(projectId, cancellationToken) : Task.FromResult<ProjectFinancialSummary?>(null);
			await Task.WhenAll(phasesTask, attributionsTask, budgetLinksTask, budgetOptionsTask, actualsTask, commitmentsTask, varianceTask, summaryTask);
			Replace(Phases, await phasesTask);
			Replace(Attributions, await attributionsTask);
			Replace(BudgetLinks, await budgetLinksTask);
			Replace(BudgetLineOptions, await budgetOptionsTask);
			Replace(Actuals, await actualsTask);
			Replace(Commitments, await commitmentsTask);
			Replace(VarianceRows, await varianceTask);
			Summary = await summaryTask;
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
		catch (Exception exception) { FailOperation(exception, "Project details could not be loaded."); }
	}

	private async Task AttributeAsync(CancellationToken cancellationToken)
	{
		var project = SelectedProject ?? throw new InvalidOperationException("Select a project first.");
		if (!long.TryParse(AttributionEntityId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var entityId) || entityId <= 0)
			throw new InvalidOperationException("Source entity ID must be a positive number.");
		BeginOperation("Saving project attribution...");
		try
		{
			await _service.AttributeAsync(new ProjectAttributionRequest(project.Id, SelectedPhase?.Id, SelectedAttributionKind, entityId), cancellationToken);
			Replace(Attributions, await _service.ListAttributionsAsync(project.Id, cancellationToken));
			AttributionEntityId = string.Empty;
			CompleteOperation(false, "Project attribution saved.");
		}
		catch (Exception exception) { FailOperation(exception, "Project attribution could not be saved."); }
	}

	private async Task RemoveAttributionAsync(CancellationToken cancellationToken)
	{
		var project = SelectedProject;
		var attribution = SelectedAttribution;
		if (project is null || attribution is null) return;
		BeginOperation("Removing project attribution...");
		try
		{
			await _service.RemoveAttributionAsync(attribution.EntityKind, attribution.EntityId, cancellationToken);
			Replace(Attributions, await _service.ListAttributionsAsync(project.Id, cancellationToken));
			SelectedAttribution = null;
			CompleteOperation(false, "Project attribution removed.");
		}
		catch (Exception exception) { FailOperation(exception, "Project attribution could not be removed."); }
	}

	private async Task LinkBudgetLineAsync(CancellationToken cancellationToken)
	{
		var project = SelectedProject ?? throw new InvalidOperationException("Select a project first.");
		var option = SelectedBudgetLineOption ?? throw new InvalidOperationException("Select a Finance budget line.");
		BeginOperation("Linking Finance budget line...");
		try
		{
			await _service.LinkBudgetLineAsync(project.Id, SelectedPhase?.Id, option.FinanceBudgetLineId, BudgetCategoryCode, cancellationToken);
			Replace(BudgetLinks, await _service.ListBudgetLinksAsync(project.Id, cancellationToken));
			Replace(BudgetLineOptions, await _service.ListBudgetLineOptionsAsync(project.Id, cancellationToken: cancellationToken));
			SelectedBudgetLineOption = null;
			CompleteOperation(false, "Finance budget line linked to project.");
		}
		catch (Exception exception) { FailOperation(exception, "Finance budget line could not be linked."); }
	}

	private async Task UnlinkBudgetLineAsync(CancellationToken cancellationToken)
	{
		var project = SelectedProject;
		var link = SelectedBudgetLink;
		if (project is null || link is null) return;
		BeginOperation("Removing project budget link...");
		try
		{
			await _service.UnlinkBudgetLineAsync(link.FinanceBudgetLineId, cancellationToken);
			Replace(BudgetLinks, await _service.ListBudgetLinksAsync(project.Id, cancellationToken));
			Replace(BudgetLineOptions, await _service.ListBudgetLineOptionsAsync(project.Id, cancellationToken: cancellationToken));
			SelectedBudgetLink = null;
			CompleteOperation(false, "Project budget link removed.");
		}
		catch (Exception exception) { FailOperation(exception, "Project budget link could not be removed."); }
	}

	private async Task<ProjectFinancialSummary?> LoadSummaryAsync(long projectId, CancellationToken cancellationToken) =>
		await _service.GetFinancialSummaryAsync(projectId, cancellationToken: cancellationToken);

	private void NewProject()
	{
		SelectedProject = null;
		_projectCode = _projectName = _legalEntityId = _ownerUserId = _customerId = _projectDescription = string.Empty;
		_plannedStartDate = _plannedEndDate = null;
		OnPropertyChanged(nameof(ProjectCode)); OnPropertyChanged(nameof(ProjectName)); OnPropertyChanged(nameof(LegalEntityId));
		OnPropertyChanged(nameof(OwnerUserId)); OnPropertyChanged(nameof(CustomerId)); OnPropertyChanged(nameof(ProjectDescription));
		OnPropertyChanged(nameof(PlannedStartDate)); OnPropertyChanged(nameof(PlannedEndDate));
	}

	private async Task SaveProjectAsync(CancellationToken cancellationToken)
	{
		if (!Guid.TryParse(LegalEntityId, out var legalEntityId)) throw new InvalidOperationException("Legal entity must be a valid GUID.");
		if (!long.TryParse(OwnerUserId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ownerUserId) || ownerUserId <= 0) throw new InvalidOperationException("Owner user ID must be a positive number.");
		long? customerId = null;
		if (!string.IsNullOrWhiteSpace(CustomerId))
		{
			if (!long.TryParse(CustomerId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedCustomer) || parsedCustomer <= 0) throw new InvalidOperationException("Customer ID must be a positive number.");
			customerId = parsedCustomer;
		}

		BeginOperation("Saving project...");
		try
		{
			var before = SelectedProject;
			var saved = await _service.SaveAsync(new ProjectRecord
			{
				Id = before?.Id ?? 0,
				Version = before?.Version ?? 1,
				Code = ProjectCode,
				Name = ProjectName,
				LegalEntityId = legalEntityId,
				OwnerUserId = ownerUserId,
				CustomerId = customerId,
				PlannedStartDate = PlannedStartDate.HasValue ? DateOnly.FromDateTime(PlannedStartDate.Value) : null,
				PlannedEndDate = PlannedEndDate.HasValue ? DateOnly.FromDateTime(PlannedEndDate.Value) : null,
				Status = before?.Status ?? ProjectStatus.Draft,
				Description = ProjectDescription
			}, cancellationToken);
			await LoadAsync(cancellationToken);
			SelectedProject = Projects.FirstOrDefault(value => value.Id == saved.Id);
			CompleteOperation(false, $"Project {saved.Code} saved.");
		}
		catch (Exception exception) { FailOperation(exception, "Project could not be saved."); }
	}

	private async Task TransitionAsync(ProjectStatus status, CancellationToken cancellationToken)
	{
		var project = SelectedProject;
		if (project is null) return;
		BeginOperation($"Updating project status to {status}...");
		try
		{
			var updated = status switch
			{
				ProjectStatus.Active => await _service.ActivateAsync(project.Id, project.Version, cancellationToken),
				ProjectStatus.OnHold => await _service.PutOnHoldAsync(project.Id, project.Version, cancellationToken),
				ProjectStatus.Closed => await _service.CloseAsync(project.Id, project.Version, cancellationToken),
				ProjectStatus.Cancelled => await _service.CancelAsync(project.Id, project.Version, cancellationToken),
				_ => throw new InvalidOperationException("Unsupported project transition.")
			};
			await LoadAsync(cancellationToken);
			SelectedProject = Projects.FirstOrDefault(value => value.Id == updated.Id);
			CompleteOperation(false, $"Project status changed to {updated.Status}.");
		}
		catch (Exception exception) { FailOperation(exception, "Project status could not be changed."); }
	}

	private void NewPhase()
	{
		SelectedPhase = null;
		_phaseCode = _phaseName = _phaseDescription = string.Empty;
		_phasePlannedStartDate = _phasePlannedEndDate = null;
		OnPropertyChanged(nameof(PhaseCode)); OnPropertyChanged(nameof(PhaseName)); OnPropertyChanged(nameof(PhaseDescription));
		OnPropertyChanged(nameof(PhasePlannedStartDate)); OnPropertyChanged(nameof(PhasePlannedEndDate));
	}

	private async Task SavePhaseAsync(CancellationToken cancellationToken)
	{
		var project = SelectedProject ?? throw new InvalidOperationException("Select a project first.");
		BeginOperation("Saving project phase...");
		try
		{
			var before = SelectedPhase;
			var saved = await _service.SavePhaseAsync(new ProjectPhase
			{
				Id = before?.Id ?? 0,
				Version = before?.Version ?? 1,
				ProjectId = project.Id,
				Code = PhaseCode,
				Name = PhaseName,
				PlannedStartDate = PhasePlannedStartDate.HasValue ? DateOnly.FromDateTime(PhasePlannedStartDate.Value) : null,
				PlannedEndDate = PhasePlannedEndDate.HasValue ? DateOnly.FromDateTime(PhasePlannedEndDate.Value) : null,
				Status = before?.Status ?? ProjectPhaseStatus.Planned,
				Description = PhaseDescription
			}, cancellationToken);
			Replace(Phases, await _service.ListPhasesAsync(project.Id, cancellationToken));
			SelectedPhase = Phases.FirstOrDefault(value => value.Id == saved.Id);
			CompleteOperation(false, $"Phase {saved.Code} saved.");
		}
		catch (Exception exception) { FailOperation(exception, "Project phase could not be saved."); }
	}

	private async Task TransitionPhaseAsync(ProjectPhaseStatus status, CancellationToken cancellationToken)
	{
		var project = SelectedProject;
		var phase = SelectedPhase;
		if (project is null || phase is null) return;
		BeginOperation($"Updating phase status to {status}...");
		try
		{
			var updated = status switch
			{
				ProjectPhaseStatus.Active => await _service.ActivatePhaseAsync(phase.Id, phase.Version, cancellationToken),
				ProjectPhaseStatus.Completed => await _service.CompletePhaseAsync(phase.Id, phase.Version, cancellationToken),
				ProjectPhaseStatus.Cancelled => await _service.CancelPhaseAsync(phase.Id, phase.Version, cancellationToken),
				_ => throw new InvalidOperationException("Unsupported project phase transition.")
			};
			Replace(Phases, await _service.ListPhasesAsync(project.Id, cancellationToken));
			SelectedPhase = Phases.FirstOrDefault(value => value.Id == updated.Id);
			CompleteOperation(false, $"Phase status changed to {updated.Status}.");
		}
		catch (Exception exception) { FailOperation(exception, "Project phase status could not be changed."); }
	}

	private void LoadProjectEditor(ProjectRecord? value)
	{
		if (value is null) return;
		_projectCode=value.Code; _projectName=value.Name; _legalEntityId=value.LegalEntityId.ToString("D"); _ownerUserId=value.OwnerUserId.ToString(CultureInfo.InvariantCulture);
		_customerId=value.CustomerId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty; _projectDescription=value.Description ?? string.Empty;
		_plannedStartDate=value.PlannedStartDate?.ToDateTime(TimeOnly.MinValue); _plannedEndDate=value.PlannedEndDate?.ToDateTime(TimeOnly.MinValue);
		OnPropertyChanged(nameof(ProjectCode)); OnPropertyChanged(nameof(ProjectName)); OnPropertyChanged(nameof(LegalEntityId)); OnPropertyChanged(nameof(OwnerUserId));
		OnPropertyChanged(nameof(CustomerId)); OnPropertyChanged(nameof(ProjectDescription)); OnPropertyChanged(nameof(PlannedStartDate)); OnPropertyChanged(nameof(PlannedEndDate));
	}

	private void RaiseCommands()
	{
		NewProjectCommand.RaiseCanExecuteChanged(); SaveProjectCommand.RaiseCanExecuteChanged(); ActivateCommand.RaiseCanExecuteChanged(); HoldCommand.RaiseCanExecuteChanged();
		CloseCommand.RaiseCanExecuteChanged(); CancelCommand.RaiseCanExecuteChanged(); NewPhaseCommand.RaiseCanExecuteChanged(); SavePhaseCommand.RaiseCanExecuteChanged();
		ActivatePhaseCommand.RaiseCanExecuteChanged(); CompletePhaseCommand.RaiseCanExecuteChanged(); CancelPhaseCommand.RaiseCanExecuteChanged();
		AttributeCommand.RaiseCanExecuteChanged(); RemoveAttributionCommand.RaiseCanExecuteChanged(); LinkBudgetLineCommand.RaiseCanExecuteChanged(); UnlinkBudgetLineCommand.RaiseCanExecuteChanged();
	}

	private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
	{
		target.Clear();
		foreach (var value in values) target.Add(value);
	}

	private bool Set<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
	{
		if (EqualityComparer<T>.Default.Equals(field, value)) return false;
		field = value; OnPropertyChanged(propertyName); return true;
	}
}
