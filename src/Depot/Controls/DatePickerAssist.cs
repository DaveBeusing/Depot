// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Depot.Controls;

public static class DatePickerAssist
{
	public static readonly DependencyProperty IsReadOnlyProperty = DependencyProperty.RegisterAttached(
		"IsReadOnly",
		typeof(bool),
		typeof(DatePickerAssist),
		new FrameworkPropertyMetadata(false, OnIsReadOnlyChanged));

	public static bool GetIsReadOnly(DependencyObject dependencyObject) =>
		(bool)dependencyObject.GetValue(IsReadOnlyProperty);

	public static void SetIsReadOnly(DependencyObject dependencyObject, bool value) =>
		dependencyObject.SetValue(IsReadOnlyProperty, value);

	private static void OnIsReadOnlyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
	{
		if (dependencyObject is not DatePicker datePicker)
			return;

		datePicker.PreviewKeyDown -= OnPreviewKeyDown;
		datePicker.CalendarOpened -= OnCalendarOpened;

		if (args.NewValue is not true)
			return;

		datePicker.IsDropDownOpen = false;
		datePicker.PreviewKeyDown += OnPreviewKeyDown;
		datePicker.CalendarOpened += OnCalendarOpened;
	}

	private static void OnPreviewKeyDown(object sender, KeyEventArgs args)
	{
		if (sender is not DatePicker datePicker || !GetIsReadOnly(datePicker))
			return;

		if (args.Key is Key.Enter or Key.Space ||
			(args.Key is Key.Down && Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)))
		{
			args.Handled = true;
		}
	}

	private static void OnCalendarOpened(object? sender, RoutedEventArgs args)
	{
		if (sender is DatePicker datePicker && GetIsReadOnly(datePicker))
			datePicker.IsDropDownOpen = false;
	}
}
