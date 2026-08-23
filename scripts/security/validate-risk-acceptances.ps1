# Copyright (c) 2026 David Beusing
# Licensed under the MIT License.

[CmdletBinding()]
param(
    [string]$Path = "security/security-risk-acceptances.json"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
    throw "Security risk-acceptance register was not found: $Path"
}

try {
    $document = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json -Depth 20
}
catch {
    throw "Security risk-acceptance register is not valid JSON: $($_.Exception.Message)"
}

if ($document.schemaVersion -ne 1) {
    throw "Unsupported security risk-acceptance schemaVersion '$($document.schemaVersion)'. Expected 1."
}

$exceptions = @($document.exceptions)
$ids = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
$allowedSeverities = @("High", "Medium", "Low")
$now = [DateTimeOffset]::UtcNow

function Require-Text([object]$entry, [string]$name) {
    $property = $entry.PSObject.Properties[$name]
    if ($null -eq $property -or [string]::IsNullOrWhiteSpace([string]$property.Value)) {
        throw "Risk acceptance is missing required non-empty field '$name'."
    }
    return ([string]$property.Value).Trim()
}

foreach ($entry in $exceptions) {
    if ($null -eq $entry) { throw "Risk-acceptance entries must not be null." }

    $id = Require-Text $entry "id"
    if (-not $ids.Add($id)) { throw "Duplicate security risk-acceptance id '$id'." }

    $severity = Require-Text $entry "severity"
    if ($severity -eq "Critical") {
        throw "Critical vulnerability risk acceptance '$id' is prohibited for production release."
    }
    if ($severity -notin $allowedSeverities) {
        throw "Risk acceptance '$id' has invalid severity '$severity'. Allowed: High, Medium, Low."
    }

    foreach ($field in @("title", "owner", "rationale", "compensatingControls", "approvedBy", "affectedVersions", "expiresUtc")) {
        [void](Require-Text $entry $field)
    }

    $activeProperty = $entry.PSObject.Properties["activelyExploited"]
    if ($null -eq $activeProperty -or $activeProperty.Value -isnot [bool]) {
        throw "Risk acceptance '$id' must declare activelyExploited as a boolean."
    }
    if ($activeProperty.Value -eq $true) {
        throw "Actively exploited vulnerability '$id' cannot be released through the normal risk-acceptance mechanism."
    }

    $expiryText = [string]$entry.expiresUtc
    $expiry = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParse($expiryText, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AssumeUniversal, [ref]$expiry)) {
        throw "Risk acceptance '$id' has invalid expiresUtc '$expiryText'. Use an ISO-8601 UTC timestamp."
    }
    if ($expiry -le $now) {
        throw "Risk acceptance '$id' expired at $($expiry.ToUniversalTime().ToString('O'))."
    }

    if ($severity -eq "High") {
        $reviewProperty = $entry.PSObject.Properties["reviewedBySecurity"]
        if ($null -eq $reviewProperty -or $reviewProperty.Value -ne $true) {
            throw "High risk acceptance '$id' requires reviewedBySecurity=true."
        }
    }
}

Write-Host "Validated $($exceptions.Count) active security risk acceptance(s)."
