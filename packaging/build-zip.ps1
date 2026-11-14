<#
.SYNOPSIS
  Builds the portable Windows ZIP of LiveClaude: published "app" folder plus install/uninstall scripts.
.EXAMPLE
  .\packaging\build-zip.ps1
  .\packaging\build-zip.ps1 -Version 2.2.0 -Package .\artifacts\package\app -Output .\artifacts
#>
param(
    [string]$Version,
    [string]$Package,
    [string]$Output = (Join-Path $PSScriptRoot '..\artifacts'),
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$Output = [IO.Path]::GetFullPath($Output)

if (-not $Version) {
    $Version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup.VersionPrefix | Where-Object { $_ } | Select-Object -First 1
}
if ($Version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') { throw "Invalid ZIP version: '$Version' (expected x.y.z)" }

if (-not $Package -or -not (Test-Path (Join-Path $Package 'LiveClaude.exe'))) {
    $packageRoot = Join-Path $root 'artifacts\package'
    & (Join-Path $PSScriptRoot 'publish.ps1') -Output $packageRoot -Configuration $Configuration -Version $Version
    $Package = Join-Path $packageRoot 'app'
}
$Package = [IO.Path]::GetFullPath($Package)
if (-not (Test-Path (Join-Path $Package 'LiveClaude.exe'))) { throw "Invalid package: missing $Package\LiveClaude.exe" }

$staging = Join-Path ([IO.Path]::GetTempPath()) ("lc-zip-" + [Guid]::NewGuid().ToString('N'))
try {
    New-Item $staging -ItemType Directory | Out-Null
    foreach ($script in 'install.ps1', 'uninstall.ps1') {
        Copy-Item (Join-Path $PSScriptRoot "scripts\$script") $staging -Force
    }
    Copy-Item $Package (Join-Path $staging 'app') -Recurse -Force
    Copy-Item (Join-Path $PSScriptRoot 'count-installation.ps1') (Join-Path $staging 'app') -Force

    New-Item $Output -ItemType Directory -Force | Out-Null
    $zip = Join-Path $Output "LiveClaude-$Version-win-x64.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip -CompressionLevel Optimal
    Write-Host ("ZIP ready: {0} ({1:N0} MB)" -f $zip, ((Get-Item $zip).Length / 1MB))
}
finally {
    Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
}
