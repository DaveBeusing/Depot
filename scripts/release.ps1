param(
    [ValidateSet('Preview', 'Stable')]
    [string]$Channel = 'Preview',
    [switch]$AcceptanceOnly
)

$ErrorActionPreference = 'Stop'
Set-Location (Resolve-Path "$PSScriptRoot\..")

if ($AcceptanceOnly -and $Channel -ne 'Stable') {
    throw 'AcceptanceOnly is reserved for the production-signed Stable release-candidate acceptance run.'
}

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw 'GitHub CLI (gh) is required to dispatch the authoritative release workflow.'
}

$status = @(git status --porcelain)
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if ($status.Count -ne 0) {
    throw 'The working tree must be clean before requesting a release.'
}

$branch = (git branch --show-current).Trim()
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if ($branch -ne 'master') {
    throw "Release requests must be dispatched from master. Current branch: $branch"
}

git fetch origin master --no-tags
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$local = (git rev-parse HEAD).Trim()
$remote = (git rev-parse origin/master).Trim()
if ($local -ne $remote) {
    throw "Local master $local does not match origin/master $remote. Pull the current master before requesting a release."
}

gh auth status
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$publishRelease = if ($AcceptanceOnly) { 'false' } else { 'true' }
gh workflow run release-integrity.yml --ref master -f "channel=$Channel" -f "publish_release=$publishRelease"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ""
if ($AcceptanceOnly) {
    Write-Host "Production-signed Stable release-candidate acceptance requested for source $local." -ForegroundColor Green
    Write-Host 'The workflow will build, sign, timestamp, verify publisher identity, run packaged RC E2E and retain evidence without publishing a GitHub Release.'
}
else {
    Write-Host "Authoritative $Channel release pipeline requested for source $local." -ForegroundColor Green
    Write-Host 'The GitHub Actions workflow performs build, tests, packaging, signing policy, manifest/hashes, evidence and release publication.'
}
