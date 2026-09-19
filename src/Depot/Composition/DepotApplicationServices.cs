// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.DocumentRendering;
using Depot.Repositories;
using Depot.Services;

namespace Depot.Composition;

internal sealed class DepotApplicationServices : IDisposable
{
	private DepotApplicationServices(
		DatabaseComposition database,
		ServiceComposition services,
		WorkspaceViewService workspaceViews,
		WorkspaceProductivityService workspaceProductivity,
		EnterpriseIdentityService enterpriseIdentity,
		EnterpriseAuthenticationService enterpriseAuthentication,
		AuthenticationSecurityService authenticationSecurity,
		SecurityAdministrationService securityAdministration,
		SecurityMaintenanceService securityMaintenance,
		SecurityEventDeliveryService securityEventDelivery,
		ViewModelFactory viewModels)
	{
		Database = database;
		Services = services;
		WorkspaceViews = workspaceViews;
		WorkspaceProductivity = workspaceProductivity;
		EnterpriseIdentity = enterpriseIdentity;
		EnterpriseAuthentication = enterpriseAuthentication;
		AuthenticationSecurity = authenticationSecurity;
		SecurityAdministration = securityAdministration;
		SecurityMaintenance = securityMaintenance;
		SecurityEventDelivery = securityEventDelivery;
		ViewModels = viewModels;
	}

	public DatabaseComposition Database { get; }
	public ServiceComposition Services { get; }
	public WorkspaceViewService WorkspaceViews { get; }
	public WorkspaceProductivityService WorkspaceProductivity { get; }
	public EnterpriseIdentityService EnterpriseIdentity { get; }
	public EnterpriseAuthenticationService EnterpriseAuthentication { get; }
	public AuthenticationSecurityService AuthenticationSecurity { get; }
	public SecurityAdministrationService SecurityAdministration { get; }
	public SecurityMaintenanceService SecurityMaintenance { get; }
	public SecurityEventDeliveryService SecurityEventDelivery { get; }
	public ViewModelFactory ViewModels { get; }

	public static DepotApplicationServices Create(IFileDialogService fileDialogs, ApplicationInformationService applicationInformation)
	{
		DatabaseComposition? database = null;
		try
		{
			database = DatabaseComposition.Create();
			var repositories = new RepositoryComposition(database.DataAccess);
			var services = new ServiceComposition(database, repositories);
			var workspaceViews = new WorkspaceViewService(database.TransactionRunner, new WorkspaceViewRepository(database.DataAccess), services.Authorization);
			WorkspaceViewRuntime.Configure(workspaceViews);
			var workspaceProductivity = new WorkspaceProductivityService(database.TransactionRunner, new WorkspaceProductivityRepository(database.DataAccess), services.Authorization);
			WorkspaceProductivityRuntime.Configure(workspaceProductivity);
			var audit = new AuditService(repositories.Audit, services.Authorization);
			var enterpriseIdentity = new EnterpriseIdentityService(database.TransactionRunner, repositories.EnterpriseIdentity, repositories.Users, repositories.Roles, repositories.Audit, audit, services.Authorization);
			var authenticationSecurity = new AuthenticationSecurityService(database.TransactionRunner, repositories.AuthenticationSecurity, repositories.Audit, audit, services.SecurityEvents, services.Authorization);
			services.Authentication.ConfigureAuthenticationSecurity(authenticationSecurity);

			var version = applicationInformation.GetVersionInfo().InformationalVersion;
			services.Session.Configure(database.TransactionRunner, repositories.UserSessions, services.SecurityEvents, new UserSessionClientInfo(Guid.NewGuid(), Environment.MachineName, version));
			services.Authentication.ConfigureSession(services.Session);
			services.Users.ConfigureSessionSecurity(services.Session, services.SecurityEvents);

			var enterpriseAuthentication = new EnterpriseAuthenticationService(repositories.EnterpriseIdentity, enterpriseIdentity, services.Session, services.Authorization, services.SecurityEvents, repositories.SecurityEvents);
			var securityAdministration = new SecurityAdministrationService(services.SecurityEvents, authenticationSecurity, services.UserSessionAdministration, repositories.UserSessions, repositories.Users, services.Users, services.Authorization);
			services.SecurityEvents.ConfigureAdministration(securityAdministration);

			var securityEventExport = new SecurityEventExportService(repositories.SecurityEventExports);
			var securityEventDelivery = new SecurityEventDeliveryService(
				database.TransactionRunner,
				repositories.SecurityEventDelivery,
				securityEventExport,
				repositories.Audit,
				audit,
				services.Authorization,
				new HttpJsonSecurityEventExportSinkFactory());
			var securityMaintenance = new SecurityMaintenanceService(
				database.TransactionRunner,
				repositories.UserSessions,
				repositories.SecurityEvents,
				repositories.AuthenticationSecurity,
				timeProvider: null,
				securityEventDelivery: repositories.SecurityEventDelivery);

			var composition = new DepotApplicationServices(
				database,
				services,
				workspaceViews,
				workspaceProductivity,
				enterpriseIdentity,
				enterpriseAuthentication,
				authenticationSecurity,
				securityAdministration,
				securityMaintenance,
				securityEventDelivery,
				new ViewModelFactory(database, services, enterpriseAuthentication, fileDialogs, applicationInformation));
			database.StartBackgroundServices();
			securityEventDelivery.Start();
			securityMaintenance.Start();
			return composition;
		}
		catch
		{
			DefaultDocumentTemplates.Runtime.DetachStore();
			database?.Dispose();
			throw;
		}
	}

	public void Dispose()
	{
		WorkspaceProductivityRuntime.Clear(WorkspaceProductivity);
		WorkspaceViewRuntime.Clear(WorkspaceViews);
		SecurityEventDelivery.Dispose();
		SecurityMaintenance.Dispose();
		Services.Session.Dispose();
		DefaultDocumentTemplates.Runtime.DetachStore();
		Database.Dispose();
	}
}
