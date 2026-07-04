// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Depot.ViewModels;

public abstract class BaseViewModel : INotifyPropertyChanged
{
	public event PropertyChangedEventHandler? PropertyChanged;

	protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
	{
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}

}