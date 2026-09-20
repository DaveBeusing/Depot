// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;
using System.Collections.Specialized;

using Depot.ViewModels;

using Xunit;

namespace Depot.Tests;

public sealed class CollectionSynchronizerPerformanceTests
{
	[Fact]
	public void EqualSharedValuesDoNotRaiseReplaceNotifications()
	{
		var first = new Row(1, "A");
		var second = new Row(2, "B");
		var target = new ObservableCollection<Row>([first, second]);
		var replacements = 0;
		target.CollectionChanged += (_, args) =>
		{
			if (args.Action == NotifyCollectionChangedAction.Replace) replacements++;
		};

		CollectionSynchronizer.Replace(target, [new Row(1, "A"), new Row(2, "B")]);

		Assert.Equal(0, replacements);
		Assert.Same(first, target[0]);
		Assert.Same(second, target[1]);
	}

	[Fact]
	public void ChangedSharedValueRaisesOnlyOneReplaceNotification()
	{
		var first = new Row(1, "A");
		var second = new Row(2, "B");
		var target = new ObservableCollection<Row>([first, second]);
		var replacements = 0;
		target.CollectionChanged += (_, args) =>
		{
			if (args.Action == NotifyCollectionChangedAction.Replace) replacements++;
		};

		CollectionSynchronizer.Replace(target, [new Row(1, "A"), new Row(2, "Changed")]);

		Assert.Equal(1, replacements);
		Assert.Same(first, target[0]);
		Assert.Equal("Changed", target[1].Name);
	}

	[Fact]
	public void SynchronizationStillHandlesGrowthAndShrinkWithoutQuadraticLookup()
	{
		var target = new ObservableCollection<Row>(Enumerable.Range(1, 1000).Select(index => new Row(index, $"Row {index}")));

		CollectionSynchronizer.Replace(target, Enumerable.Range(1, 1200).Select(index => new Row(index, $"Row {index}")).ToArray());
		Assert.Equal(1200, target.Count);

		CollectionSynchronizer.Replace(target, Enumerable.Range(1, 800).Select(index => new Row(index, $"Row {index}")).ToArray());
		Assert.Equal(800, target.Count);
		Assert.Equal(800, target[^1].Id);
	}

	private sealed record Row(int Id, string Name);
}
