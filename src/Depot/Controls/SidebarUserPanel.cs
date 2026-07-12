using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Depot.Controls;

public sealed class SidebarUserPanel : Control
{
    public static readonly DependencyProperty DisplayNameProperty = DependencyProperty.Register(
        nameof(DisplayName), typeof(string), typeof(SidebarUserPanel), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty RoleProperty = DependencyProperty.Register(
        nameof(Role), typeof(string), typeof(SidebarUserPanel), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty LogoutCommandProperty = DependencyProperty.Register(
        nameof(LogoutCommand), typeof(ICommand), typeof(SidebarUserPanel), new PropertyMetadata(null));

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
}
