// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Windows;
using System.Windows.Controls;

namespace Depot.Controls;

public sealed class TextInput : Control
{
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
}
