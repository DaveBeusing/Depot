// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Text.RegularExpressions;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Depot.Services.Help;

public sealed partial class HelpMarkdownRenderer
{
	public FlowDocument Render(string markdown)
	{
		var document = new FlowDocument
		{
			PagePadding = new Thickness(0),
			TextAlignment = TextAlignment.Left
		};
		document.SetResourceReference(FlowDocument.FontFamilyProperty, "Help.Document.FontFamily");
		document.SetResourceReference(FlowDocument.FontSizeProperty, "Help.Document.FontSize");
		document.SetResourceReference(FlowDocument.ForegroundProperty, "PrimaryTextBrush");

		var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
		for (var index = 0; index < lines.Length;)
		{
			var line = lines[index].TrimEnd();
			if (string.IsNullOrWhiteSpace(line)) { index++; continue; }

			var heading = HeadingRegex().Match(line);
			if (heading.Success)
			{
				document.Blocks.Add(CreateHeading(heading.Groups[1].Length, heading.Groups[2].Value));
				index++;
				continue;
			}

			if (TryCreateCallout(lines, ref index, out var callout))
			{
				document.Blocks.Add(callout);
				continue;
			}

			if (IsTableStart(lines, index))
			{
				document.Blocks.Add(CreateTable(lines, ref index));
				continue;
			}

			if (ListItemRegex().IsMatch(line))
			{
				document.Blocks.Add(CreateList(lines, ref index));
				continue;
			}

			var paragraphLines = new List<string>();
			while (index < lines.Length && !string.IsNullOrWhiteSpace(lines[index]) &&
				!HeadingRegex().IsMatch(lines[index]) && !ListItemRegex().IsMatch(lines[index]) &&
				!CalloutRegex().IsMatch(lines[index]) && !IsTableStart(lines, index))
			{
				paragraphLines.Add(lines[index].Trim());
				index++;
			}
			var paragraph = CreateParagraph(string.Join(' ', paragraphLines));
			document.Blocks.Add(paragraph);
		}

		return document;
	}

	private static Paragraph CreateHeading(int level, string text)
	{
		var paragraph = new Paragraph { FontWeight = FontWeights.SemiBold, KeepWithNext = true };
		paragraph.SetResourceReference(Paragraph.FontSizeProperty, level == 1 ? "Help.Heading1.FontSize" : "Help.Heading2.FontSize");
		paragraph.SetResourceReference(Block.MarginProperty, level == 1 ? "Help.Heading1.Margin" : "Help.Heading2.Margin");
		AddInlines(paragraph.Inlines, text);
		return paragraph;
	}

	private static Paragraph CreateParagraph(string text)
	{
		var paragraph = new Paragraph();
		paragraph.SetResourceReference(Block.MarginProperty, "Help.Paragraph.Margin");
		AddInlines(paragraph.Inlines, text);
		return paragraph;
	}

	private static List CreateList(IReadOnlyList<string> lines, ref int index)
	{
		var first = ListItemRegex().Match(lines[index]);
		var list = new List { MarkerStyle = char.IsDigit(first.Groups[1].Value[0]) ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc };
		list.SetResourceReference(Block.MarginProperty, "Help.List.Margin");
		while (index < lines.Count)
		{
			var match = ListItemRegex().Match(lines[index]);
			if (!match.Success) break;
			var item = new ListItem(CreateParagraph(match.Groups[2].Value));
			item.SetResourceReference(ListItem.MarginProperty, "Help.ListItem.Margin");
			list.ListItems.Add(item);
			index++;
		}
		return list;
	}

	private static bool TryCreateCallout(IReadOnlyList<string> lines, ref int index, out Section section)
	{
		var match = CalloutRegex().Match(lines[index]);
		if (!match.Success) { section = new Section(); return false; }
		var isWarning = string.Equals(match.Groups[1].Value, "WARNING", StringComparison.OrdinalIgnoreCase);
		var text = new List<string>();
		if (!string.IsNullOrWhiteSpace(match.Groups[2].Value)) text.Add(match.Groups[2].Value);
		index++;
		while (index < lines.Count && lines[index].StartsWith('>'))
		{
			text.Add(lines[index].TrimStart('>', ' '));
			index++;
		}
		section = new Section(CreateParagraph(string.Join(' ', text))) { BorderThickness = new Thickness(1) };
		section.SetResourceReference(Block.PaddingProperty, "Help.Callout.Padding");
		section.SetResourceReference(Block.MarginProperty, "Help.Callout.Margin");
		section.SetResourceReference(TextElement.BackgroundProperty, isWarning ? "WarningBrush" : "SurfaceAltBrush");
		section.SetResourceReference(Section.BorderBrushProperty, isWarning ? "WarningForegroundBrush" : "BorderBrush");
		section.SetResourceReference(TextElement.ForegroundProperty, isWarning ? "WarningForegroundBrush" : "PrimaryTextBrush");
		return true;
	}

	private static bool IsTableStart(IReadOnlyList<string> lines, int index) => index + 1 < lines.Count &&
		lines[index].Contains('|') && TableDividerRegex().IsMatch(lines[index + 1].Trim());

	private static Table CreateTable(IReadOnlyList<string> lines, ref int index)
	{
		var headers = SplitTableRow(lines[index]);
		index += 2;
		var table = new Table { CellSpacing = 0 };
		table.SetResourceReference(Block.MarginProperty, "Help.Table.Margin");
		foreach (var _ in headers) table.Columns.Add(new TableColumn());
		var group = new TableRowGroup();
		group.Rows.Add(CreateTableRow(headers, true));
		while (index < lines.Count && lines[index].Contains('|') && !string.IsNullOrWhiteSpace(lines[index]))
		{
			group.Rows.Add(CreateTableRow(SplitTableRow(lines[index]), false));
			index++;
		}
		table.RowGroups.Add(group);
		return table;
	}

	private static TableRow CreateTableRow(IReadOnlyList<string> values, bool header)
	{
		var row = new TableRow();
		if (header) row.SetResourceReference(TextElement.BackgroundProperty, "SurfaceAltBrush");
		foreach (var value in values)
		{
			var cell = new TableCell(CreateParagraph(value)) { BorderThickness = new Thickness(1), FontWeight = header ? FontWeights.SemiBold : FontWeights.Normal };
			cell.SetResourceReference(TableCell.BorderBrushProperty, "BorderBrush");
			cell.SetResourceReference(TableCell.PaddingProperty, "Help.Table.Cell.Padding");
			row.Cells.Add(cell);
		}
		return row;
	}

	private static IReadOnlyList<string> SplitTableRow(string line) => line.Trim().Trim('|').Split('|').Select(cell => cell.Trim()).ToArray();

	private static void AddInlines(InlineCollection destination, string text)
	{
		var position = 0;
		foreach (Match match in InlineTokenRegex().Matches(text))
		{
			if (match.Index > position) destination.Add(new Run(text[position..match.Index]));
			if (match.Groups[1].Success)
			{
				var image = CreateImage(match.Groups[3].Value, match.Groups[2].Value);
				destination.Add(image);
			}
			else if (match.Groups[4].Success)
			{
				var link = new Hyperlink(new Run(match.Groups[4].Value)) { NavigateUri = new Uri($"topic:{match.Groups[5].Value}", UriKind.Absolute) };
				link.SetResourceReference(TextElement.ForegroundProperty, "PrimaryBrush");
				destination.Add(link);
			}
			else if (match.Groups[6].Success) destination.Add(new Bold(new Run(match.Groups[6].Value)));
			else if (match.Groups[7].Success) destination.Add(new Italic(new Run(match.Groups[7].Value)));
			else if (match.Groups[8].Success)
			{
				var code = new Run(match.Groups[8].Value) { FontFamily = new FontFamily("Consolas") };
				code.SetResourceReference(TextElement.BackgroundProperty, "SurfaceAltBrush");
				destination.Add(code);
			}
			position = match.Index + match.Length;
		}
		if (position < text.Length) destination.Add(new Run(text[position..]));
	}

	private static Inline CreateImage(string path, string alternativeText)
	{
		try
		{
			var image = new Image
			{
				Source = new BitmapImage(new Uri(path, UriKind.RelativeOrAbsolute)),
				MaxWidth = 640,
				Stretch = Stretch.Uniform,
				ToolTip = alternativeText
			};
			return new InlineUIContainer(image);
		}
		catch (Exception exception) when (exception is UriFormatException or IOException or NotSupportedException)
		{
			return new Run($"[Image unavailable: {alternativeText}]");
		}
	}

	[GeneratedRegex("^(#{1,6})\\s+(.+)$")]
	private static partial Regex HeadingRegex();
	[GeneratedRegex("^\\s*(\\d+\\.|[-*])\\s+(.+)$")]
	private static partial Regex ListItemRegex();
	[GeneratedRegex("^>\\s*\\[!(NOTE|WARNING)\\]\\s*(.*)$", RegexOptions.IgnoreCase)]
	private static partial Regex CalloutRegex();
	[GeneratedRegex("^\\|?\\s*:?-{3,}:?\\s*(\\|\\s*:?-{3,}:?\\s*)+\\|?$")]
	private static partial Regex TableDividerRegex();
	[GeneratedRegex("(!)\\[([^\\]]*)\\]\\(([^)]+)\\)|\\[([^\\]]+)\\]\\(topic:([a-z0-9.-]+)\\)|\\*\\*([^*]+)\\*\\*|(?<!\\*)\\*([^*]+)\\*(?!\\*)|`([^`]+)`", RegexOptions.IgnoreCase)]
	private static partial Regex InlineTokenRegex();
}
