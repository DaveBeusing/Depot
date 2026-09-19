// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.ViewModels;

public sealed record WelcomeTip(string Shortcut, string Title, string Description);

public sealed class WelcomeViewModel : BaseViewModel
{
	public WelcomeViewModel(string displayName, DateTime localTime)
	{
		Greeting = $"{GetGreeting(localTime.Hour)}, {displayName}";
		Tips =
		[
			new("Ctrl+K", "Search / Quick Open", "Find permitted commands, workspaces, Help and supported records from anywhere in Depot."),
			new("Ctrl+P", "Quick Open", "Open permitted workspaces and recent supported records."),
			new("Ctrl+Shift+P", "Command Palette", "Run available shell commands without leaving the keyboard."),
			new("Ctrl+Tab", "Switch tabs", "Move through the workspace tabs you already have open."),
			new("Ctrl+W", "Close tab", "Close the active workspace tab."),
			new("Ctrl+Shift+T", "Reopen closed tab", "Reopen the last closed workspace tab in this session."),
			new("F1", "Help", "Open the context-aware Depot help center.")
		];
	}

	public string Greeting { get; }
	public string Subtitle => "Continue your work";
	public IReadOnlyList<WelcomeTip> Tips { get; }

	public static string GetGreeting(int hour) => hour switch
	{
		< 12 => "Good morning",
		< 18 => "Good afternoon",
		_ => "Good evening"
	};
}