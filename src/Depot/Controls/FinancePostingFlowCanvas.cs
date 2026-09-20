// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using Depot.Models;

namespace Depot.Controls;

public sealed class FinancePostingFlowCanvas : ListBox
{
	public static readonly DependencyProperty EdgesProperty = DependencyProperty.Register(
		nameof(Edges),
		typeof(IEnumerable),
		typeof(FinancePostingFlowCanvas),
		new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty IsReadOnlyProperty = DependencyProperty.Register(
		nameof(IsReadOnly),
		typeof(bool),
		typeof(FinancePostingFlowCanvas),
		new FrameworkPropertyMetadata(false));

	static FinancePostingFlowCanvas()
	{
		DefaultStyleKeyProperty.OverrideMetadata(
			typeof(FinancePostingFlowCanvas),
			new FrameworkPropertyMetadata(typeof(FinancePostingFlowCanvas)));
	}

	public IEnumerable? Edges
	{
		get => (IEnumerable?)GetValue(EdgesProperty);
		set => SetValue(EdgesProperty, value);
	}

	public bool IsReadOnly
	{
		get => (bool)GetValue(IsReadOnlyProperty);
		set => SetValue(IsReadOnlyProperty, value);
	}

	protected override void OnItemsChanged(NotifyCollectionChangedEventArgs e)
	{
		base.OnItemsChanged(e);
		InvalidateVisual();
	}

	protected override void OnRender(DrawingContext drawingContext)
	{
		base.OnRender(drawingContext);
		if (Edges is null) return;

		var nodes = Items
			.Cast<object>()
			.OfType<FinancePostingFlowNode>()
			.ToDictionary(node => node.Key, StringComparer.Ordinal);
		var brush = BorderBrush ?? Brushes.Gray;
		var pen = new Pen(brush, 1.25d);
		pen.Freeze();

		foreach (var edge in Edges.Cast<object>().OfType<FinancePostingFlowEdge>())
		{
			if (!nodes.TryGetValue(edge.SourceKey, out var source) ||
				!nodes.TryGetValue(edge.TargetKey, out var target))
				continue;

			var start = new Point(source.CanvasLeft + source.CanvasWidth, source.CanvasTop + (source.CanvasHeight / 2d));
			var end = new Point(target.CanvasLeft, target.CanvasTop + (target.CanvasHeight / 2d));
			var middleX = start.X + ((end.X - start.X) / 2d);
			var geometry = new StreamGeometry();
			using (var context = geometry.Open())
			{
				context.BeginFigure(start, false, false);
				context.LineTo(new Point(middleX, start.Y), true, false);
				context.LineTo(new Point(middleX, end.Y), true, false);
				context.LineTo(end, true, false);
			}
			geometry.Freeze();
			drawingContext.DrawGeometry(null, pen, geometry);
		}
	}
}
