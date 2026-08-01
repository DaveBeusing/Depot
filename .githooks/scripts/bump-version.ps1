param(
    [ValidateSet("Build", "Patch", "Minor", "Major")]
    [string]$Part = "Build",

    [string]$PropsFile = "Directory.Build.props"
)

$ErrorActionPreference = "Stop"

function Get-RepositoryRoot {
    $root = git rev-parse --show-toplevel 2>$null

    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($root)) {
        throw "Git repository root could not be determined."
    }

    return $root.Trim()
}

function Get-VersionValue {
    param(
        [Parameter(Mandatory)]
        [string]$Content,

        [Parameter(Mandatory)]
        [string]$Element
    )

    $pattern = "<$Element(?:\s+[^>]*)?>\s*(\d+)\s*</$Element>"
    $match = [regex]::Match($Content, $pattern)

    if (-not $match.Success) {
        throw "Element <$Element> was not found or does not contain a valid number."
    }

    return [int]$match.Groups[1].Value
}

function Set-VersionValue {
    param(
        [Parameter(Mandatory)]
        [string]$Content,

        [Parameter(Mandatory)]
        [string]$Element,

        [Parameter(Mandatory)]
        [int]$Value
    )

    $pattern = "(<$Element(?:\s+[^>]*)?>\s*)\d+(\s*</$Element>)"

    if (-not [regex]::IsMatch($Content, $pattern)) {
        throw "Element <$Element> could not be updated."
    }

    return [regex]::Replace(
        $Content,
        $pattern,
        "`${1}$Value`${2}",
        1
    )
}

function Get-DepotVersion {
    param(
        [Parameter(Mandatory)]
        [string]$Content
    )

    return [pscustomobject]@{
        Major = Get-VersionValue -Content $Content -Element "DepotVersionMajor"
        Minor = Get-VersionValue -Content $Content -Element "DepotVersionMinor"
        Patch = Get-VersionValue -Content $Content -Element "DepotVersionPatch"
        Build = Get-VersionValue -Content $Content -Element "DepotVersionBuild"
    }
}

function Format-DepotVersion {
    param(
        [Parameter(Mandatory)]
        $Version
    )

    return "$($Version.Major).$($Version.Minor).$($Version.Patch).$($Version.Build)"
}

$repositoryRoot = Get-RepositoryRoot
$propsPath = Join-Path $repositoryRoot $PropsFile

if (-not (Test-Path -LiteralPath $propsPath)) {
    throw "Version file not found: $propsPath"
}

$currentContent = [System.IO.File]::ReadAllText($propsPath)
$currentVersion = Get-DepotVersion -Content $currentContent

# Read the committed version from HEAD.
# If the working version is already different, a previous hook run or a
# manual version change has already taken place. Do not increment again.
$headContentLines = git show "HEAD:$PropsFile" 2>$null
$headContent = $headContentLines -join [Environment]::NewLine

if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($headContent)) {
    $headVersion = Get-DepotVersion -Content $headContent

    $currentFormatted = Format-DepotVersion -Version $currentVersion
    $headFormatted = Format-DepotVersion -Version $headVersion

    if ($currentFormatted -ne $headFormatted) {
        Write-Host "Depot version already changed: $headFormatted -> $currentFormatted"
        Write-Host "No additional version increment required."
        exit 0
    }
}

$newMajor = $currentVersion.Major
$newMinor = $currentVersion.Minor
$newPatch = $currentVersion.Patch
$newBuild = $currentVersion.Build

switch ($Part) {
    "Build" {
        $newBuild++
    }

    "Patch" {
        $newPatch++
        $newBuild = 0
    }

    "Minor" {
        $newMinor++
        $newPatch = 0
        $newBuild = 0
    }

    "Major" {
        $newMajor++
        $newMinor = 0
        $newPatch = 0
        $newBuild = 0
    }
}

$newContent = $currentContent
$newContent = Set-VersionValue -Content $newContent `
    -Element "DepotVersionMajor" -Value $newMajor
$newContent = Set-VersionValue -Content $newContent `
    -Element "DepotVersionMinor" -Value $newMinor
$newContent = Set-VersionValue -Content $newContent `
    -Element "DepotVersionPatch" -Value $newPatch
$newContent = Set-VersionValue -Content $newContent `
    -Element "DepotVersionBuild" -Value $newBuild

$utf8WithoutBom = [System.Text.UTF8Encoding]::new($false)

[System.IO.File]::WriteAllText(
    $propsPath,
    $newContent,
    $utf8WithoutBom
)

$oldFormatted = Format-DepotVersion -Version $currentVersion
$newFormatted = "$newMajor.$newMinor.$newPatch.$newBuild"

Write-Host "Depot version updated: $oldFormatted -> $newFormatted"
Write-Host "Incremented component: $Part"