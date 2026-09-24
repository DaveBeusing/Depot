// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using Depot.Commands;
using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels;

public sealed partial class FinanceBankingViewModel
{
	private FinanceSepaPaymentExportService _sepa = null!;
	private IFileDialogService _sepaFileDialogs = null!;
	private FinanceSepaPaymentExportSummary? _selectedSepaExport;
	private FinanceSepaPaymentExportPreview? _sepaPreview;
	private long _debtorProfileVersion = 1;
	private long _creditorProfileVersion = 1;
	private string _debtorName=string.Empty,_debtorStreet=string.Empty,_debtorBuilding=string.Empty,_debtorPostalCode=string.Empty,_debtorTown=string.Empty,_debtorSubdivision=string.Empty,_debtorCountry="DE";
	private string _creditorSupplierId=string.Empty,_creditorName=string.Empty,_creditorIban=string.Empty,_creditorBic=string.Empty,_creditorStreet=string.Empty,_creditorBuilding=string.Empty,_creditorPostalCode=string.Empty,_creditorTown=string.Empty,_creditorSubdivision=string.Empty,_creditorCountry="DE";
	private bool _creditorActive=true;
	private string _sepaExternalReference=string.Empty,_sepaEvidenceNote=string.Empty;
	private FinanceSepaPaymentExportStatus _sepaTargetStatus=FinanceSepaPaymentExportStatus.SubmittedExternally;

	public ObservableCollection<FinanceSepaPaymentExportSummary> SepaExports { get; } = [];
	public IReadOnlyList<FinanceSepaPaymentExportStatus> SepaManualStatuses { get; } =
	[
		FinanceSepaPaymentExportStatus.SubmittedExternally,
		FinanceSepaPaymentExportStatus.Accepted,
		FinanceSepaPaymentExportStatus.Rejected,
		FinanceSepaPaymentExportStatus.Cancelled
	];

	public AsyncRelayCommand SaveSepaDebtorProfileCommand { get; private set; } = null!;
	public AsyncRelayCommand LoadSepaCreditorProfileCommand { get; private set; } = null!;
	public AsyncRelayCommand SaveSepaCreditorProfileCommand { get; private set; } = null!;
	public AsyncRelayCommand PreviewSepaExportCommand { get; private set; } = null!;
	public AsyncRelayCommand GenerateSepaExportCommand { get; private set; } = null!;
	public AsyncRelayCommand SupersedeSepaExportCommand { get; private set; } = null!;
	public AsyncRelayCommand DownloadSepaExportCommand { get; private set; } = null!;
	public AsyncRelayCommand UpdateSepaExportStatusCommand { get; private set; } = null!;

	public bool CanManageSepaProfiles => _sepa.CanManageProfiles;
	public bool CanGenerateSepaExports => _sepa.CanGenerate;
	public bool CanDownloadSepaExports => _sepa.CanDownload;
	public bool CanManageSepaStatus => _sepa.CanManageStatus;

	public FinanceSepaPaymentExportSummary? SelectedSepaExport { get=>_selectedSepaExport; set { if(ReferenceEquals(_selectedSepaExport,value))return;_selectedSepaExport=value;OnPropertyChanged(); } }
	public FinanceSepaPaymentExportPreview? SepaPreview { get=>_sepaPreview; private set { if(ReferenceEquals(_sepaPreview,value))return;_sepaPreview=value;OnPropertyChanged();OnPropertyChanged(nameof(SepaPreviewText)); } }
	public string SepaPreviewText => SepaPreview is null ? "Validation has not been run." : SepaPreview.IsValid ? $"Ready: {SepaPreview.TransactionCount:N0} transfer(s), {SepaPreview.ControlSum:N2} EUR." : string.Join(Environment.NewLine,SepaPreview.Errors);

	public string DebtorName { get=>_debtorName; set=>Set(ref _debtorName,value); }
	public string DebtorStreet { get=>_debtorStreet; set=>Set(ref _debtorStreet,value); }
	public string DebtorBuilding { get=>_debtorBuilding; set=>Set(ref _debtorBuilding,value); }
	public string DebtorPostalCode { get=>_debtorPostalCode; set=>Set(ref _debtorPostalCode,value); }
	public string DebtorTown { get=>_debtorTown; set=>Set(ref _debtorTown,value); }
	public string DebtorSubdivision { get=>_debtorSubdivision; set=>Set(ref _debtorSubdivision,value); }
	public string DebtorCountry { get=>_debtorCountry; set=>Set(ref _debtorCountry,value); }

	public string CreditorSupplierId { get=>_creditorSupplierId; set=>Set(ref _creditorSupplierId,value); }
	public string CreditorName { get=>_creditorName; set=>Set(ref _creditorName,value); }
	public string CreditorIban { get=>_creditorIban; set=>Set(ref _creditorIban,value); }
	public string CreditorBic { get=>_creditorBic; set=>Set(ref _creditorBic,value); }
	public string CreditorStreet { get=>_creditorStreet; set=>Set(ref _creditorStreet,value); }
	public string CreditorBuilding { get=>_creditorBuilding; set=>Set(ref _creditorBuilding,value); }
	public string CreditorPostalCode { get=>_creditorPostalCode; set=>Set(ref _creditorPostalCode,value); }
	public string CreditorTown { get=>_creditorTown; set=>Set(ref _creditorTown,value); }
	public string CreditorSubdivision { get=>_creditorSubdivision; set=>Set(ref _creditorSubdivision,value); }
	public string CreditorCountry { get=>_creditorCountry; set=>Set(ref _creditorCountry,value); }
	public bool CreditorActive { get=>_creditorActive; set { if(_creditorActive==value)return;_creditorActive=value;OnPropertyChanged(); } }

	public FinanceSepaPaymentExportStatus SepaTargetStatus { get=>_sepaTargetStatus; set { if(_sepaTargetStatus==value)return;_sepaTargetStatus=value;OnPropertyChanged(); } }
	public string SepaExternalReference { get=>_sepaExternalReference; set=>Set(ref _sepaExternalReference,value); }
	public string SepaEvidenceNote { get=>_sepaEvidenceNote; set=>Set(ref _sepaEvidenceNote,value); }

	private void InitializeSepa(FinanceSepaPaymentExportService sepa, IFileDialogService fileDialogs)
	{
		_sepa=sepa;
		_sepaFileDialogs=fileDialogs;
		SaveSepaDebtorProfileCommand=new AsyncRelayCommand(SaveSepaDebtorProfileAsync);
		LoadSepaCreditorProfileCommand=new AsyncRelayCommand(LoadSepaCreditorProfileAsync);
		SaveSepaCreditorProfileCommand=new AsyncRelayCommand(SaveSepaCreditorProfileAsync);
		PreviewSepaExportCommand=new AsyncRelayCommand(PreviewSepaExportAsync);
		GenerateSepaExportCommand=new AsyncRelayCommand(token=>GenerateSepaExportAsync(false,token));
		SupersedeSepaExportCommand=new AsyncRelayCommand(token=>GenerateSepaExportAsync(true,token));
		DownloadSepaExportCommand=new AsyncRelayCommand(DownloadSepaExportAsync);
		UpdateSepaExportStatusCommand=new AsyncRelayCommand(UpdateSepaExportStatusAsync);
	}

	private void OnSepaBankAccountSelected(FinanceBankAccount? value)
	{
		if(value is null){ClearDebtorProfile();return;}
		_ = LoadSepaDebtorProfileAsync(value.Id,CancellationToken.None);
	}

	private void OnSepaPaymentRunSelected(FinancePaymentRun? value)
	{
		SepaPreview=null;
		_ = LoadSepaExportsAsync(CancellationToken.None);
		if(value?.Lines.FirstOrDefault() is { } line) OnSepaPaymentRunLineSelected(line);
	}

	private void OnSepaPaymentRunLineSelected(FinancePaymentRunLine? value)
	{
		if(value is null)return;
		CreditorSupplierId=value.SupplierId.ToString(CultureInfo.InvariantCulture);
		_ = LoadSepaCreditorProfileAsync(CancellationToken.None);
	}

	private async Task LoadSepaDebtorProfileAsync(long bankAccountId,CancellationToken token)
	{
		try
		{
			var value=await _sepa.GetDebtorProfileAsync(bankAccountId,token);
			if(value is null){ClearDebtorProfile();return;}
			_debtorProfileVersion=value.Version;
			DebtorName=value.Name;DebtorStreet=value.StreetName;DebtorBuilding=value.BuildingNumber??string.Empty;DebtorPostalCode=value.PostalCode;DebtorTown=value.TownName;DebtorSubdivision=value.CountrySubdivision??string.Empty;DebtorCountry=value.CountryCode;
		}
		catch(OperationCanceledException) when(token.IsCancellationRequested){}
		catch(Exception exception){FailOperation(exception,"SEPA debtor profile could not be loaded.");}
	}

	private async Task SaveSepaDebtorProfileAsync(CancellationToken token)
	{
		var bank=SelectedBankAccount??throw new InvalidOperationException("Select a bank account first.");
		BeginOperation("Saving SEPA debtor profile...");
		try
		{
			var value=await _sepa.SaveDebtorProfileAsync(new FinanceSepaDebtorProfile{BankAccountId=bank.Id,Version=_debtorProfileVersion,Name=DebtorName,StreetName=DebtorStreet,BuildingNumber=DebtorBuilding,PostalCode=DebtorPostalCode,TownName=DebtorTown,CountrySubdivision=DebtorSubdivision,CountryCode=DebtorCountry},token);
			_debtorProfileVersion=value.Version;
			CompleteOperation(false,"SEPA debtor profile saved.");
		}
		catch(Exception exception){FailOperation(exception,"SEPA debtor profile could not be saved.");}
	}

	private async Task LoadSepaCreditorProfileAsync(CancellationToken token)
	{
		try
		{
			var supplierId=ParseLong(CreditorSupplierId,"supplier");
			var value=await _sepa.GetCreditorProfileAsync(supplierId,token);
			if(value is null){ClearCreditorProfile(keepSupplier:true);return;}
			_creditorProfileVersion=value.Version;CreditorName=value.Name;CreditorIban=value.Iban;CreditorBic=value.Bic??string.Empty;CreditorStreet=value.StreetName;CreditorBuilding=value.BuildingNumber??string.Empty;CreditorPostalCode=value.PostalCode;CreditorTown=value.TownName;CreditorSubdivision=value.CountrySubdivision??string.Empty;CreditorCountry=value.CountryCode;CreditorActive=value.IsActive;
		}
		catch(OperationCanceledException) when(token.IsCancellationRequested){}
		catch(Exception exception){FailOperation(exception,"SEPA creditor profile could not be loaded.");}
	}

	private async Task SaveSepaCreditorProfileAsync(CancellationToken token)
	{
		BeginOperation("Saving SEPA creditor profile...");
		try
		{
			var supplierId=ParseLong(CreditorSupplierId,"supplier");
			var value=await _sepa.SaveCreditorProfileAsync(new FinanceSepaCreditorProfile{SupplierId=supplierId,Version=_creditorProfileVersion,Name=CreditorName,Iban=CreditorIban,Bic=CreditorBic,StreetName=CreditorStreet,BuildingNumber=CreditorBuilding,PostalCode=CreditorPostalCode,TownName=CreditorTown,CountrySubdivision=CreditorSubdivision,CountryCode=CreditorCountry,IsActive=CreditorActive},token);
			_creditorProfileVersion=value.Version;
			CompleteOperation(false,"SEPA creditor profile saved.");
		}
		catch(Exception exception){FailOperation(exception,"SEPA creditor profile could not be saved.");}
	}

	private async Task PreviewSepaExportAsync(CancellationToken token)
	{
		var run=SelectedPaymentRun??throw new InvalidOperationException("Select a payment run first.");
		BeginOperation("Validating SEPA payment export...");
		try
		{
			SepaPreview=await _sepa.PreviewAsync(run.Id,token);
			CompleteOperation(!SepaPreview.IsValid,SepaPreview.IsValid?"SEPA payment export validation passed.":$"SEPA validation found {SepaPreview.Errors.Count:N0} issue(s).");
		}
		catch(Exception exception){FailOperation(exception,"SEPA payment export validation failed.");}
	}

	private async Task GenerateSepaExportAsync(bool supersede,CancellationToken token)
	{
		var run=SelectedPaymentRun??throw new InvalidOperationException("Select a payment run first.");
		if(supersede&&!_sepaFileDialogs.Confirm(new ConfirmationDialogRequest("Supersede SEPA export","Create a new export sequence and retain the previous artifact as superseded evidence?",true)))return;
		BeginOperation(supersede?"Superseding SEPA payment export...":"Generating SEPA payment export...");
		try
		{
			var export=await _sepa.GenerateAsync(run.Id,supersede,token);
			await LoadSepaExportsAsync(token);
			SelectedSepaExport=SepaExports.FirstOrDefault(value=>value.Id==export.Id);
			CompleteOperation(false,$"SEPA export {export.MessageId} retained with SHA-256 {export.XmlSha256}.");
		}
		catch(Exception exception){FailOperation(exception,"SEPA payment export could not be generated.");}
	}

	private async Task DownloadSepaExportAsync(CancellationToken token)
	{
		var selected=SelectedSepaExport??throw new InvalidOperationException("Select a SEPA export first.");
		var path=_sepaFileDialogs.ShowSaveFile(new SaveFileDialogRequest("Save SEPA payment file","XML files (*.xml)|*.xml|All files (*.*)|*.*",".xml",selected.FileName));
		if(string.IsNullOrWhiteSpace(path))return;
		BeginOperation("Saving retained SEPA payment file...");
		try
		{
			var export=await _sepa.DownloadAsync(selected.Id,token);
			await File.WriteAllBytesAsync(path,export.XmlPayload,token);
			await _sepa.RecordDownloadedAsync(selected.Id,token);
			await LoadSepaExportsAsync(token);
			SelectedSepaExport=SepaExports.FirstOrDefault(value=>value.Id==selected.Id);
			CompleteOperation(false,$"Saved exact retained artifact. SHA-256: {export.XmlSha256}");
		}
		catch(Exception exception){FailOperation(exception,"SEPA payment file could not be saved.");}
	}

	private async Task UpdateSepaExportStatusAsync(CancellationToken token)
	{
		var selected=SelectedSepaExport??throw new InvalidOperationException("Select a SEPA export first.");
		BeginOperation("Recording external SEPA status...");
		try
		{
			await _sepa.UpdateStatusAsync(selected.Id,new FinanceSepaPaymentExportStatusUpdate{Status=SepaTargetStatus,ExternalReference=SepaExternalReference,EvidenceNote=SepaEvidenceNote},token);
			await LoadSepaExportsAsync(token);
			SelectedSepaExport=SepaExports.FirstOrDefault(value=>value.Id==selected.Id);
			CompleteOperation(false,"External SEPA status evidence recorded.");
		}
		catch(Exception exception){FailOperation(exception,"External SEPA status could not be recorded.");}
	}

	private async Task LoadSepaExportsAsync(CancellationToken token)
	{
		if(_sepa is null)return;
		try
		{
			var page=await _sepa.SearchExportsAsync(SelectedPaymentRun?.Id,1,100,token);
			Replace(SepaExports,page.Items);
			if(SelectedSepaExport is not null)SelectedSepaExport=SepaExports.FirstOrDefault(value=>value.Id==SelectedSepaExport.Id);
		}
		catch(OperationCanceledException) when(token.IsCancellationRequested){}
		catch(UnauthorizedAccessException){SepaExports.Clear();}
	}

	private void ClearDebtorProfile(){_debtorProfileVersion=1;DebtorName=DebtorStreet=DebtorBuilding=DebtorPostalCode=DebtorTown=DebtorSubdivision=string.Empty;DebtorCountry="DE";}
	private void ClearCreditorProfile(bool keepSupplier){var supplier=CreditorSupplierId;_creditorProfileVersion=1;CreditorName=CreditorIban=CreditorBic=CreditorStreet=CreditorBuilding=CreditorPostalCode=CreditorTown=CreditorSubdivision=string.Empty;CreditorCountry="DE";CreditorActive=true;if(keepSupplier)CreditorSupplierId=supplier;else CreditorSupplierId=string.Empty;}

	private void DisposeSepa()
	{
		SaveSepaDebtorProfileCommand.Dispose();LoadSepaCreditorProfileCommand.Dispose();SaveSepaCreditorProfileCommand.Dispose();PreviewSepaExportCommand.Dispose();GenerateSepaExportCommand.Dispose();SupersedeSepaExportCommand.Dispose();DownloadSepaExportCommand.Dispose();UpdateSepaExportStatusCommand.Dispose();
	}
}
