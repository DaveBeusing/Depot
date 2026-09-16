// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Depot.Services;

public static class DesktopAccessibilityRuntime
{
	private const string FocusVisualResourceKey = "AppKeyboardFocusVisualStyle";
	private static int _registered;

	public static void Register()
	{
		if (Interlocked.Exchange(ref _registered, 1) != 0) return;
		EventManager.RegisterClassHandler(
			typeof(Control),
			FrameworkElement.LoadedEvent,
			new RoutedEventHandler(OnControlLoaded));
	}

	private static void OnControlLoaded(object sender, RoutedEventArgs e)
	{
		if (sender is not Control control || !control.Focusable || !control.IsTabStop || control.FocusVisualStyle is not null) return;
		if (Application.Current?.TryFindResource(FocusVisualResourceKey) is Style focusVisual)
			control.SetCurrentValue(FrameworkElement.FocusVisualStyleProperty, focusVisual);
	}
}

public sealed class FocusRestorationScope : IDisposable
{
	private readonly IInputElement? _focusedElement = Keyboard.FocusedElement;
	private bool _disposed;

	private FocusRestorationScope()
	{
	}

	public static FocusRestorationScope Capture() => new();

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		if (_focusedElement is null) return;
		if (_focusedElement is UIElement element && (!element.IsVisible || !element.IsEnabled || !element.Focusable)) return;

		var dispatcher = Application.Current?.Dispatcher;
		if (dispatcher is null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) return;
		dispatcher.BeginInvoke(
			DispatcherPriority.Input,
			new Action(() =>
			{
				if (_focusedElement is UIElement candidate && (!candidate.IsVisible || !candidate.IsEnabled || !candidate.Focusable)) return;
				Keyboard.Focus(_focusedElement);
			}));
	}
}
