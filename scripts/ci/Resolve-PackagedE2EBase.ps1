param(
    [Parameter(Mandatory)][string]$BaseRef,
    [string]$SourceRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
)

$ErrorActionPreference = 'Stop'

function Read-ProductVersion([string]$Ref, [string]$Path, [string]$Prefix) {
    $text = & git -C $SourceRoot show "${Ref}:$Path"
    if ($LASTEXITCODE -ne 0) { throw "Cannot read $Path at $Ref." }
    [xml]$document = $text -join [Environment]::NewLine
    $properties = $document.Project.PropertyGroup
    return [version]::new(
        [int]$properties."${Prefix}VersionMajor",
        [int]$properties."${Prefix}VersionMinor",
        [int]$properties."${Prefix}VersionPatch")
}

$currentDepot = Read-ProductVersion HEAD 'Directory.Build.props' 'Depot'
$currentManager = Read-ProductVersion HEAD 'src/DepotManager/DepotManager.Version.props' 'DepotManager'
# Ordinary commits may share a release version. Keep the real source/version
# evidence and find an older version rather than weakening the upgrade checks.
$candidates = & git -C $SourceRoot rev-list --first-parent $BaseRef -- Directory.Build.props
if ($LASTEXITCODE -ne 0) { throw "Cannot enumerate packaged E2E base history." }
foreach ($candidate in $candidates) {
    $depot = Read-ProductVersion $candidate 'Directory.Build.props' 'Depot'
    if ($depot -ge $currentDepot) { continue }
    $manager = Read-ProductVersion $candidate 'src/DepotManager/DepotManager.Version.props' 'DepotManager'
    if ($manager -gt $currentManager) { continue }
    return $candidate
}
throw "No ancestor of $BaseRef has an older Depot version and a compatible DepotManager version. Fetch full history before packaged E2E."
