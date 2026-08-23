// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Depot.Controls;

public sealed class TextInput : Control
{
	private TextBox? _textBox;

	public static readonly DependencyProperty TextProperty =
		DependencyProperty.Register(
			nameof(Text),
			typeof(string),
			typeof(TextInput),
			new FrameworkPropertyMetadata(
				string.Empty,
				FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

	public static readonly DependencyProperty IsReadOnlyProperty =
		DependencyProperty.Register(
			nameof(IsReadOnly),
			typeof(bool),
			typeof(TextInput),
			new PropertyMetadata(false));

	static TextInput()
	{
		DefaultStyleKeyProperty.OverrideMetadata(
			typeof(TextInput),
			new FrameworkPropertyMetadata(typeof(TextInput)));
	}

	public string Text
	{
		get => (string)GetValue(TextProperty);
		set => SetValue(TextProperty, value);
	}

	public bool IsReadOnly
	{
		get => (bool)GetValue(IsReadOnlyProperty);
		set => SetValue(IsReadOnlyProperty, value);
	}

	public override void OnApplyTemplate()
	{
		base.OnApplyTemplate();
		_textBox = FindVisualChild<TextBox>(this);
	}

	protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
	{
		base.OnGotKeyboardFocus(e);

		if (ReferenceEquals(e.NewFocus, this) && _textBox is not null)
		{
			_textBox.Focus();
		}
	}

	private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
	{
		for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
		{
			var child = VisualTreeHelper.GetChild(parent, index);
			if (child is T match)
			{
				return match;
			}

			var nested = FindVisualChild<T>(child);
			if (nested is not null)
			{
				return nested;
			}
		}

		return null;
	}
}
