using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Depot.Controls;

public sealed class SearchBox : TextBox
{
    public static readonly RoutedCommand ClearCommand = new(nameof(ClearCommand), typeof(SearchBox));

    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
        nameof(Placeholder),
        typeof(string),
        typeof(SearchBox),
        new PropertyMetadata("Search"));

    static SearchBox()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(SearchBox),
            new FrameworkPropertyMetadata(typeof(SearchBox)));

        CommandManager.RegisterClassCommandBinding(
            typeof(SearchBox),
            new CommandBinding(ClearCommand, ExecuteClear, CanExecuteClear));
    }

    public string? Placeholder
    {
        get => (string?)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    private static void CanExecuteClear(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = sender is SearchBox searchBox && !string.IsNullOrEmpty(searchBox.Text);
        e.Handled = true;
    }

    private static void ExecuteClear(object sender, ExecutedRoutedEventArgs e)
    {
        if (sender is not SearchBox searchBox)
        {
            return;
        }

        searchBox.Clear();
        searchBox.Focus();
        e.Handled = true;
    }
}
