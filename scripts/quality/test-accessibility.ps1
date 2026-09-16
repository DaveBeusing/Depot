param(
    [string]$EvidencePath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$resourceRoot = Join-Path $root 'src/Depot/Resources'
$violations = @()
$focusSetterSuppressions = @()
$directFocusSuppressions = @()

Get-ChildItem (Join-Path $root 'src') -Filter *.xaml -Recurse | ForEach-Object {
    $text = Get-Content $_.FullName -Raw
    if ($text -match 'FocusVisualStyle\s*=\s*"\{x:Null\}"') {
        $directFocusSuppressions += $_.FullName
        $violations += "$($_.FullName): directly disables keyboard focus visuals."
    }
    $matches = [regex]::Matches($text, '<Setter\s+Property="FocusVisualStyle"\s+Value="\{x:Null\}"\s*/>')
    if ($matches.Count -gt 0) {
        if ($_.FullName -notmatch '[\\/]Resources[\\/]') {
            $violations += "$($_.FullName): FocusVisualStyle suppression is permitted only inside shared resource styles guarded by DesktopAccessibilityRuntime."
        }
        $focusSetterSuppressions += [pscustomobject]@{ file = $_.FullName; count = $matches.Count }
    }
    if ($text -match 'KeyboardNavigation\.TabNavigation\s*=\s*"Cycle"') {
        $violations += "$($_.FullName): cyclic Tab navigation can create a keyboard trap."
    }
}

$focusResourcePath = Join-Path $resourceRoot 'Accessibility.xaml'
$focusResource = if (Test-Path $focusResourcePath) { Get-Content $focusResourcePath -Raw } else { '' }
if ($focusResource -notmatch 'x:Key="AppKeyboardFocusVisualStyle"' -or $focusResource -notmatch 'TargetType="Control"' -or $focusResource -notmatch '<Rectangle') {
    $violations += 'Accessibility.xaml must provide AppKeyboardFocusVisualStyle as a Control focus template with a visible Rectangle.'
}

$depotApp = Get-Content (Join-Path $root 'src/Depot/App.xaml.cs') -Raw
$managerApp = Get-Content (Join-Path $root 'src/DepotManager/App.xaml.cs') -Raw
if ($depotApp -notmatch 'DesktopAccessibilityRuntime\.Register\(\)') { $violations += 'Depot App must register DesktopAccessibilityRuntime.' }
if ($managerApp -notmatch 'DesktopAccessibilityRuntime\.Register\(\)') { $violations += 'DepotManager App must register DesktopAccessibilityRuntime.' }

$theme = Get-Content (Join-Path $resourceRoot 'Theme.xaml') -Raw
$managerResources = Get-Content (Join-Path $root 'src/DepotManager/App.xaml') -Raw
if ($theme -notmatch 'Accessibility\.xaml') { $violations += 'Depot Theme must merge Accessibility.xaml.' }
if ($managerResources -notmatch 'Accessibility\.xaml') { $violations += 'DepotManager resources must merge Accessibility.xaml.' }

foreach ($project in @('src/Depot/Depot.csproj', 'src/DepotManager/DepotManager.csproj')) {
    $projectText = Get-Content (Join-Path $root $project) -Raw
    if ($projectText -notmatch '<ApplicationManifest>app\.manifest</ApplicationManifest>') {
        $violations += "$project must explicitly use app.manifest."
    }
    $manifest = Join-Path (Split-Path (Join-Path $root $project) -Parent) 'app.manifest'
    if (-not (Test-Path $manifest -PathType Leaf)) {
        $violations += "$project is missing app.manifest."
    }
    else {
        $manifestText = Get-Content $manifest -Raw
        if ($manifestText -notmatch 'PerMonitorV2,PerMonitor') { $violations += "$manifest must declare PerMonitorV2 DPI awareness." }
    }
}

foreach ($control in @('TextInput.cs', 'PasswordInput.cs')) {
    $text = Get-Content (Join-Path $root "src/Depot/Controls/$control") -Raw
    if ($text -notmatch 'AccessibilityAutomation\.ForwardInputProperties') {
        $violations += "$control must forward automation labels/properties to its native focus target."
    }
}
foreach ($control in @('OperationStatus.cs', 'ConnectionStatusIndicator.cs')) {
    $text = Get-Content (Join-Path $root "src/Depot/Controls/$control") -Raw
    if ($text -notmatch 'AccessibilityAutomation\.Announce') {
        $violations += "$control must raise UI Automation notifications for meaningful status changes."
    }
}

$login = Get-Content (Join-Path $root 'src/Depot/Views/Login/LoginWindow.xaml') -Raw
$firstRun = Get-Content (Join-Path $root 'src/Depot/Views/Login/FirstRunAdminWindow.xaml') -Raw
if ($login -notmatch 'AutomationProperties\.LabeledBy' -or $login -notmatch 'AutomationProperties\.IsRequiredForForm') {
    $violations += 'Login inputs must expose explicit labels and required-field semantics.'
}
if ($firstRun -notmatch 'AutomationProperties\.LabeledBy' -or $firstRun -notmatch 'AutomationProperties\.IsRequiredForForm') {
    $violations += 'First-run administrator inputs must expose explicit labels and required-field semantics.'
}

[xml]$colors = Get-Content (Join-Path $resourceRoot 'Colors.xaml') -Raw
$brushes = @{}
$colors.DocumentElement.ChildNodes | Where-Object { $_.LocalName -eq 'SolidColorBrush' } | ForEach-Object {
    $key = $_.GetAttribute('Key','http://schemas.microsoft.com/winfx/2006/xaml')
    if ($key) { $brushes[$key] = $_.Color }
}

function Relative-Luminance([string]$hex) {
    $hex = $hex.TrimStart('#')
    if ($hex.Length -eq 8) { $hex = $hex.Substring(2) }
    $values = 0,2,4 | ForEach-Object { [Convert]::ToInt32($hex.Substring($_,2),16) / 255.0 }
    $linear = $values | ForEach-Object { if ($_ -le 0.04045) { $_ / 12.92 } else { [Math]::Pow(($_ + 0.055) / 1.055, 2.4) } }
    return 0.2126*$linear[0] + 0.7152*$linear[1] + 0.0722*$linear[2]
}
function Contrast([string]$a,[string]$b) {
    $la = Relative-Luminance $a; $lb = Relative-Luminance $b
    if ($la -lt $lb) { $t=$la; $la=$lb; $lb=$t }
    return ($la + 0.05) / ($lb + 0.05)
}

$pairs = @(
    @('PrimaryTextBrush','BackgroundBrush',4.5),
    @('SecondaryTextBrush','BackgroundBrush',4.5),
    @('NavigationForegroundBrush','NavigationBackgroundBrush',4.5),
    @('PrimaryForegroundBrush','PrimaryBrush',4.5),
    @('SuccessForegroundBrush','SuccessBrush',4.5),
    @('WarningForegroundBrush','WarningBrush',4.5),
    @('ErrorForegroundBrush','ErrorBrush',4.5)
)
$contrastEvidence = @()
foreach ($pair in $pairs) {
    $ratio = Contrast $brushes[$pair[0]] $brushes[$pair[1]]
    $contrastEvidence += [pscustomobject]@{ foreground = $pair[0]; background = $pair[1]; ratio = [Math]::Round($ratio, 2); required = $pair[2] }
    if ($ratio -lt $pair[2]) { $violations += "Contrast $($pair[0])/$($pair[1]) is $([Math]::Round($ratio,2)):1; required $($pair[2]):1." }
}

$status = Get-Content (Join-Path $resourceRoot 'Status.xaml') -Raw
if ($status -notmatch 'Text="\{TemplateBinding Status\}"') { $violations += 'Connection status must expose textual status in addition to color.' }
if ($status -notmatch 'Text="\{TemplateBinding StatusText\}"') { $violations += 'Operation status must expose textual status in addition to color.' }

if ($violations.Count -gt 0) {
    $violations | ForEach-Object { Write-Error $_ }
    throw "Accessibility quality gate failed with $($violations.Count) violation(s)."
}

if (-not [string]::IsNullOrWhiteSpace($EvidencePath)) {
    $fullEvidencePath = [IO.Path]::GetFullPath((Join-Path $root $EvidencePath))
    Directory.CreateDirectory((Split-Path $fullEvidencePath -Parent)) | Out-Null
    $evidence = [ordered]@{
        formatVersion = 1
        status = 'PASS'
        generatedUtc = [DateTimeOffset]::UtcNow.ToString('O')
        directFocusSuppressions = $directFocusSuppressions.Count
        normalizedResourceFocusSuppressions = ($focusSetterSuppressions | Measure-Object -Property count -Sum).Sum
        focusRuntimeGuard = $true
        inputAutomationForwarding = $true
        liveStatusNotifications = $true
        perMonitorV2Manifest = $true
        keyboardTrapStaticCheck = $true
        contrast = $contrastEvidence
    }
    $evidence | ConvertTo-Json -Depth 6 | Out-File $fullEvidencePath -Encoding utf8
}

$normalized = ($focusSetterSuppressions | Measure-Object -Property count -Sum).Sum
Write-Host "Accessibility static quality gate passed. Runtime-normalized shared focus suppressions: $normalized."
