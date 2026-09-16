param(
    [string]$Path = (Join-Path (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path 'operations/TrackAAcceptance.example.json'),
    [switch]$RequirePass,
    [string]$EvidencePath = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-Value([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Test-Placeholder($Value) {
    $text = [string]$Value
    return [string]::IsNullOrWhiteSpace($text) -or $text -match '^(UNSET|TBD|TODO|CHANGE-ME|EXAMPLE)$'
}

function Assert-ConcreteText($Value, [string]$Field) {
    Assert-Value (-not (Test-Placeholder $Value)) "$Field requires concrete evidence."
}

function Assert-Timestamp($Value, [string]$Field) {
    $parsed = [DateTimeOffset]::MinValue
    Assert-Value ([DateTimeOffset]::TryParse([string]$Value, [ref]$parsed)) "$Field must be a valid timestamp."
}

function Assert-PassEvidence($Package, [string]$Key) {
    Assert-ConcreteText $Package.evidenceReference "$Key/evidenceReference"
    Assert-ConcreteText $Package.verifiedBy "$Key/verifiedBy"
    Assert-Timestamp $Package.verifiedAtUtc "$Key/verifiedAtUtc"
}

if (-not (Test-Path $Path -PathType Leaf)) {
    throw "Track A acceptance evidence was not found: $Path"
}

$document = Get-Content $Path -Raw | ConvertFrom-Json
Assert-Value ([int]$document.formatVersion -eq 1) 'formatVersion must be 1.'
Assert-Value ([string]$document.track -eq 'A') 'track must be A.'
Assert-Value ([string]$document.status -in @('BLOCKED', 'PASS')) 'status must be BLOCKED or PASS.'
Assert-Value ($null -ne $document.packages) 'packages is required.'

$packageRules = [ordered]@{
    H1 = @('ADMIN_REQUIRED', 'BLOCKED', 'PASS')
    H2 = @('BLOCKED', 'PASS')
    H3 = @('PRODUCTION_RC_REQUIRED', 'BLOCKED', 'PASS')
    H4 = @('DEPLOYMENT_REQUIRED', 'BLOCKED', 'PASS')
    H5 = @('MANUAL_REQUIRED', 'BLOCKED', 'PASS')
}

$statuses = [ordered]@{}
foreach ($key in $packageRules.Keys) {
    $property = $document.packages.PSObject.Properties[$key]
    Assert-Value ($null -ne $property) "packages/$key is required."
    $package = $property.Value
    Assert-Value (-not [string]::IsNullOrWhiteSpace([string]$package.name)) "packages/$key/name is required."
    $status = [string]$package.status
    Assert-Value ($status -in $packageRules[$key]) "packages/$key/status '$status' is not allowed."
    $statuses[$key] = $status
    if ($status -eq 'PASS') {
        Assert-PassEvidence $package "packages/$key"
    }
}

$allPass = @($statuses.Values | Where-Object { $_ -ne 'PASS' }).Count -eq 0
if ([string]$document.status -eq 'PASS') {
    Assert-Value $allPass 'Top-level PASS is invalid while one or more Track A packages are not PASS.'
}
if ($allPass) {
    Assert-Value ([string]$document.status -eq 'PASS') 'Top-level status must be PASS when all Track A packages are PASS.'
}

$enforceProductionClosure = $RequirePass -or [string]$document.status -eq 'PASS'
if ($enforceProductionClosure) {
    Assert-Value ([string]$document.status -eq 'PASS') 'Track A closure requires top-level PASS.'
    Assert-Value $allPass 'Track A closure requires H1 through H5 to be PASS.'
    Assert-Value ([string]$document.sourceSha -match '^[0-9a-fA-F]{40}$') 'sourceSha must identify the exact 40-character accepted release-candidate commit.'
    Assert-Value ([string]$document.depotVersion -match '^\d+\.\d+\.\d+$') 'depotVersion must identify the exact Stable release candidate without a prerelease suffix.'
    Assert-ConcreteText $document.acceptedBy 'acceptedBy'
    Assert-Timestamp $document.acceptedAtUtc 'acceptedAtUtc'
    Assert-Value (@($document.knownBlockingIssues).Count -eq 0) 'knownBlockingIssues must be empty for Track A closure.'

    $h1 = $document.packages.H1
    Assert-Value ($null -ne $h1.rulesetId -and [int64]$h1.rulesetId -gt 0) 'H1 PASS requires the active GitHub ruleset ID.'
    Assert-Value ([string]$h1.evidenceReference -ne '.github/rulesets/MasterGovernance.json') 'H1 PASS requires evidence of the active GitHub ruleset, not only the source-controlled template.'

    $h3 = $document.packages.H3
    Assert-Value ([string]$h3.releaseTag -match '^\d+\.\d+\.\d+$') 'H3 PASS requires the Stable release-candidate tag.'
    Assert-Value ([string]$h3.releaseTag -eq [string]$document.depotVersion) 'H3 releaseTag must match depotVersion.'
    Assert-ConcreteText $h3.productionSigningEvidenceReference 'packages/H3/productionSigningEvidenceReference'

    $h4 = $document.packages.H4
    Assert-ConcreteText $h4.deploymentId 'packages/H4/deploymentId'
    Assert-ConcreteText $h4.disasterRecoveryProfileEvidenceReference 'packages/H4/disasterRecoveryProfileEvidenceReference'
    Assert-ConcreteText $h4.restoreDrillEvidenceReference 'packages/H4/restoreDrillEvidenceReference'

    $h5 = $document.packages.H5
    Assert-ConcreteText $h5.accessibilityEvidenceReference 'packages/H5/accessibilityEvidenceReference'
}

$validation = [ordered]@{
    formatVersion = 1
    contractValidationResult = 'PASS'
    track = 'A'
    closureStatus = [string]$document.status
    closureAccepted = [bool]$allPass -and [string]$document.status -eq 'PASS'
    requirePass = [bool]$RequirePass
    packageStatuses = $statuses
    validatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
}

if (-not [string]::IsNullOrWhiteSpace($EvidencePath)) {
    $evidenceFullPath = [IO.Path]::GetFullPath($EvidencePath)
    $directory = Split-Path $evidenceFullPath -Parent
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        [IO.Directory]::CreateDirectory($directory) | Out-Null
    }
    $validation | ConvertTo-Json -Depth 6 | Set-Content $evidenceFullPath -Encoding utf8
}

Write-Host "Track A acceptance contract is valid. RequirePass=$RequirePass ClosureStatus=$($document.status) ClosureAccepted=$($validation.closureAccepted)"
