param(
    [Parameter(Mandatory = $true)]
    [string]$CoveragePath,
    [double]$MinimumLinePercent = 0,
    [double]$MinimumBranchPercent = 0,
    [double]$MinimumMethodPercent = 0,
    [double]$MinimumDepotManagerLinePercent = 0,
    [double]$MinimumDepotManagerBranchPercent = 0
)

$ErrorActionPreference = 'Stop'
$culture = [System.Globalization.CultureInfo]::InvariantCulture

if (-not (Test-Path -LiteralPath $CoveragePath -PathType Leaf)) {
    throw "Coverage report '$CoveragePath' was not found."
}

[xml]$report = Get-Content -LiteralPath $CoveragePath -Raw
$classNodes = @($report.SelectNodes('//class'))
if ($classNodes.Count -eq 0) {
    throw 'Coverage report does not contain any classes.'
}

function Get-ProductionArea([System.Xml.XmlElement]$classNode) {
    $name = [string]$classNode.GetAttribute('name')
    if ($name.StartsWith('Depot.Tests.', [StringComparison]::Ordinal)) { return $null }
    if ($name.StartsWith('DepotManager.', [StringComparison]::Ordinal)) { return 'DepotManager' }
    if ($name.StartsWith('Depot.', [StringComparison]::Ordinal)) { return 'Depot' }
    return $null
}

function Get-CoverageStats([object[]]$classes) {
    $lineTotal = 0
    $lineCovered = 0
    $branchTotal = 0
    $branchCovered = 0
    $methodTotal = 0
    $methodCovered = 0

    foreach ($class in $classes) {
        foreach ($line in @($class.SelectNodes('./lines/line'))) {
            $lineTotal++
            if ([int]$line.GetAttribute('hits') -gt 0) { $lineCovered++ }

            if ($line.GetAttribute('branch') -eq 'true') {
                $conditionCoverage = $line.GetAttribute('condition-coverage')
                if ($conditionCoverage -match '\((\d+)\s*/\s*(\d+)\)') {
                    $branchCovered += [int]$Matches[1]
                    $branchTotal += [int]$Matches[2]
                }
            }
        }

        foreach ($method in @($class.SelectNodes('./methods/method'))) {
            $methodTotal++
            $coveredLines = @($method.SelectNodes('./lines/line') | Where-Object { [int]$_.GetAttribute('hits') -gt 0 })
            if ($coveredLines.Count -gt 0) { $methodCovered++ }
        }
    }

    if ($lineTotal -eq 0) { throw 'Coverage scope does not contain any executable production lines.' }
    if ($methodTotal -eq 0) { throw 'Coverage scope does not contain any production methods.' }

    [pscustomobject]@{
        LineCovered = $lineCovered
        LineTotal = $lineTotal
        LinePercent = 100.0 * $lineCovered / $lineTotal
        BranchCovered = $branchCovered
        BranchTotal = $branchTotal
        BranchPercent = if ($branchTotal -eq 0) { 100.0 } else { 100.0 * $branchCovered / $branchTotal }
        MethodCovered = $methodCovered
        MethodTotal = $methodTotal
        MethodPercent = 100.0 * $methodCovered / $methodTotal
    }
}

$depotClasses = @()
$managerClasses = @()
foreach ($class in $classNodes) {
    switch (Get-ProductionArea $class) {
        'Depot' { $depotClasses += $class }
        'DepotManager' { $managerClasses += $class }
    }
}

if ($depotClasses.Count -eq 0) { throw 'Coverage report does not contain Depot production classes.' }
if ($managerClasses.Count -eq 0) { throw 'Coverage report does not contain DepotManager production classes.' }

$depot = Get-CoverageStats $depotClasses
$manager = Get-CoverageStats $managerClasses
$total = Get-CoverageStats @($depotClasses + $managerClasses)

function Format-Percent([double]$value) {
    return $value.ToString('0.00', $culture)
}

function Format-Ratio([int]$covered, [int]$totalCount) {
    return "$covered/$totalCount"
}

$rows = @(
    [pscustomobject]@{ Area = 'Depot'; Lines = "$(Format-Percent $depot.LinePercent)% ($(Format-Ratio $depot.LineCovered $depot.LineTotal))"; Branches = "$(Format-Percent $depot.BranchPercent)% ($(Format-Ratio $depot.BranchCovered $depot.BranchTotal))"; Methods = "$(Format-Percent $depot.MethodPercent)% ($(Format-Ratio $depot.MethodCovered $depot.MethodTotal))" },
    [pscustomobject]@{ Area = 'DepotManager'; Lines = "$(Format-Percent $manager.LinePercent)% ($(Format-Ratio $manager.LineCovered $manager.LineTotal))"; Branches = "$(Format-Percent $manager.BranchPercent)% ($(Format-Ratio $manager.BranchCovered $manager.BranchTotal))"; Methods = "$(Format-Percent $manager.MethodPercent)% ($(Format-Ratio $manager.MethodCovered $manager.MethodTotal))" },
    [pscustomobject]@{ Area = 'Combined'; Lines = "$(Format-Percent $total.LinePercent)% ($(Format-Ratio $total.LineCovered $total.LineTotal))"; Branches = "$(Format-Percent $total.BranchPercent)% ($(Format-Ratio $total.BranchCovered $total.BranchTotal))"; Methods = "$(Format-Percent $total.MethodPercent)% ($(Format-Ratio $total.MethodCovered $total.MethodTotal))" }
)

$rows | Format-Table -AutoSize | Out-String | Write-Host

if ($env:GITHUB_STEP_SUMMARY) {
    Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value '## Code coverage'
    Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value ''
    Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value '| Area | Lines | Branches | Methods |'
    Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value '| --- | ---: | ---: | ---: |'
    foreach ($row in $rows) {
        Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value "| $($row.Area) | $($row.Lines) | $($row.Branches) | $($row.Methods) |"
    }
}

$violations = @()
if ($total.LinePercent -lt $MinimumLinePercent) {
    $violations += "Combined line coverage $(Format-Percent $total.LinePercent)% is below the required $(Format-Percent $MinimumLinePercent)%."
}
if ($total.BranchPercent -lt $MinimumBranchPercent) {
    $violations += "Combined branch coverage $(Format-Percent $total.BranchPercent)% is below the required $(Format-Percent $MinimumBranchPercent)%."
}
if ($total.MethodPercent -lt $MinimumMethodPercent) {
    $violations += "Combined method coverage $(Format-Percent $total.MethodPercent)% is below the required $(Format-Percent $MinimumMethodPercent)%."
}
if ($manager.LinePercent -lt $MinimumDepotManagerLinePercent) {
    $violations += "DepotManager line coverage $(Format-Percent $manager.LinePercent)% is below the required $(Format-Percent $MinimumDepotManagerLinePercent)%."
}
if ($manager.BranchPercent -lt $MinimumDepotManagerBranchPercent) {
    $violations += "DepotManager branch coverage $(Format-Percent $manager.BranchPercent)% is below the required $(Format-Percent $MinimumDepotManagerBranchPercent)%."
}

if ($violations.Count -gt 0) {
    $violations | ForEach-Object { Write-Error $_ }
    throw "Code coverage quality gate failed with $($violations.Count) violation(s)."
}

Write-Host "Code coverage quality gate passed: combined lines $(Format-Percent $total.LinePercent)%, branches $(Format-Percent $total.BranchPercent)%, methods $(Format-Percent $total.MethodPercent)%; DepotManager lines $(Format-Percent $manager.LinePercent)%, branches $(Format-Percent $manager.BranchPercent)%."
