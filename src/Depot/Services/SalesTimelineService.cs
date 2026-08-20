// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class SalesTimelineService
{
	private readonly SalesTimelineRepository _timeline;
	private readonly IAuthorizationService _authorization;
	public SalesTimelineService(SalesTimelineRepository timeline, IAuthorizationService authorization){_timeline=timeline;_authorization=authorization;}
	public Task<IReadOnlyList<SalesOrderTimelineItem>> ListAsync(SalesOrder order,CancellationToken token=default){_authorization.RequirePermission(ApplicationPermission.SalesOrdersView);return _timeline.ListAsync(order,token);}
}
