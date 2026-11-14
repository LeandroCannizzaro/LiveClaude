<#
.SYNOPSIS
  Installs LiveClaude from the portable ZIP package (expects an "app" folder next to this script).
.PARAMETER AllUsers
  Install for all users under %ProgramFiles%\LiveClaude (requires Administrator).
.PARAMETER Autostart
  Start LiveClaude at user sign-in.
.PARAMETER NoStart
  Do not launch LiveClaude after installation.
.PARAMETER NoInstallCounter
  Do not send the anonymous installation ping to Hits.sh.
#>
[CmdletBinding()]
param(
    [switch]$AllUsers,
    [switch]$Autostart,
    [string]$InstallDir,
    [switch]$NoStart,
    [switch]$NoInstallCounter
)

$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot 'app'
if (-not (Test-Path (Join-Path $source 'LiveClaude.exe'))) { throw "Invalid package: missing $source\LiveClaude.exe" }

$identity = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
$isAdmin = $identity.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if ($AllUsers -and -not $isAdmin) { throw '-AllUsers requires Administrator privileges.' }

if (-not $InstallDir) {
    $InstallDir = if ($AllUsers) { Join-Path $env:ProgramFiles 'LiveClaude' } else { Join-Path $env:LOCALAPPDATA 'Programs\LiveClaude' }
}
$exe = Join-Path $InstallDir 'LiveClaude.exe'

$serviceWasRunning = $false
$service = Get-Service -Name LiveClaude -ErrorAction SilentlyContinue
if ($service -and $isAdmin) {
    $serviceWasRunning = $service.Status -eq 'Running'
    if ($serviceWasRunning) { Write-Host 'Stopping LiveClaude service...'; Stop-Service LiveClaude -Force }
}
Get-Process -Name 'LiveClaude', 'LiveClaude.Service' -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and $_.Path.StartsWith($InstallDir, [StringComparison]::OrdinalIgnoreCase) } |
    ForEach-Object { Write-Host "Closing $($_.Name) ($($_.Id))..."; $_ | Stop-Process -Force; $_.WaitForExit(10000) | Out-Null }

Write-Host "Copying files to $InstallDir ..."
New-Item $InstallDir -ItemType Directory -Force | Out-Null
robocopy $source $InstallDir /MIR /NFL /NDL /NJH /NJS /NP /R:2 /W:2 | Out-Null
if ($LASTEXITCODE -ge 8) { throw "Copy failed (robocopy $LASTEXITCODE)" }

$programs = [Environment]::GetFolderPath($(if ($AllUsers) { 'CommonPrograms' } else { 'Programs' }))
$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut((Join-Path $programs 'LiveClaude.lnk'))
$link.TargetPath = $exe
$link.WorkingDirectory = $InstallDir
$link.Description = 'LiveClaude'
$link.Save()

$runKey = if ($AllUsers) { 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run' } else { 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' }
if ($Autostart) { Set-ItemProperty $runKey -Name LiveClaude -Value "`"$exe`"" }

$uninstallKey = if ($AllUsers) { 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\LiveClaude' } else { 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\LiveClaude' }
$version = (Get-Item $exe).VersionInfo.ProductVersion
Copy-Item (Join-Path $PSScriptRoot 'uninstall.ps1') $InstallDir -Force
New-Item $uninstallKey -Force | Out-Null
$uninstallCmd = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$(Join-Path $InstallDir 'uninstall.ps1')`"" + $(if ($AllUsers) { ' -AllUsers' } else { '' })
Set-ItemProperty $uninstallKey -Name DisplayName -Value 'LiveClaude'
Set-ItemProperty $uninstallKey -Name DisplayVersion -Value $version
Set-ItemProperty $uninstallKey -Name Publisher -Value 'Leandro Cannizzaro'
Set-ItemProperty $uninstallKey -Name InstallLocation -Value $InstallDir
Set-ItemProperty $uninstallKey -Name UninstallString -Value $uninstallCmd
Set-ItemProperty $uninstallKey -Name NoModify -Value 1 -Type DWord
Set-ItemProperty $uninstallKey -Name NoRepair -Value 1 -Type DWord

if ($serviceWasRunning -and $isAdmin) {
    Start-Service LiveClaude
}

if (-not $NoInstallCounter) {
    $counter = Join-Path $InstallDir 'count-installation.ps1'
    if (Test-Path $counter) {
        try { & $counter } catch { Write-Warning $_ }
    }
}

if (-not $NoStart) {
    Start-Process -FilePath $exe
}
