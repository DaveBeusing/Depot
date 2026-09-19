// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.IO;
using System.Threading;

using Depot.Controls;
using Depot.Models;

using Xunit;

namespace Depot.Tests;

public sealed class WorkflowTimelineVisualPolishTests
{
	[Theory]
	[InlineData(false, false, false, WorkflowTimelineSeverity.Normal, WorkflowTimelineVisualState.Completed, "✓")]
	[InlineData(true, false, false, WorkflowTimelineSeverity.Normal, WorkflowTimelineVisualState.Current, "●")]
	[InlineData(false, true, false, WorkflowTimelineSeverity.Normal, WorkflowTimelineVisualState.Correction, "↺")]
	[InlineData(false, false, true, WorkflowTimelineSeverity.Error, WorkflowTimelineVisualState.Reversal, "↶")]
	[InlineData(false, false, false, WorkflowTimelineSeverity.Error, WorkflowTimelineVisualState.Error, "!")]
	public void VisualStateUsesTextAndGlyphInAdditionToColor(
		bool isCurrent,
		bool isCorrection,
		bool isReversal,
		WorkflowTimelineSeverity severity,
		WorkflowTimelineVisualState expectedState,
		string expectedGlyph)
	{
		var item = new WorkflowTimelineItem
		{
			IsCurrent = isCurrent,
			IsCorrection = isCorrection,
			IsReversal = isReversal,
			Severity = severity
		};

		Assert.Equal(expectedState, item.VisualState);
		Assert.Equal(expectedGlyph, item.VisualGlyph);
		Assert.False(string.IsNullOrWhiteSpace(item.VisualStateLabel));
	}

	[Fact]
	public void PendingStepIsNonNavigableEvenWhenRouteExists()
	{
		var item = new WorkflowTimelineItem
		{
			EntityId = 42,
			RouteId = "sales.orders",
			IsPending = true
		};

		Assert.Equal(WorkflowTimelineVisualState.Pending, item.VisualState);
		Assert.False(item.CanNavigate);
		Assert.Equal("Not yet occurred", item.OccurredAtText);
	}

	[Fact]
	public void AccessibleNameIncludesDocumentStatusStateAndDate()
	{
		var item = new WorkflowTimelineItem
		{
			Title = "Shipment packed",
			DisplayNumber = "SH-118",
			Status = "Posted",
			OccurredAt = new DateTime(2026, 9, 19, 10, 30, 0, DateTimeKind.Local),
			IsCurrent = true
		};

		Assert.Contains("Shipment packed", item.AccessibleName, StringComparison.Ordinal);
		Assert.Contains("Document SH-118", item.AccessibleName, StringComparison.Ordinal);
		Assert.Contains("Status Posted", item.AccessibleName, StringComparison.Ordinal);
		Assert.Contains("Current", item.AccessibleName, StringComparison.Ordinal);
		Assert.Contains("Date ", item.AccessibleName, StringComparison.Ordinal);
	}

	[Fact]
	public void CompactModeSummarizesCurrentStepAndOnlyActivatesForLongChains()
	{
		Exception? failure = null;
		var thread = new Thread(() =>
		{
			try
			{
				var timeline = new WorkflowTimeline { CompactThreshold = 4 };
				for (var index = 1; index <= 5; index++)
				{
					timeline.Items.Add(new WorkflowTimelineItem
					{
						Title = "Step " + index,
						DisplayNumber = "DOC-" + index,
						Status = index == 5 ? "Open" : "Completed",
						OccurredAt = DateTime.Today.AddMinutes(index),
						IsCurrent = index == 5
					});
				}

				Assert.True(timeline.HasCompactOverflow);
				Assert.True(timeline.HasCurrentStep);
				Assert.Equal("Current step: Step 5", timeline.CurrentStepText);

				timeline.CompactThreshold = 6;
				Assert.False(timeline.HasCompactOverflow);
			}
			catch (Exception exception)
			{
				failure = exception;
			}
		});

		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
		thread.Join();

		Assert.Null(failure);
	}

	[Fact]
	public void SparseTimelineDoesNotSynthesizeMissingOptionalSteps()
	{
		Exception? failure = null;
		var thread = new Thread(() =>
		{
			try
			{
				var timeline = new WorkflowTimeline();
				timeline.Items.Add(new WorkflowTimelineItem { Kind = WorkflowTimelineKind.SalesOrder, Title = "Order" });
				timeline.Items.Add(new WorkflowTimelineItem { Kind = WorkflowTimelineKind.SalesInvoice, Title = "Invoice", IsCurrent = true });

				Assert.Equal(2, timeline.Items.Count);
				Assert.Equal("Current step: Invoice", timeline.CurrentStepText);
			}
			catch (Exception exception)
			{
				failure = exception;
			}
		});

		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
		thread.Join();

		Assert.Null(failure);
	}

	[Fact]
	public void TimelineTemplateKeepsNativeKeyboardNavigationAndAccessibilityContract()
	{
		var repositoryRoot = FindRepositoryRoot();
		var xaml = File.ReadAllText(Path.Combine(repositoryRoot, "src", "Depot", "Resources", "Workflows.xaml"));

		Assert.Contains("<Button Grid.Column=\"2\"", xaml, StringComparison.Ordinal);
		Assert.Contains("CommandParameter=\"{Binding}\"", xaml, StringComparison.Ordinal);
		Assert.Contains("Visibility=\"{Binding CanNavigate, Converter={StaticResource BooleanToVisibilityConverter}}\"", xaml, StringComparison.Ordinal);
		Assert.Contains("AutomationProperties.Name=\"{Binding AccessibleName}\"", xaml, StringComparison.Ordinal);
		Assert.Contains("<ToggleButton", xaml, StringComparison.Ordinal);
	}

	[Fact]
	public void VisualPolishFilesDoNotIntroducePersistenceDefinitions()
	{
		var repositoryRoot = FindRepositoryRoot();
		var paths = new[]
		{
			Path.Combine(repositoryRoot, "src", "Depot", "Models", "WorkflowTimelineItem.cs"),
			Path.Combine(repositoryRoot, "src", "Depot", "Controls", "WorkflowControls.cs"),
			Path.Combine(repositoryRoot, "src", "Depot", "Resources", "Workflows.xaml")
		};

		foreach (var path in paths)
		{
			var content = File.ReadAllText(path);
			Assert.DoesNotContain("CREATE TABLE", content, StringComparison.OrdinalIgnoreCase);
			Assert.DoesNotContain("ALTER TABLE", content, StringComparison.OrdinalIgnoreCase);
			Assert.DoesNotContain("CurrentVersion =", content, StringComparison.Ordinal);
		}
	}

	private static string FindRepositoryRoot()
	{
		for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
		{
			if (File.Exists(Path.Combine(directory.FullName, "Directory.Build.props"))) return directory.FullName;
		}

		throw new DirectoryNotFoundException("Could not locate the Depot repository root from the test output directory.");
	}
}
