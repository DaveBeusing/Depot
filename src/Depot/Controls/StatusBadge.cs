using System.Windows;
using System.Windows.Controls;

namespace Depot.Controls;

public enum StatusBadgeVariant
{
    Neutral,
    Success,
    Warning,
    Error
}

public sealed class StatusBadge : ContentControl
{
    public static readonly DependencyProperty VariantProperty = DependencyProperty.Register(
        nameof(Variant),
        typeof(StatusBadgeVariant),
        typeof(StatusBadge),
        new PropertyMetadata(StatusBadgeVariant.Neutral));

    static StatusBadge()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(StatusBadge),
            new FrameworkPropertyMetadata(typeof(StatusBadge)));
    }

    public StatusBadgeVariant Variant
    {
        get => (StatusBadgeVariant)GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }
}
