// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace Depot.Controls;

public static class FocusRequest
{
	public static readonly DependencyProperty RequestIdProperty = DependencyProperty.RegisterAttached(
		"RequestId",
		typeof(int),
		typeof(FocusRequest),
		new PropertyMetadata(0, OnRequestChanged));

	public static int GetRequestId(DependencyObject element) => (int)element.GetValue(RequestIdProperty);

	public static void SetRequestId(DependencyObject element, int value) => element.SetValue(RequestIdProperty, value);

	private static void OnRequestChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
	{
		if (dependencyObject is not FrameworkElement element || Equals(args.OldValue, args.NewValue)) return;
		element.Dispatcher.BeginInvoke(new Action(() =>
		{
			if (!element.IsVisible || !element.IsEnabled) return;
			element.Focus();
			Keyboard.Focus(element);
		}), DispatcherPriority.Input);
	}
}
