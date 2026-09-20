// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Depot.Controls;

public enum MotionSpeed
{
	Fast,
	Standard,
	Emphasis
}

public enum MotionTransitionKind
{
	None,
	Workspace,
	DetailPane,
	State,
	Status,
	NotificationBadge,
	Timeline
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

public static class MotionTransitions
{
	public static void Begin(FrameworkElement element, MotionTransitionKind kind)
	{
		ArgumentNullException.ThrowIfNull(element);
		if (kind == MotionTransitionKind.None) return;

		var reduceMotion = MotionPreferences.IsReducedMotionEnabled;
		var speed = kind switch
		{
			MotionTransitionKind.Workspace => MotionSpeed.Standard,
			MotionTransitionKind.DetailPane => MotionSpeed.Emphasis,
			_ => MotionSpeed.Fast
		};
		var offset = kind switch
		{
			MotionTransitionKind.Workspace => 8d,
			MotionTransitionKind.DetailPane => 10d,
			MotionTransitionKind.State => 4d,
			MotionTransitionKind.Status => 2d,
			MotionTransitionKind.Timeline => 6d,
			_ => 0d
		};
		var duration = MotionDurations.Resolve(speed, reduceMotion);
		var easing = new CubicEase { EasingMode = EasingMode.EaseOut };

		element.BeginAnimation(UIElement.OpacityProperty, null);
		element.Opacity = 1d;

		TranslateTransform? translate = element.RenderTransform as TranslateTransform;
		if (offset != 0d && translate is null && ReferenceEquals(element.RenderTransform, Transform.Identity))
		{
			translate = new TranslateTransform();
			element.RenderTransform = translate;
		}
		if (translate is not null)
		{
			translate.BeginAnimation(TranslateTransform.YProperty, null);
			translate.Y = 0d;
		}

		if (reduceMotion) return;

		element.BeginAnimation(
			UIElement.OpacityProperty,
			new DoubleAnimation(0d, 1d, duration)
			{
				EasingFunction = easing,
				FillBehavior = FillBehavior.Stop
			});

		if (translate is not null && offset != 0d)
		{
			translate.BeginAnimation(
				TranslateTransform.YProperty,
				new DoubleAnimation(offset, 0d, duration)
				{
					EasingFunction = easing,
					FillBehavior = FillBehavior.Stop
				});
		}
	}
}

public sealed class MotionContentControl : ContentControl
{
	public static readonly DependencyProperty TransitionKindProperty = DependencyProperty.Register(
		nameof(TransitionKind),
		typeof(MotionTransitionKind),
		typeof(MotionContentControl),
		new FrameworkPropertyMetadata(MotionTransitionKind.Workspace));

	public MotionContentControl()
	{
		RenderTransform = new TranslateTransform();
		Loaded += (_, _) => MotionTransitions.Begin(this, TransitionKind);
	}

	public MotionTransitionKind TransitionKind
	{
		get => (MotionTransitionKind)GetValue(TransitionKindProperty);
		set => SetValue(TransitionKindProperty, value);
	}

	protected override void OnContentChanged(object oldContent, object newContent)
	{
		base.OnContentChanged(oldContent, newContent);
		if (IsLoaded) MotionTransitions.Begin(this, TransitionKind);
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
