param(
    [ValidateSet('Preview', 'Stable')]
    [string]$Channel = 'Preview'
)

$ErrorActionPreference = 'Stop'
Set-Location (Resolve-Path "$PSScriptRoot\..")

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

gh workflow run release-integrity.yml --ref master -f "channel=$Channel"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ""
Write-Host "Authoritative $Channel release pipeline requested for source $local." -ForegroundColor Green
Write-Host "The GitHub Actions workflow performs build, tests, packaging, signing policy, manifest/hashes, evidence and release publication."
