// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
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
		var initialScale = kind == MotionTransitionKind.NotificationBadge ? 0.92d : 1d;
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

		ScaleTransform? scale = element.RenderTransform as ScaleTransform;
		if (initialScale != 1d && scale is null && ReferenceEquals(element.RenderTransform, Transform.Identity))
		{
			scale = new ScaleTransform();
			element.RenderTransform = scale;
			element.RenderTransformOrigin = new Point(0.5d, 0.5d);
		}
		if (scale is not null)
		{
			scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
			scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
			scale.ScaleX = 1d;
			scale.ScaleY = 1d;
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

		if (scale is not null && initialScale != 1d)
		{
			scale.BeginAnimation(
				ScaleTransform.ScaleXProperty,
				new DoubleAnimation(initialScale, 1d, duration)
				{
					EasingFunction = easing,
					FillBehavior = FillBehavior.Stop
				});
			scale.BeginAnimation(
				ScaleTransform.ScaleYProperty,
				new DoubleAnimation(initialScale, 1d, duration)
				{
					EasingFunction = easing,
					FillBehavior = FillBehavior.Stop
				});
		}
	}
}

public static class MotionBehavior
{
	public static readonly DependencyProperty TransitionKindProperty = DependencyProperty.RegisterAttached(
		"TransitionKind",
		typeof(MotionTransitionKind),
		typeof(MotionBehavior),
		new FrameworkPropertyMetadata(MotionTransitionKind.None, OnTransitionKindChanged));

	public static readonly DependencyProperty IsButtonFeedbackEnabledProperty = DependencyProperty.RegisterAttached(
		"IsButtonFeedbackEnabled",
		typeof(bool),
		typeof(MotionBehavior),
		new FrameworkPropertyMetadata(false, OnIsButtonFeedbackEnabledChanged));

	public static readonly DependencyProperty ButtonHoverScaleProperty = DependencyProperty.RegisterAttached(
		"ButtonHoverScale",
		typeof(double),
		typeof(MotionBehavior),
		new FrameworkPropertyMetadata(1d));

	public static readonly DependencyProperty ButtonPressedScaleProperty = DependencyProperty.RegisterAttached(
		"ButtonPressedScale",
		typeof(double),
		typeof(MotionBehavior),
		new FrameworkPropertyMetadata(0.97d));

	public static void SetTransitionKind(DependencyObject element, MotionTransitionKind value) =>
		element.SetValue(TransitionKindProperty, value);

	public static MotionTransitionKind GetTransitionKind(DependencyObject element) =>
		(MotionTransitionKind)element.GetValue(TransitionKindProperty);

	public static void SetIsButtonFeedbackEnabled(DependencyObject element, bool value) =>
		element.SetValue(IsButtonFeedbackEnabledProperty, value);

	public static bool GetIsButtonFeedbackEnabled(DependencyObject element) =>
		(bool)element.GetValue(IsButtonFeedbackEnabledProperty);

	public static void SetButtonHoverScale(DependencyObject element, double value) =>
		element.SetValue(ButtonHoverScaleProperty, value);

	public static double GetButtonHoverScale(DependencyObject element) =>
		(double)element.GetValue(ButtonHoverScaleProperty);

	public static void SetButtonPressedScale(DependencyObject element, double value) =>
		element.SetValue(ButtonPressedScaleProperty, value);

	public static double GetButtonPressedScale(DependencyObject element) =>
		(double)element.GetValue(ButtonPressedScaleProperty);

	private static void OnTransitionKindChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
	{
		if (dependencyObject is not FrameworkElement element) return;

		element.Loaded -= OnLoaded;
		element.IsVisibleChanged -= OnIsVisibleChanged;

		if ((MotionTransitionKind)args.NewValue == MotionTransitionKind.None) return;

		element.Loaded += OnLoaded;
		element.IsVisibleChanged += OnIsVisibleChanged;
		if (element.IsLoaded && element.IsVisible) MotionTransitions.Begin(element, (MotionTransitionKind)args.NewValue);
	}

	private static void OnIsButtonFeedbackEnabledChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
	{
		if (dependencyObject is not ButtonBase button) return;

		DetachButtonFeedback(button);
		ResetButtonScale(button);

		if (args.NewValue is not true) return;

		EnsureButtonScale(button);
		button.MouseEnter += OnButtonMouseEnter;
		button.MouseLeave += OnButtonMouseLeave;
		button.PreviewMouseLeftButtonDown += OnButtonMouseLeftButtonDown;
		button.PreviewMouseLeftButtonUp += OnButtonMouseLeftButtonUp;
		button.LostMouseCapture += OnButtonLostMouseCapture;
		button.PreviewKeyDown += OnButtonPreviewKeyDown;
		button.PreviewKeyUp += OnButtonPreviewKeyUp;
		button.LostKeyboardFocus += OnButtonLostKeyboardFocus;
		button.IsEnabledChanged += OnButtonIsEnabledChanged;
	}

	private static void DetachButtonFeedback(ButtonBase button)
	{
		button.MouseEnter -= OnButtonMouseEnter;
		button.MouseLeave -= OnButtonMouseLeave;
		button.PreviewMouseLeftButtonDown -= OnButtonMouseLeftButtonDown;
		button.PreviewMouseLeftButtonUp -= OnButtonMouseLeftButtonUp;
		button.LostMouseCapture -= OnButtonLostMouseCapture;
		button.PreviewKeyDown -= OnButtonPreviewKeyDown;
		button.PreviewKeyUp -= OnButtonPreviewKeyUp;
		button.LostKeyboardFocus -= OnButtonLostKeyboardFocus;
		button.IsEnabledChanged -= OnButtonIsEnabledChanged;
	}

	private static void OnButtonMouseEnter(object sender, MouseEventArgs args)
	{
		if (sender is ButtonBase button && button.IsEnabled && !button.IsPressed)
			AnimateButtonScale(button, GetButtonHoverScale(button));
	}

	private static void OnButtonMouseLeave(object sender, MouseEventArgs args)
	{
		if (sender is ButtonBase button && button.IsEnabled && !button.IsPressed)
			AnimateButtonScale(button, 1d);
	}

	private static void OnButtonMouseLeftButtonDown(object sender, MouseButtonEventArgs args)
	{
		if (sender is ButtonBase button && button.IsEnabled)
			AnimateButtonScale(button, GetButtonPressedScale(button));
	}

	private static void OnButtonMouseLeftButtonUp(object sender, MouseButtonEventArgs args)
	{
		if (sender is ButtonBase button && button.IsEnabled)
			AnimateButtonScale(button, button.IsMouseOver ? GetButtonHoverScale(button) : 1d);
	}

	private static void OnButtonLostMouseCapture(object sender, MouseEventArgs args)
	{
		if (sender is ButtonBase button && button.IsEnabled)
			AnimateButtonScale(button, button.IsMouseOver ? GetButtonHoverScale(button) : 1d);
	}

	private static void OnButtonPreviewKeyDown(object sender, KeyEventArgs args)
	{
		if (sender is ButtonBase button && button.IsEnabled && IsButtonActivationKey(args.Key))
			AnimateButtonScale(button, GetButtonPressedScale(button));
	}

	private static void OnButtonPreviewKeyUp(object sender, KeyEventArgs args)
	{
		if (sender is ButtonBase button && button.IsEnabled && IsButtonActivationKey(args.Key))
			AnimateButtonScale(button, button.IsMouseOver ? GetButtonHoverScale(button) : 1d);
	}

	private static void OnButtonLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs args)
	{
		if (sender is ButtonBase button && button.IsEnabled && !button.IsPressed)
			AnimateButtonScale(button, button.IsMouseOver ? GetButtonHoverScale(button) : 1d);
	}

	private static void OnButtonIsEnabledChanged(object sender, DependencyPropertyChangedEventArgs args)
	{
		if (sender is not ButtonBase button) return;
		if (args.NewValue is true)
			AnimateButtonScale(button, button.IsMouseOver ? GetButtonHoverScale(button) : 1d);
		else
			ResetButtonScale(button);
	}

	private static bool IsButtonActivationKey(Key key) => key is Key.Space or Key.Enter;

	private static ScaleTransform? EnsureButtonScale(ButtonBase button)
	{
		if (button.RenderTransform is ScaleTransform scale) return scale;
		if (!ReferenceEquals(button.RenderTransform, Transform.Identity)) return null;

		scale = new ScaleTransform(1d, 1d);
		button.RenderTransform = scale;
		button.RenderTransformOrigin = new Point(0.5d, 0.5d);
		return scale;
	}

	private static void AnimateButtonScale(ButtonBase button, double targetScale)
	{
		var scale = EnsureButtonScale(button);
		if (scale is null) return;

		var duration = MotionDurations.Resolve(MotionSpeed.Fast, MotionPreferences.IsReducedMotionEnabled);
		if (!duration.HasTimeSpan || duration.TimeSpan == TimeSpan.Zero)
		{
			scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
			scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
			scale.ScaleX = targetScale;
			scale.ScaleY = targetScale;
			return;
		}

		var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
		scale.BeginAnimation(
			ScaleTransform.ScaleXProperty,
			new DoubleAnimation(targetScale, duration) { EasingFunction = easing });
		scale.BeginAnimation(
			ScaleTransform.ScaleYProperty,
			new DoubleAnimation(targetScale, duration) { EasingFunction = easing });
	}

	private static void ResetButtonScale(ButtonBase button)
	{
		if (button.RenderTransform is not ScaleTransform scale) return;
		scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
		scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
		scale.ScaleX = 1d;
		scale.ScaleY = 1d;
	}

	private static void OnLoaded(object sender, RoutedEventArgs args)
	{
		if (sender is FrameworkElement element && element.IsVisible)
			MotionTransitions.Begin(element, GetTransitionKind(element));
	}

	private static void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs args)
	{
		if (sender is FrameworkElement element && element.IsLoaded && args.NewValue is true)
			MotionTransitions.Begin(element, GetTransitionKind(element));
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
