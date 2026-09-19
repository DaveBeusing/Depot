// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Xunit;

using Depot.DocumentRendering;
using Depot.Models;
using Depot.Services;
using Depot.ViewModels;
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


	[Fact]
	public void DesignerDirtyStateParticipatesInDiscardGuardAndUndoRedoHistory()
	{
		var authorization = Authorization([ApplicationPermission.DocumentTemplatesView, ApplicationPermission.DocumentTemplatesManage]);
		var viewModel = new DocumentTemplateDesignerViewModel(
			new DocumentTemplateDesignerService(authorization, new DocumentTemplateRuntimeCatalog(DefaultDocumentTemplates.Defaults)));

		viewModel.SelectedType = DocumentTemplateType.SalesQuote;
		viewModel.NewFromDefaultCommand.Execute(null);

		Assert.True(viewModel.IsDirty);
		Assert.True(viewModel.IsEditing);
		Assert.True(UnsavedChangesGuard.TryGet(viewModel, out var changes));
		Assert.Equal("document template", changes?.Name);

		viewModel.SnapToGrid = false;
		var original = viewModel.Elements.First();
		var elementId = original.Id;
		var startX = original.X;
		viewModel.SetSelectedElements([original]);
		viewModel.MoveSelection(7, 0);

		Assert.True(viewModel.UndoCommand.CanExecute(null));
		viewModel.UndoCommand.Execute(null);
		Assert.Equal(startX, viewModel.Elements.Single(element => element.Id == elementId).X, 3);
		Assert.True(viewModel.RedoCommand.CanExecute(null));

		viewModel.RedoCommand.Execute(null);
		Assert.Equal(startX + 7, viewModel.Elements.Single(element => element.Id == elementId).X, 3);
		Assert.True(viewModel.IsDirty);

		changes!.Discard();

		Assert.False(viewModel.IsDirty);
		Assert.False(viewModel.IsEditing);
		Assert.False(UnsavedChangesGuard.TryGet(viewModel, out _));
	}

	[Fact]
	public void ResizeHandlesRespectPageBoundsMinimumSizeAndGridSnap()
	{
		var authorization = Authorization([ApplicationPermission.DocumentTemplatesView, ApplicationPermission.DocumentTemplatesManage]);
		var viewModel = new DocumentTemplateDesignerViewModel(
			new DocumentTemplateDesignerService(authorization, new DocumentTemplateRuntimeCatalog(DefaultDocumentTemplates.Defaults)));
		viewModel.NewFromDefaultCommand.Execute(null);
		viewModel.SnapToGrid = false;

		var element = viewModel.Elements.First(value => value.ModelType == DocumentTemplateElementType.Text);
		viewModel.ResizeElement(element, "NW", -1000, -1000);
		Assert.Equal(0, element.X, 3);
		Assert.Equal(0, element.Y, 3);
		Assert.True(element.Width >= 4);
		Assert.True(element.Height >= 4);

		viewModel.ResizeElement(element, "SE", 2000, 2000);
		Assert.True(element.X + element.Width <= viewModel.PageWidth + 0.001);
		Assert.True(element.Y + element.Height <= viewModel.PageHeight + 0.001);

		var second = viewModel.Elements.First(value => value.ModelType == DocumentTemplateElementType.BoundText);
		second.SetBounds(40, 100, 180, 24);
		viewModel.GridSize = 10;
		viewModel.SnapToGrid = true;
		viewModel.ResizeElement(second, "E", 7, 0);

		Assert.Equal(230, second.X + second.Width, 3);
	}

	[Fact]
	public void AlignmentDistributionAndSizeCommandsRequireMeaningfulSelections()
	{
		var authorization = Authorization([ApplicationPermission.DocumentTemplatesView, ApplicationPermission.DocumentTemplatesManage]);
		var viewModel = new DocumentTemplateDesignerViewModel(
			new DocumentTemplateDesignerService(authorization, new DocumentTemplateRuntimeCatalog(DefaultDocumentTemplates.Defaults)));
		viewModel.NewFromDefaultCommand.Execute(null);
		viewModel.SnapToGrid = false;

		var selected = viewModel.Elements.Take(3).ToArray();
		selected[0].SetBounds(10, 40, 50, 20);
		selected[1].SetBounds(250, 90, 50, 30);
		selected[2].SetBounds(500, 140, 50, 40);

		viewModel.SetSelectedElements([selected[0]]);
		Assert.False(viewModel.AlignLeftCommand.CanExecute(null));
		Assert.False(viewModel.DistributeHorizontallyCommand.CanExecute(null));

		viewModel.SetSelectedElements(selected);
		Assert.True(viewModel.AlignTopCommand.CanExecute(null));
		Assert.True(viewModel.DistributeHorizontallyCommand.CanExecute(null));
		Assert.True(viewModel.SameHeightCommand.CanExecute(null));

		viewModel.DistributeHorizontallyCommand.Execute(null);
		Assert.Equal(10, selected[0].X, 3);
		Assert.Equal(255, selected[1].X, 3);
		Assert.Equal(500, selected[2].X, 3);

		viewModel.AlignTopCommand.Execute(null);
		Assert.All(selected, element => Assert.Equal(40, element.Y, 3));

		viewModel.SameHeightCommand.Execute(null);
		Assert.Single(selected.Select(element => Math.Round(element.Height, 3)).Distinct());
	}

	[Fact]
	public void PropertyInspectorOnlyExposesPropertiesValidForSelectedElementType()
	{
		var authorization = Authorization([ApplicationPermission.DocumentTemplatesView, ApplicationPermission.DocumentTemplatesManage]);
		var viewModel = new DocumentTemplateDesignerViewModel(
			new DocumentTemplateDesignerService(authorization, new DocumentTemplateRuntimeCatalog(DefaultDocumentTemplates.Defaults)));
		viewModel.NewFromDefaultCommand.Execute(null);

		var text = viewModel.Elements.First(element => element.ModelType == DocumentTemplateElementType.Text);
		viewModel.SetSelectedElements([text]);
		Assert.True(viewModel.ShowTextProperty);
		Assert.True(viewModel.ShowTypographyProperties);
		Assert.False(viewModel.ShowBindingProperty);
		Assert.False(viewModel.ShowRowHeightProperty);

		var bound = viewModel.Elements.First(element => element.ModelType == DocumentTemplateElementType.BoundText);
		viewModel.SetSelectedElements([bound]);
		Assert.True(viewModel.ShowBindingProperty);
		Assert.True(viewModel.ShowFormatProperty);
		Assert.False(viewModel.ShowTextProperty);

		var line = viewModel.Elements.First(element => element.ModelType == DocumentTemplateElementType.Line);
		viewModel.SetSelectedElements([line]);
		Assert.False(viewModel.ShowBindingProperty);
		Assert.False(viewModel.ShowTypographyProperties);
		Assert.False(viewModel.ShowPlacementProperty);

		var table = viewModel.Elements.First(element => element.ModelType == DocumentTemplateElementType.LineTable);
		viewModel.SetSelectedElements([table]);
		Assert.True(viewModel.ShowRowHeightProperty);
		Assert.True(viewModel.ShowLineTableHint);
		Assert.False(viewModel.ShowBindingProperty);
	}

	[Fact]
	public void LifecycleActionsUseDistinctDraftCopySemanticsAndPreviewDoesNotClearDirtyState()
	{
		var authorization = Authorization([ApplicationPermission.DocumentTemplatesView, ApplicationPermission.DocumentTemplatesManage]);
		var catalog = new DocumentTemplateRuntimeCatalog(DefaultDocumentTemplates.Defaults);
		var service = new DocumentTemplateDesignerService(authorization, catalog);
		var viewModel = new DocumentTemplateDesignerViewModel(service);

		Assert.NotSame(viewModel.DuplicateCommand, viewModel.CopySelectedVersionAsDraftCommand);
		Assert.True(viewModel.CopySelectedVersionAsDraftCommand.CanExecute(null));
		viewModel.CopySelectedVersionAsDraftCommand.Execute(null);

		Assert.True(viewModel.IsEditing);
		Assert.True(viewModel.IsDirty);
		Assert.True(viewModel.TemplateName.EndsWith("-draft-copy", StringComparison.Ordinal));

		var activeBeforePreview = catalog.Snapshot.GetActive(viewModel.SelectedType);
		var preview = service.Preview(new DocumentTemplate
		{
			Id = viewModel.TemplateName,
			Type = viewModel.SelectedType,
			Version = 1,
			PageWidth = viewModel.PageWidth,
			PageHeight = viewModel.PageHeight,
			Elements = viewModel.Elements.Select(element => element.ToModel()).ToArray()
		});

		Assert.NotEmpty(preview);
		Assert.True(viewModel.IsDirty);
		Assert.Equal(activeBeforePreview.Version, catalog.Snapshot.GetActive(viewModel.SelectedType).Version);
	}

	[Fact]
	public void ProfessionalEditingViewKeepsKeyboardAccessibilityToolbarAndDirtyMarkerContracts()
	{
		var root = FindRepositoryRoot();
		var xaml = File.ReadAllText(Path.Combine(root, "src", "Depot", "Views", "Administration", "DocumentTemplateDesignerView.xaml"));
		var codeBehind = File.ReadAllText(Path.Combine(root, "src", "Depot", "Views", "Administration", "DocumentTemplateDesignerView.xaml.cs"));
		var shell = File.ReadAllText(Path.Combine(root, "src", "Depot", "Resources", "Shell.xaml"));
		var guard = File.ReadAllText(Path.Combine(root, "src", "Depot", "ViewModels", "UnsavedChangesGuard.cs"));

		Assert.Contains("Text=\"{Binding Title}\"", xaml, StringComparison.Ordinal);
		Assert.Contains("Text=\"{Binding Subtitle}\"", xaml, StringComparison.Ordinal);
		Assert.Contains("Key=\"Z\" Modifiers=\"Control\"", xaml, StringComparison.Ordinal);
		Assert.Contains("Key=\"Y\" Modifiers=\"Control\"", xaml, StringComparison.Ordinal);
		Assert.Contains("Copy selected version as draft", xaml, StringComparison.Ordinal);
		Assert.DoesNotContain("Revert as Draft", xaml, StringComparison.Ordinal);
		Assert.Contains("AlignLeftCommand", xaml, StringComparison.Ordinal);
		Assert.Contains("DistributeHorizontallyCommand", xaml, StringComparison.Ordinal);
		Assert.Contains("ResizeThumb_DragDelta", xaml, StringComparison.Ordinal);
		Assert.Contains("AutomationProperties.Name=\"Undo designer change\"", xaml, StringComparison.Ordinal);
		Assert.Contains("viewModel.ResizeElement", codeBehind, StringComparison.Ordinal);
		Assert.Contains("CurrentViewModel.CurrentViewModel.IsDirty", shell, StringComparison.Ordinal);
		Assert.Contains("AutomationProperties.Name=\"Unsaved changes\"", shell, StringComparison.Ordinal);
		Assert.Contains("DocumentTemplateDesignerViewModel designer when designer.IsDirty", guard, StringComparison.Ordinal);
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
