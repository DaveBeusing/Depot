// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Depot.Converters;

public sealed class NonZeroToVisibilityConverter
	: IValueConverter
{
	public object Convert(
		object value,
		Type targetType,
		object parameter,
		CultureInfo culture)
	{
		if (value is int number && number > 0)
		{
			return Visibility.Visible;
		}

		return Visibility.Collapsed;
	}

	public object ConvertBack(
		object value,
		Type targetType,
		object parameter,
		CultureInfo culture)
	{
		throw new NotSupportedException();
	}
}