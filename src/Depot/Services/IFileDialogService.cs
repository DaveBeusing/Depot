namespace Depot.Services;

public interface IFileDialogService
{
    string? ShowOpenFile(OpenFileDialogRequest request);

    string? ShowSaveFile(SaveFileDialogRequest request);

    bool Confirm(ConfirmationDialogRequest request);
}

public sealed record OpenFileDialogRequest(
    string Title,
    string Filter);

public sealed record SaveFileDialogRequest(
    string Title,
    string Filter,
    string DefaultExtension,
    string SuggestedFileName,
    bool OverwritePrompt = true);

public sealed record ConfirmationDialogRequest(
    string Title,
    string Message,
    bool IsDestructive = false);
