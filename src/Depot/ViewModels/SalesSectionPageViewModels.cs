// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Commands;
using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels;

public abstract class SalesSectionPageViewModel : BaseViewModel, IDisposable
{
	protected SalesSectionPageViewModel(SalesViewModel workspace, SalesSection section)
	{
		Workspace = workspace;
		Section = section;
		Workspace.Section = section;
	}

	public SalesViewModel Workspace { get; }
	public SalesSection Section { get; }

	public virtual Task LoadAsync(CancellationToken cancellationToken = default)
	{
		Workspace.Section = Section;
		return Workspace.LoadAsync(cancellationToken);
	}

	public virtual Task RefreshAsync(CancellationToken cancellationToken = default) => LoadAsync(cancellationToken);

	public bool HasUnsavedChanges()
	{
		Workspace.Section = Section;
		return Workspace.HasUnsavedChanges();
	}

	public void DiscardUnsavedChanges()
	{
		Workspace.Section = Section;
		Workspace.DiscardUnsavedChanges();
	}

	public virtual void Dispose() => Workspace.Dispose();
}

public sealed class SalesOverviewViewModel(SalesViewModel workspace) : SalesSectionPageViewModel(workspace, SalesSection.Overview);
public sealed class CustomersViewModel(SalesViewModel workspace) : SalesSectionPageViewModel(workspace, SalesSection.Customers);
public sealed class SalesOrdersViewModel(SalesViewModel workspace) : SalesSectionPageViewModel(workspace, SalesSection.SalesOrders);
public sealed class SalesApprovalsViewModel(SalesViewModel workspace) : SalesSectionPageViewModel(workspace, SalesSection.Approvals);
public sealed class ShippingViewModel(SalesViewModel workspace) : SalesSectionPageViewModel(workspace, SalesSection.Shipping);

public sealed class SalesInvoicesViewModel : SalesSectionPageViewModel
{
	private readonly SalesInvoiceService _invoices;
	private readonly IFileDialogService _fileDialogs;
	private readonly SalesDocumentService _documents;
	private SalesInvoiceLine? _selectedInvoiceLine;
	private int _creditQuantity = 1;

	public SalesInvoicesViewModel(SalesViewModel workspace, SalesInvoiceService invoices, IFileDialogService fileDialogs, SalesDocumentService documents)
		: base(workspace, SalesSection.Invoices)
	{
		_invoices = invoices;
		_fileDialogs = fileDialogs;
		_documents = documents;
		CreatePartialCreditNoteCommand = new AsyncRelayCommand(CreatePartialCreditNoteAsync, CanCreatePartialCreditNote);
		CreditNotePdfCommand = new RelayCommand(CreateCreditNotePdf, () => Workspace.SelectedCreditNote is not null && Workspace.SelectedInvoice is not null);
	}

	public SalesInvoiceLine? SelectedInvoiceLine
	{
		get => _selectedInvoiceLine;
		set { if (_selectedInvoiceLine == value) return; _selectedInvoiceLine = value; OnPropertyChanged(); CreatePartialCreditNoteCommand.RaiseCanExecuteChanged(); }
	}

	public int CreditQuantity
	{
		get => _creditQuantity;
		set { if (_creditQuantity == value) return; _creditQuantity = value; OnPropertyChanged(); CreatePartialCreditNoteCommand.RaiseCanExecuteChanged(); }
	}

	public decimal CreditedGrossAmount => Workspace.SelectedInvoice is null ? 0m : Workspace.CreditNotes.Where(note => note.SalesInvoiceId == Workspace.SelectedInvoice.Id && note.Status == SalesCreditNoteStatus.Posted).Sum(note => note.GrossAmount);
	public decimal EffectiveGrossAmount => Math.Max(0m, (Workspace.SelectedInvoice?.GrossAmount ?? 0m) - CreditedGrossAmount);

	public AsyncRelayCommand CreatePartialCreditNoteCommand { get; }
	public RelayCommand CreditNotePdfCommand { get; }

	public override async Task LoadAsync(CancellationToken cancellationToken = default)
	{
		await base.LoadAsync(cancellationToken);
		SelectedInvoiceLine = Workspace.SelectedInvoice?.Lines.FirstOrDefault();
		OnPropertyChanged(nameof(CreditedGrossAmount));
		OnPropertyChanged(nameof(EffectiveGrossAmount));
		CreatePartialCreditNoteCommand.RaiseCanExecuteChanged();
		CreditNotePdfCommand.RaiseCanExecuteChanged();
	}

	private bool CanCreatePartialCreditNote() =>
		_invoices.CanCreateCreditNote &&
		Workspace.SelectedInvoice?.Status == SalesInvoiceStatus.Posted &&
		SelectedInvoiceLine is not null &&
		CreditQuantity > 0 &&
		CreditQuantity <= SelectedInvoiceLine.Quantity &&
		!string.IsNullOrWhiteSpace(Workspace.CorrectionReason);

	private async Task CreatePartialCreditNoteAsync(CancellationToken token)
	{
		if (Workspace.SelectedInvoice is null || SelectedInvoiceLine is null) return;
		Workspace.SelectedCreditNote = await _invoices.CreateCreditNoteAsync(
			Workspace.SelectedInvoice.Id,
			[new SalesCreditRequest(SelectedInvoiceLine.Id, CreditQuantity)],
			Workspace.CorrectionReason,
			token);
		Workspace.CorrectionReason = string.Empty;
		await LoadAsync(token);
	}

	private void CreateCreditNotePdf()
	{
		if (Workspace.SelectedCreditNote is null || Workspace.SelectedInvoice is null) return;
		var path = _fileDialogs.ShowSaveFile(new SaveFileDialogRequest("Save credit note", "PDF document (*.pdf)|*.pdf", ".pdf", $"{Workspace.SelectedCreditNote.CreditNoteNumber}.pdf"));
		if (path is not null) _documents.CreateCreditNote(path, Workspace.SelectedCreditNote, Workspace.SelectedInvoice);
	}

	public override void Dispose()
	{
		CreatePartialCreditNoteCommand.Dispose();
		base.Dispose();
	}
}
