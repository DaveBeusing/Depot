// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Depot.ViewModels;

public abstract class BaseViewModel : INotifyPropertyChanged
{
	private ViewModelState _state = ViewModelState.Loaded;
	private string _statusText = string.Empty;
	private string? _operationError;

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

	public event PropertyChangedEventHandler? PropertyChanged;

	protected void BeginOperation(string statusText)
	{
		OperationError = null;
		StatusText = statusText;
		State = ViewModelState.Loading;
	}

	protected void CompleteOperation(bool isEmpty = false, string statusText = "")
	{
		OperationError = null;
		StatusText = statusText;
		State = isEmpty ? ViewModelState.Empty : ViewModelState.Loaded;
	}

	protected void FailOperation(Exception exception, string statusText = "Operation failed")
	{
		OperationError = exception.Message;
		StatusText = statusText;
		State = ViewModelState.Error;
	}

	protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
	{
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}

}
