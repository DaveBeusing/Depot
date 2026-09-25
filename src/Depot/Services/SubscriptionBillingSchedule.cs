// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;

namespace Depot.Services;

public static class SubscriptionBillingSchedule
{
	public static DateOnly Advance(DateOnly anchor,DateOnly current,SubscriptionBillingCadence cadence)
	{
		var months=cadence switch{SubscriptionBillingCadence.Monthly=>1,SubscriptionBillingCadence.Quarterly=>3,SubscriptionBillingCadence.Annual=>12,_=>throw new ArgumentOutOfRangeException(nameof(cadence))};
		var target=current.AddMonths(months);var days=DateTime.DaysInMonth(target.Year,target.Month);var anchorIsEnd=anchor.Day==DateTime.DaysInMonth(anchor.Year,anchor.Month);
		return new DateOnly(target.Year,target.Month,anchorIsEnd?days:Math.Min(anchor.Day,days));
	}
	public static (DateOnly PeriodStart,DateOnly PeriodEnd,DateOnly NextBillingDate) Period(SubscriptionContract contract,DateOnly billingDate)
	{
		ArgumentNullException.ThrowIfNull(contract);var next=Advance(contract.StartDate,billingDate,contract.Cadence);var end=next.AddDays(-1);if(contract.EndDate is{} finite&&finite<end)end=finite;return(billingDate,end,next);
	}
}
