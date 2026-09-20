// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Windows;
using System.Windows.Markup;
using System.Windows.Media.Animation;

namespace Depot.Controls;

public enum MotionSpeed
{
	Fast,
	Standard,
	Emphasis
}

public static class MotionPreferences
{
	public static bool IsReducedMotionEnabled => !SystemParameters.ClientAreaAnimation;
}

public static class MotionDurations
{
	public static TimeSpan Fast => TimeSpan.FromMilliseconds(100);
	public static TimeSpan Standard => TimeSpan.FromMilliseconds(160);
	public static TimeSpan Emphasis => TimeSpan.FromMilliseconds(220);

	public static Duration Resolve(MotionSpeed speed, bool reduceMotion)
	{
		if (reduceMotion) return new Duration(TimeSpan.Zero);

		return new Duration(speed switch
		{
			MotionSpeed.Fast => Fast,
			MotionSpeed.Emphasis => Emphasis,
			_ => Standard
		});
	}
}

[MarkupExtensionReturnType(typeof(Duration))]
public sealed class MotionDurationExtension : MarkupExtension
{
	public MotionSpeed Kind { get; set; } = MotionSpeed.Standard;

	public bool Essential { get; set; }

	public override object ProvideValue(IServiceProvider serviceProvider) =>
		MotionDurations.Resolve(Kind, MotionPreferences.IsReducedMotionEnabled && !Essential);
}
