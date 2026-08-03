// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

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

	static OperationPanel() => DefaultStyleKeyProperty.OverrideMetadata(typeof(OperationPanel), new FrameworkPropertyMetadata(typeof(OperationPanel)));

	public bool IsBusy { get => (bool)GetValue(IsBusyProperty); set => SetValue(IsBusyProperty, value); }
	public bool HasError { get => (bool)GetValue(HasErrorProperty); set => SetValue(HasErrorProperty, value); }
	public string StatusText { get => (string)GetValue(StatusTextProperty); set => SetValue(StatusTextProperty, value); }
	public string ErrorText { get => (string)GetValue(ErrorTextProperty); set => SetValue(ErrorTextProperty, value); }
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
		var status = args.NewValue as string ?? string.Empty;
		badge.Content = SplitWords(status);
		var technicalStatus = new string(status.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
		badge.Variant = technicalStatus switch
		{
			"APPROVED" or "ORDERED" or "RECEIVED" or "POSTED" or "COMPLETED" or "CLOSED" => StatusBadgeVariant.Success,
			"PENDINGAPPROVAL" or "PARTIALLYRECEIVED" or "COUNTING" or "REVIEW" => StatusBadgeVariant.Warning,
			"REJECTED" or "CANCELLED" or "REVERSED" or "ERROR" => StatusBadgeVariant.Error,
			_ => StatusBadgeVariant.Neutral
		};
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
