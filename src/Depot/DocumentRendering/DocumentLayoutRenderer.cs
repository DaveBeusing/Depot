// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;

using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace Depot.DocumentRendering;

public sealed class DocumentLayoutRenderer
{
	private const double BottomContentMargin = 84;
	private readonly DocumentDrawing _drawing = new();

	public void Render(string path, DocumentTemplate template, DocumentRenderModel model)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		ArgumentNullException.ThrowIfNull(template);
		ArgumentNullException.ThrowIfNull(model);
		using var document = CreateDocument(model);
		RenderInto(document, template, model);
		Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
		document.Save(path);
	}

	public byte[] RenderToBytes(DocumentTemplate template, DocumentRenderModel model)
	{
		ArgumentNullException.ThrowIfNull(template);
		ArgumentNullException.ThrowIfNull(model);
		using var document = CreateDocument(model);
		RenderInto(document, template, model);
		using var stream = new MemoryStream();
		document.Save(stream, false);
		return stream.ToArray();
	}

	public void RenderInto(PdfDocument document, DocumentTemplate template, DocumentRenderModel model)
	{
		ArgumentNullException.ThrowIfNull(document);
		ArgumentNullException.ThrowIfNull(template);
		ArgumentNullException.ThrowIfNull(model);
		DocumentTemplateValidator.ValidateAndThrow(template);

		var page = AddPage(document);
		var graphics = XGraphics.FromPdfPage(page);
		var flowY = 0d;
		try
		{
			foreach (var element in template.Elements.Where(value => !value.RepeatOnEveryPage))
			{
				if (!IsVisible(element, model)) continue;
				var y = element.Placement == DocumentTemplatePlacement.Flow ? flowY + element.Y : element.Y;
				switch (element.Type)
				{
					case DocumentTemplateElementType.LineTable:
						flowY = RenderLineTable(document, ref page, ref graphics, element, model, y);
						break;
					case DocumentTemplateElementType.TotalsBlock:
						flowY = RenderTotals(document, ref page, ref graphics, element, model, y);
						break;
					default:
						RenderElement(graphics, element, model, y, null, null);
						if (element.Placement == DocumentTemplatePlacement.Flow)
							flowY = Math.Max(flowY, y + element.Height);
						break;
				}
			}
		}
		finally
		{
			graphics.Dispose();
		}

		for (var pageIndex = 0; pageIndex < document.PageCount; pageIndex++)
		{
			using var repeatedGraphics = XGraphics.FromPdfPage(document.Pages[pageIndex], XGraphicsPdfPageOptions.Append);
			foreach (var element in template.Elements.Where(value => value.RepeatOnEveryPage))
			{
				if (IsVisible(element, model))
					RenderElement(repeatedGraphics, element, model, element.Y, pageIndex + 1, document.PageCount);
			}
		}
	}

	private static PdfDocument CreateDocument(DocumentRenderModel model)
	{
		var document = new PdfDocument();
		document.Info.Title = model.PdfTitle;
		document.Info.Subject = model.PdfSubject;
		document.Info.Creator = "Depot";
		document.Info.Author = model.PdfAuthor;
		return document;
	}

	private static PdfPage AddPage(PdfDocument document)
	{
		var page = document.AddPage();
		page.Size = PdfSharp.PageSize.A4;
		return page;
	}

	private double RenderLineTable(
		PdfDocument document,
		ref PdfPage page,
		ref XGraphics graphics,
		DocumentTemplateElement element,
		DocumentRenderModel model,
		double y)
	{
		DrawTableHeader(graphics, element, y);
		y += element.RowHeight + 2;
		foreach (var row in model.Lines)
		{
			if (y + element.RowHeight > page.Height.Point - BottomContentMargin)
			{
				graphics.Dispose();
				page = AddPage(document);
				graphics = XGraphics.FromPdfPage(page);
				y = 55;
				DrawTableHeader(graphics, element, y);
				y += element.RowHeight + 2;
			}

			var x = element.X;
			foreach (var column in element.Columns)
			{
				var text = DocumentDrawing.FormatValue(row.Get(column.Binding), column.Format);
				_drawing.DrawText(graphics, text, x, y, column.Width, element.RowHeight, element.Font, element.FontSize, DocumentTemplateFontWeight.Regular, column.Alignment);
				x += column.Width;
			}
			y += element.RowHeight;
		}
		return y + 8;
	}

	private void DrawTableHeader(XGraphics graphics, DocumentTemplateElement element, double y)
	{
		graphics.DrawLine(XPens.LightGray, element.X, y + 5, element.X + element.Width, y + 5);
		var x = element.X;
		foreach (var column in element.Columns)
		{
			_drawing.DrawText(graphics, column.Header, x, y, column.Width, element.RowHeight, element.Font, element.FontSize, DocumentTemplateFontWeight.Bold, column.Alignment);
			x += column.Width;
		}
	}

	private double RenderTotals(
		PdfDocument document,
		ref PdfPage page,
		ref XGraphics graphics,
		DocumentTemplateElement element,
		DocumentRenderModel model,
		double y)
	{
		if (y + element.Height > page.Height.Point - BottomContentMargin)
		{
			graphics.Dispose();
			page = AddPage(document);
			graphics = XGraphics.FromPdfPage(page);
			y = 55;
		}

		graphics.DrawLine(XPens.LightGray, element.X, y, element.X + element.Width, y);
		var labelX = element.X + 40;
		var valueX = element.X + 118;
		_drawing.DrawText(graphics, "Net", labelX, y + 18, 70, 14, "Segoe UI", 9, DocumentTemplateFontWeight.Regular, DocumentTemplateAlignment.Left);
		_drawing.DrawText(graphics, DocumentDrawing.FormatValue(model.Get("Document.Net"), null), valueX, y + 18, 67, 14, "Segoe UI", 9, DocumentTemplateFontWeight.Regular, DocumentTemplateAlignment.Right);
		_drawing.DrawText(graphics, "Tax", labelX, y + 34, 70, 14, "Segoe UI", 9, DocumentTemplateFontWeight.Regular, DocumentTemplateAlignment.Left);
		_drawing.DrawText(graphics, DocumentDrawing.FormatValue(model.Get("Document.Tax"), null), valueX, y + 34, 67, 14, "Segoe UI", 9, DocumentTemplateFontWeight.Regular, DocumentTemplateAlignment.Right);
		_drawing.DrawText(graphics, "Total", labelX, y + 54, 70, 14, "Segoe UI", 11, DocumentTemplateFontWeight.Bold, DocumentTemplateAlignment.Left);
		_drawing.DrawText(graphics, DocumentDrawing.FormatValue(model.Get("Document.Total"), null), valueX, y + 54, 67, 14, "Segoe UI", 11, DocumentTemplateFontWeight.Bold, DocumentTemplateAlignment.Right);
		return y + element.Height;
	}

	private void RenderElement(
		XGraphics graphics,
		DocumentTemplateElement element,
		DocumentRenderModel model,
		double y,
		int? pageNumber,
		int? pageCount)
	{
		switch (element.Type)
		{
			case DocumentTemplateElementType.Text:
				_drawing.DrawMultilineText(graphics, element.Text ?? string.Empty, element.X, y, element);
				break;
			case DocumentTemplateElementType.BoundText:
				_drawing.DrawMultilineText(graphics, DocumentDrawing.FormatValue(model.Get(element.Binding!), element.Format), element.X, y, element);
				break;
			case DocumentTemplateElementType.Image:
				DocumentDrawing.DrawImage(graphics, model.Get(element.Binding!), element.X, y, element.Width, element.Height);
				break;
			case DocumentTemplateElementType.Line:
				graphics.DrawLine(XPens.LightGray, element.X, y, element.X + element.Width, y + element.Height);
				break;
			case DocumentTemplateElementType.Rectangle:
				graphics.DrawRectangle(XPens.LightGray, element.X, y, element.Width, element.Height);
				break;
			case DocumentTemplateElementType.PageNumber:
				var format = string.IsNullOrWhiteSpace(element.Format) ? "Page {0} of {1}" : element.Format;
				var value = string.Format(CultureInfo.CurrentCulture, format, pageNumber ?? 1, pageCount ?? 1);
				_drawing.DrawText(graphics, value, element.X, y, element.Width, element.Height, element.Font, element.FontSize, element.FontWeight, element.Alignment);
				break;
			case DocumentTemplateElementType.LineTable:
			case DocumentTemplateElementType.TotalsBlock:
				throw new InvalidOperationException($"Element type {element.Type} must use the flow renderer.");
			default:
				throw new ArgumentOutOfRangeException(nameof(element.Type), element.Type, "Unsupported document template element type.");
		}
	}

	private static bool IsVisible(DocumentTemplateElement element, DocumentRenderModel model)
	{
		if (string.IsNullOrWhiteSpace(element.Visibility)) return true;
		var value = model.Get(element.Visibility);
		return value switch
		{
			bool boolean => boolean,
			string text when bool.TryParse(text, out var parsed) => parsed,
			_ => false
		};
	}
}
