// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Services;

namespace Depot.Composition;

internal sealed class DepotApplicationServices : IDisposable
{
	private DepotApplicationServices(
		DatabaseComposition database,
		ServiceComposition services,
		AuthenticationSecurityService authenticationSecurity,
		SecurityAdministrationService securityAdministration,
		SecurityMaintenanceService securityMaintenance,
		ViewModelFactory viewModels)
	{
		Database = database;
		Services = services;
		AuthenticationSecurity = authenticationSecurity;
		SecurityAdministration = securityAdministration;
		SecurityMaintenance = securityMaintenance;
		ViewModels = viewModels;
	}

	public DatabaseComposition Database { get; }
	public ServiceComposition Services { get; }
	public AuthenticationSecurityService AuthenticationSecurity { get; }
	public SecurityAdministrationService SecurityAdministration { get; }
	public SecurityMaintenanceService SecurityMaintenance { get; }
	public ViewModelFactory ViewModels { get; }

	public static DepotApplicationServices Create(IFileDialogService fileDialogs, ApplicationInformationService applicationInformation)
	{
		DatabaseComposition? database = null;
		try
		{
			database = DatabaseComposition.Create();
			var repositories = new RepositoryComposition(database.DataAccess);
			var services = new ServiceComposition(database, repositories);
			var audit = new AuditService(repositories.Audit, services.Authorization);
			var authenticationSecurity = new AuthenticationSecurityService(
				database.TransactionRunner,
				repositories.AuthenticationSecurity,
				repositories.Audit,
				audit,
				services.SecurityEvents,
				services.Authorization);
			services.Authentication.ConfigureAuthenticationSecurity(authenticationSecurity);

			var version = applicationInformation.GetVersionInfo().InformationalVersion;
			services.Session.Configure(
				database.TransactionRunner,
				repositories.UserSessions,
				services.SecurityEvents,
				new UserSessionClientInfo(Guid.NewGuid(), Environment.MachineName, version));
			services.Authentication.ConfigureSession(services.Session);
			services.Users.ConfigureSessionSecurity(services.Session, services.SecurityEvents);

			var securityAdministration = new SecurityAdministrationService(
				services.SecurityEvents,
				authenticationSecurity,
				services.UserSessionAdministration,
				repositories.UserSessions,
				repositories.Users,
				services.Users,
				services.Authorization);
			services.SecurityEvents.ConfigureAdministration(securityAdministration);

			var securityMaintenance = new SecurityMaintenanceService(
				database.TransactionRunner,
				repositories.UserSessions,
				repositories.SecurityEvents,
				repositories.AuthenticationSecurity);

			var composition = new DepotApplicationServices(
				database,
				services,
				authenticationSecurity,
				securityAdministration,
				securityMaintenance,
				new ViewModelFactory(database, services, fileDialogs, applicationInformation));
			database.StartBackgroundServices();
			securityMaintenance.Start();
			return composition;
		}
		catch
		{
			database?.Dispose();
			throw;
		}
	}

	public void Dispose()
	{
		SecurityMaintenance.Dispose();
		Services.Session.Dispose();
		Database.Dispose();
	}
}
