// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Depot.DocumentRendering;

public enum DocumentTemplateType
{
	SalesQuote,
	SalesOrderConfirmation,
	SalesInvoice,
	CreditNote,
	PickList,
	PackingSlip,
	DeliveryNote,
	CustomerReturn
}

public enum DocumentTemplateElementType
{
	Text,
	BoundText,
	Image,
	Line,
	Rectangle,
	LineTable,
	TotalsBlock,
	PageNumber
}

public enum DocumentTemplateFontWeight
{
	Regular,
	Bold
}

public enum DocumentTemplateAlignment
{
	Left,
	Center,
	Right
}

public enum DocumentTemplatePlacement
{
	Absolute,
	Flow
}

public sealed record DocumentTemplateColumn(
	string Header,
	string Binding,
	double Width,
	DocumentTemplateAlignment Alignment = DocumentTemplateAlignment.Left,
	string? Format = null);

public sealed record DocumentTemplateElement
{
	public required string Id { get; init; }
	public DocumentTemplateElementType Type { get; init; }
	public double X { get; init; }
	public double Y { get; init; }
	public double Width { get; init; }
	public double Height { get; init; }
	public string Font { get; init; } = "Segoe UI";
	public double FontSize { get; init; } = 9;
	public DocumentTemplateFontWeight FontWeight { get; init; }
	public DocumentTemplateAlignment Alignment { get; init; }
	public string? Format { get; init; }
	public string? Visibility { get; init; }
	public string? Binding { get; init; }
	public string? Text { get; init; }
	public DocumentTemplatePlacement Placement { get; init; }
	public bool RepeatOnEveryPage { get; init; }
	public double RowHeight { get; init; } = 20;
	public IReadOnlyList<DocumentTemplateColumn> Columns { get; init; } = Array.Empty<DocumentTemplateColumn>();
}

public sealed record DocumentTemplate
{
	public required string Id { get; init; }
	public DocumentTemplateType Type { get; init; }
	public int Version { get; init; }
	public bool IsActive { get; init; }
	public double PageWidth { get; init; } = 595;
	public double PageHeight { get; init; } = 842;
	public IReadOnlyList<DocumentTemplateElement> Elements { get; init; } = Array.Empty<DocumentTemplateElement>();
}

public sealed record DocumentRenderRow(IReadOnlyDictionary<string, object?> Values)
{
	public object? Get(string binding) => Values.TryGetValue(binding, out var value) ? value : null;
}

public sealed record DocumentRenderModel(
	string PdfTitle,
	string PdfSubject,
	string PdfAuthor,
	IReadOnlyDictionary<string, object?> Values,
	IReadOnlyList<DocumentRenderRow> Lines)
{
	public object? Get(string binding) => Values.TryGetValue(binding, out var value) ? value : null;
}

public static class DocumentTemplateBindings
{
	private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
	{
		"Company.Name",
		"Company.Address",
		"Company.LegalLine",
		"Company.ContactLine",
		"Company.Logo",
		"Document.Title",
		"Document.Number",
		"Document.Date",
		"Document.ValidUntil",
		"Document.Contact",
		"Document.CustomerReference",
		"Document.Status",
		"Document.Currency",
		"Document.RequestedDeliveryDate",
		"Document.SalesOrderNumber",
		"Document.PackingStatus",
		"Document.Carrier",
		"Document.TrackingNumber",
		"Document.PackedAt",
		"Document.ShipmentNumber",
		"Document.DueDate",
		"Document.InvoiceNumber",
		"Document.Reason",
		"Document.Net",
		"Document.Tax",
		"Document.Total",
		"Customer.Name",
		"Customer.Address",
		"Customer.BillingAddress",
		"Customer.ShippingAddress",
		"Line.Item",
		"Line.Description",
		"Line.Quantity",
		"Line.UnitPrice",
		"Line.TaxRate",
		"Line.Total",
		"Page.Number",
		"Page.Count"
	};

	public static IReadOnlyCollection<string> All { get; } = new ReadOnlyCollection<string>(Allowed.OrderBy(value => value, StringComparer.Ordinal).ToArray());

	public static bool IsAllowed(string? binding) => !string.IsNullOrWhiteSpace(binding) && Allowed.Contains(binding);
}

public sealed class DocumentTemplateCatalog
{
	private readonly IReadOnlyDictionary<DocumentTemplateType, IReadOnlyList<DocumentTemplate>> _templates;

	public DocumentTemplateCatalog(IEnumerable<DocumentTemplate> templates)
	{
		ArgumentNullException.ThrowIfNull(templates);
		var materialized = templates.ToArray();
		foreach (var template in materialized) DocumentTemplateValidator.ValidateAndThrow(template);

		var duplicate = materialized
			.GroupBy(value => (value.Type, value.Version))
			.FirstOrDefault(group => group.Count() > 1);
		if (duplicate is not null)
			throw new InvalidOperationException($"Duplicate document template version {duplicate.Key.Type} v{duplicate.Key.Version}.");

		foreach (var group in materialized.GroupBy(value => value.Type))
		{
			if (group.Count(value => value.IsActive) != 1)
				throw new InvalidOperationException($"Document template type {group.Key} must have exactly one active version.");
		}

		_templates = materialized
			.GroupBy(value => value.Type)
			.ToDictionary(
				group => group.Key,
				group => (IReadOnlyList<DocumentTemplate>)new ReadOnlyCollection<DocumentTemplate>(
					group.OrderBy(value => value.Version).ToArray()));
	}

	public DocumentTemplate GetActive(DocumentTemplateType type)
	{
		if (!_templates.TryGetValue(type, out var values))
			throw new KeyNotFoundException($"No document template is registered for {type}.");
		return values.Single(value => value.IsActive);
	}

	public DocumentTemplate Get(DocumentTemplateType type, int version)
	{
		if (!_templates.TryGetValue(type, out var values))
			throw new KeyNotFoundException($"No document template is registered for {type}.");
		return values.SingleOrDefault(value => value.Version == version)
			?? throw new KeyNotFoundException($"Document template {type} v{version} is not registered.");
	}

	public IReadOnlyList<DocumentTemplate> ListVersions(DocumentTemplateType type) =>
		_templates.TryGetValue(type, out var values) ? values : Array.Empty<DocumentTemplate>();
}

public static class DocumentTemplateSerializer
{
	private static readonly JsonSerializerOptions Options = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = false,
		Converters = { new JsonStringEnumConverter() }
	};

	public static string Serialize(DocumentTemplate template)
	{
		ArgumentNullException.ThrowIfNull(template);
		DocumentTemplateValidator.ValidateAndThrow(template);
		return JsonSerializer.Serialize(template, Options);
	}
}

public static class DocumentTemplateValidator
{
	public static IReadOnlyList<string> Validate(DocumentTemplate template)
	{
		ArgumentNullException.ThrowIfNull(template);
		var errors = new List<string>();
		if (string.IsNullOrWhiteSpace(template.Id)) errors.Add("Template Id is required.");
		if (template.Version <= 0) errors.Add("Template version must be positive.");
		if (template.PageWidth <= 0 || template.PageHeight <= 0) errors.Add("Template page dimensions must be positive.");
		if (template.Elements.Count == 0) errors.Add("Template must contain at least one element.");

		var duplicateIds = template.Elements
			.Where(value => !string.IsNullOrWhiteSpace(value.Id))
			.GroupBy(value => value.Id, StringComparer.Ordinal)
			.Where(group => group.Count() > 1)
			.Select(group => group.Key);
		foreach (var id in duplicateIds) errors.Add($"Element id '{id}' is duplicated.");

		foreach (var element in template.Elements)
		{
			var prefix = string.IsNullOrWhiteSpace(element.Id) ? "Element" : $"Element '{element.Id}'";
			if (string.IsNullOrWhiteSpace(element.Id)) errors.Add("Element Id is required.");
			if (element.X < 0 || element.Y < 0 || element.Width < 0 || element.Height < 0)
				errors.Add($"{prefix} has invalid negative coordinates or size.");
			if (element.X > template.PageWidth || element.Y > template.PageHeight)
				errors.Add($"{prefix} starts outside the page.");
			if (element.Placement == DocumentTemplatePlacement.Absolute &&
				(element.X + element.Width > template.PageWidth + 0.01 || element.Y + element.Height > template.PageHeight + 0.01))
				errors.Add($"{prefix} exceeds the page bounds.");
			if (element.Type is DocumentTemplateElementType.Text or DocumentTemplateElementType.BoundText or DocumentTemplateElementType.PageNumber)
			{
				if (element.FontSize <= 0) errors.Add($"{prefix} must use a positive font size.");
				if (string.IsNullOrWhiteSpace(element.Font)) errors.Add($"{prefix} must define a font.");
			}
			if (element.Type == DocumentTemplateElementType.Text && element.Text is null)
				errors.Add($"{prefix} requires literal text.");
			if (element.Type is DocumentTemplateElementType.BoundText or DocumentTemplateElementType.Image)
			{
				if (!DocumentTemplateBindings.IsAllowed(element.Binding))
					errors.Add($"{prefix} uses unsupported binding '{element.Binding}'.");
			}
			if (!string.IsNullOrWhiteSpace(element.Visibility) && !DocumentTemplateBindings.IsAllowed(element.Visibility))
				errors.Add($"{prefix} uses unsupported visibility binding '{element.Visibility}'.");
			if (element.Type == DocumentTemplateElementType.LineTable)
			{
				if (element.RowHeight <= 0) errors.Add($"{prefix} must use a positive row height.");
				if (element.Columns.Count == 0) errors.Add($"{prefix} requires at least one column.");
				if (element.Columns.Any(column => column.Width <= 0)) errors.Add($"{prefix} contains an invalid column width.");
				if (element.Columns.Sum(column => column.Width) > element.Width + 0.01) errors.Add($"{prefix} columns exceed the table width.");
				foreach (var column in element.Columns)
				{
					if (!DocumentTemplateBindings.IsAllowed(column.Binding) || !column.Binding.StartsWith("Line.", StringComparison.Ordinal))
						errors.Add($"{prefix} uses unsupported line binding '{column.Binding}'.");
				}
			}
		}

		return errors;
	}

	public static void ValidateAndThrow(DocumentTemplate template)
	{
		var errors = Validate(template);
		if (errors.Count > 0)
			throw new InvalidOperationException("Document template validation failed: " + string.Join(" ", errors));
	}
}
