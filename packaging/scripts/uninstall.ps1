<#
.SYNOPSIS
  Uninstalls a portable ZIP installation of LiveClaude.
#>
[CmdletBinding()]
param(
    [switch]$AllUsers,
    [string]$InstallDir
)

$ErrorActionPreference = 'Stop'

$identity = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
$isAdmin = $identity.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if ($AllUsers -and -not $isAdmin) { throw '-AllUsers requires Administrator privileges.' }

if (-not $InstallDir) {
    $InstallDir = if ($AllUsers) { Join-Path $env:ProgramFiles 'LiveClaude' } else { Join-Path $env:LOCALAPPDATA 'Programs\LiveClaude' }
}

$service = Get-Service -Name LiveClaude -ErrorAction SilentlyContinue
if ($service -and $isAdmin) {
    if ($service.Status -eq 'Running') { Stop-Service LiveClaude -Force }
}

Get-Process -Name 'LiveClaude', 'LiveClaude.Service' -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and $_.Path.StartsWith($InstallDir, [StringComparison]::OrdinalIgnoreCase) } |
    ForEach-Object { $_ | Stop-Process -Force; $_.WaitForExit(10000) | Out-Null }

$programs = [Environment]::GetFolderPath($(if ($AllUsers) { 'CommonPrograms' } else { 'Programs' }))
Remove-Item (Join-Path $programs 'LiveClaude.lnk') -Force -ErrorAction SilentlyContinue

$runKey = if ($AllUsers) { 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run' } else { 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' }
Remove-ItemProperty $runKey -Name LiveClaude -ErrorAction SilentlyContinue

$uninstallKey = if ($AllUsers) { 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\LiveClaude' } else { 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\LiveClaude' }
Remove-Item $uninstallKey -Recurse -Force -ErrorAction SilentlyContinue

if (Test-Path $InstallDir) {
    Remove-Item $InstallDir -Recurse -Force
}
