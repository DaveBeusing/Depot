using System.Windows;
using System.Windows.Controls;

namespace Depot.Controls;

public sealed class EmptyState : Control
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(object), typeof(EmptyState), new PropertyMetadata(null));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(object), typeof(EmptyState), new PropertyMetadata(null));

    public static readonly DependencyProperty ActionProperty = DependencyProperty.Register(
        nameof(Action), typeof(object), typeof(EmptyState), new PropertyMetadata(null));

    public static readonly DependencyProperty TitleTemplateProperty = DependencyProperty.Register(
        nameof(TitleTemplate), typeof(DataTemplate), typeof(EmptyState), new PropertyMetadata(null));

    public static readonly DependencyProperty DescriptionTemplateProperty = DependencyProperty.Register(
        nameof(DescriptionTemplate), typeof(DataTemplate), typeof(EmptyState), new PropertyMetadata(null));

    public static readonly DependencyProperty ActionTemplateProperty = DependencyProperty.Register(
        nameof(ActionTemplate), typeof(DataTemplate), typeof(EmptyState), new PropertyMetadata(null));

    static EmptyState()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(EmptyState),
            new FrameworkPropertyMetadata(typeof(EmptyState)));
    }

    public object? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public object? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public object? Action
    {
        get => GetValue(ActionProperty);
        set => SetValue(ActionProperty, value);
    }

    public DataTemplate? TitleTemplate
    {
        get => (DataTemplate?)GetValue(TitleTemplateProperty);
        set => SetValue(TitleTemplateProperty, value);
    }

    public DataTemplate? DescriptionTemplate
    {
        get => (DataTemplate?)GetValue(DescriptionTemplateProperty);
        set => SetValue(DescriptionTemplateProperty, value);
    }

    public DataTemplate? ActionTemplate
    {
        get => (DataTemplate?)GetValue(ActionTemplateProperty);
        set => SetValue(ActionTemplateProperty, value);
    }
}
