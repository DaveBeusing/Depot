// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels;

public sealed class CommercialRoleCenterViewModel : BaseViewModel
{
	private readonly CommercialRoleCenterService _service;
	private string _title = string.Empty;
	private string _subtitle = string.Empty;
	private string _decisionComment = string.Empty;
	private string _searchText = string.Empty;
	private CommercialRoleItem? _selectedItem;
	private IReadOnlyList<CommercialRoleSection> _sourceSections = [];
	private string _partialFailureText = string.Empty;
	private bool _isDetailPaneVisible = true;

	public CommercialRoleCenterViewModel(CommercialRoleCenterService service, CommercialRoleCenterKind kind)
	{
		_service = service;
		Kind = kind;
	}

	public CommercialRoleCenterKind Kind { get; }
	public string Title { get => _title; private set { if (_title == value) return; _title = value; OnPropertyChanged(); } }
	public string Subtitle { get => _subtitle; private set { if (_subtitle == value) return; _subtitle = value; OnPropertyChanged(); } }
	public ObservableCollection<CommercialRoleSection> Sections { get; } = [];
	public ObservableCollection<CommercialRoleKpi> Kpis { get; } = [];
	public ObservableCollection<CommercialRoleQuickAction> QuickActions { get; } = [];
	public ObservableCollection<CommercialRoleQuickAction> PrimaryQuickActions { get; } = [];
	public ObservableCollection<CommercialRoleQuickAction> SecondaryQuickActions { get; } = [];
	public ObservableCollection<CommercialRoleQuickAction> OverflowQuickActions { get; } = [];
	public bool HasKpis => Kpis.Count > 0;
	public bool HasQuickActions => QuickActions.Count > 0;
	public bool HasPrimaryQuickActions => PrimaryQuickActions.Count > 0;
	public bool HasSecondaryQuickActions => SecondaryQuickActions.Count > 0;
	public bool HasOverflowQuickActions => OverflowQuickActions.Count > 0;
	public bool IsApprovalInbox => Kind == CommercialRoleCenterKind.ApprovalInbox;
	public string WorkspaceId => CommercialRoleColumnProfiles.Get(Kind).WorkspaceId;
	public string DecisionComment { get => _decisionComment; set { if (_decisionComment == value) return; _decisionComment = value; OnPropertyChanged(); } }
	public string SearchText
	{
		get => _searchText;
		set
		{
			if (_searchText == value) return;
			_searchText = value;
			OnPropertyChanged();
			ApplyFilter();
		}
	}
	public CommercialRoleItem? SelectedItem
	{
		get => _selectedItem;
		set
		{
			if (ReferenceEquals(_selectedItem, value)) return;
			_selectedItem = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(HasSelectedItem));
			OnPropertyChanged(nameof(HasNoSelectedItem));
		}
	}
	public bool HasSelectedItem => SelectedItem is not null;
	public bool HasNoSelectedItem => SelectedItem is null;
	public bool IsDetailPaneVisible
	{
		get => _isDetailPaneVisible;
		set { if (_isDetailPaneVisible == value) return; _isDetailPaneVisible = value; OnPropertyChanged(); }
	}
	public string PartialFailureText
	{
		get => _partialFailureText;
		private set { if (_partialFailureText == value) return; _partialFailureText = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasPartialFailures)); }
	}
	public bool HasPartialFailures => !string.IsNullOrWhiteSpace(PartialFailureText);

	public async Task LoadAsync(CancellationToken cancellationToken = default)
	{
		BeginOperation($"Loading {Kind}");
		try
		{
			var snapshot = await _service.GetAsync(Kind, cancellationToken);
			Title = snapshot.Title;
			Subtitle = snapshot.Subtitle;
			_sourceSections = snapshot.Sections;
			ApplyFilter();
			SelectedItem = null;
			PartialFailureText = snapshot.Failures.Count == 0
				? string.Empty
				: $"Some work sources are unavailable: {string.Join(", ", snapshot.Failures.Select(failure => failure.Provider))}.";
			Replace(Kpis, snapshot.Kpis);
			Replace(QuickActions, snapshot.QuickActions);
			Replace(PrimaryQuickActions, snapshot.QuickActions.Where(action => action.IsPrimary));
			Replace(SecondaryQuickActions, snapshot.QuickActions.Where(action => action.IsSecondary));
			Replace(OverflowQuickActions, snapshot.QuickActions.Where(action => action.IsOverflow));
			OnPropertyChanged(nameof(HasKpis));
			OnPropertyChanged(nameof(HasQuickActions));
			OnPropertyChanged(nameof(HasPrimaryQuickActions));
			OnPropertyChanged(nameof(HasSecondaryQuickActions));
			OnPropertyChanged(nameof(HasOverflowQuickActions));
			CompleteOperation(Sections.All(section => section.IsEmpty), snapshot.Failures.Count == 0
				? "Role center is current."
				: PartialFailureText);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
		catch (Exception exception) { FailOperation(exception, "Role center could not be loaded."); }
	}

	public async Task DecideAsync(CommercialRoleItem item, bool approve, CancellationToken cancellationToken = default)
	{
		BeginOperation(approve ? "Approving..." : "Rejecting...");
		try
		{
			await _service.DecideAsync(item, approve, DecisionComment, cancellationToken);
			DecisionComment = string.Empty;
			await LoadAsync(cancellationToken);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
		catch (Exception exception) { FailOperation(exception, approve ? "Approval failed." : "Rejection failed."); }
	}

	public void ToggleDetailPane() => IsDetailPaneVisible = !IsDetailPaneVisible;

	private void ApplyFilter()
	{
		var filter = SearchText.Trim();
		var isFiltered = filter.Length > 0;
		Replace(Sections, _sourceSections.Select(section => isFiltered
			? section with
			{
				Items = section.Items.Where(item => Matches(item, filter)).ToArray(),
				IsFiltered = true
			}
			: section with { IsFiltered = false }));
	}

	private static bool Matches(CommercialRoleItem item, string filter)
	{
		return Contains(item.DisplayNumber, filter) ||
			Contains(item.Title, filter) ||
			Contains(item.Requester, filter) ||
			Contains(item.Context, filter) ||
			Contains(item.Status, filter) ||
			Contains(item.Currency, filter) ||
			Contains(item.StateDetail, filter) ||
			Contains(item.NextAction, filter);
	}

	private static bool Contains(string? value, string filter) =>
		!string.IsNullOrWhiteSpace(value) && value.Contains(filter, StringComparison.CurrentCultureIgnoreCase);

	private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
	{
		target.Clear();
		foreach (var value in values) target.Add(value);
	}
}
