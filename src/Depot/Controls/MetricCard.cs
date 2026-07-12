using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

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
        nameof(Icon), typeof(Geometry), typeof(MetricCard), new PropertyMetadata(null));

    public static readonly DependencyProperty ValueTemplateProperty = DependencyProperty.Register(
        nameof(ValueTemplate), typeof(DataTemplate), typeof(MetricCard), new PropertyMetadata(null));

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

    public Geometry? Icon
    {
        get => (Geometry?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public DataTemplate? ValueTemplate
    {
        get => (DataTemplate?)GetValue(ValueTemplateProperty);
        set => SetValue(ValueTemplateProperty, value);
    }
}
