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
