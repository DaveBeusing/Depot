$ErrorActionPreference = 'Stop'
$resolver = Join-Path $PSScriptRoot 'Resolve-PackagedE2EBase.ps1'
$fixture = Join-Path ([IO.Path]::GetTempPath()) ("DepotE2EBase-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $fixture 'src/DepotManager') -Force | Out-Null

function Invoke-FixtureGit([string[]]$Arguments) {
    $result = & git -C $fixture @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Fixture git command failed: $Arguments" }
    return $result
}

function Add-VersionCommit([int]$DepotPatch, [int]$ManagerPatch) {
    "<Project><PropertyGroup><DepotVersionMajor>0</DepotVersionMajor><DepotVersionMinor>15</DepotVersionMinor><DepotVersionPatch>$DepotPatch</DepotVersionPatch></PropertyGroup></Project>" |
        Set-Content (Join-Path $fixture 'Directory.Build.props')
    "<Project><PropertyGroup><DepotManagerVersionMajor>0</DepotManagerVersionMajor><DepotManagerVersionMinor>1</DepotManagerVersionMinor><DepotManagerVersionPatch>$ManagerPatch</DepotManagerVersionPatch></PropertyGroup></Project>" |
        Set-Content (Join-Path $fixture 'src/DepotManager/DepotManager.Version.props')
    Invoke-FixtureGit @('add', '.') | Out-Null
    Invoke-FixtureGit @('-c', 'user.name=E2E fixture', '-c', 'user.email=e2e@example.test', '-c', 'core.hooksPath=NUL', 'commit', '--allow-empty', '-m', "Version $DepotPatch / $ManagerPatch") | Out-Null
    return Invoke-FixtureGit @('rev-parse', 'HEAD')
}

try {
    Invoke-FixtureGit @('init', '--initial-branch=main') | Out-Null
    $old = Add-VersionCommit 1 1
    $failedClosed = $false
    try { & $resolver -SourceRoot $fixture -BaseRef HEAD | Out-Null }
    catch {
        if ($_.Exception.Message -notlike 'No ancestor*') { throw }
        $failedClosed = $true
    }
    if (-not $failedClosed) { throw 'Resolver accepted a history with no older product version.' }

    $new = Add-VersionCommit 2 1
    $selected = & $resolver -SourceRoot $fixture -BaseRef 'HEAD^'
    if ($selected -ne $old) { throw 'Resolver did not select the immediate older product version.' }

    Add-VersionCommit 2 1 | Out-Null
    $selected = & $resolver -SourceRoot $fixture -BaseRef 'HEAD^'
    if ($selected -ne $old) { throw 'Resolver did not skip an equal-version base commit.' }

    Add-VersionCommit 3 2 | Out-Null
    Add-VersionCommit 4 1 | Out-Null
    $selected = & $resolver -SourceRoot $fixture -BaseRef 'HEAD^'
    if ($selected -ne $new) { throw 'Resolver did not skip a newer manager version.' }
    Write-Host 'Packaged E2E baseline regression scenarios passed.'
}
finally {
    $resolvedFixture = [IO.Path]::GetFullPath($fixture)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $resolvedFixture.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolvedFixture) -notlike 'DepotE2EBase-*') {
        throw 'Refusing to remove an unexpected fixture path.'
    }
    Remove-Item -LiteralPath $resolvedFixture -Recurse -Force
}
