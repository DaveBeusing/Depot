// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;

using Depot.ViewModels;

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
		DependencyProperty.Register(nameof(IsBusy), typeof(bool), typeof(OperationStatus), new PropertyMetadata(false, OnAnnouncementPropertyChanged));

	public string StatusText
	{
		get => (string)GetValue(StatusTextProperty);
		set => SetValue(StatusTextProperty, value);
	}

	public static readonly DependencyProperty StatusTextProperty =
		DependencyProperty.Register(nameof(StatusText), typeof(string), typeof(OperationStatus), new PropertyMetadata(string.Empty, OnAnnouncementPropertyChanged));

	public string? ErrorText
	{
		get => (string?)GetValue(ErrorTextProperty);
		set => SetValue(ErrorTextProperty, value);
	}

	public static readonly DependencyProperty ErrorTextProperty =
		DependencyProperty.Register(nameof(ErrorText), typeof(string), typeof(OperationStatus), new PropertyMetadata(null, OnAnnouncementPropertyChanged));

	public bool HasError
	{
		get => (bool)GetValue(HasErrorProperty);
		set => SetValue(HasErrorProperty, value);
	}

	public static readonly DependencyProperty HasErrorProperty =
		DependencyProperty.Register(nameof(HasError), typeof(bool), typeof(OperationStatus), new PropertyMetadata(false, OnAnnouncementPropertyChanged));

	public OperationSeverity Severity
	{
		get => (OperationSeverity)GetValue(SeverityProperty);
		set => SetValue(SeverityProperty, value);
	}

	public static readonly DependencyProperty SeverityProperty =
		DependencyProperty.Register(nameof(Severity), typeof(OperationSeverity), typeof(OperationStatus), new PropertyMetadata(OperationSeverity.None, OnAnnouncementPropertyChanged));

	public string? ActionText
	{
		get => (string?)GetValue(ActionTextProperty);
		set => SetValue(ActionTextProperty, value);
	}

	public static readonly DependencyProperty ActionTextProperty =
		DependencyProperty.Register(nameof(ActionText), typeof(string), typeof(OperationStatus), new PropertyMetadata(null, OnActionPropertyChanged));

	public ICommand? ActionCommand
	{
		get => (ICommand?)GetValue(ActionCommandProperty);
		set => SetValue(ActionCommandProperty, value);
	}

	public static readonly DependencyProperty ActionCommandProperty =
		DependencyProperty.Register(nameof(ActionCommand), typeof(ICommand), typeof(OperationStatus), new PropertyMetadata(null, OnActionPropertyChanged));

	public object? ActionCommandParameter
	{
		get => GetValue(ActionCommandParameterProperty);
		set => SetValue(ActionCommandParameterProperty, value);
	}

	public static readonly DependencyProperty ActionCommandParameterProperty =
		DependencyProperty.Register(nameof(ActionCommandParameter), typeof(object), typeof(OperationStatus), new PropertyMetadata(null));

	private static readonly DependencyPropertyKey HasActionPropertyKey =
		DependencyProperty.RegisterReadOnly(nameof(HasAction), typeof(bool), typeof(OperationStatus), new PropertyMetadata(false));

	public static readonly DependencyProperty HasActionProperty = HasActionPropertyKey.DependencyProperty;

	public bool HasAction => (bool)GetValue(HasActionProperty);

	protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);

	private static void OnActionPropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
	{
		var status = (OperationStatus)dependencyObject;
		status.SetValue(HasActionPropertyKey, !string.IsNullOrWhiteSpace(status.ActionText) && status.ActionCommand is not null);
	}

	private static void OnAnnouncementPropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
	{
		var status = (OperationStatus)dependencyObject;
		var message = status.HasError && !string.IsNullOrWhiteSpace(status.ErrorText)
			? status.ErrorText
			: status.StatusText;
		var important = status.HasError || status.Severity == OperationSeverity.Warning;
		AccessibilityAutomation.Announce(status, message, important, "Depot.OperationStatus");
	}
}
