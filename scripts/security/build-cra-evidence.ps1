# Copyright (c) 2026 David Beusing
# Licensed under the MIT License.

[CmdletBinding()]
param(
    [string]$OutputPath = "artifacts/cra-evidence",
    [string]$SbomPath = "artifacts/sbom/depot.cdx.json"
)

$ErrorActionPreference = "Stop"

$requiredFiles = @(
    "SECURITY.md",
    "Directory.Build.props",
    "docs/SecurityRoadmap.md",
    "docs/compliance/CraClassification.md",
    "docs/compliance/CraRiskAssessment.md",
    "docs/compliance/CraIncidentReporting.md",
    "docs/compliance/CraTechnicalDocumentation.md",
    "docs/compliance/VulnerabilityManagement.md",
    "docs/compliance/SupportPolicy.md",
    "docs/compliance/SecurityUpdateLifecycle.md",
    "docs/compliance/SecureDefaultsReview.md",
    "docs/compliance/SecureConfiguration.md",
    "docs/compliance/ThreatModel.md",
    "docs/compliance/ReleaseIntegrity.md",
    "security/security-risk-acceptances.json"
)

foreach ($file in $requiredFiles) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
        throw "Required CRA evidence file is missing: $file"
    }
}

if (-not (Test-Path -LiteralPath $SbomPath -PathType Leaf)) {
    throw "Required CRA SBOM evidence is missing: $SbomPath"
}

Remove-Item -LiteralPath $OutputPath -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null

$evidenceFiles = @($requiredFiles) + @($SbomPath)
$manifestFiles = @()

foreach ($source in $evidenceFiles) {
    $relative = $source.Replace('\\', '/')
    if ($source -eq $SbomPath) { $relative = "sbom/depot.cdx.json" }
    $destination = Join-Path $OutputPath $relative
    $destinationDirectory = Split-Path -Parent $destination
    New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $destination -Force
    $hash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()
    $manifestFiles += [ordered]@{ path = $relative; sha256 = $hash }
}

[xml]$props = Get-Content -LiteralPath "Directory.Build.props" -Raw -Encoding UTF8
$group = $props.Project.PropertyGroup | Select-Object -First 1
$version = "$($group.DepotVersionMajor).$($group.DepotVersionMinor).$($group.DepotVersionPatch)"
if (-not [string]::IsNullOrWhiteSpace([string]$group.DepotVersionSuffix)) {
    $version = "$version-$($group.DepotVersionSuffix)"
}

$commit = if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_SHA)) { $env:GITHUB_SHA } else { (git rev-parse HEAD).Trim() }
$manifest = [ordered]@{
    schema = "depot-cra-technical-evidence/1.0"
    product = "Depot"
    version = $version
    sourceCommit = $commit
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    files = $manifestFiles
}

$manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $OutputPath "evidence-manifest.json") -Encoding UTF8
Write-Host "CRA evidence package created at $OutputPath with $($manifestFiles.Count) evidence file(s)."
