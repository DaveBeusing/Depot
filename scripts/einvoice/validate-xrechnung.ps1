param(
    [string]$InvoicePath = "tests/Depot.Tests/Fixtures/ElectronicInvoice/xrechnung-cii-basic.xml",
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
if (-not (Test-Path $validatorJar) -or -not $scenario) { throw "KoSIT validator assets are incomplete." }

$invoiceFull = (Resolve-Path $InvoicePath).Path
& java -jar $validatorJar -s $scenario.FullName -r $scenario.DirectoryName -h $invoiceFull
if ($LASTEXITCODE -ne 0) { throw "KoSIT validation failed with exit code $LASTEXITCODE." }

$report = Get-ChildItem -Path (Split-Path $invoiceFull) -Filter "*-report.xml" | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
if (-not $report) { throw "KoSIT validation report was not generated." }
[xml]$xml = Get-Content $report.FullName
$failed = Select-Xml -Xml $xml -XPath "//*[local-name()='assessment'][@accept='false'] | //*[local-name()='assessment'][@valid='false'] | //*[local-name()='error']"
if ($failed.Count -gt 0) { throw "KoSIT report contains validation errors: $($report.FullName)" }
Write-Host "XRechnung validation succeeded: $($report.FullName)"
