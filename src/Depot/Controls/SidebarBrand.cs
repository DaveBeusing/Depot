using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Depot.Controls;

public sealed class SidebarBrand : Control
{
    public static readonly DependencyProperty LogoSourceProperty = DependencyProperty.Register(
        nameof(LogoSource), typeof(ImageSource), typeof(SidebarBrand), new PropertyMetadata(null));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(SidebarBrand), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register(
        nameof(Subtitle), typeof(string), typeof(SidebarBrand), new PropertyMetadata(string.Empty));

    static SidebarBrand()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(SidebarBrand),
            new FrameworkPropertyMetadata(typeof(SidebarBrand)));
    }

    public ImageSource? LogoSource
    {
        get => (ImageSource?)GetValue(LogoSourceProperty);
        set => SetValue(LogoSourceProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }
}
