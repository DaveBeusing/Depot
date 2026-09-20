// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Commands;
using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels;

public sealed record FinancePostingFlowPaletteItem(
	string AmountKey,
	FinancePostingDirection Direction)
{
	public string DisplayName => $"{AmountKey} · {Direction}";
}

public sealed class FinancePostingFlowRuleViewModel : BaseViewModel
{
	private readonly Action _changed;
	private Guid _accountId;
	private FinanceAccount? _selectedAccount;
	private FinancePostingDirection _direction;
	private string _amountKey;
	private decimal _multiplier;
	private string? _description;

	public FinancePostingFlowRuleViewModel(
		FinancePostingProfileLine line,
		FinanceAccount? selectedAccount,
		Action changed)
	{
		Id = line.Id;
		LineNumber = line.LineNumber;
		_accountId = line.AccountId;
		_selectedAccount = selectedAccount;
		_direction = line.Direction;
		_amountKey = line.AmountKey;
		_multiplier = line.Multiplier;
		_description = line.Description;
		_changed = changed;
	}

	public long Id { get; }
	public int LineNumber { get; internal set; }
	public Guid AccountId => _accountId;

	public FinanceAccount? SelectedAccount
	{
		get => _selectedAccount;
		set
		{
			if (Equals(_selectedAccount, value)) return;
			_selectedAccount = value;
			_accountId = value?.Id ?? Guid.Empty;
			OnPropertyChanged();
			OnPropertyChanged(nameof(AccountId));
			_changed();
		}
	}

	public FinancePostingDirection Direction
	{
		get => _direction;
		set
		{
			if (_direction == value) return;
			_direction = value;
			OnPropertyChanged();
			_changed();
		}
	}

	public string AmountKey
	{
		get => _amountKey;
		set
		{
			var normalized = value ?? string.Empty;
			if (_amountKey == normalized) return;
			_amountKey = normalized;
			OnPropertyChanged();
			_changed();
		}
	}

	public decimal Multiplier
	{
		get => _multiplier;
		set
		{
			if (_multiplier == value) return;
			_multiplier = value;
			OnPropertyChanged();
			_changed();
		}
	}

	public string? Description
	{
		get => _description;
		set
		{
			if (_description == value) return;
			_description = value;
			OnPropertyChanged();
			_changed();
		}
	}

	public FinancePostingFlowLineDraft ToDraft() =>
		new(Id, LineNumber, _accountId, Direction, AmountKey, Multiplier, Description);
}

public sealed class FinancePostingFlowDesignerViewModel : BaseViewModel, IDisposable
{
	private readonly FinanceGeneralLedgerService _ledger;
	private FinancePostingProfile? _selectedProfile;
	private FinancePostingFlowContext _context = FinancePostingFlowContext.Empty;
	private FinancePostingFlowNode? _selectedNode;
	private FinancePostingFlowRuleViewModel? _selectedRule;
	private FinancePostingFlowPaletteItem? _selectedPaletteItem;
	private JournalDefinition? _selectedJournal;
	private string _code = string.Empty;
	private string _name = string.Empty;
	private string _sourceType = string.Empty;
	private string _sourceEvent = string.Empty;
	private string _numberSequenceCode = string.Empty;
	private bool _isActive = true;
	private bool _applyingProfile;
	private bool _disposed;

	public FinancePostingFlowDesignerViewModel(FinanceGeneralLedgerService ledger)
	{
		_ledger = ledger;
		RefreshCommand = new AsyncRelayCommand(LoadAsync);
		SaveCommand = new AsyncRelayCommand(SaveAsync, () => CanSave);
		AddRuleCommand = new RelayCommand(AddSelectedPaletteRule, () => CanManage && SelectedPaletteItem is not null && HasProfiles);
		RemoveRuleCommand = new RelayCommand(RemoveSelectedRule, () => CanManage && SelectedRule is not null);
	}

	public ObservableCollection<FinancePostingProfile> Profiles { get; } = [];
	public ObservableCollection<FinancePostingFlowRuleViewModel> Rules { get; } = [];
	public ObservableCollection<FinancePostingFlowNode> Nodes { get; } = [];
	public ObservableCollection<FinancePostingFlowEdge> Edges { get; } = [];
	public ObservableCollection<FinancePostingFlowIssue> Issues { get; } = [];
	public ObservableCollection<FinanceAccount> Accounts { get; } = [];
	public ObservableCollection<JournalDefinition> Journals { get; } = [];
	public ObservableCollection<FinancePostingFlowPaletteItem> PaletteItems { get; } = [];
	public IReadOnlyList<FinancePostingDirection> Directions { get; } = Enum.GetValues<FinancePostingDirection>();

	public AsyncRelayCommand RefreshCommand { get; }
	public AsyncRelayCommand SaveCommand { get; }
	public RelayCommand AddRuleCommand { get; }
	public RelayCommand RemoveRuleCommand { get; }

	public bool CanManage => _ledger.CanManagePostingProfiles;
	public bool IsReadOnly => !CanManage;
	public bool HasProfiles => Profiles.Count > 0;
	public bool HasSelectedRule => SelectedRule is not null;
	public bool CanSave =>
		CanManage &&
		SelectedProfile is not null &&
		Issues.All(issue => issue.Severity != FinancePostingFlowIssueSeverity.Error);

	public string ValidationSummary => Issues.Count == 0
		? "Flow is valid and ready to save."
		: $"{Issues.Count(issue => issue.Severity == FinancePostingFlowIssueSeverity.Error)} blocking issue(s), {Issues.Count(issue => issue.Severity == FinancePostingFlowIssueSeverity.Warning)} warning(s).";

	public FinancePostingProfile? SelectedProfile
	{
		get => _selectedProfile;
		set
		{
			if (ReferenceEquals(_selectedProfile, value)) return;
			_selectedProfile = value;
			OnPropertyChanged();
			_ = LoadSelectedProfileAsync(value, CancellationToken.None);
		}
	}

	public FinancePostingFlowNode? SelectedNode
	{
		get => _selectedNode;
		set
		{
			if (ReferenceEquals(_selectedNode, value)) return;
			_selectedNode = value;
			OnPropertyChanged();
			SelectedRule = value?.LineNumber is int lineNumber
				? Rules.FirstOrDefault(rule => rule.LineNumber == lineNumber)
				: null;
		}
	}

	public FinancePostingFlowRuleViewModel? SelectedRule
	{
		get => _selectedRule;
		private set
		{
			if (ReferenceEquals(_selectedRule, value)) return;
			_selectedRule = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(HasSelectedRule));
			RemoveRuleCommand.RaiseCanExecuteChanged();
		}
	}

	public FinancePostingFlowPaletteItem? SelectedPaletteItem
	{
		get => _selectedPaletteItem;
		set
		{
			if (Equals(_selectedPaletteItem, value)) return;
			_selectedPaletteItem = value;
			OnPropertyChanged();
			AddRuleCommand.RaiseCanExecuteChanged();
		}
	}

	public JournalDefinition? SelectedJournal
	{
		get => _selectedJournal;
		set
		{
			if (Equals(_selectedJournal, value)) return;
			_selectedJournal = value;
			OnPropertyChanged();
			if (!_applyingProfile) RebuildProjection();
		}
	}

	public string Code
	{
		get => _code;
		set => SetHeader(ref _code, value, nameof(Code));
	}

	public string Name
	{
		get => _name;
		set => SetHeader(ref _name, value, nameof(Name));
	}

	public string SourceType
	{
		get => _sourceType;
		set => SetHeader(ref _sourceType, value, nameof(SourceType), rebuildPalette: true);
	}

	public string SourceEvent
	{
		get => _sourceEvent;
		set => SetHeader(ref _sourceEvent, value, nameof(SourceEvent), rebuildPalette: true);
	}

	public string NumberSequenceCode
	{
		get => _numberSequenceCode;
		set => SetHeader(ref _numberSequenceCode, value, nameof(NumberSequenceCode));
	}

	public bool IsActive
	{
		get => _isActive;
		set
		{
			if (_isActive == value) return;
			_isActive = value;
			OnPropertyChanged();
			if (!_applyingProfile) RebuildProjection();
		}
	}

	public async Task LoadAsync(CancellationToken cancellationToken = default)
	{
		BeginOperation("Loading posting profiles...");
		try
		{
			var selectedId = SelectedProfile?.Id;
			Replace(Profiles, await _ledger.GetPostingProfilesAsync(cancellationToken));
			OnPropertyChanged(nameof(HasProfiles));
			AddRuleCommand.RaiseCanExecuteChanged();
			var selected = Profiles.FirstOrDefault(profile => profile.Id == selectedId) ?? Profiles.FirstOrDefault();
			_selectedProfile = selected;
			OnPropertyChanged(nameof(SelectedProfile));
			await LoadSelectedProfileAsync(selected, cancellationToken);
			CompleteOperation(!HasProfiles, HasProfiles ? "Posting flow designer loaded." : "No posting profiles are configured.");
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
		catch (Exception exception) { FailOperation(exception, "Posting flow designer could not be loaded."); }
	}

	public bool TryAddRule(FinancePostingFlowPaletteItem item)
	{
		ArgumentNullException.ThrowIfNull(item);
		if (!CanManage || SelectedProfile is null) return false;

		var account = Accounts.FirstOrDefault(candidate =>
			candidate.IsActive &&
			candidate.AllowDirectPosting &&
			(_context.AccountingBook is null || candidate.ChartOfAccountsId == _context.AccountingBook.ChartOfAccountsId) &&
			!Rules.Any(rule =>
				string.Equals(rule.AmountKey.Trim(), item.AmountKey, StringComparison.Ordinal) &&
				rule.Direction == item.Direction &&
				rule.AccountId == candidate.Id &&
				rule.Multiplier == 1m));
		if (account is null) return false;

		var line = new FinancePostingProfileLine
		{
			PostingProfileId = SelectedProfile.Id,
			LineNumber = Rules.Count == 0 ? 1 : Rules.Max(rule => rule.LineNumber) + 1,
			AccountId = account.Id,
			Direction = item.Direction,
			AmountKey = item.AmountKey,
			Multiplier = 1m
		};
		var rule = new FinancePostingFlowRuleViewModel(line, account, OnRuleChanged);
		Rules.Add(rule);
		RenumberRules();
		SelectedRule = rule;
		RebuildProjection($"rule:{rule.LineNumber}");
		return true;
	}

	private async Task LoadSelectedProfileAsync(FinancePostingProfile? profile, CancellationToken cancellationToken)
	{
		if (profile is null)
		{
			_context = FinancePostingFlowContext.Empty;
			Rules.Clear();
			Accounts.Clear();
			Journals.Clear();
			Nodes.Clear();
			Edges.Clear();
			Issues.Clear();
			PaletteItems.Clear();
			SelectedRule = null;
			OnProjectionChanged();
			return;
		}

		try
		{
			var context = await _ledger.GetPostingFlowContextAsync(profile.AccountingBookId, profile.JournalId, cancellationToken);
			if (!ReferenceEquals(_selectedProfile, profile) && _selectedProfile?.Id != profile.Id) return;
			_context = context;
			_applyingProfile = true;
			try
			{
				SetHeader(ref _code, profile.Code, nameof(Code));
				SetHeader(ref _name, profile.Name, nameof(Name));
				SetHeader(ref _sourceType, profile.SourceType, nameof(SourceType));
				SetHeader(ref _sourceEvent, profile.SourceEvent, nameof(SourceEvent));
				SetHeader(ref _numberSequenceCode, profile.NumberSequenceCode, nameof(NumberSequenceCode));
				_isActive = profile.IsActive;
				OnPropertyChanged(nameof(IsActive));
				Replace(Accounts, context.Accounts.Values.OrderBy(account => account.Number));
				Replace(Journals, context.Journals);
				_selectedJournal = context.Journal ?? Journals.FirstOrDefault(journal => journal.Id == profile.JournalId);
				OnPropertyChanged(nameof(SelectedJournal));
				Rules.Clear();
				foreach (var line in profile.Lines.OrderBy(line => line.LineNumber))
					Rules.Add(new FinancePostingFlowRuleViewModel(
						line,
						Accounts.FirstOrDefault(account => account.Id == line.AccountId),
						OnRuleChanged));
				SelectedRule = null;
			}
			finally
			{
				_applyingProfile = false;
			}
			RebuildPalette();
			RebuildProjection();
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
		catch (Exception exception) { FailOperation(exception, "Posting profile could not be loaded."); }
	}

	private async Task SaveAsync(CancellationToken cancellationToken)
	{
		if (SelectedProfile is null) return;
		BeginOperation("Saving posting profile...");
		try
		{
			RebuildProjection();
			if (!CanSave) throw new InvalidOperationException("Resolve the blocking posting-flow validation issues before saving.");
			var saved = await _ledger.SavePostingProfileAsync(BuildDraft(), cancellationToken);
			var index = -1;
			for (var candidate = 0; candidate < Profiles.Count; candidate++)
			{
				if (Profiles[candidate].Id != saved.Id) continue;
				index = candidate;
				break;
			}
			if (index >= 0) Profiles[index] = saved;
			else Profiles.Add(saved);
			_selectedProfile = saved;
			OnPropertyChanged(nameof(SelectedProfile));
			await LoadSelectedProfileAsync(saved, cancellationToken);
			CompleteOperation(false, $"Posting profile {saved.Code} saved.");
		}
		catch (Exception exception) { FailOperation(exception, "Posting profile could not be saved."); }
	}

	private FinancePostingProfile BuildDraft()
	{
		var profile = SelectedProfile ?? throw new InvalidOperationException("A posting profile must be selected.");
		var draft = profile with
		{
			Code = Code,
			Name = Name,
			SourceType = SourceType,
			SourceEvent = SourceEvent,
			NumberSequenceCode = NumberSequenceCode,
			JournalId = SelectedJournal?.Id ?? profile.JournalId,
			IsActive = IsActive
		};
		return FinancePostingFlowProjector.ApplyLines(draft, Rules.Select(rule => rule.ToDraft()));
	}

	private void RebuildProjection(string? selectedNodeKey = null)
	{
		if (_applyingProfile || SelectedProfile is null) return;
		var priorKey = selectedNodeKey ?? SelectedNode?.Key;
		var draft = BuildDraft();
		var selectedContext = _context with { Journal = SelectedJournal };
		var projection = FinancePostingFlowProjector.Project(draft, selectedContext);
		Replace(Nodes, projection.Nodes);
		Replace(Edges, projection.Edges);
		Replace(Issues, projection.Issues);
		_selectedNode = priorKey is null ? null : Nodes.FirstOrDefault(node => node.Key == priorKey);
		OnPropertyChanged(nameof(SelectedNode));
		SelectedRule = _selectedNode?.LineNumber is int lineNumber
			? Rules.FirstOrDefault(rule => rule.LineNumber == lineNumber)
			: SelectedRule;
		RebuildPalette();
		OnProjectionChanged();
	}

	private void RebuildPalette()
	{
		var keys = FinancePostingFlowProjector.RequiredAmountKeys(SourceType, SourceEvent)
			.Concat(Rules.Select(rule => rule.AmountKey.Trim()).Where(value => value.Length > 0))
			.Distinct(StringComparer.Ordinal)
			.OrderBy(value => value, StringComparer.Ordinal)
			.ToArray();
		Replace(PaletteItems, keys.SelectMany(key => Directions.Select(direction => new FinancePostingFlowPaletteItem(key, direction))));
		if (SelectedPaletteItem is null || !PaletteItems.Contains(SelectedPaletteItem))
			SelectedPaletteItem = PaletteItems.FirstOrDefault();
	}

	private void AddSelectedPaletteRule()
	{
		if (SelectedPaletteItem is not null) TryAddRule(SelectedPaletteItem);
	}

	private void RemoveSelectedRule()
	{
		if (!CanManage || SelectedRule is null) return;
		Rules.Remove(SelectedRule);
		RenumberRules();
		SelectedRule = null;
		RebuildProjection();
	}

	private void RenumberRules()
	{
		for (var index = 0; index < Rules.Count; index++) Rules[index].LineNumber = index + 1;
	}

	private void OnRuleChanged()
	{
		if (!_applyingProfile) RebuildProjection();
	}

	private void SetHeader(ref string field, string? value, string propertyName, bool rebuildPalette = false)
	{
		var normalized = value ?? string.Empty;
		if (field == normalized) return;
		field = normalized;
		OnPropertyChanged(propertyName);
		if (_applyingProfile) return;
		if (rebuildPalette) RebuildPalette();
		RebuildProjection();
	}

	private void OnProjectionChanged()
	{
		OnPropertyChanged(nameof(CanSave));
		OnPropertyChanged(nameof(ValidationSummary));
		SaveCommand.RaiseCanExecuteChanged();
		AddRuleCommand.RaiseCanExecuteChanged();
		RemoveRuleCommand.RaiseCanExecuteChanged();
	}

	private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> source)
	{
		target.Clear();
		foreach (var item in source) target.Add(item);
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		RefreshCommand.Dispose();
		SaveCommand.Dispose();
	}
}
