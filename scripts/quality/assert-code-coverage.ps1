param(
    [Parameter(Mandatory = $true)]
    [string]$CoveragePath,
    [Parameter(Mandatory = $true)]
    [ValidateRange(0, 100)]
    [double]$MinimumLinePercent,
    [Parameter(Mandatory = $true)]
    [ValidateRange(0, 100)]
    [double]$MinimumBranchPercent,
    [ValidateRange(0, 100)]
    [double]$MinimumMethodPercent = 0,
    [Parameter(Mandatory = $true)]
    [ValidateRange(0, 100)]
    [double]$MinimumDepotManagerLinePercent,
    [Parameter(Mandatory = $true)]
    [ValidateRange(0, 100)]
    [double]$MinimumDepotManagerBranchPercent
)

$ErrorActionPreference = 'Stop'
$culture = [System.Globalization.CultureInfo]::InvariantCulture

if (-not (Test-Path -LiteralPath $CoveragePath)) {
    throw "Coverage path '$CoveragePath' was not found."
}

$coverageFiles = if (Test-Path -LiteralPath $CoveragePath -PathType Leaf) {
    @(Get-Item -LiteralPath $CoveragePath)
} else {
    @(Get-ChildItem -LiteralPath $CoveragePath -Recurse -File -Filter '*.cobertura.xml')
}

if ($coverageFiles.Count -eq 0) {
    throw "Coverage path '$CoveragePath' does not contain any Cobertura reports."
}

function Get-ProductionArea([System.Xml.XmlElement]$classNode) {
    $package = $classNode.ParentNode.ParentNode
    $moduleName = if ($package -is [System.Xml.XmlElement]) { [string]$package.GetAttribute('name') } else { '' }

    if ($moduleName -match '(^|[\\/])DepotManager(\.dll)?$' -or $moduleName -eq 'DepotManager') { return 'DepotManager' }
    if ($moduleName -match '(^|[\\/])Depot(\.dll)?$' -or $moduleName -eq 'Depot') { return 'Depot' }

    $name = [string]$classNode.GetAttribute('name')
    if ($name.StartsWith('Depot.Tests.', [StringComparison]::Ordinal) -or
        $name.StartsWith('DepotManager.Tests.', [StringComparison]::Ordinal)) { return $null }
    if ($name.StartsWith('DepotManager.', [StringComparison]::Ordinal)) { return 'DepotManager' }
    if ($name.StartsWith('Depot.', [StringComparison]::Ordinal)) { return 'Depot' }
    return $null
}

function New-CoverageAccumulator {
    return [pscustomobject]@{
        Lines = @{}
        BranchLines = @{}
        Methods = @{}
    }
}

$areas = @{
    Depot = New-CoverageAccumulator
    DepotManager = New-CoverageAccumulator
}

foreach ($coverageFile in $coverageFiles) {
    [xml]$report = Get-Content -LiteralPath $coverageFile.FullName -Raw
    foreach ($class in @($report.SelectNodes('//class'))) {
        $area = Get-ProductionArea $class
        if (-not $area) { continue }

        $data = $areas[$area]
        $package = $class.ParentNode.ParentNode
        $moduleName = if ($package -is [System.Xml.XmlElement]) { [string]$package.GetAttribute('name') } else { '' }
        $classKey = "$moduleName|$($class.GetAttribute('name'))|$($class.GetAttribute('filename'))"

        foreach ($line in @($class.SelectNodes('./lines/line'))) {
            $lineKey = "$classKey|$($line.GetAttribute('number'))"
            if (-not $data.Lines.ContainsKey($lineKey)) { $data.Lines[$lineKey] = $false }
            if ([int]$line.GetAttribute('hits') -gt 0) { $data.Lines[$lineKey] = $true }

            if ($line.GetAttribute('branch') -ne 'true') { continue }
            if (-not $data.BranchLines.ContainsKey($lineKey)) {
                $data.BranchLines[$lineKey] = [pscustomobject]@{
                    Conditions = @{}
                    FallbackCovered = 0
                    FallbackTotal = 0
                }
            }

            $branchLine = $data.BranchLines[$lineKey]
            $conditions = @($line.SelectNodes('./conditions/condition'))
            if ($conditions.Count -gt 0) {
                for ($conditionIndex = 0; $conditionIndex -lt $conditions.Count; $conditionIndex++) {
                    $condition = $conditions[$conditionIndex]
                    $conditionKey = "$($condition.GetAttribute('number'))|$($condition.GetAttribute('type'))|$conditionIndex"
                    if (-not $branchLine.Conditions.ContainsKey($conditionKey)) {
                        $branchLine.Conditions[$conditionKey] = $false
                    }
                    $coverage = [string]$condition.GetAttribute('coverage')
                    if ($coverage -match '^([0-9]+(?:\.[0-9]+)?)%') {
                        if ([double]::Parse($Matches[1], $culture) -gt 0) {
                            $branchLine.Conditions[$conditionKey] = $true
                        }
                    }
                }
            } else {
                $conditionCoverage = [string]$line.GetAttribute('condition-coverage')
                if ($conditionCoverage -match '\((\d+)\s*/\s*(\d+)\)') {
                    $branchLine.FallbackCovered = [Math]::Max($branchLine.FallbackCovered, [int]$Matches[1])
                    $branchLine.FallbackTotal = [Math]::Max($branchLine.FallbackTotal, [int]$Matches[2])
                }
            }
        }

        foreach ($method in @($class.SelectNodes('./methods/method'))) {
            $methodKey = "$classKey|$($method.GetAttribute('name'))|$($method.GetAttribute('signature'))"
            if (-not $data.Methods.ContainsKey($methodKey)) { $data.Methods[$methodKey] = $false }
            if (@($method.SelectNodes('./lines/line') | Where-Object { [int]$_.GetAttribute('hits') -gt 0 }).Count -gt 0) {
                $data.Methods[$methodKey] = $true
            }
        }
    }
}

function Get-CoverageStats($data) {
    $lineTotal = $data.Lines.Count
    $lineCovered = @($data.Lines.Values | Where-Object { $_ }).Count
    $branchTotal = 0
    $branchCovered = 0
    $methodTotal = $data.Methods.Count
    $methodCovered = @($data.Methods.Values | Where-Object { $_ }).Count

    foreach ($branchLine in $data.BranchLines.Values) {
        if ($branchLine.Conditions.Count -gt 0) {
            $branchTotal += $branchLine.Conditions.Count
            $branchCovered += @($branchLine.Conditions.Values | Where-Object { $_ }).Count
        } else {
            $branchTotal += $branchLine.FallbackTotal
            $branchCovered += $branchLine.FallbackCovered
        }
    }

    if ($lineTotal -eq 0) { throw 'Coverage scope does not contain any executable production lines.' }
    if ($methodTotal -eq 0) { throw 'Coverage scope does not contain any production methods.' }

    return [pscustomobject]@{
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

$depot = Get-CoverageStats $areas.Depot
$manager = Get-CoverageStats $areas.DepotManager
$total = [pscustomobject]@{
    LineCovered = $depot.LineCovered + $manager.LineCovered
    LineTotal = $depot.LineTotal + $manager.LineTotal
    BranchCovered = $depot.BranchCovered + $manager.BranchCovered
    BranchTotal = $depot.BranchTotal + $manager.BranchTotal
    MethodCovered = $depot.MethodCovered + $manager.MethodCovered
    MethodTotal = $depot.MethodTotal + $manager.MethodTotal
}
$total | Add-Member -NotePropertyName LinePercent -NotePropertyValue (100.0 * $total.LineCovered / $total.LineTotal)
$total | Add-Member -NotePropertyName BranchPercent -NotePropertyValue $(if ($total.BranchTotal -eq 0) { 100.0 } else { 100.0 * $total.BranchCovered / $total.BranchTotal })
$total | Add-Member -NotePropertyName MethodPercent -NotePropertyValue (100.0 * $total.MethodCovered / $total.MethodTotal)

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

Write-Host "Coverage reports: $($coverageFiles.Count)"
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
