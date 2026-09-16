// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;

namespace Depot.Controls;

internal static class AccessibilityAutomation
{
	public static void ForwardInputProperties(DependencyObject source, DependencyObject target)
	{
		var name = AutomationProperties.GetName(source);
		if (!string.IsNullOrWhiteSpace(name)) AutomationProperties.SetName(target, name);
		var automationId = AutomationProperties.GetAutomationId(source);
		if (!string.IsNullOrWhiteSpace(automationId)) AutomationProperties.SetAutomationId(target, automationId);
		var helpText = AutomationProperties.GetHelpText(source);
		if (!string.IsNullOrWhiteSpace(helpText)) AutomationProperties.SetHelpText(target, helpText);
		var itemStatus = AutomationProperties.GetItemStatus(source);
		if (!string.IsNullOrWhiteSpace(itemStatus)) AutomationProperties.SetItemStatus(target, itemStatus);
		var labeledBy = AutomationProperties.GetLabeledBy(source);
		if (labeledBy is not null) AutomationProperties.SetLabeledBy(target, labeledBy);
		AutomationProperties.SetIsRequiredForForm(target, AutomationProperties.GetIsRequiredForForm(source));
	}

	public static void Announce(FrameworkElement element, string? message, bool important, string activityId)
	{
		if (!element.IsLoaded || string.IsNullOrWhiteSpace(message)) return;
		AutomationProperties.SetName(element, message);
		AutomationProperties.SetLiveSetting(element, important ? AutomationLiveSetting.Assertive : AutomationLiveSetting.Polite);
		var peer = UIElementAutomationPeer.FromElement(element) ?? UIElementAutomationPeer.CreatePeerForElement(element);
		peer?.RaiseNotificationEvent(
			AutomationNotificationKind.Other,
			important ? AutomationNotificationProcessing.ImportantMostRecent : AutomationNotificationProcessing.MostRecent,
			message,
			activityId);
	}
}
