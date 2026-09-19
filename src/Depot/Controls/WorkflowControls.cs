// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.Specialized;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

using Depot.Models;

namespace Depot.Controls;

public sealed class FilterBar : ContentControl
{
	static FilterBar() => DefaultStyleKeyProperty.OverrideMetadata(typeof(FilterBar), new FrameworkPropertyMetadata(typeof(FilterBar)));
}

public sealed class PaginationControl : Control
{
	public static readonly DependencyProperty PreviousCommandProperty = DependencyProperty.Register(nameof(PreviousCommand), typeof(ICommand), typeof(PaginationControl));
	public static readonly DependencyProperty NextCommandProperty = DependencyProperty.Register(nameof(NextCommand), typeof(ICommand), typeof(PaginationControl));
	public static readonly DependencyProperty PageTextProperty = DependencyProperty.Register(nameof(PageText), typeof(string), typeof(PaginationControl), new PropertyMetadata(string.Empty));

	static PaginationControl() => DefaultStyleKeyProperty.OverrideMetadata(typeof(PaginationControl), new FrameworkPropertyMetadata(typeof(PaginationControl)));

	public ICommand? PreviousCommand { get => (ICommand?)GetValue(PreviousCommandProperty); set => SetValue(PreviousCommandProperty, value); }
	public ICommand? NextCommand { get => (ICommand?)GetValue(NextCommandProperty); set => SetValue(NextCommandProperty, value); }
	public string PageText { get => (string)GetValue(PageTextProperty); set => SetValue(PageTextProperty, value); }
}

public sealed class WorkflowHeader : Control
{
	public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string), typeof(WorkflowHeader), new PropertyMetadata(string.Empty));
	public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(WorkflowHeader), new PropertyMetadata(string.Empty));
	public static readonly DependencyProperty StatusProperty = DependencyProperty.Register(nameof(Status), typeof(string), typeof(WorkflowHeader), new PropertyMetadata(string.Empty));

	static WorkflowHeader() => DefaultStyleKeyProperty.OverrideMetadata(typeof(WorkflowHeader), new FrameworkPropertyMetadata(typeof(WorkflowHeader)));

	public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
	public string Subtitle { get => (string)GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }
	public string Status { get => (string)GetValue(StatusProperty); set => SetValue(StatusProperty, value); }
}

public sealed class WorkflowActionBar : Control
{
	public static readonly DependencyProperty SecondaryActionsProperty = DependencyProperty.Register(nameof(SecondaryActions), typeof(object), typeof(WorkflowActionBar));
	public static readonly DependencyProperty PrimaryActionProperty = DependencyProperty.Register(nameof(PrimaryAction), typeof(object), typeof(WorkflowActionBar));

	static WorkflowActionBar() => DefaultStyleKeyProperty.OverrideMetadata(typeof(WorkflowActionBar), new FrameworkPropertyMetadata(typeof(WorkflowActionBar)));

	public object? SecondaryActions { get => GetValue(SecondaryActionsProperty); set => SetValue(SecondaryActionsProperty, value); }
	public object? PrimaryAction { get => GetValue(PrimaryActionProperty); set => SetValue(PrimaryActionProperty, value); }
}

public sealed class OperationPanel : Control
{
	public static readonly DependencyProperty IsBusyProperty = DependencyProperty.Register(nameof(IsBusy), typeof(bool), typeof(OperationPanel), new PropertyMetadata(false));
	public static readonly DependencyProperty HasErrorProperty = DependencyProperty.Register(nameof(HasError), typeof(bool), typeof(OperationPanel), new PropertyMetadata(false));
	public static readonly DependencyProperty StatusTextProperty = DependencyProperty.Register(nameof(StatusText), typeof(string), typeof(OperationPanel), new PropertyMetadata(string.Empty));
	public static readonly DependencyProperty ErrorTextProperty = DependencyProperty.Register(nameof(ErrorText), typeof(string), typeof(OperationPanel), new PropertyMetadata(string.Empty));
	public static readonly DependencyProperty HelpTopicIdProperty = DependencyProperty.Register(nameof(HelpTopicId), typeof(string), typeof(OperationPanel), new PropertyMetadata(string.Empty));

	static OperationPanel() => DefaultStyleKeyProperty.OverrideMetadata(typeof(OperationPanel), new FrameworkPropertyMetadata(typeof(OperationPanel)));

	public bool IsBusy { get => (bool)GetValue(IsBusyProperty); set => SetValue(IsBusyProperty, value); }
	public bool HasError { get => (bool)GetValue(HasErrorProperty); set => SetValue(HasErrorProperty, value); }
	public string StatusText { get => (string)GetValue(StatusTextProperty); set => SetValue(StatusTextProperty, value); }
	public string ErrorText { get => (string)GetValue(ErrorTextProperty); set => SetValue(ErrorTextProperty, value); }
	public string HelpTopicId { get => (string)GetValue(HelpTopicIdProperty); set => SetValue(HelpTopicIdProperty, value); }
}

public sealed record WorkflowStatusPresentation(string Text, string Glyph, StatusBadgeVariant Variant);

public static class WorkflowStatusLanguage
{
	public static WorkflowStatusPresentation Resolve(string? status)
	{
		var value = status?.Trim() ?? string.Empty;
		var text = SplitWords(value);
		var technicalStatus = new string(value.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
		var (glyph, variant) = technicalStatus switch
		{
			"DRAFT" => ("○", StatusBadgeVariant.Neutral),
			"WAITING" or "PENDING" or "PENDINGAPPROVAL" or "PARTIALLYRECEIVED" or "COUNTING" or "REVIEW" => ("…", StatusBadgeVariant.Warning),
			"READY" or "ACTIVE" or "INPROGRESS" or "OPEN" => ("→", StatusBadgeVariant.Primary),
			"DUETODAY" => ("!", StatusBadgeVariant.Warning),
			"APPROVED" or "ORDERED" or "RECEIVED" or "POSTED" or "COMPLETED" or "CLOSED" or "PAID" => ("✓", StatusBadgeVariant.Success),
			"REJECTED" or "CANCELLED" => ("×", StatusBadgeVariant.Error),
			"REVERSED" => ("↶", StatusBadgeVariant.Error),
			"ERROR" or "FAILED" or "BLOCKED" or "OVERDUE" => ("!", StatusBadgeVariant.Error),
			"ARCHIVED" or "DISABLED" or "INACTIVE" => ("•", StatusBadgeVariant.Muted),
			_ => ("•", StatusBadgeVariant.Neutral)
		};
		return new WorkflowStatusPresentation(text, glyph, variant);
	}

	private static string SplitWords(string value)
	{
		if (string.IsNullOrWhiteSpace(value)) return "Unknown";
		var result = new System.Text.StringBuilder(value.Length + 4);
		for (var index = 0; index < value.Length; index++)
		{
			if (index > 0 && char.IsUpper(value[index]) && char.IsLower(value[index - 1])) result.Append(' ');
			result.Append(value[index]);
		}
		return result.ToString();
	}
}

public sealed class DocumentStatusBadge : StatusBadge
{
	public static readonly DependencyProperty StatusProperty = DependencyProperty.Register(
		nameof(Status),
		typeof(string),
		typeof(DocumentStatusBadge),
		new PropertyMetadata(string.Empty, OnStatusChanged));

	public string Status { get => (string)GetValue(StatusProperty); set => SetValue(StatusProperty, value); }

	private static void OnStatusChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
	{
		var badge = (DocumentStatusBadge)dependencyObject;
		var presentation = WorkflowStatusLanguage.Resolve(args.NewValue as string);
		badge.Content = $"{presentation.Glyph} {presentation.Text}";
		badge.Variant = presentation.Variant;
		AutomationProperties.SetName(badge, presentation.Text);
	}
}

public sealed class MasterDetailGrid : Control
{
	public static readonly DependencyProperty MasterProperty = DependencyProperty.Register(nameof(Master), typeof(object), typeof(MasterDetailGrid));
	public static readonly DependencyProperty DetailProperty = DependencyProperty.Register(nameof(Detail), typeof(object), typeof(MasterDetailGrid));

	static MasterDetailGrid() => DefaultStyleKeyProperty.OverrideMetadata(typeof(MasterDetailGrid), new FrameworkPropertyMetadata(typeof(MasterDetailGrid)));

	public object? Master { get => GetValue(MasterProperty); set => SetValue(MasterProperty, value); }
	public object? Detail { get => GetValue(DetailProperty); set => SetValue(DetailProperty, value); }
}

public sealed class WorkflowListState : Control
{
	public static readonly DependencyProperty ItemCountProperty = DependencyProperty.Register(nameof(ItemCount), typeof(int), typeof(WorkflowListState), new PropertyMetadata(0));
	public static readonly DependencyProperty SearchTextProperty = DependencyProperty.Register(nameof(SearchText), typeof(string), typeof(WorkflowListState), new PropertyMetadata(string.Empty, OnSearchTextChanged));
	private static readonly DependencyPropertyKey IsSearchActivePropertyKey = DependencyProperty.RegisterReadOnly(nameof(IsSearchActive), typeof(bool), typeof(WorkflowListState), new PropertyMetadata(false));
	public static readonly DependencyProperty IsSearchActiveProperty = IsSearchActivePropertyKey.DependencyProperty;
	public static readonly DependencyProperty IsBusyProperty = DependencyProperty.Register(nameof(IsBusy), typeof(bool), typeof(WorkflowListState), new PropertyMetadata(false));
	public static readonly DependencyProperty HasErrorProperty = DependencyProperty.Register(nameof(HasError), typeof(bool), typeof(WorkflowListState), new PropertyMetadata(false));
	public static readonly DependencyProperty ErrorTextProperty = DependencyProperty.Register(nameof(ErrorText), typeof(string), typeof(WorkflowListState), new PropertyMetadata(string.Empty));

	static WorkflowListState() => DefaultStyleKeyProperty.OverrideMetadata(typeof(WorkflowListState), new FrameworkPropertyMetadata(typeof(WorkflowListState)));

	public int ItemCount { get => (int)GetValue(ItemCountProperty); set => SetValue(ItemCountProperty, value); }
	public string SearchText { get => (string)GetValue(SearchTextProperty); set => SetValue(SearchTextProperty, value); }
	public bool IsSearchActive => (bool)GetValue(IsSearchActiveProperty);
	public bool IsBusy { get => (bool)GetValue(IsBusyProperty); set => SetValue(IsBusyProperty, value); }
	public bool HasError { get => (bool)GetValue(HasErrorProperty); set => SetValue(HasErrorProperty, value); }
	public string ErrorText { get => (string)GetValue(ErrorTextProperty); set => SetValue(ErrorTextProperty, value); }

	private static void OnSearchTextChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args) =>
		((WorkflowListState)dependencyObject).SetValue(IsSearchActivePropertyKey, !string.IsNullOrWhiteSpace(args.NewValue as string));
}

public sealed class ActivationStatusBadge : StatusBadge
{
	public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
		nameof(IsActive),
		typeof(bool),
		typeof(ActivationStatusBadge),
		new PropertyMetadata(true, OnIsActiveChanged));

	public bool IsActive { get => (bool)GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }

	private static void OnIsActiveChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
	{
		var badge = (ActivationStatusBadge)dependencyObject;
		badge.Content = (bool)args.NewValue ? "Active" : "Inactive";
		badge.Variant = (bool)args.NewValue ? StatusBadgeVariant.Primary : StatusBadgeVariant.Muted;
	}
}


public sealed class WorkflowTimeline : ItemsControl
{
	public static readonly DependencyProperty NavigateCommandProperty = DependencyProperty.Register(
		nameof(NavigateCommand),
		typeof(ICommand),
		typeof(WorkflowTimeline),
		new PropertyMetadata(null));

	public static readonly DependencyProperty CompactThresholdProperty = DependencyProperty.Register(
		nameof(CompactThreshold),
		typeof(int),
		typeof(WorkflowTimeline),
		new FrameworkPropertyMetadata(8, OnCompactThresholdChanged, CoerceCompactThreshold));

	public static readonly DependencyProperty IsExpandedProperty = DependencyProperty.Register(
		nameof(IsExpanded),
		typeof(bool),
		typeof(WorkflowTimeline),
		new PropertyMetadata(false, OnIsExpandedChanged));

	private static readonly DependencyPropertyKey HasCompactOverflowPropertyKey = DependencyProperty.RegisterReadOnly(
		nameof(HasCompactOverflow),
		typeof(bool),
		typeof(WorkflowTimeline),
		new PropertyMetadata(false));

	public static readonly DependencyProperty HasCompactOverflowProperty = HasCompactOverflowPropertyKey.DependencyProperty;

	private static readonly DependencyPropertyKey HasCurrentStepPropertyKey = DependencyProperty.RegisterReadOnly(
		nameof(HasCurrentStep),
		typeof(bool),
		typeof(WorkflowTimeline),
		new PropertyMetadata(false));

	public static readonly DependencyProperty HasCurrentStepProperty = HasCurrentStepPropertyKey.DependencyProperty;

	private static readonly DependencyPropertyKey CurrentStepTextPropertyKey = DependencyProperty.RegisterReadOnly(
		nameof(CurrentStepText),
		typeof(string),
		typeof(WorkflowTimeline),
		new PropertyMetadata(string.Empty));

	public static readonly DependencyProperty CurrentStepTextProperty = CurrentStepTextPropertyKey.DependencyProperty;

	public WorkflowTimeline()
	{
		Loaded += (_, _) =>
		{
			UpdatePresentation();
			ScrollCurrentIntoView();
		};
	}

	public ICommand? NavigateCommand
	{
		get => (ICommand?)GetValue(NavigateCommandProperty);
		set => SetValue(NavigateCommandProperty, value);
	}

	public int CompactThreshold
	{
		get => (int)GetValue(CompactThresholdProperty);
		set => SetValue(CompactThresholdProperty, value);
	}

	public bool IsExpanded
	{
		get => (bool)GetValue(IsExpandedProperty);
		set => SetValue(IsExpandedProperty, value);
	}

	public bool HasCompactOverflow => (bool)GetValue(HasCompactOverflowProperty);
	public bool HasCurrentStep => (bool)GetValue(HasCurrentStepProperty);
	public string CurrentStepText => (string)GetValue(CurrentStepTextProperty);

	protected override void OnItemsChanged(NotifyCollectionChangedEventArgs e)
	{
		base.OnItemsChanged(e);
		UpdatePresentation();
	}

	private static object CoerceCompactThreshold(DependencyObject dependencyObject, object baseValue) =>
		Math.Max(3, (int)baseValue);

	private static void OnCompactThresholdChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args) =>
		((WorkflowTimeline)dependencyObject).UpdatePresentation();

	private static void OnIsExpandedChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
	{
		var timeline = (WorkflowTimeline)dependencyObject;
		if (!(bool)args.NewValue) timeline.Dispatcher.BeginInvoke(new Action(timeline.ScrollCurrentIntoView));
	}

	private void UpdatePresentation()
	{
		var current = Items.OfType<WorkflowTimelineItem>().LastOrDefault(item => item.IsCurrent);
		SetValue(HasCurrentStepPropertyKey, current is not null);
		SetValue(CurrentStepTextPropertyKey, current is null ? string.Empty : "Current step: " + current.Title);

		var hasOverflow = Items.Count > CompactThreshold;
		SetValue(HasCompactOverflowPropertyKey, hasOverflow);
		if (!hasOverflow && IsExpanded) SetCurrentValue(IsExpandedProperty, false);
	}

	private void ScrollCurrentIntoView()
	{
		if (!HasCompactOverflow || IsExpanded) return;
		var current = Items.OfType<WorkflowTimelineItem>().LastOrDefault(item => item.IsCurrent);
		if (current is null) return;
		if (ItemContainerGenerator.ContainerFromItem(current) is FrameworkElement container) container.BringIntoView();
	}
}

