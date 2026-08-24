// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Depot;

internal static partial class WindowsTitleBarTheme
{
	private const int DwmwaUseImmersiveDarkMode = 20;
	private const int DwmwaCaptionColor = 35;
	private const int DwmwaTextColor = 36;

	public static void Apply(Window window)
	{
		ArgumentNullException.ThrowIfNull(window);
		if (!OperatingSystem.IsWindows()) return;

		var handle = new WindowInteropHelper(window).Handle;
		if (handle == IntPtr.Zero) return;

		var darkMode = 1;
		SetAttribute(handle, DwmwaUseImmersiveDarkMode, darkMode);

		if (Application.Current.TryFindResource("BackgroundBrush") is SolidColorBrush backgroundBrush)
			SetAttribute(handle, DwmwaCaptionColor, ToColorRef(backgroundBrush.Color));

		if (Application.Current.TryFindResource("PrimaryTextBrush") is SolidColorBrush textBrush)
			SetAttribute(handle, DwmwaTextColor, ToColorRef(textBrush.Color));
	}

	private static void SetAttribute(IntPtr handle, int attribute, int value) =>
		_ = DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int));

	private static int ToColorRef(Color color) => color.R | color.G << 8 | color.B << 16;

	[LibraryImport("dwmapi.dll")]
	private static partial int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);
}
