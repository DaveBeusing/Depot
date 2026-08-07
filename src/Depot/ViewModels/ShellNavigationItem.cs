// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.ViewModels;

public sealed class ShellNavigationItem : IDisposable
{
	private readonly Lazy<BaseViewModel> _content;
	private readonly Func<BaseViewModel, CancellationToken, Task> _loadAsync;
	private readonly Func<BaseViewModel, CancellationToken, Task>? _refreshAsync;
	private readonly bool _ownsLoadState;
	private readonly NavigationLoadState _loadState = new();

	public ShellNavigationItem(
		string name,
		string iconData,
		Func<BaseViewModel> createContent,
		Func<BaseViewModel, CancellationToken, Task> loadAsync,
		string helpTopicId,
		bool isSeparated = false,
		bool ownsLoadState = true,
		Func<BaseViewModel, CancellationToken, Task>? refreshAsync = null)
	{
		Name = name;
		IconData = iconData;
		_content = new Lazy<BaseViewModel>(createContent);
		_loadAsync = loadAsync;
		_refreshAsync = refreshAsync;
		_ownsLoadState = ownsLoadState;
		HelpTopicId = helpTopicId;
		IsSeparated = isSeparated;
	}

	public string Name { get; }
	public string IconData { get; }
	public BaseViewModel Content => _content.Value;
	public bool IsContentCreated => _content.IsValueCreated;
	public string HelpTopicId { get; }
	public bool IsSeparated { get; }
	public NavigationLoadStatus LoadStatus => _loadState.Status;

	public Task ActivateAsync(CancellationToken cancellationToken = default) => _ownsLoadState
		? _loadState.ActivateAsync(token => _loadAsync(Content, token), cancellationToken)
		: _loadAsync(Content, cancellationToken);

	public Task RefreshAsync(CancellationToken cancellationToken = default) => _ownsLoadState
		? _loadState.RefreshAsync(token => _loadAsync(Content, token), cancellationToken)
		: (_refreshAsync ?? _loadAsync)(Content, cancellationToken);

	public void MarkStale() => _loadState.MarkStale();

	public void Dispose()
	{
		_loadState.Dispose();
		if (_content.IsValueCreated && _content.Value is IDisposable disposable) disposable.Dispose();
	}
}
