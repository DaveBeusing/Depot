// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Windows.Input;

namespace Depot.Commands;

public sealed class AsyncRelayCommand : ICommand, IDisposable
{
	private readonly Func<CancellationToken, Task> _execute;
	private readonly Func<bool>? _canExecute;
	private CancellationTokenSource? _cancellationTokenSource;
	private bool _isExecuting;

	public AsyncRelayCommand(Func<CancellationToken, Task> execute, Func<bool>? canExecute = null)
	{
		_execute = execute;
		_canExecute = canExecute;
	}

	public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
		: this(_ => execute(), canExecute)
	{
	}

	public bool CanExecute(object? parameter) =>
		!_isExecuting && (_canExecute?.Invoke() ?? true);

	public async void Execute(object? parameter) => await ExecuteAsync();

	public async Task ExecuteAsync()
	{
		if (!CanExecute(null))
		{
			return;
		}

		_isExecuting = true;
		_cancellationTokenSource = new CancellationTokenSource();
		RaiseCanExecuteChanged();
		try
		{
			await _execute(_cancellationTokenSource.Token);
		}
		catch (OperationCanceledException) when (_cancellationTokenSource.IsCancellationRequested)
		{
		}
		finally
		{
			_cancellationTokenSource.Dispose();
			_cancellationTokenSource = null;
			_isExecuting = false;
			RaiseCanExecuteChanged();
		}
	}

	public void Cancel() => _cancellationTokenSource?.Cancel();

	public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

	public event EventHandler? CanExecuteChanged;

	public void Dispose()
	{
		_cancellationTokenSource?.Cancel();
		_cancellationTokenSource?.Dispose();
	}
}


public sealed class AsyncRelayCommand<T> : ICommand, IDisposable where T : class
{
	private readonly Func<T, CancellationToken, Task> _execute;
	private readonly Predicate<T>? _canExecute;
	private CancellationTokenSource? _executionCancellation;
	private bool _isExecuting;
	private bool _disposed;

	public AsyncRelayCommand(Func<T, CancellationToken, Task> execute, Predicate<T>? canExecute = null)
	{
		_execute = execute ?? throw new ArgumentNullException(nameof(execute));
		_canExecute = canExecute;
	}

	public bool CanExecute(object? parameter) =>
		!_isExecuting && parameter is T value && (_canExecute?.Invoke(value) ?? true);

	public async void Execute(object? parameter)
	{
		if (parameter is T value) await ExecuteAsync(value);
	}

	public async Task ExecuteAsync(T parameter)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		if (!CanExecute(parameter)) return;
		_isExecuting = true;
		_executionCancellation = new CancellationTokenSource();
		RaiseCanExecuteChanged();
		try { await _execute(parameter, _executionCancellation.Token); }
		finally
		{
			_executionCancellation.Dispose();
			_executionCancellation = null;
			_isExecuting = false;
			RaiseCanExecuteChanged();
		}
	}

	public void Cancel() => _executionCancellation?.Cancel();

	public event EventHandler? CanExecuteChanged;

	public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		_executionCancellation?.Cancel();
		_executionCancellation?.Dispose();
		_executionCancellation = null;
	}
}
