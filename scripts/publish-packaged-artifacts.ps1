param(
    [string]$SourceRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [string]$OutputRoot = (Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..')).Path 'artifacts\packaged'),
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
$source = (Resolve-Path $SourceRoot).Path
$output = [IO.Path]::GetFullPath($OutputRoot)
$depotProject = Join-Path $source 'src\Depot\Depot.csproj'
$managerProject = Join-Path $source 'src\DepotManager\DepotManager.csproj'
$depotOutput = Join-Path $output 'depot'
$managerOutput = Join-Path $output 'manager'

foreach ($directory in @($depotOutput, $managerOutput)) {
    if (Test-Path $directory) { Remove-Item $directory -Recurse -Force }
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}

if (-not $NoRestore) {
    & dotnet restore $depotProject --runtime win-x64 -p:AuditPipeline=true
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & dotnet restore $managerProject --runtime win-x64 -p:AuditPipeline=true
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

$publishProperties = @(
    '--configuration', 'Release',
    '--runtime', 'win-x64',
    '--self-contained', 'true',
    '--no-restore',
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:DebugType=None',
    '-p:DebugSymbols=false'
)

& dotnet publish $depotProject @publishProperties -o $depotOutput
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& dotnet publish $managerProject @publishProperties -o $managerOutput
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$expected = @(
    (Join-Path $depotOutput 'Depot.exe'),
    (Join-Path $managerOutput 'DepotManager.exe')
)
foreach ($file in $expected) {
    if (-not (Test-Path $file -PathType Leaf)) { throw "Expected packaged artifact was not produced: $file" }
}

foreach ($directory in @($depotOutput, $managerOutput)) {
    $files = @(Get-ChildItem $directory -File)
    if ($files.Count -ne 1) {
        throw "Packaged output '$directory' must contain exactly one shipped single-file executable, but contains $($files.Count) files."
    }
}

Write-Host "Packaged Depot artifact: $($expected[0])"
Write-Host "Packaged Depot Manager artifact: $($expected[1])"
