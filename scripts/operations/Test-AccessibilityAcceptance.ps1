param(
    [string]$Path = (Join-Path (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path 'docs/operations/AccessibilityAcceptance.example.json'),
    [switch]$RequirePass
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not (Test-Path $Path -PathType Leaf)) { throw "Accessibility acceptance evidence was not found: $Path" }
$document = Get-Content $Path -Raw | ConvertFrom-Json
$allowedStatuses = @('MANUAL_REQUIRED', 'PASS', 'BLOCKED')

function Assert-Value([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}
function Assert-Status($Value, [string]$Field) {
    Assert-Value ($null -ne $Value -and $allowedStatuses -contains [string]$Value) "$Field must be MANUAL_REQUIRED, PASS or BLOCKED."
}
function Assert-EvidenceRows($Rows, [string[]]$ExpectedScenarios, [string]$Field) {
    Assert-Value ($null -ne $Rows) "$Field is required."
    $actual = @($Rows | ForEach-Object { [string]$_.scenario })
    foreach ($scenario in $ExpectedScenarios) {
        Assert-Value ($actual -contains $scenario) "$Field is missing required scenario '$scenario'."
    }
    foreach ($row in @($Rows)) {
        Assert-Status $row.status "$Field/$($row.scenario)/status"
        Assert-Value (-not [string]::IsNullOrWhiteSpace([string]$row.evidence)) "$Field/$($row.scenario)/evidence is required."
        if ($RequirePass) {
            Assert-Value ([string]$row.status -eq 'PASS') "$Field/$($row.scenario) must be PASS for production acceptance."
            Assert-Value ([string]$row.evidence -ne 'UNSET') "$Field/$($row.scenario) requires concrete evidence for production acceptance."
        }
    }
}

Assert-Value ([int]$document.formatVersion -eq 1) 'formatVersion must be 1.'
Assert-Status $document.status 'status'
Assert-Value (-not [string]::IsNullOrWhiteSpace([string]$document.depotVersion)) 'depotVersion is required.'
Assert-Value ($null -ne $document.environment) 'environment is required.'

Assert-EvidenceRows $document.keyboardOnly @(
    'Login',
    'First-run administrator',
    'Shell, navigation and workspace tabs',
    'Critical CRUD and workflow dialogs',
    'Finance workspaces and approval actions',
    'Audit, privacy and export flows',
    'DepotManager install, update, repair and diagnostics'
) 'keyboardOnly'
Assert-EvidenceRows $document.focus @(
    'Logical Tab and Shift+Tab order',
    'No keyboard traps',
    'Focus restoration after modal dialogs',
    'Visible keyboard focus at every actionable control'
) 'focus'
Assert-EvidenceRows $document.narrator @(
    'Input labels and required fields',
    'Data grids, selected rows and available actions',
    'Validation and error announcements',
    'Operation and connection status announcements',
    'Dialogs and focus return'
) 'narrator'

Assert-Status $document.accessibilityInsights.status 'accessibilityInsights/status'
Assert-Value (-not [string]::IsNullOrWhiteSpace([string]$document.accessibilityInsights.evidence)) 'accessibilityInsights/evidence is required.'

$expectedScales = @(100, 125, 150, 200)
$actualScales = @($document.dpi | ForEach-Object { [int]$_.scalePercent } | Sort-Object)
Assert-Value ($actualScales.Count -eq $expectedScales.Count) 'dpi must contain exactly 100, 125, 150 and 200 percent rows.'
for ($index = 0; $index -lt $expectedScales.Count; $index++) {
    Assert-Value ($actualScales[$index] -eq $expectedScales[$index]) 'dpi must contain exactly 100, 125, 150 and 200 percent rows.'
}
foreach ($row in @($document.dpi)) {
    Assert-Status $row.status "dpi/$($row.scalePercent)/status"
    Assert-Value (-not [string]::IsNullOrWhiteSpace([string]$row.evidence)) "dpi/$($row.scalePercent)/evidence is required."
    if ($RequirePass) {
        Assert-Value ([string]$row.status -eq 'PASS') "dpi/$($row.scalePercent) must be PASS for production acceptance."
        Assert-Value ([string]$row.evidence -ne 'UNSET') "dpi/$($row.scalePercent) requires concrete evidence for production acceptance."
    }
}

if ($RequirePass) {
    Assert-Value ([string]$document.status -eq 'PASS') 'Top-level status must be PASS for production acceptance.'
    Assert-Value (-not [string]::IsNullOrWhiteSpace([string]$document.depotVersion) -and [string]$document.depotVersion -ne 'UNSET') 'depotVersion must identify the exact accepted release candidate.'
    Assert-Value ([string]$document.sourceSha -match '^[0-9a-fA-F]{40}$') 'sourceSha must be the exact 40-character Git commit SHA.'
    Assert-Value (-not [string]::IsNullOrWhiteSpace([string]$document.tester) -and [string]$document.tester -ne 'UNSET') 'tester must identify the person performing acceptance.'
    $testedAt = [DateTimeOffset]::MinValue
    Assert-Value ([DateTimeOffset]::TryParse([string]$document.testedAtUtc, [ref]$testedAt)) 'testedAtUtc must be a valid timestamp.'
    foreach ($field in @('windowsVersion', 'displayResolution', 'graphicsAdapter', 'screenReader', 'accessibilityInspector')) {
        $value = [string]$document.environment.$field
        Assert-Value (-not [string]::IsNullOrWhiteSpace($value) -and $value -ne 'UNSET') "environment/$field must be recorded for production acceptance."
    }
    Assert-Value ([string]$document.accessibilityInsights.status -eq 'PASS') 'Accessibility Insights status must be PASS.'
    Assert-Value ([string]$document.accessibilityInsights.evidence -ne 'UNSET') 'Accessibility Insights requires concrete evidence.'
    Assert-Value (@($document.accessibilityInsights.blockingFindings).Count -eq 0) 'Accessibility Insights contains blocking findings.'
    Assert-Value (@($document.knownBlockingIssues).Count -eq 0) 'knownBlockingIssues must be empty for production acceptance.'
}

Write-Host "Accessibility acceptance evidence contract is valid. RequirePass=$RequirePass Status=$($document.status)"
