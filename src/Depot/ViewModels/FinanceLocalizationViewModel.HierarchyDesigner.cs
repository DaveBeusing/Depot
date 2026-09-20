// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;
using System.ComponentModel;
using Depot.Commands;
using Depot.Models;

namespace Depot.ViewModels;

public sealed partial class FinanceLocalizationViewModel
{
	private FinanceLocalizationHierarchyRow? _selectedHierarchyRow;
	private int _localizationWorkspaceModeIndex;
	private string _hierarchyWarningText = string.Empty;
	private FinanceLocalizationAssignmentTimelineRow? _selectedAssignmentTimelineRow;
	private string _assignmentValidationText = "Select or create an assignment, then validate it against the localization service.";
	private bool _isAssignmentDraftValid;

	public ObservableCollection<FinanceLocalizationHierarchyRow> LocalizationHierarchyRows { get; } = [];
	public ObservableCollection<FinanceLocalizationRegistryEntry> HierarchyRegistryEntries { get; } = [];
	public ObservableCollection<FinanceLocalizationAssignmentTimelineRow> AssignmentTimelineRows { get; } = [];

	public AsyncRelayCommand OpenHierarchyPackEditorCommand { get; private set; } = null!;
	public AsyncRelayCommand NewHierarchyPackCommand { get; private set; } = null!;
	public AsyncRelayCommand ValidateDesignerAssignmentCommand { get; private set; } = null!;

	public int LocalizationWorkspaceModeIndex
	{
		get => _localizationWorkspaceModeIndex;
		set
		{
			if (_localizationWorkspaceModeIndex == value) return;
			_localizationWorkspaceModeIndex = value;
			OnPropertyChanged();
		}
	}

	public FinanceLocalizationHierarchyRow? SelectedHierarchyRow
	{
		get => _selectedHierarchyRow;
		set
		{
			if (ReferenceEquals(_selectedHierarchyRow, value)) return;
			_selectedHierarchyRow = value;
			OnPropertyChanged();
			if (value is not null)
				SelectedCatalogPack = value.Pack;
			RefreshHierarchyInspector();
			OnPropertyChanged(nameof(HierarchySelectionTitle));
			OnPropertyChanged(nameof(HierarchySelectionSubtitle));
			OnPropertyChanged(nameof(HierarchyRegistrySummary));
			OnPropertyChanged(nameof(CanEditHierarchySelection));
			OnPropertyChanged(nameof(IsHierarchySelectionReadOnly));
			OpenHierarchyPackEditorCommand?.RaiseCanExecuteChanged();
		}
	}

	public FinanceLocalizationAssignmentTimelineRow? SelectedAssignmentTimelineRow
	{
		get => _selectedAssignmentTimelineRow;
		set
		{
			if (ReferenceEquals(_selectedAssignmentTimelineRow, value)) return;
			_selectedAssignmentTimelineRow = value;
			OnPropertyChanged();
			if (value is not null) SelectedAssignment = value.Assignment;
			AssignmentValidationText = value?.ValidationMessage ?? "Select or create an assignment, then validate it against the localization service.";
			IsAssignmentDraftValid = false;
		}
	}

	public string AssignmentValidationText
	{
		get => _assignmentValidationText;
		private set => Set(ref _assignmentValidationText, value);
	}

	public bool IsAssignmentDraftValid
	{
		get => _isAssignmentDraftValid;
		private set
		{
			if (_isAssignmentDraftValid == value) return;
			_isAssignmentDraftValid = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(CanSaveDesignerAssignment));
		}
	}

	public bool CanSaveDesignerAssignment => CanManage && IsAssignmentDraftValid;
	public string SelectedLegalEntitySummary => SelectedLegalEntity is null
		? "No legal entity selected."
		: $"{SelectedLegalEntity.Code} · {SelectedLegalEntity.Name} · {SelectedLegalEntity.CountryCode}";

	public string HierarchyWarningText
	{
		get => _hierarchyWarningText;
		private set => Set(ref _hierarchyWarningText, value);
	}

	public string HierarchySelectionTitle => SelectedHierarchyRow is null
		? "Select a localization pack"
		: $"{SelectedHierarchyRow.Code} · {SelectedHierarchyRow.Name}";

	public string HierarchySelectionSubtitle => SelectedHierarchyRow is null
		? "The hierarchy is a projection of the existing localization pack model."
		: $"{SelectedHierarchyRow.Layer} · {(SelectedHierarchyRow.CountryCode ?? "No country")} · {SelectedHierarchyRow.StateText} · {(SelectedHierarchyRow.IsBuiltIn ? "Built-in" : "Custom")}";

	public string HierarchyRegistrySummary => SelectedHierarchyRow?.RegistrySummary ?? "No localization pack selected.";
	public bool CanEditHierarchySelection => CanManage && SelectedHierarchyRow is { IsReadOnly: false };
	public bool IsHierarchySelectionReadOnly => SelectedHierarchyRow?.IsReadOnly ?? true;

	private void InitializeHierarchyDesigner()
	{
		OpenHierarchyPackEditorCommand = new AsyncRelayCommand(_ =>
		{
			if (SelectedHierarchyRow is not null)
				SelectedCatalogPack = SelectedHierarchyRow.Pack;
			LocalizationWorkspaceModeIndex = 3;
			return Task.CompletedTask;
		}, () => SelectedHierarchyRow is not null);
		NewHierarchyPackCommand = new AsyncRelayCommand(_ =>
		{
			ClearPack();
			LocalizationWorkspaceModeIndex = 3;
			return Task.CompletedTask;
		}, () => CanManage);
		ValidateDesignerAssignmentCommand = new AsyncRelayCommand(ValidateDesignerAssignmentAsync, () => CanManage && SelectedLegalEntity is not null && SelectedAssignmentPack is not null);
		PropertyChanged += OnHierarchyDesignerPropertyChanged;
	}

	private void RefreshHierarchyProjection()
	{
		var selectedCode = SelectedHierarchyRow?.Code ?? SelectedCatalogPack?.Code;
		var projection = FinanceLocalizationHierarchyProjector.Project(Packs, RegistryEntries);
		Replace(LocalizationHierarchyRows, projection.Rows);
		HierarchyWarningText = projection.Warnings.Count == 0
			? "Hierarchy is consistent with the localization service layer contract."
			: string.Join(Environment.NewLine, projection.Warnings);
		SelectedHierarchyRow = selectedCode is null
			? LocalizationHierarchyRows.FirstOrDefault()
			: LocalizationHierarchyRows.FirstOrDefault(value => string.Equals(value.Code, selectedCode, StringComparison.Ordinal))
				?? LocalizationHierarchyRows.FirstOrDefault();
	}

	private void RefreshAssignmentTimeline()
	{
		var selectedId = SelectedAssignmentTimelineRow?.Assignment.Id ?? SelectedAssignment?.Id;
		if (SelectedLegalEntity is null)
		{
			AssignmentTimelineRows.Clear();
			SelectedAssignmentTimelineRow = null;
			OnPropertyChanged(nameof(SelectedLegalEntitySummary));
			return;
		}
		Replace(AssignmentTimelineRows, FinanceLocalizationAssignmentProjector.Project(SelectedLegalEntity, Assignments, Packs));
		SelectedAssignmentTimelineRow = selectedId.HasValue
			? AssignmentTimelineRows.FirstOrDefault(value => value.Assignment.Id == selectedId.Value)
			: AssignmentTimelineRows.FirstOrDefault();
		OnPropertyChanged(nameof(SelectedLegalEntitySummary));
	}

	private FinanceLocalizationAssignment CreateAssignmentDraft()
	{
		var entity = SelectedLegalEntity ?? throw new InvalidOperationException("Select a legal entity.");
		var pack = SelectedAssignmentPack ?? throw new InvalidOperationException("Select a localization pack.");
		var current = SelectedAssignment;
		return new FinanceLocalizationAssignment
		{
			Id=current?.Id??0,
			Version=current?.Version??1,
			LegalEntityId=entity.Id,
			PackCode=pack.Code,
			EffectiveFrom=DateOnly.FromDateTime(AssignmentFrom),
			EffectiveTo=AssignmentTo.HasValue?DateOnly.FromDateTime(AssignmentTo.Value):null,
			IsActive=AssignmentActive,
			CreatedAtUtc=current?.CreatedAtUtc??default,
			CreatedByUserId=current?.CreatedByUserId??0
		};
	}

	private async Task ValidateDesignerAssignmentAsync(CancellationToken token)
	{
		try
		{
			var result = await _localization.ValidateAssignmentAsync(CreateAssignmentDraft(), token);
			IsAssignmentDraftValid = result.IsValid;
			AssignmentValidationText = result.Summary;
		}
		catch (OperationCanceledException) when (token.IsCancellationRequested) { }
		catch (Exception exception)
		{
			IsAssignmentDraftValid = false;
			AssignmentValidationText = exception.Message;
		}
	}

	private void OnHierarchyDesignerPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName is nameof(SelectedLegalEntity))
		{
			OnPropertyChanged(nameof(SelectedLegalEntitySummary));
			ValidateDesignerAssignmentCommand.RaiseCanExecuteChanged();
		}
		if (e.PropertyName is nameof(SelectedAssignmentPack))
			ValidateDesignerAssignmentCommand.RaiseCanExecuteChanged();
		if (e.PropertyName is nameof(SelectedLegalEntity) or nameof(SelectedAssignmentPack) or nameof(AssignmentFrom) or nameof(AssignmentTo) or nameof(AssignmentActive))
		{
			IsAssignmentDraftValid = false;
			AssignmentValidationText = "Assignment draft changed. Validate it before saving from the hierarchy designer.";
		}
	}

	private void RefreshHierarchyInspector()
	{
		if (SelectedHierarchyRow is null)
		{
			HierarchyRegistryEntries.Clear();
			return;
		}
		Replace(HierarchyRegistryEntries, RegistryEntries
			.Where(value => string.Equals(value.PackCode, SelectedHierarchyRow.Code, StringComparison.Ordinal))
			.OrderBy(value => value.SupportLevel)
			.ThenBy(value => value.RequirementCode, StringComparer.Ordinal)
			.ThenByDescending(value => value.EffectiveFrom)
			.ThenByDescending(value => value.Id));
	}

	private void DisposeHierarchyDesigner()
	{
		PropertyChanged -= OnHierarchyDesignerPropertyChanged;
		OpenHierarchyPackEditorCommand.Dispose();
		NewHierarchyPackCommand.Dispose();
		ValidateDesignerAssignmentCommand.Dispose();
	}
}
