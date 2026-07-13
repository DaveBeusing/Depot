using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using Depot.Models;

namespace Depot.Controls;

public sealed class SidebarUserPanel : Control
{
    public static readonly DependencyProperty DisplayNameProperty = DependencyProperty.Register(
        nameof(DisplayName), typeof(string), typeof(SidebarUserPanel), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty RoleProperty = DependencyProperty.Register(
        nameof(Role), typeof(string), typeof(SidebarUserPanel), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty LogoutCommandProperty = DependencyProperty.Register(
        nameof(LogoutCommand), typeof(ICommand), typeof(SidebarUserPanel), new PropertyMetadata(null));

	public static readonly DependencyProperty ConnectionStateProperty = DependencyProperty.Register(
		nameof(ConnectionState), typeof(ConnectionState), typeof(SidebarUserPanel), new PropertyMetadata(ConnectionState.Disconnected));

	public static readonly DependencyProperty ConnectionStatusProperty = DependencyProperty.Register(
		nameof(ConnectionStatus), typeof(string), typeof(SidebarUserPanel), new PropertyMetadata(string.Empty));

    static SidebarUserPanel()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(SidebarUserPanel),
            new FrameworkPropertyMetadata(typeof(SidebarUserPanel)));
    }

    public string DisplayName
    {
        get => (string)GetValue(DisplayNameProperty);
        set => SetValue(DisplayNameProperty, value);
    }

    public string Role
    {
        get => (string)GetValue(RoleProperty);
        set => SetValue(RoleProperty, value);
    }

    public ICommand? LogoutCommand
    {
        get => (ICommand?)GetValue(LogoutCommandProperty);
        set => SetValue(LogoutCommandProperty, value);
    }

	public ConnectionState ConnectionState
	{
		get => (ConnectionState)GetValue(ConnectionStateProperty);
		set => SetValue(ConnectionStateProperty, value);
	}

	public string ConnectionStatus
	{
		get => (string)GetValue(ConnectionStatusProperty);
		set => SetValue(ConnectionStatusProperty, value);
	}
}
