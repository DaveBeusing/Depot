using System.Windows;
using System.Windows.Controls;

namespace Depot.Controls;

public sealed class PageHeader : Control
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(object), typeof(PageHeader), new PropertyMetadata(null));

    public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register(
        nameof(Subtitle), typeof(object), typeof(PageHeader), new PropertyMetadata(null));

    public static readonly DependencyProperty ActionsProperty = DependencyProperty.Register(
        nameof(Actions), typeof(object), typeof(PageHeader), new PropertyMetadata(null));

    public static readonly DependencyProperty TitleTemplateProperty = DependencyProperty.Register(
        nameof(TitleTemplate), typeof(DataTemplate), typeof(PageHeader), new PropertyMetadata(null));

    public static readonly DependencyProperty SubtitleTemplateProperty = DependencyProperty.Register(
        nameof(SubtitleTemplate), typeof(DataTemplate), typeof(PageHeader), new PropertyMetadata(null));

    public static readonly DependencyProperty ActionsTemplateProperty = DependencyProperty.Register(
        nameof(ActionsTemplate), typeof(DataTemplate), typeof(PageHeader), new PropertyMetadata(null));

    static PageHeader()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(PageHeader),
            new FrameworkPropertyMetadata(typeof(PageHeader)));
    }

    public object? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public object? Subtitle
    {
        get => GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public object? Actions
    {
        get => GetValue(ActionsProperty);
        set => SetValue(ActionsProperty, value);
    }

    public DataTemplate? TitleTemplate
    {
        get => (DataTemplate?)GetValue(TitleTemplateProperty);
        set => SetValue(TitleTemplateProperty, value);
    }

    public DataTemplate? SubtitleTemplate
    {
        get => (DataTemplate?)GetValue(SubtitleTemplateProperty);
        set => SetValue(SubtitleTemplateProperty, value);
    }

    public DataTemplate? ActionsTemplate
    {
        get => (DataTemplate?)GetValue(ActionsTemplateProperty);
        set => SetValue(ActionsTemplateProperty, value);
    }
}
