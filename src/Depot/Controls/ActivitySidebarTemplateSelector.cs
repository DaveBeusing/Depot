// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Windows;
using System.Windows.Controls;

using Depot.ViewModels;

namespace Depot.Controls;

public sealed class ActivitySidebarTemplateSelector : DataTemplateSelector
{
	public DataTemplate? ModuleTemplate { get; set; }
	public DataTemplate? DefaultTemplate { get; set; }

	public override DataTemplate? SelectTemplate(object item, DependencyObject container) =>
		item is ShellModuleViewModel ? ModuleTemplate : DefaultTemplate;
}
