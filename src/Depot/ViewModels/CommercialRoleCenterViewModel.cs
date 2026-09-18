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
	public bool HasKpis => Kpis.Count > 0;
	public bool HasQuickActions => QuickActions.Count > 0;
	public bool IsApprovalInbox => Kind == CommercialRoleCenterKind.ApprovalInbox;
	public string DecisionComment { get => _decisionComment; set { if (_decisionComment == value) return; _decisionComment = value; OnPropertyChanged(); } }

	public async Task LoadAsync(CancellationToken cancellationToken = default)
	{
		BeginOperation($"Loading {Kind}");
		try
		{
			var snapshot = await _service.GetAsync(Kind, cancellationToken);
			Title = snapshot.Title;
			Subtitle = snapshot.Subtitle;
			Replace(Sections, snapshot.Sections);
			Replace(Kpis, snapshot.Kpis);
			Replace(QuickActions, snapshot.QuickActions);
			OnPropertyChanged(nameof(HasKpis));
			OnPropertyChanged(nameof(HasQuickActions));
			CompleteOperation(Sections.All(section => section.IsEmpty), snapshot.Failures.Count == 0
				? "Role center is current."
				: $"Some work sources are unavailable: {string.Join(", ", snapshot.Failures.Select(failure => failure.Provider))}.");
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

	private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
	{
		target.Clear();
		foreach (var value in values) target.Add(value);
	}
}
