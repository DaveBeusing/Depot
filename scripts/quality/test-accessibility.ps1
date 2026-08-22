$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '../..')
$resourceRoot = Join-Path $root 'src/Depot/Resources'

$violations = @()
Get-ChildItem (Join-Path $root 'src/Depot') -Filter *.xaml -Recurse | ForEach-Object {
    $text = Get-Content $_.FullName -Raw
    if ($text -match 'FocusVisualStyle\s*=\s*"\{x:Null\}"') {
        $violations += "$($_.FullName): disables keyboard focus visuals."
    }
}

[xml]$colors = Get-Content (Join-Path $resourceRoot 'Colors.xaml') -Raw
$ns = New-Object System.Xml.XmlNamespaceManager($colors.NameTable)
$ns.AddNamespace('x','http://schemas.microsoft.com/winfx/2006/xaml')
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
foreach ($pair in $pairs) {
    $ratio = Contrast $brushes[$pair[0]] $brushes[$pair[1]]
    if ($ratio -lt $pair[2]) { $violations += "Contrast $($pair[0])/$($pair[1]) is $([Math]::Round($ratio,2)):1; required $($pair[2]):1." }
}

$status = Get-Content (Join-Path $resourceRoot 'Status.xaml') -Raw
if ($status -notmatch 'Text="\{TemplateBinding Status\}"') { $violations += 'Connection status must expose textual status in addition to color.' }
if ($status -notmatch 'Text="\{TemplateBinding StatusText\}"') { $violations += 'Operation status must expose textual status in addition to color.' }

if ($violations.Count -gt 0) {
    $violations | ForEach-Object { Write-Error $_ }
    throw "Accessibility quality gate failed with $($violations.Count) violation(s)."
}
Write-Host 'Accessibility static quality gate passed.'
