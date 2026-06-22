// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Windows;
using Depot.Data;

namespace Depot;

public partial class App : Application
{
	public static SqliteConnectionFactory ConnectionFactory { get; private set; } = null!;

	public static DepotDatabase Database { get; private set; } = null!;

	protected override void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);

		ConnectionFactory =
			new SqliteConnectionFactory("depot.db");

		Database =
			new DepotDatabase(ConnectionFactory);

		Database.Initialize();
	}

}