// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;
using Depot.Commands;
using Depot.Models;

namespace Depot.ViewModels;

public sealed partial class FinanceLocalizationViewModel
{
	private FinanceLocalizationHierarchyRow? _selectedHierarchyRow;
	private int _localizationWorkspaceModeIndex;
	private string _hierarchyWarningText = string.Empty;

	public ObservableCollection<FinanceLocalizationHierarchyRow> LocalizationHierarchyRows { get; } = [];
	public ObservableCollection<FinanceLocalizationRegistryEntry> HierarchyRegistryEntries { get; } = [];

	public AsyncRelayCommand OpenHierarchyPackEditorCommand { get; private set; } = null!;
	public AsyncRelayCommand NewHierarchyPackCommand { get; private set; } = null!;

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
		OpenHierarchyPackEditorCommand.Dispose();
		NewHierarchyPackCommand.Dispose();
	}
}
