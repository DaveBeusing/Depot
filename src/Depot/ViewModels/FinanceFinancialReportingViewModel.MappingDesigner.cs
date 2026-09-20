// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;
using System.ComponentModel;
using Depot.Commands;
using Depot.Models;

namespace Depot.ViewModels;

public sealed partial class FinanceFinancialReportingViewModel
{
	private FinanceReportingMappingProjectionRow? _selectedMappingDesignerRow;
	private string _mappingValidationText = "Select an account to inspect its reporting mapping.";
	private bool _isMappingDraftValid;
	private FinanceReportingMappingCoverage _mappingCoverage = new(0, 0, 0, 0, 0);
	private FinanceReportKind _mappingPreviewKind = FinanceReportKind.BalanceSheet;
	private string _mappingPreviewWarningText = string.Empty;

	public ObservableCollection<FinanceReportingMappingProjectionRow> MappingDesignerRows { get; } = [];
	public ObservableCollection<FinanceReportingMappingTarget> MappingTargets { get; } = [];
	public ObservableCollection<FinanceReportRow> MappingPreviewRows { get; } = [];
	public AsyncRelayCommand? MoveMappingUpCommand { get; private set; }
	public AsyncRelayCommand? MoveMappingDownCommand { get; private set; }
	public AsyncRelayCommand? RefreshMappingPreviewCommand { get; private set; }
	public IReadOnlyList<FinanceReportKind> MappingPreviewKinds { get; } =
	[
		FinanceReportKind.BalanceSheet,
		FinanceReportKind.ProfitLoss,
		FinanceReportKind.CashFlow,
		FinanceReportKind.TaxSummary,
		FinanceReportKind.CostOfGoodsSold
	];

	public FinanceReportingMappingProjectionRow? SelectedMappingDesignerRow
	{
		get => _selectedMappingDesignerRow;
		set
		{
			if (ReferenceEquals(_selectedMappingDesignerRow, value)) return;
			_selectedMappingDesignerRow = value;
			OnPropertyChanged();
			if (value is null)
			{
				ClearMapping();
			}
			else if (value.Mapping is not null)
			{
				SelectedMapping = value.Mapping;
			}
			else
			{
				ClearMapping();
				SelectedAccount = value.Account;
				MappingActive = value.Account.IsActive;
			}
			OnPropertyChanged(nameof(MappingDesignerSelectionTitle));
			OnPropertyChanged(nameof(MappingDesignerSelectionSubtitle));
			MoveMappingUpCommand?.RaiseCanExecuteChanged();
			MoveMappingDownCommand?.RaiseCanExecuteChanged();
			RefreshMappingDraftValidation();
		}
	}

	public string MappingDesignerSelectionTitle =>
		SelectedMappingDesignerRow is null
			? "Select an account"
			: $"{SelectedMappingDesignerRow.AccountNumber} · {SelectedMappingDesignerRow.AccountName}";

	public string MappingDesignerSelectionSubtitle =>
		SelectedMappingDesignerRow is null
			? "Drag an account to an explicit reporting target or edit the mapping in the inspector."
			: $"{SelectedMappingDesignerRow.AccountType} · {SelectedMappingDesignerRow.StateText}";

	public string MappingValidationText
	{
		get => _mappingValidationText;
		private set
		{
			if (_mappingValidationText == value) return;
			_mappingValidationText = value;
			OnPropertyChanged();
		}
	}

	public bool IsMappingDraftValid
	{
		get => _isMappingDraftValid;
		private set
		{
			if (_isMappingDraftValid == value) return;
			_isMappingDraftValid = value;
			OnPropertyChanged();
		}
	}
	public FinanceReportingMappingCoverage MappingCoverage
	{
		get => _mappingCoverage;
		private set
		{
			if (_mappingCoverage == value) return;
			_mappingCoverage = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(MappingCoverageSummary));
		}
	}

	public string MappingCoverageSummary => MappingCoverage.Summary;

	public FinanceReportKind MappingPreviewKind
	{
		get => _mappingPreviewKind;
		set
		{
			if (_mappingPreviewKind == value) return;
			_mappingPreviewKind = value;
			OnPropertyChanged();
			MappingPreviewRows.Clear();
			MappingPreviewWarningText = "Preview parameters changed. Refresh to regenerate.";
		}
	}

	public string MappingPreviewWarningText
	{
		get => _mappingPreviewWarningText;
		private set
		{
			if (_mappingPreviewWarningText == value) return;
			_mappingPreviewWarningText = value;
			OnPropertyChanged();
		}
	}


	private void InitializeMappingDesigner()
	{
		Replace(MappingTargets, FinanceReportingMappingProjector.Targets);
		MoveMappingUpCommand = new AsyncRelayCommand(token => MoveMappingAsync(-1, token), CanMoveMappingUp);
		MoveMappingDownCommand = new AsyncRelayCommand(token => MoveMappingAsync(1, token), CanMoveMappingDown);
		RefreshMappingPreviewCommand = new AsyncRelayCommand(RefreshMappingDesignerPreviewAsync, () => _reporting.CanView && Guid.TryParse(AccountingBookId, out _));
		PropertyChanged += OnMappingDraftPropertyChanged;
	}

	private void LoadMappingDesignerProjection()
	{
		var selectedAccountId = SelectedMappingDesignerRow?.AccountId ?? SelectedAccount?.Id;
		var projection = FinanceReportingMappingProjector.Project(Accounts, Mappings);
		MappingCoverage = projection.Coverage;
		Replace(MappingDesignerRows, projection.Rows);
		SelectedMappingDesignerRow = selectedAccountId is Guid accountId
			? MappingDesignerRows.FirstOrDefault(value => value.AccountId == accountId)
			: MappingDesignerRows.FirstOrDefault();
	}

	public bool ApplyMappingDesignerTarget(FinanceReportingMappingProjectionRow row, FinanceReportingMappingTarget target)
	{
		ArgumentNullException.ThrowIfNull(row);
		ArgumentNullException.ThrowIfNull(target);
		if (!CanManage) return false;
		SelectedMappingDesignerRow = row;
		var changed = FinanceReportingMappingProjector.ApplyTarget(CreateMappingDraft(), target);
		StatementSection = changed.StatementSection;
		CashFlowCategory = changed.CashFlowCategory;
		TaxCategory = changed.TaxCategory;
		IsCashAccount = changed.IsCashAccount;
		IsCostOfGoodsSold = changed.IsCostOfGoodsSold;
		RefreshMappingDraftValidation();
		return true;
	}

	private async Task RefreshMappingDesignerPreviewAsync(CancellationToken token)
	{
		if (!Guid.TryParse(AccountingBookId, out var bookId))
		{
			MappingPreviewRows.Clear();
			MappingPreviewWarningText = "A valid accounting book is required.";
			return;
		}
		var parameters = new FinanceReportParameters
		{
			Kind = MappingPreviewKind,
			AccountingBookId = bookId,
			FromDate = DateOnly.FromDateTime(FromDate),
			ToDate = DateOnly.FromDateTime(ToDate),
			AsOfDate = DateOnly.FromDateTime(AsOfDate),
			DimensionId = OptionalGuid(DimensionId, "dimension"),
			DimensionValueId = OptionalGuid(DimensionValueId, "dimension value"),
			IncludeZeroBalances = IncludeZeroBalances
		};
		var result = await _reporting.GenerateAsync(parameters, token);
		Replace(MappingPreviewRows, result.Rows);
		MappingPreviewWarningText = result.Warnings.Count == 0
			? $"{MappingPreviewKind} preview generated with {result.Rows.Count} row(s)."
			: string.Join(Environment.NewLine, result.Warnings);
	}

	private bool CanMoveMappingUp() => CanMoveMapping(-1);
	private bool CanMoveMappingDown() => CanMoveMapping(1);

	private bool CanMoveMapping(int direction)
	{
		if (!CanManage || SelectedMappingDesignerRow?.Mapping is null) return false;
		var mapped = MappingDesignerRows.Where(value => value.Mapping is not null).ToArray();
		var index = Array.FindIndex(mapped, value => value.AccountId == SelectedMappingDesignerRow.AccountId);
		return direction < 0 ? index > 0 : index >= 0 && index < mapped.Length - 1;
	}

	private async Task MoveMappingAsync(int direction, CancellationToken token)
	{
		if (SelectedMappingDesignerRow?.Mapping is null) return;
		var selectedAccountId = SelectedMappingDesignerRow.AccountId;
		var ordered = MappingDesignerRows.Where(value => value.Mapping is not null).ToList();
		var index = ordered.FindIndex(value => value.AccountId == selectedAccountId);
		var targetIndex = index + direction;
		if (index < 0 || targetIndex < 0 || targetIndex >= ordered.Count) return;
		(ordered[index], ordered[targetIndex]) = (ordered[targetIndex], ordered[index]);
		for (var position = 0; position < ordered.Count; position++)
		{
			var mapping = ordered[position].Mapping!;
			var desiredSortOrder = (position + 1) * 10;
			if (mapping.SortOrder == desiredSortOrder) continue;
			await _reporting.SaveMappingAsync(mapping with { SortOrder = desiredSortOrder }, token);
		}
		await LoadAsync(token);
		SelectedMappingDesignerRow = MappingDesignerRows.FirstOrDefault(value => value.AccountId == selectedAccountId);
		CompleteOperation(false, "Reporting mapping order updated.");
	}

	private void OnMappingDraftPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName is nameof(SelectedAccount)
			or nameof(StatementSection)
			or nameof(CashFlowCategory)
			or nameof(TaxCategory)
			or nameof(IsCashAccount)
			or nameof(IsCostOfGoodsSold)
			or nameof(MappingActive)
			or nameof(SortOrder))
			RefreshMappingDraftValidation();
	}

	private void RefreshMappingDraftValidation()
	{
		if (SelectedAccount is null)
		{
			IsMappingDraftValid = false;
			MappingValidationText = "Select an account to inspect its reporting mapping.";
			return;
		}
		var validation = FinanceReportingMappingValidator.Validate(CreateMappingDraft(), SelectedAccount);
		IsMappingDraftValid = validation.IsValid;
		MappingValidationText = validation.Summary;
	}

	private FinanceReportingAccountMapping CreateMappingDraft()
	{
		if (SelectedAccount is null) throw new InvalidOperationException("Select an account.");
		return new FinanceReportingAccountMapping
		{
			Id = SelectedMapping?.Id ?? 0,
			Version = SelectedMapping?.Version ?? 1,
			AccountingBookId = Guid.TryParse(AccountingBookId, out var bookId) ? bookId : Guid.Empty,
			AccountId = SelectedAccount.Id,
			StatementSection = StatementSection,
			CashFlowCategory = CashFlowCategory,
			TaxCategory = TaxCategory,
			IsCashAccount = IsCashAccount,
			IsCostOfGoodsSold = IsCostOfGoodsSold,
			SortOrder = int.TryParse(SortOrder, out var sortOrder) ? sortOrder : 0,
			IsActive = MappingActive
		};
	}

	private void DisposeMappingDesigner()
	{
		PropertyChanged -= OnMappingDraftPropertyChanged;
		MoveMappingUpCommand?.Dispose();
		MoveMappingDownCommand?.Dispose();
		RefreshMappingPreviewCommand?.Dispose();
	}
}
