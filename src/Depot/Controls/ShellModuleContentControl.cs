// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Depot.Controls;

public sealed class ShellModuleContentControl : ContentControl
{
	protected override void OnContentChanged(object oldContent, object newContent)
	{
		base.OnContentChanged(oldContent, newContent);
		Dispatcher.BeginInvoke(new Action(HideNestedPageHeaders), DispatcherPriority.Loaded);
	}

	private void HideNestedPageHeaders()
	{
		for (var index = 0; index < VisualTreeHelper.GetChildrenCount(this); index++)
			HideNestedPageHeaders(VisualTreeHelper.GetChild(this, index));
	}

	private static void HideNestedPageHeaders(DependencyObject parent)
	{
		if (parent is PageHeader pageHeader)
		{
			pageHeader.Visibility = Visibility.Collapsed;
			return;
		}
		for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
			HideNestedPageHeaders(VisualTreeHelper.GetChild(parent, index));
	}
}
