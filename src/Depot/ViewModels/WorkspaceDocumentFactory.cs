// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.ViewModels;

public sealed record WorkspaceDocumentDescriptor(
	string Key,
	string Title,
	ShellRoute Route,
	string IconData,
	string HelpTopicId);

public static class WorkspaceDocumentFactory
{
	public static ShellNavigationItem Create<TViewModel>(
		WorkspaceDocumentDescriptor descriptor,
		Func<TViewModel> createContent,
		Func<TViewModel, CancellationToken, Task> loadAsync,
		bool ownsContent = true)
		where TViewModel : BaseViewModel
	{
		ArgumentNullException.ThrowIfNull(descriptor);
		ArgumentNullException.ThrowIfNull(createContent);
		ArgumentNullException.ThrowIfNull(loadAsync);
		return new ShellNavigationItem(
			descriptor.Title,
			descriptor.IconData,
			() => createContent(),
			(viewModel, token) => loadAsync((TViewModel)viewModel, token),
			descriptor.HelpTopicId,
			route: descriptor.Route,
			tabKey: descriptor.Key,
			isDocument: true,
			ownsContent: ownsContent);
	}
}
