# Copyright (c) 2026 David Beusing
# Licensed under the MIT License.

param(
	[string[]]$PdfPath,
	[string]$FixtureDirectory = "artifacts/einvoice/zugferd",
	[string]$EvidenceDirectory = "artifacts/einvoice/validation",
	[string]$WorkDir = "$PSScriptRoot/.verapdf"
)

$ErrorActionPreference = "Stop"
$validatorVersion = "1.30.2"
$validatorSeries = "1.30"
$installerSha256 = "6cc6341cb1af644044054b81f00a6590a7918abb18f762243de115258bcad838"
$installerUri = "https://software.verapdf.org/rel/$validatorSeries/verapdf-greenfield-$validatorVersion-installer.zip"

function Assert-InstallerHash([string]$Path) {
	$actual = (Get-FileHash -Path $Path -Algorithm SHA256).Hash.ToLowerInvariant()
	if ($actual -ne $installerSha256) {
		throw "veraPDF installer SHA-256 mismatch. Expected $installerSha256 but got $actual."
	}
}

function Assert-ZeroBatchValue($Node, [string]$Name, [string]$PdfName) {
	if (-not $Node) { return }
	$attribute = $Node.Attributes[$Name]
	if ($attribute -and [int]$attribute.Value -ne 0) {
		throw "veraPDF reported $Name=$($attribute.Value) for $PdfName."
	}
}

New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null
New-Item -ItemType Directory -Force -Path $EvidenceDirectory | Out-Null
$installerZip = Join-Path $WorkDir "verapdf-greenfield-$validatorVersion-installer.zip"
$packageDir = Join-Path $WorkDir "package"
$installDir = Join-Path $WorkDir "install"
$veraPdf = Join-Path $installDir "verapdf.bat"

if (-not (Test-Path $installerZip)) {
	Write-Host "Downloading pinned veraPDF $validatorVersion installer."
	Invoke-WebRequest $installerUri -OutFile $installerZip
}
Assert-InstallerHash $installerZip

if (-not (Test-Path $veraPdf)) {
	Remove-Item $packageDir -Recurse -Force -ErrorAction SilentlyContinue
	Remove-Item $installDir -Recurse -Force -ErrorAction SilentlyContinue
	New-Item -ItemType Directory -Force -Path $packageDir | Out-Null
	Expand-Archive $installerZip -DestinationPath $packageDir -Force
	$installerJar = Join-Path $packageDir "verapdf-greenfield-$validatorVersion/verapdf-izpack-installer-$validatorVersion.jar"
	if (-not (Test-Path $installerJar)) {
		throw "Pinned veraPDF installer archive does not contain the expected installer JAR."
	}

	$escapedInstallDir = [System.Security.SecurityElement]::Escape((Resolve-Path (Split-Path $installDir -Parent)).Path + "\" + (Split-Path $installDir -Leaf))
	$autoInstallPath = Join-Path $WorkDir "auto-install.xml"
	$autoInstall = @"
<AutomatedInstallation langpack="eng">
<com.izforge.izpack.panels.htmlhello.HTMLHelloPanel id="welcome"/>
<com.izforge.izpack.panels.target.TargetPanel id="install_dir">
<installpath>$escapedInstallDir</installpath>
</com.izforge.izpack.panels.target.TargetPanel>
<com.izforge.izpack.panels.packs.PacksPanel id="sdk_pack_select">
<pack index="0" name="veraPDF GUI" selected="false"/>
<pack index="1" name="veraPDF CLI" selected="true"/>
<pack index="2" name="veraPDF Documentation" selected="false"/>
<pack index="3" name="veraPDF Sample Plugins" selected="false"/>
</com.izforge.izpack.panels.packs.PacksPanel>
<com.izforge.izpack.panels.install.InstallPanel id="install"/>
<com.izforge.izpack.panels.finish.FinishPanel id="finish"/>
</AutomatedInstallation>
"@
	[System.IO.File]::WriteAllText($autoInstallPath, $autoInstall, [System.Text.UTF8Encoding]::new($false))
	& java -jar $installerJar $autoInstallPath
	if ($LASTEXITCODE -ne 0) {
		throw "veraPDF unattended installation failed with exit code $LASTEXITCODE."
	}
}

if (-not (Test-Path $veraPdf)) {
	throw "veraPDF CLI was not installed at the expected path '$veraPdf'."
}

$versionOutput = (& $veraPdf --version 2>&1 | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $versionOutput -notmatch [regex]::Escape($validatorVersion)) {
	throw "Pinned veraPDF version check failed. Expected $validatorVersion; output was '$versionOutput'."
}

if ($PdfPath) {
	$pdfFiles = @($PdfPath | ForEach-Object { Get-Item (Resolve-Path $_).Path })
}
else {
	$pdfFiles = @(Get-ChildItem -Path $FixtureDirectory -Filter "zugferd-facturx-*.pdf" -File | Sort-Object Name)
}
if ($pdfFiles.Count -eq 0) {
	throw "No ZUGFeRD/Factur-X conformance PDFs were found."
}

$validated = @()
foreach ($pdf in $pdfFiles) {
	Write-Host "Validating PDF/A-3B conformance with veraPDF $validatorVersion`: $($pdf.Name)"
	$baseName = [System.IO.Path]::GetFileNameWithoutExtension($pdf.Name)
	$reportPath = Join-Path $EvidenceDirectory "$baseName.verapdf.xml"
	$logPath = Join-Path $EvidenceDirectory "$baseName.verapdf.log"
	$reportLines = & $veraPdf --flavour 3b --format xml $pdf.FullName 2> $logPath
	$exitCode = $LASTEXITCODE
	$reportText = $reportLines -join [Environment]::NewLine
	[System.IO.File]::WriteAllText($reportPath, $reportText, [System.Text.UTF8Encoding]::new($false))
	if ($exitCode -ne 0) {
		throw "veraPDF execution failed for $($pdf.Name) with exit code $exitCode. See $logPath."
	}

	try {
		[xml]$report = $reportText
	}
	catch {
		throw "veraPDF did not emit a parseable XML report for $($pdf.Name): $($_.Exception.Message)"
	}

	$validationReports = @($report.SelectNodes("//validationReport"))
	if ($validationReports.Count -eq 0) {
		throw "veraPDF report for $($pdf.Name) contains no validationReport."
	}
	$nonCompliant = @($validationReports | Where-Object { $_.GetAttribute("isCompliant") -ne "true" })
	if ($nonCompliant.Count -gt 0) {
		throw "veraPDF PDF/A-3B validation failed for $($pdf.Name). See $reportPath."
	}

	$batchSummary = $report.SelectSingleNode("//batchSummary")
	Assert-ZeroBatchValue $batchSummary "nonCompliant" $pdf.Name
	Assert-ZeroBatchValue $batchSummary "failedJobs" $pdf.Name
	Assert-ZeroBatchValue $batchSummary "failedToParse" $pdf.Name
	Assert-ZeroBatchValue $batchSummary "encrypted" $pdf.Name
	Assert-ZeroBatchValue $batchSummary "outOfMemory" $pdf.Name
	Assert-ZeroBatchValue $batchSummary "veraExceptions" $pdf.Name

	$validated += [ordered]@{
		pdf = $pdf.Name
		sha256 = (Get-FileHash -Path $pdf.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
		report = [System.IO.Path]::GetFileName($reportPath)
		isCompliant = $true
	}
}

$summary = [ordered]@{
	validator = "veraPDF"
	validatorVersion = $validatorVersion
	profile = "PDF/A-3B"
	installerUri = $installerUri
	installerSha256 = $installerSha256
	validatedAtUtc = [DateTime]::UtcNow.ToString("O")
	count = $validated.Count
	files = $validated
}
$summaryPath = Join-Path $EvidenceDirectory "verapdf-summary.json"
$summary | ConvertTo-Json -Depth 5 | Set-Content -Path $summaryPath -Encoding utf8
Write-Host "ZUGFeRD/Factur-X PDF/A-3B conformance succeeded for $($validated.Count) artifact(s) with veraPDF $validatorVersion."
