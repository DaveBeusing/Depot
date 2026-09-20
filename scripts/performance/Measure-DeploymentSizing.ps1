# Copyright (c) 2026 David Beusing
# Licensed under the MIT License.

[CmdletBinding()]
param(
	[string]$ScenarioCsvPath,
	[string]$PerformanceLogPath,
	[string]$EnvironmentName,
	[ValidateSet('SQLite', 'SqlServer', 'MariaDB', 'MySQL')]
	[string]$Provider,
	[string]$CommitSha,
	[string]$DepotVersion,
	[string]$DataProfile,
	[long]$DataVolumeRecords,
	[int]$ConcurrentUsers,
	[double]$NetworkLatencyMs,
	[string]$OutputDirectory = (Join-Path $PSScriptRoot '..\..\artifacts\deployment-sizing'),
	[string[]]$Optimization = @(),
	[string[]]$KnownLimit = @(),
	[string]$CiState = 'Not recorded',
	[switch]$RequireCompleteProfile,
	[switch]$InitializeTemplate
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$requiredScenarios = @(
	'Startup',
	'Home',
	'Navigation',
	'Search',
	'My Work',
	'Large List',
	'Finance Report',
	'Export',
	'Import',
	'Document/PDF',
	'Backup Window'
)

function Get-Percentile {
	param(
		[Parameter(Mandatory)]
		[double[]]$Values,
		[Parameter(Mandatory)]
		[ValidateRange(0.0, 1.0)]
		[double]$Percentile
	)

	if ($Values.Count -eq 0) { return $null }
	$ordered = @($Values | Sort-Object)
	$index = [Math]::Max(0, [Math]::Ceiling($Percentile * $ordered.Count) - 1)
	return [double]$ordered[$index]
}

function ConvertTo-InvariantDouble {
	param(
		[Parameter(Mandatory)]
		[string]$Value,
		[Parameter(Mandatory)]
		[string]$Field
	)

	$parsed = 0.0
	if (-not [double]::TryParse(
		$Value,
		[Globalization.NumberStyles]::Float,
		[Globalization.CultureInfo]::InvariantCulture,
		[ref]$parsed)) {
		throw "Field '$Field' must contain an invariant numeric value. Received '$Value'."
	}
	return $parsed
}

function Assert-RunMetadata {
	$requiredValues = @{
		EnvironmentName = $EnvironmentName
		Provider = $Provider
		CommitSha = $CommitSha
		DepotVersion = $DepotVersion
		DataProfile = $DataProfile
	}
	foreach ($entry in $requiredValues.GetEnumerator()) {
		if ([string]::IsNullOrWhiteSpace([string]$entry.Value)) {
			throw "Parameter -$($entry.Key) is required for a sizing run."
		}
	}
	if ($DataVolumeRecords -lt 1) { throw '-DataVolumeRecords must be greater than zero.' }
	if ($ConcurrentUsers -lt 1) { throw '-ConcurrentUsers must be greater than zero.' }
	if ($NetworkLatencyMs -lt 0) { throw '-NetworkLatencyMs cannot be negative.' }
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

if ($InitializeTemplate) {
	$templatePath = Join-Path $OutputDirectory 'DeploymentSizingScenarios.csv'
	$template = foreach ($scenario in $requiredScenarios) {
		[pscustomobject]@{
			Scenario = $scenario
			Iteration = ''
			DurationMs = ''
			CpuPercent = ''
			WorkingSetMb = ''
			Status = ''
		}
	}
	$template | Export-Csv -LiteralPath $templatePath -NoTypeInformation -Encoding utf8
	Write-Host "Created deployment sizing scenario template: $templatePath"
	return
}

Assert-RunMetadata

if ([string]::IsNullOrWhiteSpace($ScenarioCsvPath) -or -not (Test-Path -LiteralPath $ScenarioCsvPath -PathType Leaf)) {
	throw '-ScenarioCsvPath must reference an existing CSV file.'
}

$rows = @(Import-Csv -LiteralPath $ScenarioCsvPath)
if ($rows.Count -eq 0) { throw 'The scenario CSV does not contain any measurements.' }

$requiredColumns = @('Scenario', 'Iteration', 'DurationMs', 'CpuPercent', 'WorkingSetMb', 'Status')
$columns = @($rows[0].PSObject.Properties.Name)
foreach ($column in $requiredColumns) {
	if ($columns -notcontains $column) { throw "Scenario CSV is missing required column '$column'." }
}

$measurements = foreach ($row in $rows) {
	if ([string]::IsNullOrWhiteSpace($row.Scenario)) { throw 'Every measurement requires a Scenario.' }
	$status = ([string]$row.Status).Trim()
	if ($status -notin @('PASS', 'FAIL')) { throw "Scenario '$($row.Scenario)' must use Status PASS or FAIL." }
	[pscustomobject]@{
		Scenario = ([string]$row.Scenario).Trim()
		Iteration = ([string]$row.Iteration).Trim()
		DurationMs = ConvertTo-InvariantDouble -Value ([string]$row.DurationMs) -Field 'DurationMs'
		CpuPercent = ConvertTo-InvariantDouble -Value ([string]$row.CpuPercent) -Field 'CpuPercent'
		WorkingSetMb = ConvertTo-InvariantDouble -Value ([string]$row.WorkingSetMb) -Field 'WorkingSetMb'
		Status = $status
	}
}

$scenarioMetrics = foreach ($group in ($measurements | Group-Object Scenario | Sort-Object Name)) {
	$durations = [double[]]@($group.Group | ForEach-Object DurationMs)
	$cpu = [double[]]@($group.Group | ForEach-Object CpuPercent)
	$memory = [double[]]@($group.Group | ForEach-Object WorkingSetMb)
	[pscustomobject]@{
		Scenario = $group.Name
		Samples = $group.Count
		Passed = @($group.Group | Where-Object Status -eq 'PASS').Count
		Failed = @($group.Group | Where-Object Status -eq 'FAIL').Count
		P50Ms = [Math]::Round((Get-Percentile -Values $durations -Percentile 0.50), 1)
		P95Ms = [Math]::Round((Get-Percentile -Values $durations -Percentile 0.95), 1)
		MaxMs = [Math]::Round(($durations | Measure-Object -Maximum).Maximum, 1)
		AverageCpuPercent = [Math]::Round(($cpu | Measure-Object -Average).Average, 1)
		PeakWorkingSetMb = [Math]::Round(($memory | Measure-Object -Maximum).Maximum, 1)
	}
}

$presentScenarios = @($scenarioMetrics.Scenario)
$missingScenarios = @($requiredScenarios | Where-Object { $presentScenarios -notcontains $_ })

$runtimeSignals = @()
if (-not [string]::IsNullOrWhiteSpace($PerformanceLogPath)) {
	if (-not (Test-Path -LiteralPath $PerformanceLogPath -PathType Leaf)) {
		throw "-PerformanceLogPath '$PerformanceLogPath' was supplied but does not exist."
	}
	$runtimeSamples = [System.Collections.Generic.List[object]]::new()
	foreach ($line in Get-Content -LiteralPath $PerformanceLogPath) {
		if ($line -match ' home firstContent=(?<detail>\S+) elapsedMs=(?<elapsed>[0-9]+(?:\.[0-9]+)?)$') {
			$runtimeSamples.Add([pscustomobject]@{
				Signal = 'Home First Content'
				Detail = $Matches.detail
				ElapsedMs = ConvertTo-InvariantDouble -Value $Matches.elapsed -Field 'elapsedMs'
			})
			continue
		}
		if ($line -match ' my-work provider=(?<detail>.+?) eligible=(?<eligible>True|False) elapsedMs=(?<elapsed>[0-9]+(?:\.[0-9]+)?) rows=') {
			if ($Matches.eligible -eq 'True') {
				$runtimeSamples.Add([pscustomobject]@{
					Signal = 'My Work Provider'
					Detail = $Matches.detail
					ElapsedMs = ConvertTo-InvariantDouble -Value $Matches.elapsed -Field 'elapsedMs'
				})
			}
		}
	}
	$runtimeSignals = foreach ($group in ($runtimeSamples | Group-Object Signal, Detail | Sort-Object Name)) {
		$values = [double[]]@($group.Group | ForEach-Object ElapsedMs)
		[pscustomobject]@{
			Signal = $group.Group[0].Signal
			Detail = $group.Group[0].Detail
			Samples = $group.Count
			P50Ms = [Math]::Round((Get-Percentile -Values $values -Percentile 0.50), 1)
			P95Ms = [Math]::Round((Get-Percentile -Values $values -Percentile 0.95), 1)
		}
	}
}

$evidence = [ordered]@{
	GeneratedAtUtc = [DateTimeOffset]::UtcNow.ToString('O', [Globalization.CultureInfo]::InvariantCulture)
	Repository = 'DaveBeusing/Depot'
	CommitSha = $CommitSha
	DepotVersion = $DepotVersion
	Environment = $EnvironmentName
	Provider = $Provider
	DataProfile = $DataProfile
	DataVolumeRecords = $DataVolumeRecords
	NetworkLatencyMs = $NetworkLatencyMs
	ConcurrentUsers = $ConcurrentUsers
	ScenarioMetrics = @($scenarioMetrics)
	RuntimeSignals = @($runtimeSignals)
	MissingRequiredScenarios = @($missingScenarios)
	Optimizations = @($Optimization)
	KnownLimits = @($KnownLimit)
	CiState = $CiState
}

$jsonPath = Join-Path $OutputDirectory 'DeploymentSizingEvidence.json'
$evidence | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $jsonPath -Encoding utf8

$markdown = [System.Collections.Generic.List[string]]::new()
$markdown.Add('# Deployment Sizing Evidence')
$markdown.Add('')
$markdown.Add("- Commit: `$CommitSha`")
$markdown.Add("- Depot: `$DepotVersion`")
$markdown.Add("- Environment: $EnvironmentName")
$markdown.Add("- Provider: $Provider")
$markdown.Add("- Data profile: $DataProfile ($DataVolumeRecords records)")
$markdown.Add("- Network latency: $NetworkLatencyMs ms")
$markdown.Add("- Concurrent users: $ConcurrentUsers")
$markdown.Add("- CI: $CiState")
$markdown.Add('')
$markdown.Add('## Scenario metrics')
$markdown.Add('')
$markdown.Add('| Scenario | Samples | Pass | Fail | p50 ms | p95 ms | max ms | avg CPU % | peak working set MB |')
$markdown.Add('| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |')
foreach ($metric in $scenarioMetrics) {
	$markdown.Add("| $($metric.Scenario) | $($metric.Samples) | $($metric.Passed) | $($metric.Failed) | $($metric.P50Ms) | $($metric.P95Ms) | $($metric.MaxMs) | $($metric.AverageCpuPercent) | $($metric.PeakWorkingSetMb) |")
}
$markdown.Add('')
$markdown.Add('## Runtime signals')
$markdown.Add('')
if ($runtimeSignals.Count -eq 0) {
	$markdown.Add('No performance.log runtime signals were supplied.')
} else {
	$markdown.Add('| Signal | Detail | Samples | p50 ms | p95 ms |')
	$markdown.Add('| --- | --- | ---: | ---: | ---: |')
	foreach ($signal in $runtimeSignals) {
		$markdown.Add("| $($signal.Signal) | $($signal.Detail) | $($signal.Samples) | $($signal.P50Ms) | $($signal.P95Ms) |")
	}
}
$markdown.Add('')
$markdown.Add('## Missing required scenarios')
$markdown.Add('')
if ($missingScenarios.Count -eq 0) {
	$markdown.Add('None.')
} else {
	foreach ($scenario in $missingScenarios) { $markdown.Add("- $scenario") }
}
$markdown.Add('')
$markdown.Add('## Optimizations applied')
$markdown.Add('')
if ($Optimization.Count -eq 0) { $markdown.Add('None recorded.') } else { foreach ($item in $Optimization) { $markdown.Add("- $item") } }
$markdown.Add('')
$markdown.Add('## Known limits')
$markdown.Add('')
if ($KnownLimit.Count -eq 0) { $markdown.Add('None recorded.') } else { foreach ($item in $KnownLimit) { $markdown.Add("- $item") } }

$reportPath = Join-Path $OutputDirectory 'DeploymentSizingReport.md'
$markdown | Set-Content -LiteralPath $reportPath -Encoding utf8

Write-Host "Deployment sizing evidence: $jsonPath"
Write-Host "Deployment sizing report: $reportPath"

if ($RequireCompleteProfile -and $missingScenarios.Count -gt 0) {
	throw "Deployment sizing profile is incomplete. Missing: $($missingScenarios -join ', ')."
}
