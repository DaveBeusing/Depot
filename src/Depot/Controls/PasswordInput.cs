// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Windows;
using System.Windows.Controls;

namespace Depot.Controls;

[TemplatePart(Name = PasswordBoxPartName, Type = typeof(PasswordBox))]
public sealed class PasswordInput : Control
{
	private const string PasswordBoxPartName = "PART_PasswordBox";
	private PasswordBox? _passwordBox;
	private bool _isSynchronizingPassword;

	public static readonly DependencyProperty PasswordValueProperty =
		DependencyProperty.Register(
			nameof(PasswordValue),
			typeof(string),
			typeof(PasswordInput),
			new FrameworkPropertyMetadata(
				string.Empty,
				FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
				OnPasswordValueChanged));

	static PasswordInput()
	{
		DefaultStyleKeyProperty.OverrideMetadata(
			typeof(PasswordInput),
			new FrameworkPropertyMetadata(typeof(PasswordInput)));
	}

	public string PasswordValue
	{
		get => (string)GetValue(PasswordValueProperty);
		set => SetValue(PasswordValueProperty, value);
	}

	public override void OnApplyTemplate()
	{
		if (_passwordBox is not null)
		{
			_passwordBox.PasswordChanged -= OnPasswordChanged;
		}

		base.OnApplyTemplate();
		_passwordBox = GetTemplateChild(PasswordBoxPartName) as PasswordBox;

		if (_passwordBox is null)
		{
			return;
		}

		SynchronizePasswordBox(PasswordValue);
		_passwordBox.PasswordChanged += OnPasswordChanged;
	}

	private static void OnPasswordValueChanged(
		DependencyObject dependencyObject,
		DependencyPropertyChangedEventArgs eventArgs)
	{
		var input = (PasswordInput)dependencyObject;
		if (!input._isSynchronizingPassword)
		{
			input.SynchronizePasswordBox(eventArgs.NewValue as string ?? string.Empty);
		}
	}

	private void OnPasswordChanged(object sender, RoutedEventArgs e)
	{
		if (_passwordBox is null)
		{
			return;
		}

		_isSynchronizingPassword = true;
		SetCurrentValue(PasswordValueProperty, _passwordBox.Password);
		_isSynchronizingPassword = false;
	}

	private void SynchronizePasswordBox(string password)
	{
		if (_passwordBox is null || _passwordBox.Password == password)
		{
			return;
		}

		_isSynchronizingPassword = true;
		_passwordBox.Password = password;
		_isSynchronizingPassword = false;
	}
}
