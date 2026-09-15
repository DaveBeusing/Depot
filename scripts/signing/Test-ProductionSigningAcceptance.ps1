param(
    [Parameter(Mandatory = $true)]
    [string]$DepotPath,
    [Parameter(Mandatory = $true)]
    [string]$DepotManagerPath,
    [Parameter(Mandatory = $true)]
    [string]$ExpectedPublisherSubject,
    [Parameter(Mandatory = $true)]
    [string]$SourceSha,
    [Parameter(Mandatory = $true)]
    [string]$ReleaseTag,
    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$codeSigningOid = '1.3.6.1.5.5.7.3.3'

function Get-ProductionSignatureEvidence {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$ExpectedSubject
    )

    $resolved = (Resolve-Path -LiteralPath $Path).Path
    $signature = Get-AuthenticodeSignature -LiteralPath $resolved
    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
        throw "Authenticode status for '$resolved' is '$($signature.Status)': $($signature.StatusMessage)"
    }

    $certificate = $signature.SignerCertificate
    if ($null -eq $certificate) {
        throw "No signer certificate was returned for '$resolved'."
    }
    if (-not [string]::Equals($certificate.Subject, $ExpectedSubject, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Signer '$($certificate.Subject)' does not match expected production publisher '$ExpectedSubject'."
    }

    $hasCodeSigningEku = $false
    foreach ($extension in $certificate.Extensions) {
        if ($extension -is [System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]) {
            foreach ($oid in $extension.EnhancedKeyUsages) {
                if ($oid.Value -eq $codeSigningOid) {
                    $hasCodeSigningEku = $true
                    break
                }
            }
        }
        if ($hasCodeSigningEku) { break }
    }
    if (-not $hasCodeSigningEku) {
        throw "Signer certificate for '$resolved' does not contain the Code Signing EKU."
    }

    $timestampCertificate = $signature.TimeStamperCertificate
    if ($null -eq $timestampCertificate) {
        throw "A trusted RFC 3161 timestamp was not detected for '$resolved'."
    }

    [ordered]@{
        file = [IO.Path]::GetFileName($resolved)
        size = (Get-Item -LiteralPath $resolved).Length
        sha256 = (Get-FileHash -LiteralPath $resolved -Algorithm SHA256).Hash.ToLowerInvariant()
        authenticodeStatus = $signature.Status.ToString()
        publisherSubject = $certificate.Subject
        publisherIssuer = $certificate.Issuer
        publisherThumbprint = $certificate.Thumbprint
        publisherNotBeforeUtc = $certificate.NotBefore.ToUniversalTime().ToString('O')
        publisherNotAfterUtc = $certificate.NotAfter.ToUniversalTime().ToString('O')
        codeSigningEku = $true
        timestampSubject = $timestampCertificate.Subject
        timestampIssuer = $timestampCertificate.Issuer
        timestampThumbprint = $timestampCertificate.Thumbprint
        timestampNotBeforeUtc = $timestampCertificate.NotBefore.ToUniversalTime().ToString('O')
        timestampNotAfterUtc = $timestampCertificate.NotAfter.ToUniversalTime().ToString('O')
    }
}

if ([string]::IsNullOrWhiteSpace($ExpectedPublisherSubject)) {
    throw 'ExpectedPublisherSubject is required for production signing acceptance.'
}
if ($SourceSha -notmatch '^[0-9a-fA-F]{40}$') {
    throw 'SourceSha must be a full 40-character Git commit SHA.'
}
if ([string]::IsNullOrWhiteSpace($ReleaseTag)) {
    throw 'ReleaseTag is required.'
}

$depot = Get-ProductionSignatureEvidence -Path $DepotPath -ExpectedSubject $ExpectedPublisherSubject
$manager = Get-ProductionSignatureEvidence -Path $DepotManagerPath -ExpectedSubject $ExpectedPublisherSubject

if (-not [string]::Equals($depot.publisherThumbprint, $manager.publisherThumbprint, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Depot.exe and DepotManager.exe were not signed by the same production certificate.'
}

$report = [ordered]@{
    formatVersion = 1
    acceptanceStatus = 'PASS'
    validatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    sourceSha = $SourceSha.ToLowerInvariant()
    releaseTag = $ReleaseTag
    expectedPublisherSubject = $ExpectedPublisherSubject
    signerCertificateThumbprint = $depot.publisherThumbprint
    artifacts = [ordered]@{
        depot = $depot
        depotManager = $manager
    }
}

$outputFullPath = [IO.Path]::GetFullPath($OutputPath)
$outputDirectory = [IO.Path]::GetDirectoryName($outputFullPath)
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
}
$report | ConvertTo-Json -Depth 8 | Out-File -LiteralPath $outputFullPath -Encoding utf8
Write-Host "Production signing acceptance PASS: $outputFullPath"
