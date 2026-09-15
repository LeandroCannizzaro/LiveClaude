<#
.SYNOPSIS
    Installs the extracted LiveClaude zip for the current user.

.DESCRIPTION
    Copies the files next to this script into %LOCALAPPDATA%\Programs\LiveClaude, creates a Start
    Menu shortcut and, with -RegisterTask, registers the supervisor scheduled task. Nothing here
    needs administrator rights; installing the Windows service does, and is done from the app or
    with 'LiveClaude.Service.exe install-service' in an elevated prompt.

.EXAMPLE
    ./Install-LiveClaude.ps1 -RegisterTask -Launch
#>
[CmdletBinding()]
param(
    [string] $InstallDirectory = "$env:LOCALAPPDATA\Programs\LiveClaude",
    [switch] $RegisterTask,
    [switch] $Launch,
    [switch] $Uninstall
)

$ErrorActionPreference = 'Stop'

$startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\LiveClaude.lnk'

function New-Shortcut([string] $target, [string] $path) {
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($path)
    $shortcut.TargetPath = $target
    $shortcut.WorkingDirectory = Split-Path $target
    $shortcut.Description = 'LiveClaude - Claude Code Remote Control supervisor'
    $shortcut.Save()
}

if ($Uninstall) {
    $supervisor = Join-Path $InstallDirectory 'LiveClaude.Service.exe'
    if (Test-Path $supervisor) {
        & $supervisor uninstall-task | Out-Null
    }

    if (Test-Path $startMenu) {
        Remove-Item $startMenu -Force
    }

    if (Test-Path $InstallDirectory) {
        Remove-Item $InstallDirectory -Recurse -Force
    }

    Write-Host 'LiveClaude removed. Configuration and logs under %ProgramData%\LiveClaude were kept.'
    return
}

$source = Split-Path -Parent $MyInvocation.MyCommand.Path
Write-Host "Installing LiveClaude into $InstallDirectory"

New-Item -ItemType Directory -Force -Path $InstallDirectory | Out-Null
Get-ChildItem -Path $source -Exclude 'Install-LiveClaude.ps1' | Copy-Item -Destination $InstallDirectory -Recurse -Force

$app = Join-Path $InstallDirectory 'LiveClaude.exe'
if (-not (Test-Path $app)) {
    throw "LiveClaude.exe was not found in $source. Extract the whole zip before running this script."
}

New-Shortcut -target $app -path $startMenu
Write-Host 'Start Menu shortcut created.'

if ($RegisterTask) {
    & (Join-Path $InstallDirectory 'LiveClaude.Service.exe') install-task
}

if ($Launch) {
    Start-Process $app
}

Write-Host ''
Write-Host 'Done. Next steps:'
Write-Host '  1. Open LiveClaude and add a session for each project directory.'
Write-Host '  2. Use "Trust this directory..." once per folder.'
Write-Host '  3. Install the scheduled task (or the service) from the "Service & startup" tab.'
