<#
.SYNOPSIS
  Publishes LiveClaude.App + LiveClaude.Service self-contained (win-x64) into <Output>\app.
.EXAMPLE
  .\packaging\publish.ps1
  .\packaging\publish.ps1 -Output C:\temp\package
#>
param(
    [string]$Output = (Join-Path $PSScriptRoot '..\artifacts\package'),
    [string]$Configuration = 'Release',
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$Output = [IO.Path]::GetFullPath($Output)
$app = Join-Path $Output 'app'

if (Test-Path $Output) { Remove-Item $Output -Recurse -Force }
New-Item $app -ItemType Directory | Out-Null

$common = @('-c', $Configuration, '-r', 'win-x64', '--self-contained', '-p:DebugType=none', '-p:DebugSymbols=false', '--nologo', '-v', 'q')
if ($Version) { $common += "-p:Version=$Version" }

dotnet publish (Join-Path $root 'src\LiveClaude.App\LiveClaude.App.csproj') @common -o $app
if ($LASTEXITCODE) { throw 'LiveClaude.App publish failed' }

dotnet publish (Join-Path $root 'src\LiveClaude.Service\LiveClaude.Service.csproj') @common -o $app
if ($LASTEXITCODE) { throw 'LiveClaude.Service publish failed' }

Get-ChildItem $app -Recurse -Filter *.pdb | Remove-Item -Force

$size = (Get-ChildItem $app -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
Write-Host ("Package ready in {0} ({1:N0} MB)" -f $Output, $size)
