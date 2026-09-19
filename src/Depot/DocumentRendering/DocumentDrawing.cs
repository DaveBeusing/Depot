// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;

using PdfSharp.Drawing;

namespace Depot.DocumentRendering;

internal sealed class DocumentDrawing
{
	private readonly Dictionary<(string Font, double Size, DocumentTemplateFontWeight Weight), XFont> _fonts = [];

	public void DrawMultilineText(XGraphics graphics, string text, double x, double y, DocumentTemplateElement element)
	{
		var lineHeight = Math.Max(element.FontSize + 3, 10);
		var maxLines = element.Height <= 0 ? 1 : Math.Max(1, (int)Math.Floor(element.Height / lineHeight));
		var lines = NormalizeLines(text).Take(maxLines).ToArray();
		if (lines.Length == 0) lines = ["—"];
		for (var index = 0; index < lines.Length; index++)
		{
			DrawText(
				graphics,
				lines[index],
				x,
				y + index * lineHeight,
				element.Width,
				lineHeight,
				element.Font,
				element.FontSize,
				element.FontWeight,
				element.Alignment);
		}
	}

	public void DrawText(
		XGraphics graphics,
		string text,
		double x,
		double y,
		double width,
		double height,
		string fontName,
		double fontSize,
		DocumentTemplateFontWeight weight,
		DocumentTemplateAlignment alignment)
	{
		if (width <= 0 || height < 0) return;
		var font = GetFont(fontName, fontSize, weight);
		var clipped = ClipToWidth(graphics, text, font, width);
		var measured = graphics.MeasureString(clipped, font);
		var drawX = alignment switch
		{
			DocumentTemplateAlignment.Right => x + Math.Max(0, width - measured.Width),
			DocumentTemplateAlignment.Center => x + Math.Max(0, (width - measured.Width) / 2),
			_ => x
		};
		graphics.DrawString(clipped, font, XBrushes.Black, new XPoint(drawX, y));
	}

	public static void DrawImage(XGraphics graphics, object? value, double x, double y, double width, double height)
	{
		if (value is not byte[] bytes || bytes.Length == 0 || width <= 0 || height <= 0) return;
		using var stream = new MemoryStream(bytes, 0, bytes.Length, false, true);
		using var image = XImage.FromStream(stream);
		graphics.DrawImage(image, x, y, width, height);
	}

	public static string FormatValue(object? value, string? format)
	{
		if (value is null) return "—";
		if (value is string text) return string.IsNullOrWhiteSpace(text) ? "—" : text;
		if (value is DateOnly dateOnly) return string.IsNullOrWhiteSpace(format)
			? dateOnly.ToString("d", CultureInfo.CurrentCulture)
			: dateOnly.ToString(format, CultureInfo.CurrentCulture);
		if (value is DateTime dateTime) return string.IsNullOrWhiteSpace(format)
			? dateTime.ToString("d", CultureInfo.CurrentCulture)
			: dateTime.ToString(format, CultureInfo.CurrentCulture);
		if (value is IFormattable formattable)
			return formattable.ToString(format, CultureInfo.CurrentCulture) ?? "—";
		return value.ToString() ?? "—";
	}

	private XFont GetFont(string fontName, double fontSize, DocumentTemplateFontWeight weight)
	{
		var key = (fontName, fontSize, weight);
		if (_fonts.TryGetValue(key, out var value)) return value;
		value = new XFont(fontName, fontSize, weight == DocumentTemplateFontWeight.Bold ? XFontStyleEx.Bold : XFontStyleEx.Regular);
		_fonts.Add(key, value);
		return value;
	}

	private static IEnumerable<string> NormalizeLines(string text)
	{
		if (string.IsNullOrWhiteSpace(text)) yield break;
		foreach (var line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
		{
			var trimmed = line.Trim();
			if (trimmed.Length > 0) yield return trimmed;
		}
	}

	private static string ClipToWidth(XGraphics graphics, string value, XFont font, double width)
	{
		if (string.IsNullOrEmpty(value) || width <= 0) return string.Empty;
		if (graphics.MeasureString(value, font).Width <= width) return value;
		const string ellipsis = "…";
		if (graphics.MeasureString(ellipsis, font).Width > width) return string.Empty;
		var length = value.Length;
		while (length > 0)
		{
			var candidate = value[..length].TrimEnd() + ellipsis;
			if (graphics.MeasureString(candidate, font).Width <= width) return candidate;
			length--;
		}
		return ellipsis;
	}
}
