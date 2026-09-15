<#
.SYNOPSIS
    Renders the winget manifests for a release.

.EXAMPLE
    ./Update-Manifests.ps1 -Version 1.0.0 -X64Sha256 ABC... -Arm64Sha256 DEF... -OutputDirectory ../../artifacts/winget
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $X64Sha256,
    [Parameter(Mandatory)] [string] $Arm64Sha256,
    [string] $OutputDirectory = "$PSScriptRoot/../../artifacts/winget",
    [string] $ReleaseBaseUrl = 'https://github.com/LeandroCannizzaro/LiveClaude/releases/download'
)

$ErrorActionPreference = 'Stop'

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$replacements = @{
    '{VERSION}'      = $Version
    '{X64_SHA256}'   = $X64Sha256.ToUpperInvariant()
    '{ARM64_SHA256}' = $Arm64Sha256.ToUpperInvariant()
    '{BASE_URL}'     = $ReleaseBaseUrl
    '{RELEASE_DATE}' = (Get-Date -Format 'yyyy-MM-dd')
}

Get-ChildItem -Path $PSScriptRoot -Filter '*.template.yaml' | ForEach-Object {
    $content = Get-Content $_.FullName -Raw
    foreach ($key in $replacements.Keys) {
        $content = $content.Replace($key, $replacements[$key])
    }

    $name = $_.Name -replace '\.template\.yaml$', '.yaml'
    $target = Join-Path $OutputDirectory $name
    Set-Content -Path $target -Value $content -Encoding utf8
    Write-Host "Wrote $target"
}
