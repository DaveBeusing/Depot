param(
    [Parameter(Mandatory = $true)]
    [string]$ProfilePath,
    [switch]$AllowTemplate,
    [string]$EvidencePath = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Require-PositiveInteger($Value, [string]$Name) {
    if ($null -eq $Value -or [int]$Value -le 0) { throw "$Name must be a positive integer." }
    return [int]$Value
}

function Require-Text($Value, [string]$Name) {
    $text = [string]$Value
    if ([string]::IsNullOrWhiteSpace($text)) { throw "$Name must be configured." }
    return $text.Trim()
}

$fullPath = (Resolve-Path $ProfilePath).Path
$profile = Get-Content $fullPath -Raw | ConvertFrom-Json

if ([int]$profile.formatVersion -ne 1) { throw 'Unsupported disaster recovery profile formatVersion.' }
$status = Require-Text $profile.status 'status'
if ($status -notin @('TEMPLATE', 'ACTIVE')) { throw "status must be TEMPLATE or ACTIVE." }
if ($status -eq 'TEMPLATE' -and -not $AllowTemplate) {
    throw 'A TEMPLATE disaster recovery profile cannot satisfy production readiness. Create an ACTIVE deployment profile.'
}

$deploymentId = Require-Text $profile.deploymentId 'deploymentId'
$provider = Require-Text $profile.provider 'provider'
if ($provider -notin @('SQLite', 'SqlServer', 'MariaDB', 'MySQL')) {
    throw "Unsupported provider '$provider'."
}

$rpo = Require-PositiveInteger $profile.objectives.rpoMinutes 'objectives.rpoMinutes'
$rto = Require-PositiveInteger $profile.objectives.rtoMinutes 'objectives.rtoMinutes'
$retention = Require-PositiveInteger $profile.backupPolicy.retentionDays 'backupPolicy.retentionDays'
$offHost = Require-PositiveInteger $profile.backupPolicy.minimumOffHostCopies 'backupPolicy.minimumOffHostCopies'
$drillAge = Require-PositiveInteger $profile.restoreDrill.maximumAgeDays 'restoreDrill.maximumAgeDays'

if ($profile.backupPolicy.encryptionAtRestRequired -ne $true) {
    throw 'backupPolicy.encryptionAtRestRequired must be true for a production DR profile.'
}
if ($profile.backupPolicy.backupMonitoringRequired -ne $true) {
    throw 'backupPolicy.backupMonitoringRequired must be true for a production DR profile.'
}
if ($profile.restoreDrill.isolatedTargetRequired -ne $true) {
    throw 'restoreDrill.isolatedTargetRequired must be true. Production data must never be overwritten by a routine restore drill.'
}

$owners = [ordered]@{
    backupOwner = Require-Text $profile.ownership.backupOwner 'ownership.backupOwner'
    restoreOwner = Require-Text $profile.ownership.restoreOwner 'ownership.restoreOwner'
    applicationSupportOwner = Require-Text $profile.ownership.applicationSupportOwner 'ownership.applicationSupportOwner'
    escalationOwner = Require-Text $profile.ownership.escalationOwner 'ownership.escalationOwner'
}

if ($status -eq 'ACTIVE') {
    foreach ($value in @($deploymentId) + @($owners.Values)) {
        if ($value -match 'CHANGE-ME|ASSIGN|TBD|TODO|EXAMPLE') {
            throw 'ACTIVE profiles must not contain placeholder ownership or deployment values.'
        }
    }
}

$evidence = [ordered]@{
    formatVersion = 1
    result = 'PASS'
    profileStatus = $status
    deploymentId = $deploymentId
    provider = $provider
    rpoMinutes = $rpo
    rtoMinutes = $rto
    retentionDays = $retention
    minimumOffHostCopies = $offHost
    encryptionAtRestRequired = $true
    backupMonitoringRequired = $true
    restoreDrillMaximumAgeDays = $drillAge
    isolatedTargetRequired = $true
    contractualCommitment = [bool]$profile.objectives.contractualCommitment
    generatedUtc = [DateTimeOffset]::UtcNow.ToString('O')
}

if (-not [string]::IsNullOrWhiteSpace($EvidencePath)) {
    $evidenceFullPath = [IO.Path]::GetFullPath($EvidencePath)
    $directory = Split-Path $evidenceFullPath -Parent
    if (-not [string]::IsNullOrWhiteSpace($directory)) { New-Item -ItemType Directory -Force $directory | Out-Null }
    $evidence | ConvertTo-Json -Depth 5 | Set-Content $evidenceFullPath -Encoding utf8
}

Write-Host "Disaster recovery profile '$deploymentId' validated: status=$status provider=$provider RPO=${rpo}m RTO=${rto}m retention=${retention}d offHostCopies=$offHost."
