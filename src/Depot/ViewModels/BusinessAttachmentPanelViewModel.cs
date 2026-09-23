// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Commands;
using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels;

public sealed class BusinessAttachmentPanelViewModel : BaseViewModel, IDisposable
{
	private readonly BusinessAttachmentService _attachments;
	private readonly IFileDialogService _fileDialogs;
	private readonly LatestRequest _loadRequest = new();
	private BusinessAttachmentEntityKind? _entityKind;
	private long? _entityId;
	private BusinessAttachment? _selectedAttachment;
	private string? _description;
	private string? _category;
	private string? _errorMessage;
	private bool _disposed;

	public BusinessAttachmentPanelViewModel(BusinessAttachmentService attachments, IFileDialogService fileDialogs)
	{
		_attachments = attachments ?? throw new ArgumentNullException(nameof(attachments));
		_fileDialogs = fileDialogs ?? throw new ArgumentNullException(nameof(fileDialogs));
		UploadCommand = new AsyncRelayCommand(UploadAsync, CanManage);
		DownloadCommand = new AsyncRelayCommand(DownloadAsync, CanOpen);
		ReplaceCommand = new AsyncRelayCommand(ReplaceAsync, CanManageSelected);
		RetireCommand = new AsyncRelayCommand(RetireAsync, CanManageSelected);
		SaveMetadataCommand = new AsyncRelayCommand(SaveMetadataAsync, CanManageSelected);
		RefreshCommand = new AsyncRelayCommand(RefreshAsync, HasTarget);
	}

	public ObservableCollection<BusinessAttachment> Attachments { get; } = [];
	public ObservableCollection<BusinessAttachmentRevision> Revisions { get; } = [];
	public AsyncRelayCommand UploadCommand { get; }
	public AsyncRelayCommand DownloadCommand { get; }
	public AsyncRelayCommand ReplaceCommand { get; }
	public AsyncRelayCommand RetireCommand { get; }
	public AsyncRelayCommand SaveMetadataCommand { get; }
	public AsyncRelayCommand RefreshCommand { get; }

	public bool HasTarget => _entityKind is not null && _entityId is > 0;
	public bool HasAttachments => Attachments.Count > 0;
	public bool HasNoAttachments => HasTarget && !HasAttachments;
	public bool CanManageTarget => _entityKind is { } kind && _attachments.CanManage(kind);
	public string TargetHint => HasTarget ? "Files are versioned and retained with this business record." : "Select a saved business record to manage attachments.";

	public BusinessAttachment? SelectedAttachment
	{
		get => _selectedAttachment;
		set
		{
			if (_selectedAttachment == value) return;
			_selectedAttachment = value;
			Description = value?.Description;
			Category = value?.Category;
			OnPropertyChanged();
			RaiseCommands();
			_ = LoadRevisionsAsync(value);
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
			SaveMetadataCommand.RaiseCanExecuteChanged();
		}
	}

	public string? Category
	{
		get => _category;
		set
		{
			if (_category == value) return;
			_category = value;
			OnPropertyChanged();
			SaveMetadataCommand.RaiseCanExecuteChanged();
		}
	}

	public string? ErrorMessage
	{
		get => _errorMessage;
		private set
		{
			if (_errorMessage == value) return;
			_errorMessage = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(HasError));
		}
	}

	public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

	public async Task SetTargetAsync(BusinessAttachmentEntityKind entityKind, long? entityId, CancellationToken cancellationToken = default)
	{
		_entityKind = entityId is > 0 ? entityKind : null;
		_entityId = entityId is > 0 ? entityId : null;
		OnPropertyChanged(nameof(HasTarget));
		OnPropertyChanged(nameof(CanManageTarget));
		OnPropertyChanged(nameof(TargetHint));
		RaiseCommands();
		await RefreshAsync(cancellationToken);
	}

	public async Task RefreshAsync(CancellationToken cancellationToken = default)
	{
		var request = _loadRequest.Begin(cancellationToken);
		ErrorMessage = null;
		if (!HasTarget)
		{
			Attachments.Clear();
			Revisions.Clear();
			SelectedAttachment = null;
			RaiseCollectionState();
			return;
		}

		try
		{
			var items = await _attachments.ListAsync(_entityKind!.Value, _entityId!.Value, includeRetired: true, cancellationToken: request.Token);
			if (!request.IsCurrent) return;
			var selectedId = SelectedAttachment?.Id;
			Attachments.Clear();
			foreach (var item in items) Attachments.Add(item);
			SelectedAttachment = Attachments.FirstOrDefault(item => item.Id == selectedId) ?? Attachments.FirstOrDefault();
			RaiseCollectionState();
		}
		catch (OperationCanceledException) when (request.IsCancellationRequested)
		{
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	private async Task UploadAsync(CancellationToken cancellationToken)
	{
		if (!HasTarget) return;
		var path = _fileDialogs.ShowOpenFile(new("Attach business document", "All files (*.*)|*.*"));
		if (string.IsNullOrWhiteSpace(path)) return;
		await RunMutationAsync(async token =>
		{
			await using var stream = File.OpenRead(path);
			await _attachments.AddAsync(_entityKind!.Value, _entityId!.Value, Path.GetFileName(path), null, stream, cancellationToken: token);
		}, cancellationToken);
	}

	private async Task DownloadAsync(CancellationToken cancellationToken)
	{
		if (SelectedAttachment is null) return;
		var path = _fileDialogs.ShowSaveFile(new(
			"Save attachment",
			"All files (*.*)|*.*",
			Path.GetExtension(SelectedAttachment.FileName),
			SelectedAttachment.FileName));
		if (string.IsNullOrWhiteSpace(path)) return;

		ErrorMessage = null;
		try
		{
			var content = await _attachments.OpenAsync(SelectedAttachment.Id, cancellationToken: cancellationToken);
			await using var source = content.Content;
			await using var destination = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
			await source.CopyToAsync(destination, cancellationToken);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	private async Task ReplaceAsync(CancellationToken cancellationToken)
	{
		if (SelectedAttachment is null) return;
		var path = _fileDialogs.ShowOpenFile(new("Replace attachment content", "All files (*.*)|*.*"));
		if (string.IsNullOrWhiteSpace(path)) return;
		var current = SelectedAttachment;
		await RunMutationAsync(async token =>
		{
			await using var stream = File.OpenRead(path);
			await _attachments.ReplaceAsync(current.Id, current.Version, Path.GetFileName(path), null, stream, token);
		}, cancellationToken);
	}

	private async Task SaveMetadataAsync(CancellationToken cancellationToken)
	{
		if (SelectedAttachment is null) return;
		var current = SelectedAttachment;
		await RunMutationAsync(
			token => _attachments.UpdateMetadataAsync(current.Id, new(Description, Category, current.Version), token),
			cancellationToken);
	}

	private async Task RetireAsync(CancellationToken cancellationToken)
	{
		if (SelectedAttachment is null || SelectedAttachment.Status == BusinessAttachmentStatus.Retired) return;
		if (!_fileDialogs.Confirm(new(
			"Retire attachment",
			$"Retire '{SelectedAttachment.FileName}'? Existing revisions remain retained.",
			IsDestructive: true))) return;
		var current = SelectedAttachment;
		await RunMutationAsync(token => _attachments.RetireAsync(current.Id, current.Version, token), cancellationToken);
	}

	private async Task RunMutationAsync(Func<CancellationToken, Task> mutation, CancellationToken cancellationToken)
	{
		ErrorMessage = null;
		try
		{
			await mutation(cancellationToken);
			await RefreshAsync(cancellationToken);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	private async Task LoadRevisionsAsync(BusinessAttachment? attachment, CancellationToken cancellationToken = default)
	{
		Revisions.Clear();
		if (attachment is null) return;
		try
		{
			foreach (var revision in await _attachments.ListRevisionsAsync(attachment.Id, cancellationToken))
				Revisions.Add(revision);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	private bool CanManage() => HasTarget && CanManageTarget;
	private bool CanOpen() => SelectedAttachment is not null;
	private bool CanManageSelected() => SelectedAttachment?.Status == BusinessAttachmentStatus.Active && CanManage();

	private void RaiseCollectionState()
	{
		OnPropertyChanged(nameof(HasAttachments));
		OnPropertyChanged(nameof(HasNoAttachments));
		RaiseCommands();
	}

	private void RaiseCommands()
	{
		UploadCommand.RaiseCanExecuteChanged();
		DownloadCommand.RaiseCanExecuteChanged();
		ReplaceCommand.RaiseCanExecuteChanged();
		RetireCommand.RaiseCanExecuteChanged();
		SaveMetadataCommand.RaiseCanExecuteChanged();
		RefreshCommand.RaiseCanExecuteChanged();
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		_loadRequest.Dispose();
		UploadCommand.Dispose();
		DownloadCommand.Dispose();
		ReplaceCommand.Dispose();
		RetireCommand.Dispose();
		SaveMetadataCommand.Dispose();
		RefreshCommand.Dispose();
	}
}
