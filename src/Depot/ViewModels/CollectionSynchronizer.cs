// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

namespace Depot.ViewModels;

internal static class CollectionSynchronizer
{
	public static void Replace<T>(ObservableCollection<T> target, IReadOnlyList<T> values)
	{
		var sharedCount = Math.Min(target.Count, values.Count);
		var comparer = EqualityComparer<T>.Default;
		for (var index = 0; index < sharedCount; index++)
		{
			var current = target[index];
			var next = values[index];
			if (ReferenceEquals(current, next) || comparer.Equals(current, next)) continue;
			target[index] = next;
		}
		while (target.Count > values.Count) target.RemoveAt(target.Count - 1);
		for (var index = sharedCount; index < values.Count; index++) target.Add(values[index]);
	}
}
