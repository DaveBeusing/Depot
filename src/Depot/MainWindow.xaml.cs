// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Windows;

namespace Depot;

public partial class MainWindow : Window
{
	public MainWindow()
	{
		InitializeComponent();

		DataContext =
			App.MainViewModel;
	}

}