using System.Windows;
using System.Windows.Controls;

namespace Depot.Controls;

public sealed class MetricCard : Control
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(object), typeof(MetricCard), new PropertyMetadata(null));

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(object), typeof(MetricCard), new PropertyMetadata(null));

    public static readonly DependencyProperty SupportingTextProperty = DependencyProperty.Register(
        nameof(SupportingText), typeof(object), typeof(MetricCard), new PropertyMetadata(null));

    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(object), typeof(MetricCard), new PropertyMetadata(null));

    public static readonly DependencyProperty ValueTemplateProperty = DependencyProperty.Register(
        nameof(ValueTemplate), typeof(DataTemplate), typeof(MetricCard), new PropertyMetadata(null));

    public static readonly DependencyProperty IconTemplateProperty = DependencyProperty.Register(
        nameof(IconTemplate), typeof(DataTemplate), typeof(MetricCard), new PropertyMetadata(null));

    static MetricCard()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(MetricCard),
            new FrameworkPropertyMetadata(typeof(MetricCard)));
    }

    public object? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public object? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public object? SupportingText
    {
        get => GetValue(SupportingTextProperty);
        set => SetValue(SupportingTextProperty, value);
    }

    public object? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public DataTemplate? ValueTemplate
    {
        get => (DataTemplate?)GetValue(ValueTemplateProperty);
        set => SetValue(ValueTemplateProperty, value);
    }

    public DataTemplate? IconTemplate
    {
        get => (DataTemplate?)GetValue(IconTemplateProperty);
        set => SetValue(IconTemplateProperty, value);
    }
}
