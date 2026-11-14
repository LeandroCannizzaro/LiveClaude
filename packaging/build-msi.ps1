<#
.SYNOPSIS
  Builds the LiveClaude MSI (unsigned): publishes app and service self-contained x64 and packages them with WiX v5.
.EXAMPLE
  .\packaging\build-msi.ps1
  .\packaging\build-msi.ps1 -Version 2.2.0 -Output .\artifacts
#>
param(
    [string]$Version,
    [string]$Output = (Join-Path $PSScriptRoot '..\artifacts'),
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$Output = [IO.Path]::GetFullPath($Output)

if (-not $Version) {
    $Version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup.VersionPrefix | Where-Object { $_ } | Select-Object -First 1
}
if ($Version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') { throw "Invalid MSI version: '$Version' (expected x.y.z)" }

$package = Join-Path $root 'artifacts\package'
& (Join-Path $PSScriptRoot 'publish.ps1') -Output $package -Configuration $Configuration -Version $Version

dotnet build (Join-Path $PSScriptRoot 'LiveClaude.Setup') -c $Configuration --nologo -v q `
    -p:AppSource="$package\app" -p:ProductVersion=$Version -p:OutDir="$package\msi"
if ($LASTEXITCODE) { throw 'MSI build failed' }

New-Item $Output -ItemType Directory -Force | Out-Null
$msi = Join-Path $Output "LiveClaude-$Version-x64.msi"
Copy-Item (Join-Path $package 'msi\LiveClaude.msi') $msi -Force
Write-Host ("MSI ready: {0} ({1:N0} MB)" -f $msi, ((Get-Item $msi).Length / 1MB))
