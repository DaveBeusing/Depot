$ErrorActionPreference = "Stop"

Set-Location (Resolve-Path "$PSScriptRoot\..")

# Versions
[xml]$depotProps = Get-Content "Directory.Build.props"
$depot = $depotProps.Project.PropertyGroup
$depotVersion = "$($depot.DepotVersionMajor).$($depot.DepotVersionMinor).$($depot.DepotVersionPatch)"

[xml]$managerProps = Get-Content "src/DepotManager/DepotManager.Version.props"
$manager = $managerProps.Project.PropertyGroup
$managerVersion = "$($manager.DepotManagerVersionMajor).$($manager.DepotManagerVersionMinor).$($manager.DepotManagerVersionPatch)"

Write-Host "Depot:        $depotVersion"
Write-Host "DepotManager: $managerVersion"

$release = "artifacts\release"

Remove-Item $release -Recurse -Force -ErrorAction SilentlyContinue
New-Item $release -ItemType Directory | Out-Null

# Depot
dotnet publish src/Depot/Depot.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -p:DepotStableRelease=true `
    -o "$release\depot"

if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# DepotManager
dotnet publish src/DepotManager/DepotManager.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -p:DepotManagerStableRelease=true `
    -o "$release\manager"

if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Release assets
Copy-Item "$release\depot\Depot.exe" `
    "$release\Depot-$depotVersion.exe"

Copy-Item "$release\manager\DepotManager.exe" `
    "$release\DepotManager-$managerVersion.exe"

# Database schema version
$schema = Select-String `
    "src/Depot/Data/DatabaseVersion.cs" `
    -Pattern 'CurrentVersion\s*=\s*(\d+)' `
    | Select-Object -First 1

if ($null -eq $schema -or $schema.Matches.Count -eq 0) {
    throw "DatabaseVersion.CurrentVersion could not be determined."
}

$schemaVersion = [int]$schema.Matches[0].Groups[1].Value

# Update manifest
[ordered]@{
    depotVersion           = $depotVersion
    depotManagerVersion    = $managerVersion
    databaseSchemaVersion  = $schemaVersion
    managerCommandProtocol = 1
} |
ConvertTo-Json |
Set-Content "$release\Depot-$depotVersion.manifest.json" -Encoding utf8

# GitHub release
gh release create $depotVersion `
    "$release\Depot-$depotVersion.exe" `
    "$release\Depot-$depotVersion.manifest.json" `
    "$release\DepotManager-$managerVersion.exe" `
    --title $depotVersion `
    --generate-notes

if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ""
Write-Host "Release $depotVersion created." -ForegroundColor Green
