// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

namespace Depot.ViewModels;

internal static class CollectionSynchronizer
{
	public static void Replace<T>(ObservableCollection<T> target, IReadOnlyList<T> values)
	{
		var sharedCount = Math.Min(target.Count, values.Count);
		for (var index = 0; index < sharedCount; index++) target[index] = values[index];
		while (target.Count > values.Count) target.RemoveAt(target.Count - 1);
		for (var index = sharedCount; index < values.Count; index++) target.Add(values[index]);
	}
}
