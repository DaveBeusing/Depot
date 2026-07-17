using System.Windows;

using Microsoft.Win32;

namespace Depot.Services;

public sealed class FileDialogService : IFileDialogService
{
    public string? ShowOpenFile(OpenFileDialogRequest request)
    {
        var dialog = new OpenFileDialog
        {
            Title = request.Title,
            Filter = request.Filter
        };

        return ShowDialog(dialog) ? dialog.FileName : null;
    }

    public string? ShowSaveFile(SaveFileDialogRequest request)
    {
        var dialog = new SaveFileDialog
        {
            Title = request.Title,
            Filter = request.Filter,
            DefaultExt = request.DefaultExtension,
            FileName = request.SuggestedFileName,
            OverwritePrompt = request.OverwritePrompt
        };

        return ShowDialog(dialog) ? dialog.FileName : null;
    }

    public bool Confirm(ConfirmationDialogRequest request)
    {
        var owner = Application.Current?.MainWindow;
        var image = request.IsDestructive ? MessageBoxImage.Warning : MessageBoxImage.Question;
        var result = owner is null
            ? MessageBox.Show(request.Message, request.Title, MessageBoxButton.YesNo, image, MessageBoxResult.No)
            : MessageBox.Show(owner, request.Message, request.Title, MessageBoxButton.YesNo, image, MessageBoxResult.No);
        return result == MessageBoxResult.Yes;
    }

    private static bool ShowDialog(CommonDialog dialog)
    {
        var owner = Application.Current?.MainWindow;
        return owner is null
            ? dialog.ShowDialog() == true
            : dialog.ShowDialog(owner) == true;
    }
}
