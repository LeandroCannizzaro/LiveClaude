<#
.SYNOPSIS
  Prepares and validates release notes and the user manual before publishing a release.
.DESCRIPTION
  Without -Check, lists all changes since the last tag.
  With -Check (used by the Release workflow), fails if:
    - docs\release-notes.html does not contain section id="vX.Y.Z";
    - docs\index.html has not been updated since the last tag.
#>
param(
    [string]$Version,
    [switch]$Check
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$notes = Join-Path $root 'docs\release-notes.html'
$manual = 'docs/index.html'

if (-not $Version) {
    $Version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup.VersionPrefix | Where-Object { $_ } | Select-Object -First 1
}

$tag = "v$Version"
$headTags = @(git -C $root tag --points-at HEAD)
$from = if ($headTags -contains $tag) { 'HEAD^' } else { 'HEAD' }
$previous = git -C $root describe --tags --abbrev=0 --match 'v*' $from 2>$null
if ($LASTEXITCODE) { $previous = $null }
$range = if ($previous) { "$previous..HEAD" } else { 'HEAD' }

if (-not $Check) {
    Write-Host "Version $Version - changes since $(if ($previous) { $previous } else { 'beginning' })" -ForegroundColor Cyan
    git -C $root --no-pager log $range --reverse --format='%n=== %h %ad %s%n%b' --date=short
    Write-Host "`nModified files:" -ForegroundColor Cyan
    git -C $root --no-pager diff --stat $range
    Write-Host "`nUncommitted:" -ForegroundColor Cyan
    git -C $root status --short
    Write-Host "`nAdd section id=`"$tag`" in docs\release-notes.html and update docs\index.html." -ForegroundColor Yellow
    return
}

$problems = @()
if (-not (Select-String -Path $notes -Pattern "id=`"$([regex]::Escape($tag))`"" -Quiet)) {
    $problems += "docs/release-notes.html is missing section id=`"$tag`": add release notes for version $Version."
}

if ($previous) {
    git -C $root diff --quiet $previous HEAD -- $manual
    if ($LASTEXITCODE -eq 0) {
        $problems += "User manual ($manual) has not changed since ${previous}: update it with the changes in version $Version."
    }
}

if ($problems) {
    $problems | ForEach-Object { Write-Host "::error::$_" }
    throw "Release notes or user manual not ready for version $Version."
}

Write-Host "Release notes and user manual verified for version $Version."
exit 0
