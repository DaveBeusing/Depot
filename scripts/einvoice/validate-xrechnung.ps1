param(
	[string[]]$InvoicePath,
	[string]$FixtureDirectory = "tests/Depot.Tests/Fixtures/ElectronicInvoice",
	[string]$WorkDir = "$PSScriptRoot/.kosit"
)

$ErrorActionPreference = "Stop"
$validatorVersion = "1.6.2"
$configRelease = "2026-01-31"
$configVersion = "3.0.2"

New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null
$validatorJar = Join-Path $WorkDir "validator-$validatorVersion-standalone.jar"
$configZip = Join-Path $WorkDir "validator-config.zip"
$configDir = Join-Path $WorkDir "config"

if (-not (Test-Path $validatorJar)) {
	Invoke-WebRequest "https://github.com/itplr-kosit/validator/releases/download/v$validatorVersion/validator-$validatorVersion-standalone.jar" -OutFile $validatorJar
}
if (-not (Test-Path $configDir)) {
	Invoke-WebRequest "https://github.com/itplr-kosit/validator-configuration-xrechnung/releases/download/v$configRelease/xrechnung-$configVersion-validator-configuration-$configRelease.zip" -OutFile $configZip
	Expand-Archive $configZip -DestinationPath $configDir -Force
}

$scenario = Get-ChildItem -Path $configDir -Filter "scenarios.xml" -Recurse | Select-Object -First 1
if (-not (Test-Path $validatorJar) -or -not $scenario) {
	throw "KoSIT validator assets are incomplete."
}

if ($InvoicePath) {
	$invoiceFiles = @(
		$InvoicePath | ForEach-Object {
			Get-Item (Resolve-Path $_).Path
		}
	)
}
else {
	$invoiceFiles = @(
		Get-ChildItem -Path $FixtureDirectory -Filter "xrechnung-cii-*.xml" -File |
			Sort-Object Name
	)
}

if ($invoiceFiles.Count -eq 0) {
	throw "No XRechnung conformance fixtures were found."
}

foreach ($invoice in $invoiceFiles) {
	Write-Host "Validating XRechnung conformance fixture: $($invoice.Name)"
	& java -jar $validatorJar -s $scenario.FullName -r $scenario.DirectoryName -h $invoice.FullName
	if ($LASTEXITCODE -ne 0) {
		throw "KoSIT validation failed for $($invoice.Name) with exit code $LASTEXITCODE."
	}
}

Write-Host "XRechnung conformance matrix succeeded for $($invoiceFiles.Count) fixture(s) with KoSIT Validator $validatorVersion / XRechnung $configVersion ($configRelease)."
