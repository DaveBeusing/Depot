// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Commands;
using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels.Administration;

public sealed class PrivacyDataViewModel : BaseViewModel, IDisposable
{
	private readonly DataSubjectAccessService _service;
	private readonly IFileDialogService _fileDialogs;
	private string _searchText = string.Empty;
	private DateTime? _generatedUtc;

	public PrivacyDataViewModel(DataSubjectAccessService service, IFileDialogService fileDialogs)
	{
		_service = service;
		_fileDialogs = fileDialogs;
		SearchCommand = new AsyncRelayCommand(SearchAsync);
		ExportCommand = new AsyncRelayCommand(ExportAsync, () => Records.Count > 0 && SearchText.Trim().Length >= 2);
	}

	public ObservableCollection<PersonalDataRecord> Records { get; } = [];
	public AsyncRelayCommand SearchCommand { get; }
	public AsyncRelayCommand ExportCommand { get; }

	public string SearchText
	{
		get => _searchText;
		set
		{
			if (_searchText == value) return;
			_searchText = value;
			OnPropertyChanged();
			ExportCommand.RaiseCanExecuteChanged();
		}
	}

	public DateTime? GeneratedUtc
	{
		get => _generatedUtc;
		private set { _generatedUtc = value; OnPropertyChanged(); OnPropertyChanged(nameof(GeneratedDisplay)); }
	}

	public string GeneratedDisplay => GeneratedUtc is null ? "No search run yet" : $"Discovery generated {GeneratedUtc.Value.ToLocalTime():g}";

	public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

	private async Task SearchAsync(CancellationToken cancellationToken)
	{
		BeginOperation("Searching personal data");
		try
		{
			var result = await _service.SearchAsync(SearchText, cancellationToken);
			CollectionSynchronizer.Replace(Records, result.Records);
			GeneratedUtc = result.GeneratedUtc;
			ExportCommand.RaiseCanExecuteChanged();
			CompleteOperation(Records.Count == 0, Records.Count == 0 ? "No matching personal data" : $"{Records.Count:N0} matching records");
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
		catch (Exception exception) { FailOperation(exception, "Personal-data search failed"); }
	}

	private async Task ExportAsync(CancellationToken cancellationToken)
	{
		if (Records.Count == 0) return;
		var path = _fileDialogs.ShowSaveFile(new SaveFileDialogRequest(
			"Export personal-data discovery",
			"JSON files (*.json)|*.json",
			".json",
			$"depot-personal-data-{DateTime.Now:yyyyMMdd-HHmmss}.json"));
		if (string.IsNullOrWhiteSpace(path)) return;
		BeginOperation("Exporting personal data");
		try
		{
			await _service.ExportJsonAsync(SearchText, path, cancellationToken);
			CompleteOperation(false, "Personal-data discovery exported");
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
		catch (Exception exception) { FailOperation(exception, "Personal-data export failed"); }
	}

	public void Dispose()
	{
		SearchCommand.Dispose();
		ExportCommand.Dispose();
	}
}
