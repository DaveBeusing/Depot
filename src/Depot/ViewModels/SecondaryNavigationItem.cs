// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.ViewModels;

public sealed class SecondaryNavigationItem : IDisposable
{
	private readonly Lazy<BaseViewModel> _content;
	private readonly Func<BaseViewModel, CancellationToken, Task> _loadAsync;
	private readonly NavigationLoadState _loadState = new();
	private readonly bool _ownsContent;
	private readonly bool _alwaysActivate;

	public SecondaryNavigationItem(
		string name,
		Func<BaseViewModel> createContent,
		Func<BaseViewModel, CancellationToken, Task> loadAsync,
		string helpTopicId,
		Action? activate = null,
		bool ownsContent = true,
		bool alwaysActivate = false,
		bool isVisible = true)
	{
		Name = name;
		_content = new Lazy<BaseViewModel>(createContent);
		_loadAsync = loadAsync;
		HelpTopicId = helpTopicId;
		Activate = activate;
		_ownsContent = ownsContent;
		_alwaysActivate = alwaysActivate;
		IsVisible = isVisible;
	}

	public string Name { get; }
	public BaseViewModel Content => _content.Value;
	public bool IsContentCreated => _content.IsValueCreated;
	public string HelpTopicId { get; }
	public Action? Activate { get; }
	public bool IsVisible { get; set; }
	public NavigationLoadStatus LoadStatus => _loadState.Status;

	public Task ActivateAsync(CancellationToken cancellationToken = default)
	{
		Activate?.Invoke();
		return _alwaysActivate
			? _loadState.RefreshAsync(token => _loadAsync(Content, token), cancellationToken)
			: _loadState.ActivateAsync(token => _loadAsync(Content, token), cancellationToken);
	}

	public Task RefreshAsync(CancellationToken cancellationToken = default)
	{
		Activate?.Invoke();
		return _loadState.RefreshAsync(token => _loadAsync(Content, token), cancellationToken);
	}

	public void MarkStale() => _loadState.MarkStale();

	public void Dispose()
	{
		_loadState.Dispose();
		if (_ownsContent && _content.IsValueCreated && _content.Value is IDisposable disposable) disposable.Dispose();
	}
}
