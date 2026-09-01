// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Commands;
using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels.Administration;

public sealed class SecurityCenterViewModel : BaseViewModel, IDisposable
{
	private readonly SecurityEventService _service;
	private string _searchText = string.Empty;
	private SecurityEventSeverity? _minimumSeverity;
	private bool _onlyUnreviewed = true;
	private SecurityEventRowViewModel? _selectedEvent;
	private long _events24Hours;
	private long _suspicious24Hours;
	private long _highRiskOpen;
	private long _blocked24Hours;
	private bool _disposed;

	public SecurityCenterViewModel(SecurityEventService service)
	{
		_service = service;
		RefreshCommand = new AsyncRelayCommand(LoadAsync);
		MarkReviewedCommand = new AsyncRelayCommand(MarkReviewedAsync, () => SelectedEvent is { IsReviewed: false } && CanManage);
	}

	public ObservableCollection<SecurityEventRowViewModel> Events { get; } = [];
	public AsyncRelayCommand RefreshCommand { get; }
	public AsyncRelayCommand MarkReviewedCommand { get; }
	public bool CanManage => _service.CanManage;
	public bool HasEvents => Events.Count > 0;
	public bool HasNoEvents => !HasEvents;
	public long Events24Hours { get => _events24Hours; private set { _events24Hours = value; OnPropertyChanged(); } }
	public long Suspicious24Hours { get => _suspicious24Hours; private set { _suspicious24Hours = value; OnPropertyChanged(); } }
	public long HighRiskOpen { get => _highRiskOpen; private set { _highRiskOpen = value; OnPropertyChanged(); } }
	public long Blocked24Hours { get => _blocked24Hours; private set { _blocked24Hours = value; OnPropertyChanged(); } }
	public string SearchText { get => _searchText; set { if (_searchText == value) return; _searchText = value; OnPropertyChanged(); } }
	public SecurityEventSeverity? MinimumSeverity { get => _minimumSeverity; set { if (_minimumSeverity == value) return; _minimumSeverity = value; OnPropertyChanged(); } }
	public bool OnlyUnreviewed { get => _onlyUnreviewed; set { if (_onlyUnreviewed == value) return; _onlyUnreviewed = value; OnPropertyChanged(); } }
	public IReadOnlyList<SecurityEventSeverity?> SeverityOptions { get; } = [null, SecurityEventSeverity.Warning, SecurityEventSeverity.High, SecurityEventSeverity.Critical];

	public SecurityEventRowViewModel? SelectedEvent
	{
		get => _selectedEvent;
		set
		{
			if (_selectedEvent == value) return;
			_selectedEvent = value;
			OnPropertyChanged();
			MarkReviewedCommand.RaiseCanExecuteChanged();
		}
	}

	public async Task LoadAsync(CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		BeginOperation("Refreshing security events");
		try
		{
			var metricsTask = _service.GetMetricsAsync(cancellationToken);
			var eventsTask = _service.GetRecentAsync(new SecurityEventFilter(SearchText, MinimumSeverity, OnlyUnreviewed ? false : null), cancellationToken);
			await Task.WhenAll(metricsTask, eventsTask);
			var metrics = await metricsTask;
			Events24Hours = metrics.Events24Hours;
			Suspicious24Hours = metrics.Suspicious24Hours;
			HighRiskOpen = metrics.HighRiskOpen;
			Blocked24Hours = metrics.Blocked24Hours;
			CollectionSynchronizer.Replace(Events, (await eventsTask).Select(item => new SecurityEventRowViewModel(item)).ToArray());
			SelectedEvent = null;
			OnPropertyChanged(nameof(HasEvents));
			OnPropertyChanged(nameof(HasNoEvents));
			CompleteOperation(Events.Count == 0, "Security events refreshed");
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { CompleteOperation(Events.Count == 0); }
		catch (Exception exception) { FailOperation(exception, "Security events could not be loaded"); }
	}

	private async Task MarkReviewedAsync(CancellationToken cancellationToken)
	{
		var selected = SelectedEvent;
		if (selected is null || selected.IsReviewed || !CanManage) return;
		BeginOperation("Marking security event reviewed");
		try
		{
			await _service.MarkReviewedAsync(selected.Id, selected.Version, cancellationToken);
			await LoadAsync(cancellationToken);
			CompleteOperation(Events.Count == 0, "Security event reviewed");
		}
		catch (Exception exception) when (exception is not OperationCanceledException) { FailOperation(exception, "Security event could not be reviewed"); }
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		RefreshCommand.Dispose();
		MarkReviewedCommand.Dispose();
	}
}

public sealed class SecurityEventRowViewModel
{
	public SecurityEventRowViewModel(SecurityEventListItem item)
	{
		Id = item.Id; Version = item.Version; TimestampLocal = item.TimestampLocal; EventType = Split(item.EventType.ToString());
		Severity = item.Severity.ToString(); Account = string.IsNullOrWhiteSpace(item.AccountIdentifier) ? "—" : item.AccountIdentifier;
		MachineName = string.IsNullOrWhiteSpace(item.MachineName) ? "—" : item.MachineName; Summary = item.Summary;
		Details = string.IsNullOrWhiteSpace(item.Details) ? "—" : item.Details; IsReviewed = item.IsReviewed;
		Status = item.IsReviewed ? "Reviewed" : "Open";
	}
	public long Id { get; }
	public long Version { get; }
	public DateTime TimestampLocal { get; }
	public string EventType { get; }
	public string Severity { get; }
	public string Account { get; }
	public string MachineName { get; }
	public string Summary { get; }
	public string Details { get; }
	public bool IsReviewed { get; }
	public string Status { get; }
	private static string Split(string value) => string.Concat(value.Select((c, i) => i > 0 && char.IsUpper(c) ? $" {c}" : c.ToString()));
}
