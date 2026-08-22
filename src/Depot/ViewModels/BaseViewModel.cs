// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.ComponentModel;
using System.Runtime.CompilerServices;

using Depot.Services;

namespace Depot.ViewModels;

public abstract class BaseViewModel : INotifyPropertyChanged
{
	private ViewModelState _state = ViewModelState.Loaded;
	private string _statusText = string.Empty;
	private string? _operationError;
	private OperationSeverity _operationSeverity = OperationSeverity.None;
	private string? _operationActionText;
	private int _editorFocusRequest;

	public int EditorFocusRequest => _editorFocusRequest;

	public ViewModelState State
	{
		get => _state;
		private set
		{
			if (_state == value) return;
			_state = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(IsBusy));
			OnPropertyChanged(nameof(IsLoaded));
			OnPropertyChanged(nameof(IsEmpty));
			OnPropertyChanged(nameof(HasOperationError));
		}
	}

	public bool IsBusy => State == ViewModelState.Loading;
	public bool IsLoaded => State == ViewModelState.Loaded;
	public bool IsEmpty => State == ViewModelState.Empty;
	public bool HasOperationError => State == ViewModelState.Error;
	public bool HasRecoverableConflict => HasOperationError && OperationSeverity == OperationSeverity.Warning;

	public string StatusText
	{
		get => _statusText;
		private set
		{
			if (_statusText == value) return;
			_statusText = value;
			OnPropertyChanged();
		}
	}

	public string? OperationError
	{
		get => _operationError;
		private set
		{
			if (_operationError == value) return;
			_operationError = value;
			OnPropertyChanged();
		}
	}

	public OperationSeverity OperationSeverity
	{
		get => _operationSeverity;
		private set
		{
			if (_operationSeverity == value) return;
			_operationSeverity = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(HasRecoverableConflict));
		}
	}

	public string? OperationActionText
	{
		get => _operationActionText;
		private set
		{
			if (_operationActionText == value) return;
			_operationActionText = value;
			OnPropertyChanged();
		}
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	protected void BeginOperation(string statusText)
	{
		OperationError = null;
		OperationActionText = null;
		OperationSeverity = OperationSeverity.Information;
		StatusText = statusText;
		State = ViewModelState.Loading;
	}

	protected void UpdateOperationStatus(string statusText)
	{
		StatusText = statusText;
	}

	protected void CompleteOperation(bool isEmpty = false, string statusText = "")
	{
		OperationError = null;
		OperationActionText = null;
		OperationSeverity = string.IsNullOrWhiteSpace(statusText) ? OperationSeverity.None : OperationSeverity.Success;
		StatusText = statusText;
		State = isEmpty ? ViewModelState.Empty : ViewModelState.Loaded;
	}

	protected void FailOperation(Exception exception, string statusText = "Operation failed")
	{
		if (exception is ConcurrencyConflictException)
		{
			OperationError = "This record was changed by another user. Reload the latest data and try again.";
			OperationSeverity = OperationSeverity.Warning;
			OperationActionText = "Reload";
		}
		else
		{
			OperationError = exception.Message;
			OperationSeverity = OperationSeverity.Error;
			OperationActionText = null;
		}

		StatusText = statusText;
		State = ViewModelState.Error;
		OnPropertyChanged(nameof(HasRecoverableConflict));
	}

	protected void RequestEditorFocus()
	{
		_editorFocusRequest++;
		OnPropertyChanged(nameof(EditorFocusRequest));
	}

	protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
	{
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}
}
