// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using Depot.Models;

namespace Depot.Controls;

public sealed class ConnectionStatusIndicator : Control
{
	public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
		nameof(State),
		typeof(ConnectionState),
		typeof(ConnectionStatusIndicator),
		new PropertyMetadata(ConnectionState.Disconnected));

	public static readonly DependencyProperty StatusProperty = DependencyProperty.Register(
		nameof(Status),
		typeof(string),
		typeof(ConnectionStatusIndicator),
		new PropertyMetadata(string.Empty));

	public static readonly DependencyProperty DetailProperty = DependencyProperty.Register(
		nameof(Detail),
		typeof(string),
		typeof(ConnectionStatusIndicator),
		new PropertyMetadata(string.Empty));

	public static readonly DependencyProperty DetailForegroundProperty = DependencyProperty.Register(
		nameof(DetailForeground),
		typeof(Brush),
		typeof(ConnectionStatusIndicator),
		new PropertyMetadata(null));

	public static readonly DependencyProperty CompactProperty = DependencyProperty.Register(
		nameof(Compact),
		typeof(bool),
		typeof(ConnectionStatusIndicator),
		new PropertyMetadata(false));

	static ConnectionStatusIndicator()
	{
		DefaultStyleKeyProperty.OverrideMetadata(
			typeof(ConnectionStatusIndicator),
			new FrameworkPropertyMetadata(typeof(ConnectionStatusIndicator)));
	}

	public ConnectionState State
	{
		get => (ConnectionState)GetValue(StateProperty);
		set => SetValue(StateProperty, value);
	}

	public string Status
	{
		get => (string)GetValue(StatusProperty);
		set => SetValue(StatusProperty, value);
	}

	public string Detail
	{
		get => (string)GetValue(DetailProperty);
		set => SetValue(DetailProperty, value);
	}

	public Brush? DetailForeground
	{
		get => (Brush?)GetValue(DetailForegroundProperty);
		set => SetValue(DetailForegroundProperty, value);
	}

	public bool Compact
	{
		get => (bool)GetValue(CompactProperty);
		set => SetValue(CompactProperty, value);
	}
}
