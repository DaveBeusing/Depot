// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Services;

namespace Depot.Composition;

internal sealed class DepotApplicationServices : IDisposable
{
	private DepotApplicationServices(
		DatabaseComposition database,
		ServiceComposition services,
		ViewModelFactory viewModels)
	{
		Database = database;
		Services = services;
		ViewModels = viewModels;
	}

	public DatabaseComposition Database { get; }
	public ServiceComposition Services { get; }
	public ViewModelFactory ViewModels { get; }

	public static DepotApplicationServices Create(
		IFileDialogService fileDialogs,
		ApplicationInformationService applicationInformation)
	{
		DatabaseComposition? database = null;
		try
		{
			database = DatabaseComposition.Create();
			var repositories = new RepositoryComposition(database.DataAccess);
			var services = new ServiceComposition(database, repositories);
			var composition = new DepotApplicationServices(
				database,
				services,
				new ViewModelFactory(database, services, fileDialogs, applicationInformation));
			database.StartBackgroundServices();
			return composition;
		}
		catch
		{
			database?.Dispose();
			throw;
		}
	}

	public void Dispose() => Database.Dispose();
}
