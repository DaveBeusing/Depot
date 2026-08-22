// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.ViewModels;

public sealed class NavigationHistoryService
{
	private readonly List<ShellRoute> _back = [];
	private readonly List<ShellRoute> _forward = [];
	private ShellRoute? _current;

	public bool CanGoBack => _back.Count > 0;
	public bool CanGoForward => _forward.Count > 0;
	public ShellRoute? Current => _current;

	public void Record(ShellRoute route)
	{
		if (_current == route) return;
		if (_current is { } current) _back.Add(current);
		_current = route;
		_forward.Clear();
	}

	public ShellRoute? GoBack()
	{
		if (_back.Count == 0) return null;
		if (_current is { } current) _forward.Add(current);
		var target = _back[^1];
		_back.RemoveAt(_back.Count - 1);
		_current = target;
		return target;
	}

	public ShellRoute? GoForward()
	{
		if (_forward.Count == 0) return null;
		if (_current is { } current) _back.Add(current);
		var target = _forward[^1];
		_forward.RemoveAt(_forward.Count - 1);
		_current = target;
		return target;
	}

	public void Reset(ShellRoute? current = null)
	{
		_back.Clear();
		_forward.Clear();
		_current = current;
	}
}
