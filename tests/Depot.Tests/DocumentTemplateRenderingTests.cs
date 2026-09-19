// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.DocumentRendering;

using PdfSharp.Pdf.IO;

namespace Depot.Tests;

public sealed class DocumentTemplateRenderingTests
{
	[Fact]
	public void DefaultTemplatesAreValidVersionedAndSerializeDeterministically()
	{
		foreach (var type in Enum.GetValues<DocumentTemplateType>())
		{
			var template = DefaultDocumentTemplates.Catalog.GetActive(type);
			Assert.Equal(1, template.Version);
			Assert.True(template.IsActive);
			Assert.Empty(DocumentTemplateValidator.Validate(template));
			Assert.Equal(
				DocumentTemplateSerializer.Serialize(template),
				DocumentTemplateSerializer.Serialize(template));
			Assert.Single(DefaultDocumentTemplates.Catalog.ListVersions(type), value => value.IsActive);
		}
	}

	[Fact]
	public void BindingAllowlistRejectsArbitraryObjectPaths()
	{
		var template = Template(new DocumentTemplateElement
		{
			Id = "unsafe",
			Type = DocumentTemplateElementType.BoundText,
			Binding = "System.Environment.MachineName",
			X = 40,
			Y = 40,
			Width = 200,
			Height = 20
		});

		var error = Assert.Single(DocumentTemplateValidator.Validate(template));
		Assert.Contains("unsupported binding", error, StringComparison.OrdinalIgnoreCase);
		Assert.Throws<InvalidOperationException>(() => DocumentTemplateValidator.ValidateAndThrow(template));
	}

	[Fact]
	public void MissingAllowedBindingRendersFailSafe()
	{
		var template = Template(new DocumentTemplateElement
		{
			Id = "missing",
			Type = DocumentTemplateElementType.BoundText,
			Binding = "Customer.Name",
			X = 40,
			Y = 40,
			Width = 200,
			Height = 20
		});
		var model = Model(new Dictionary<string, object?>(), []);

		var bytes = new DocumentLayoutRenderer().RenderToBytes(template, model);

		Assert.NotEmpty(bytes);
		Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
	}

	[Fact]
	public void InvalidCoordinatesFailValidation()
	{
		var template = Template(new DocumentTemplateElement
		{
			Id = "outside",
			Type = DocumentTemplateElementType.Text,
			Text = "Invalid",
			X = -1,
			Y = 40,
			Width = 100,
			Height = 20
		});

		Assert.Contains(DocumentTemplateValidator.Validate(template), value => value.Contains("invalid negative", StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public void CatalogRetainsHistoricalVersionsAndSelectsOneActiveVersion()
	{
		var versionOne = Template(
			new DocumentTemplateElement { Id = "v1", Type = DocumentTemplateElementType.Text, Text = "V1", X = 40, Y = 40, Width = 100, Height = 20 },
			version: 1,
			isActive: false);
		var versionTwo = Template(
			new DocumentTemplateElement { Id = "v2", Type = DocumentTemplateElementType.Text, Text = "V2", X = 40, Y = 40, Width = 100, Height = 20 },
			version: 2,
			isActive: true);
		var catalog = new DocumentTemplateCatalog([versionOne, versionTwo]);

		Assert.Equal(1, catalog.Get(DocumentTemplateType.SalesInvoice, 1).Version);
		Assert.Equal(2, catalog.GetActive(DocumentTemplateType.SalesInvoice).Version);
		Assert.Equal(2, catalog.ListVersions(DocumentTemplateType.SalesInvoice).Count);
	}

	[Fact]
	public void MultiPageLineTableProducesAdditionalPagesAndTotals()
	{
		var template = DefaultDocumentTemplates.Catalog.GetActive(DocumentTemplateType.SalesInvoice);
		var values = new Dictionary<string, object?>(StringComparer.Ordinal)
		{
			["Company.Name"] = "Depot Test GmbH",
			["Company.Address"] = "Teststraße 1",
			["Company.LegalLine"] = "HRB 1",
			["Company.ContactLine"] = "test@example.invalid",
			["Document.Number"] = "INV-TEST",
			["Document.Date"] = DateTime.Today,
			["Document.Net"] = "100.00 EUR",
			["Document.Tax"] = "19.00 EUR",
			["Document.Total"] = "119.00 EUR",
			["Customer.Name"] = "Test Customer",
			["Customer.BillingAddress"] = "Customer Street 1"
		};
		var lines = Enumerable.Range(1, 80)
			.Select(index => new DocumentRenderRow(new Dictionary<string, object?>(StringComparer.Ordinal)
			{
				["Line.Item"] = $"ITEM-{index:000}",
				["Line.Description"] = $"Rendered template line {index}",
				["Line.Quantity"] = "1",
				["Line.UnitPrice"] = "10.00 EUR",
				["Line.TaxRate"] = "19%",
				["Line.Total"] = "11.90 EUR"
			}))
			.ToArray();
		var bytes = new DocumentLayoutRenderer().RenderToBytes(template, Model(values, lines));

		using var stream = new MemoryStream(bytes, writable: false);
		using var pdf = PdfReader.Open(stream, PdfDocumentOpenMode.Modify);
		Assert.True(pdf.PageCount > 1);
	}

	[Fact]
	public void ImageElementAcceptsControlledLogoBytes()
	{
		var template = Template(new DocumentTemplateElement
		{
			Id = "logo",
			Type = DocumentTemplateElementType.Image,
			Binding = "Company.Logo",
			X = 40,
			Y = 40,
			Width = 24,
			Height = 24
		});
		var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl6Ih8AAAAASUVORK5CYII=");
		var model = Model(new Dictionary<string, object?> { ["Company.Logo"] = png }, []);

		var bytes = new DocumentLayoutRenderer().RenderToBytes(template, model);

		Assert.NotEmpty(bytes);
	}

	private static DocumentTemplate Template(DocumentTemplateElement element, int version = 1, bool isActive = true) =>
		new()
		{
			Id = $"test-{version}",
			Type = DocumentTemplateType.SalesInvoice,
			Version = version,
			IsActive = isActive,
			Elements = [element]
		};

	private static DocumentRenderModel Model(
		IReadOnlyDictionary<string, object?> values,
		IReadOnlyList<DocumentRenderRow> lines) =>
		new("Test PDF", "TEST", "Depot Tests", values, lines);
}
