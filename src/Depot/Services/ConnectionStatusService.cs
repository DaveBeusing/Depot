// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.ComponentModel;
using System.Runtime.CompilerServices;

using Depot.Models;

namespace Depot.Services;

public sealed class ConnectionStatusService : INotifyPropertyChanged
{
	private ConnectionState _state = ConnectionState.Disconnected;
	private string _status = "Database unavailable";
	private string _detail = string.Empty;

	public ConnectionState State
	{
		get => _state;
		private set
		{
			if (_state == value)
			{
				return;
			}

			_state = value;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(State)));
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CompactState)));
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CompactStatus)));
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DatabaseStatus)));
		}
	}

	public ConnectionState CompactState =>
		State == ConnectionState.Disconnected
			? ConnectionState.Disconnected
			: ConnectionState.Connected;

	public string CompactStatus =>
		State == ConnectionState.Disconnected
			? "Disconnected"
			: "Connected";

	public string DatabaseStatus =>
		State == ConnectionState.Disconnected
			? "Database disconnected"
			: "Database connected";

	public string Status
	{
		get => _status;
		private set => SetField(ref _status, value);
	}

	public string Detail
	{
		get => _detail;
		private set => SetField(ref _detail, value);
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	public void Apply(DatabaseConnectionSettings settings)
	{
		if (settings.Provider == DatabaseProvider.Local)
		{
			State = ConnectionState.Connected;
			Status = "Local database connected";
			Detail = settings.LocalDatabasePath;
			return;
		}

		State = ConnectionState.Pending;
		Status = "Local fallback active";
		Detail = $"SQL Server prepared: {settings.SqlServerHost}:{settings.SqlServerPort}/{settings.SqlServerDatabase}";
	}

	public void SetDisconnected(string detail)
	{
		State = ConnectionState.Disconnected;
		Status = "Database unavailable";
		Detail = detail;
	}

	private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
	{
		if (EqualityComparer<T>.Default.Equals(field, value))
		{
			return;
		}

		field = value;
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}
}
