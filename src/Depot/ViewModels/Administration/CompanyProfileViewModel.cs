// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Commands;
using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels.Administration;

public sealed class CompanyProfileViewModel : BaseViewModel, IDisposable
{
	private readonly CompanyProfileService _service;
	private CompanyProfile _profile = new();

	public CompanyProfileViewModel(CompanyProfileService service)
	{
		_service = service;
		SaveCommand = new AsyncRelayCommand(SaveAsync);
	}

	public AsyncRelayCommand SaveCommand { get; }
	public bool IsComplete => ValidationErrors.Count == 0;
	public IReadOnlyList<string> ValidationErrors => CompanyProfileService.Validate(_profile);
	public IReadOnlyList<string> Recommendations => CompanyProfileService.GetRecommendations(_profile);
	public string ValidationSummary => IsComplete ? "All required legal master data is complete." : string.Join(Environment.NewLine, ValidationErrors);
	public string RecommendationSummary => Recommendations.Count == 0 ? "No additional master-data recommendations." : string.Join(Environment.NewLine, Recommendations.Select(item => "• " + item));
	public string CompletionText => IsComplete ? "Ready for business documents" : $"{ValidationErrors.Count} required item(s) missing or invalid";

	public string LegalName { get => _profile.LegalName; set => Set(value, v => _profile.LegalName = v); }
	public string LegalForm { get => _profile.LegalForm; set => Set(value, v => _profile.LegalForm = v); }
	public string TradingName { get => _profile.TradingName; set => Set(value, v => _profile.TradingName = v); }
	public string Street { get => _profile.Street; set => Set(value, v => _profile.Street = v); }
	public string AddressLine2 { get => _profile.AddressLine2; set => Set(value, v => _profile.AddressLine2 = v); }
	public string PostalCode { get => _profile.PostalCode; set => Set(value, v => _profile.PostalCode = v); }
	public string City { get => _profile.City; set => Set(value, v => _profile.City = v); }
	public new string State { get => _profile.State; set => Set(value, v => _profile.State = v); }
	public string CountryCode { get => _profile.CountryCode; set => SetUpper(value, v => _profile.CountryCode = v); }
	public string TaxResidenceCountryCode { get => _profile.TaxResidenceCountryCode; set => SetUpper(value, v => _profile.TaxResidenceCountryCode = v); }
	public string RegisteredOffice { get => _profile.RegisteredOffice; set => Set(value, v => _profile.RegisteredOffice = v); }
	public bool IsRegisteredEntity { get => _profile.IsRegisteredEntity; set => SetBool(value, v => _profile.IsRegisteredEntity = v); }
	public string RegisterCourt { get => _profile.RegisterCourt; set => Set(value, v => _profile.RegisterCourt = v); }
	public string RegisterType { get => _profile.RegisterType; set => Set(value, v => _profile.RegisterType = v); }
	public string RegisterNumber { get => _profile.RegisterNumber; set => Set(value, v => _profile.RegisterNumber = v); }
	public string LegalEntityIdentifier { get => _profile.LegalEntityIdentifier; set => SetUpper(value, v => _profile.LegalEntityIdentifier = v); }
	public string Gln { get => _profile.Gln; set => Set(value, v => _profile.Gln = v); }
	public string DunsNumber { get => _profile.DunsNumber; set => Set(value, v => _profile.DunsNumber = v); }

	public string ManagingDirectors { get => _profile.ManagingDirectors; set => Set(value, v => _profile.ManagingDirectors = v); }
	public bool HasSupervisoryBoard { get => _profile.HasSupervisoryBoard; set => SetBool(value, v => _profile.HasSupervisoryBoard = v); }
	public string SupervisoryBoardChair { get => _profile.SupervisoryBoardChair; set => Set(value, v => _profile.SupervisoryBoardChair = v); }
	public bool PublishesShareCapital { get => _profile.PublishesShareCapital; set => SetBool(value, v => _profile.PublishesShareCapital = v); }
	public string ShareCapital { get => _profile.ShareCapital; set => Set(value, v => _profile.ShareCapital = v); }
	public string OutstandingCapital { get => _profile.OutstandingCapital; set => Set(value, v => _profile.OutstandingCapital = v); }
	public bool IsInLiquidation { get => _profile.IsInLiquidation; set => SetBool(value, v => _profile.IsInLiquidation = v); }
	public string Liquidators { get => _profile.Liquidators; set => Set(value, v => _profile.Liquidators = v); }
	public bool IsBranch { get => _profile.IsBranch; set => SetBool(value, v => _profile.IsBranch = v); }
	public string BranchName { get => _profile.BranchName; set => Set(value, v => _profile.BranchName = v); }
	public string BranchRegistrationAuthority { get => _profile.BranchRegistrationAuthority; set => Set(value, v => _profile.BranchRegistrationAuthority = v); }
	public string BranchRegistrationNumber { get => _profile.BranchRegistrationNumber; set => Set(value, v => _profile.BranchRegistrationNumber = v); }

	public string TaxNumber { get => _profile.TaxNumber; set => Set(value, v => _profile.TaxNumber = v); }
	public string VatId { get => _profile.VatId; set => SetUpper(value, v => _profile.VatId = v); }
	public string BusinessId { get => _profile.BusinessId; set => Set(value, v => _profile.BusinessId = v); }
	public string AdditionalTaxRegistrations { get => _profile.AdditionalTaxRegistrations; set => SetMultiline(value, v => _profile.AdditionalTaxRegistrations = v); }
	public string OssRegistration { get => _profile.OssRegistration; set => Set(value, v => _profile.OssRegistration = v); }
	public string IossIdentificationNumber { get => _profile.IossIdentificationNumber; set => Set(value, v => _profile.IossIdentificationNumber = v); }
	public bool HasFiscalRepresentative { get => _profile.HasFiscalRepresentative; set => SetBool(value, v => _profile.HasFiscalRepresentative = v); }
	public string FiscalRepresentativeName { get => _profile.FiscalRepresentativeName; set => Set(value, v => _profile.FiscalRepresentativeName = v); }
	public string FiscalRepresentativeVatId { get => _profile.FiscalRepresentativeVatId; set => SetUpper(value, v => _profile.FiscalRepresentativeVatId = v); }
	public string FiscalRepresentativeAddress { get => _profile.FiscalRepresentativeAddress; set => SetMultiline(value, v => _profile.FiscalRepresentativeAddress = v); }

	public string EoriNumber { get => _profile.EoriNumber; set => SetUpper(value, v => _profile.EoriNumber = v); }
	public string RexNumber { get => _profile.RexNumber; set => SetUpper(value, v => _profile.RexNumber = v); }
	public string AeoAuthorizationNumber { get => _profile.AeoAuthorizationNumber; set => SetUpper(value, v => _profile.AeoAuthorizationNumber = v); }
	public string CustomsAccountReference { get => _profile.CustomsAccountReference; set => Set(value, v => _profile.CustomsAccountReference = v); }
	public string DefaultIncoterm { get => _profile.DefaultIncoterm; set => SetUpper(value, v => _profile.DefaultIncoterm = v); }
	public string DefaultIncotermPlace { get => _profile.DefaultIncotermPlace; set => Set(value, v => _profile.DefaultIncotermPlace = v); }
	public string ExporterStatement { get => _profile.ExporterStatement; set => SetMultiline(value, v => _profile.ExporterStatement = v); }

	public string PackagingRegistrationNumber { get => _profile.PackagingRegistrationNumber; set => Set(value, v => _profile.PackagingRegistrationNumber = v); }
	public string WeeeRegistrationNumber { get => _profile.WeeeRegistrationNumber; set => Set(value, v => _profile.WeeeRegistrationNumber = v); }
	public string BatteryRegistrationNumber { get => _profile.BatteryRegistrationNumber; set => Set(value, v => _profile.BatteryRegistrationNumber = v); }
	public string AdditionalRegulatoryRegistrations { get => _profile.AdditionalRegulatoryRegistrations; set => SetMultiline(value, v => _profile.AdditionalRegulatoryRegistrations = v); }
	public string RegulatoryAuthority { get => _profile.RegulatoryAuthority; set => Set(value, v => _profile.RegulatoryAuthority = v); }
	public string ProfessionalTitle { get => _profile.ProfessionalTitle; set => Set(value, v => _profile.ProfessionalTitle = v); }
	public string ProfessionalTitleCountryCode { get => _profile.ProfessionalTitleCountryCode; set => SetUpper(value, v => _profile.ProfessionalTitleCountryCode = v); }
	public string ProfessionalRulesReference { get => _profile.ProfessionalRulesReference; set => Set(value, v => _profile.ProfessionalRulesReference = v); }

	public string Phone { get => _profile.Phone; set => Set(value, v => _profile.Phone = v); }
	public string Email { get => _profile.Email; set => Set(value, v => _profile.Email = v); }
	public string Website { get => _profile.Website; set => Set(value, v => _profile.Website = v); }
	public string InvoiceEmail { get => _profile.InvoiceEmail; set => Set(value, v => _profile.InvoiceEmail = v); }
	public string AccountHolder { get => _profile.AccountHolder; set => Set(value, v => _profile.AccountHolder = v); }
	public string BankName { get => _profile.BankName; set => Set(value, v => _profile.BankName = v); }
	public string Iban { get => _profile.Iban; set => SetUpper(value, v => _profile.Iban = v); }
	public string Bic { get => _profile.Bic; set => SetUpper(value, v => _profile.Bic = v); }
	public string SepaCreditorIdentifier { get => _profile.SepaCreditorIdentifier; set => SetUpper(value, v => _profile.SepaCreditorIdentifier = v); }
	public string DefaultCurrency { get => _profile.DefaultCurrency; set => SetUpper(value, v => _profile.DefaultCurrency = v); }
	public string DefaultLanguage { get => _profile.DefaultLanguage; set => Set(value, v => _profile.DefaultLanguage = v); }
	public int PaymentTermsDays { get => _profile.PaymentTermsDays; set { if (_profile.PaymentTermsDays == value) return; _profile.PaymentTermsDays = value; Changed(); } }
	public string EInvoiceEndpoint { get => _profile.EInvoiceEndpoint; set => Set(value, v => _profile.EInvoiceEndpoint = v); }
	public string EInvoiceEndpointScheme { get => _profile.EInvoiceEndpointScheme; set => Set(value, v => _profile.EInvoiceEndpointScheme = v); }
	public string LeitwegId { get => _profile.LeitwegId; set => Set(value, v => _profile.LeitwegId = v); }
	public string LegalFooterAdditionalText { get => _profile.LegalFooterAdditionalText; set => SetMultiline(value, v => _profile.LegalFooterAdditionalText = v); }

	public async Task LoadAsync(CancellationToken cancellationToken = default)
	{
		BeginOperation("Loading company master data");
		try
		{
			_profile = await _service.LoadAsync(cancellationToken);
			RaiseAll();
			CompleteOperation(false, IsComplete ? "Company master data loaded" : CompletionText);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			FailOperation(exception, "Company master data could not be loaded");
		}
	}

	private async Task SaveAsync(CancellationToken cancellationToken)
	{
		BeginOperation("Saving company master data");
		try
		{
			_profile = await _service.SaveAsync(_profile, cancellationToken);
			RaiseAll();
			CompleteOperation(false, "Company master data saved");
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			FailOperation(exception, "Company master data could not be saved");
		}
	}

	private void Set(string? value, Action<string> setter)
	{
		setter(value?.Trim() ?? string.Empty);
		Changed();
	}

	private void SetUpper(string? value, Action<string> setter) => Set(value?.ToUpperInvariant(), setter);

	private void SetMultiline(string? value, Action<string> setter)
	{
		setter(value?.Trim() ?? string.Empty);
		Changed();
	}

	private void SetBool(bool value, Action<bool> setter)
	{
		setter(value);
		Changed();
	}

	private void Changed()
	{
		OnPropertyChanged(string.Empty);
		OnPropertyChanged(nameof(IsComplete));
		OnPropertyChanged(nameof(ValidationErrors));
		OnPropertyChanged(nameof(Recommendations));
		OnPropertyChanged(nameof(ValidationSummary));
		OnPropertyChanged(nameof(RecommendationSummary));
		OnPropertyChanged(nameof(CompletionText));
	}

	private void RaiseAll() => Changed();

	public void Dispose() => SaveCommand.Dispose();
}
