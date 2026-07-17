// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Windows;
using System.Windows.Controls;

namespace Depot.Controls;

public sealed class OperationStatus : Control
{
	static OperationStatus()
	{
		DefaultStyleKeyProperty.OverrideMetadata(
			typeof(OperationStatus),
			new FrameworkPropertyMetadata(typeof(OperationStatus)));
	}

	public bool IsBusy
	{
		get => (bool)GetValue(IsBusyProperty);
		set => SetValue(IsBusyProperty, value);
	}

	public static readonly DependencyProperty IsBusyProperty =
		DependencyProperty.Register(
			nameof(IsBusy),
			typeof(bool),
			typeof(OperationStatus),
			new PropertyMetadata(false));

	public string StatusText
	{
		get => (string)GetValue(StatusTextProperty);
		set => SetValue(StatusTextProperty, value);
	}

	public static readonly DependencyProperty StatusTextProperty =
		DependencyProperty.Register(
			nameof(StatusText),
			typeof(string),
			typeof(OperationStatus),
			new PropertyMetadata(string.Empty));

	public string? ErrorText
	{
		get => (string?)GetValue(ErrorTextProperty);
		set => SetValue(ErrorTextProperty, value);
	}

	public static readonly DependencyProperty ErrorTextProperty =
		DependencyProperty.Register(
			nameof(ErrorText),
			typeof(string),
			typeof(OperationStatus),
			new PropertyMetadata(null));

	public bool HasError
	{
		get => (bool)GetValue(HasErrorProperty);
		set => SetValue(HasErrorProperty, value);
	}

	public static readonly DependencyProperty HasErrorProperty =
		DependencyProperty.Register(
			nameof(HasError),
			typeof(bool),
			typeof(OperationStatus),
			new PropertyMetadata(false));
}
