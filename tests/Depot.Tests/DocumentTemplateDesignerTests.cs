// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Xunit;

using Depot.DocumentRendering;
using Depot.Models;
using Depot.Services;
using Depot.ViewModels.Administration;

namespace Depot.Tests;

public sealed class DocumentTemplateDesignerTests
{
	[Fact]
	public void DesignerRequiresExplicitViewPermission()
	{
		var authorization = Authorization([]);
		var service = new DocumentTemplateDesignerService(
			authorization,
			new DocumentTemplateRuntimeCatalog(DefaultDocumentTemplates.Defaults));

		var exception = Assert.Throws<UnauthorizedAccessException>(() => service.ListVersions(DocumentTemplateType.SalesInvoice));

		Assert.Contains("DocumentTemplates.View", exception.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void DraftActivationPreviewAndDefaultResetUseValidatedVersionCatalog()
	{
		var authorization = Authorization([ApplicationPermission.DocumentTemplatesView, ApplicationPermission.DocumentTemplatesManage]);
		var catalog = new DocumentTemplateRuntimeCatalog(DefaultDocumentTemplates.Defaults);
		var service = new DocumentTemplateDesignerService(authorization, catalog);
		var source = service.GetDefault(DocumentTemplateType.SalesInvoice);
		var candidate = source with { Id = "sales-invoice-custom", Version = 0, IsActive = false };

		var saved = service.SaveDraft(candidate);

		Assert.Equal(2, saved.Version);
		Assert.False(saved.IsActive);
		Assert.Equal(1, catalog.Snapshot.GetActive(DocumentTemplateType.SalesInvoice).Version);

		var preview = service.Preview(saved);
		Assert.NotEmpty(preview);
		Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(preview, 0, 4));

		var activated = service.Activate(DocumentTemplateType.SalesInvoice, saved.Version);
		Assert.True(activated.IsActive);
		Assert.Equal(saved.Version, catalog.Snapshot.GetActive(DocumentTemplateType.SalesInvoice).Version);

		var reset = service.ResetToDefault(DocumentTemplateType.SalesInvoice);
		Assert.Equal(1, reset.Version);
		Assert.Equal(1, catalog.Snapshot.GetActive(DocumentTemplateType.SalesInvoice).Version);
		Assert.Equal(2, service.ListVersions(DocumentTemplateType.SalesInvoice).Count);
	}

	[Fact]
	public void InvalidBindingPreventsDesignerDraftSave()
	{
		var authorization = Authorization([ApplicationPermission.DocumentTemplatesView, ApplicationPermission.DocumentTemplatesManage]);
		var service = new DocumentTemplateDesignerService(
			authorization,
			new DocumentTemplateRuntimeCatalog(DefaultDocumentTemplates.Defaults));
		var viewModel = new DocumentTemplateDesignerViewModel(service);

		viewModel.NewFromDefaultCommand.Execute(null);
		var bound = viewModel.Elements.First(element => element.ModelType == DocumentTemplateElementType.BoundText);
		bound.Binding = "System.Environment.MachineName";

		Assert.Contains(viewModel.ValidationErrors, error => error.Contains("unsupported binding", StringComparison.OrdinalIgnoreCase));
		Assert.False(viewModel.SaveDraftCommand.CanExecute(null));
	}

	[Fact]
	public void DesignerCommandsSaveAndActivateWithoutMutatingDefaultVersion()
	{
		var authorization = Authorization([ApplicationPermission.DocumentTemplatesView, ApplicationPermission.DocumentTemplatesManage]);
		var catalog = new DocumentTemplateRuntimeCatalog(DefaultDocumentTemplates.Defaults);
		var viewModel = new DocumentTemplateDesignerViewModel(new DocumentTemplateDesignerService(authorization, catalog));

		viewModel.SelectedType = DocumentTemplateType.SalesQuote;
		viewModel.NewFromDefaultCommand.Execute(null);
		viewModel.AddElement(DocumentTemplateElementType.Rectangle);

		Assert.True(viewModel.SaveDraftCommand.CanExecute(null));
		viewModel.SaveDraftCommand.Execute(null);
		Assert.Equal(2, viewModel.SelectedVersion?.Version);
		Assert.False(viewModel.SelectedVersion?.IsActive);

		Assert.True(viewModel.ActivateCommand.CanExecute(null));
		viewModel.ActivateCommand.Execute(null);
		Assert.Equal(2, catalog.Snapshot.GetActive(DocumentTemplateType.SalesQuote).Version);

		var original = catalog.GetDefault(DocumentTemplateType.SalesQuote);
		Assert.Equal(1, original.Version);
		Assert.DoesNotContain(original.Elements, element => element.Id.StartsWith("rectangle-", StringComparison.Ordinal));
	}

	[Fact]
	public void ApplicationAdministratorGetsTemplateManagementButBusinessRolesDoNot()
	{
		var applicationAdministrator = SystemRoleCatalog.Definitions.Single(role => role.Code == SystemRoleCatalog.ApplicationAdministratorCode);
		var salesManager = SystemRoleCatalog.Definitions.Single(role => role.Code == SystemRoleCatalog.SalesManagerCode);
		var finance = SystemRoleCatalog.Definitions.Single(role => role.Code == SystemRoleCatalog.FinanceCode);

		Assert.Contains(ApplicationPermission.DocumentTemplatesView, applicationAdministrator.Permissions);
		Assert.Contains(ApplicationPermission.DocumentTemplatesManage, applicationAdministrator.Permissions);
		Assert.DoesNotContain(ApplicationPermission.DocumentTemplatesView, salesManager.Permissions);
		Assert.DoesNotContain(ApplicationPermission.DocumentTemplatesManage, salesManager.Permissions);
		Assert.DoesNotContain(ApplicationPermission.DocumentTemplatesView, finance.Permissions);
		Assert.DoesNotContain(ApplicationPermission.DocumentTemplatesManage, finance.Permissions);
	}

	[Fact]
	public void BorderPropertyParticipatesInDeterministicTemplateSerialization()
	{
		var source = DefaultDocumentTemplates.GetDefault(DocumentTemplateType.SalesInvoice);
		var element = source.Elements.First(element => element.Type == DocumentTemplateElementType.BoundText);
		var changed = source with
		{
			Elements = source.Elements.Select(value => value.Id == element.Id ? value with { Border = true } : value).ToArray()
		};

		var json = DocumentTemplateSerializer.Serialize(changed);

		Assert.Contains("\"Border\":true", json, StringComparison.Ordinal);
		Assert.Equal(json, DocumentTemplateSerializer.Serialize(changed));
	}

	[Fact]
	public void DesignerViewKeepsKeyboardAndAutomationAccessVisible()
	{
		var root = FindRepositoryRoot();
		var xaml = File.ReadAllText(Path.Combine(root, "src", "Depot", "Views", "Administration", "DocumentTemplateDesignerView.xaml"));
		var codeBehind = File.ReadAllText(Path.Combine(root, "src", "Depot", "Views", "Administration", "DocumentTemplateDesignerView.xaml.cs"));

		Assert.Contains("SelectionMode=\"Extended\"", xaml, StringComparison.Ordinal);
		Assert.Contains("AutomationProperties.Name", xaml, StringComparison.Ordinal);
		Assert.Contains("PreviewKeyDown=\"CanvasElements_PreviewKeyDown\"", xaml, StringComparison.Ordinal);
		Assert.Contains("Key.Left", codeBehind, StringComparison.Ordinal);
		Assert.Contains("Key.Delete", codeBehind, StringComparison.Ordinal);
		Assert.Contains("ModifierKeys.Control", codeBehind, StringComparison.Ordinal);
	}

	private static string FindRepositoryRoot()
	{
		for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
		{
			if (File.Exists(Path.Combine(directory.FullName, "Depot.slnx"))) return directory.FullName;
		}
		throw new DirectoryNotFoundException("Repository root could not be located.");
	}

	private static AuthorizationService Authorization(IEnumerable<ApplicationPermission> permissions)
	{
		var authorization = new AuthorizationService();
		authorization.SignIn(
			new User { Id = 100, Email = "designer@test.local", DisplayName = "Designer Test", IsActive = true },
			permissions);
		return authorization;
	}
}
