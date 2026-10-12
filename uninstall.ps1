<#
.SYNOPSIS
    Removes the rdp:// / vnc:// URL handlers installed by install.ps1.

.DESCRIPTION
    Restores the previous registration from the .reg backup when one exists, otherwise
    deletes the keys the installer created. Reads %LOCALAPPDATA%\RdpVncUrlHandler\install.json
    to find which protocols were registered and where the backups are; both can be
    overridden with parameters.

.PARAMETER Protocol
    Protocols to unregister. Default: everything recorded in install.json, else rdp and vnc.

.PARAMETER Machine
    Remove machine wide (HKLM) registrations instead of the per user ones.

.PARAMETER KeepState
    Keep the log and settings files in %LOCALAPPDATA%\RdpVncUrlHandler

.EXAMPLE
    .\uninstall.ps1
    .\uninstall.ps1 -Protocol vnc -KeepState
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string[]] $Protocol,
    [switch] $Machine,
    [switch] $KeepState
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

function Invoke-ShellChangeNotify {
    if (-not ('Native.ShellNotify' -as [type])) {
        Add-Type -Namespace 'Native' -Name 'ShellNotify' -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("shell32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
public static extern void SHChangeNotify(int wEventId, uint uFlags, System.IntPtr dwItem1, System.IntPtr dwItem2);
'@ | Out-Null
    }
    [Native.ShellNotify]::SHChangeNotify(0x08000000, 0x0000, [IntPtr]::Zero, [IntPtr]::Zero)
}

$stateDir = Join-Path $env:LOCALAPPDATA 'RdpVncUrlHandler'
$stateFile = Join-Path $stateDir 'install.json'

$state = $null
if (Test-Path -LiteralPath $stateFile) {
    try { $state = Get-Content -LiteralPath $stateFile -Raw | ConvertFrom-Json } catch { $state = $null }
}

$records = @()
if ($state) {
    if ($state.PSObject.Properties.Name -contains 'registrations') { $records = @($state.registrations) }
    elseif ($state.PSObject.Properties.Name -contains 'protocol') { $records = @($state) }
}

if (-not $Protocol -or $Protocol.Count -eq 0) {
    if ($records.Count) { $Protocol = @($records | ForEach-Object { $_.protocol }) }
    else { $Protocol = @('rdp', 'vnc') }
}

foreach ($name in $Protocol) {
    if ($name -notmatch '^[A-Za-z][A-Za-z0-9+.\-]{0,30}$') { throw "invalid protocol name: $name" }
}

# a record knows the exact scope it used; -Machine forces the machine wide variant
$machineScope = $Machine
if (-not $Machine -and $records.Count -eq 1 -and $records[0].scope -eq 'Machine') { $machineScope = $true }

if ($machineScope) {
    $classRoot = 'HKLM:\SOFTWARE\Classes'
} else {
    $classRoot = 'HKCU:\SOFTWARE\Classes'
}

foreach ($name in $Protocol) {
    $protocolKey = Join-Path $classRoot $name
    $record = $records | Where-Object { $_.protocol -eq $name } | Select-Object -First 1

    $supportedKey = $null
    if ($record -and $record.protocolKey) {
        foreach ($candidate in @($record.protocolKey, (Join-Path $classRoot $name))) {
            if (Test-Path -LiteralPath $candidate) { $protocolKey = $candidate; break }
        }
        $owner = if ($name -eq 'vnc') { 'tvnviewer.exe' } else { 'mstsc.exe' }
        $supportedKey = Join-Path $classRoot "Applications\$owner\supportedProtocols\$name"
    }

    $backupFile = $null
    if ($record -and $record.backupFile -and (Test-Path -LiteralPath ([string]$record.backupFile))) {
        $backupFile = [string]$record.backupFile
    }

    if ($PSCmdlet.ShouldProcess($protocolKey, "unregister ${name}: url protocol")) {
        if (-not (Test-Path -LiteralPath $protocolKey)) {
            Write-Host "${name}::// was not registered, nothing to do" -ForegroundColor DarkGray
        } elseif ($backupFile) {
            $regExe = Join-Path $env:SystemRoot 'System32\reg.exe'
            Write-Host "${name}::// restoring the previous registration from $backupFile" -ForegroundColor DarkGray
            & $regExe import $backupFile | Out-Null
            if ($LASTEXITCODE -ne 0) {
                Write-Warning "reg import failed (exit $LASTEXITCODE), removing the key instead"
                Remove-Item -Path $protocolKey -Recurse -Force
            }
        } else {
            Remove-Item -Path $protocolKey -Recurse -Force
        }

        if ($supportedKey -and (Test-Path -LiteralPath $supportedKey)) {
            Remove-Item -Path $supportedKey -Force
        }

        Invoke-ShellChangeNotify
        Write-Host "unregistered ${name}://" -ForegroundColor Green
    }
}

if (Test-Path -LiteralPath $stateFile) { Remove-Item -LiteralPath $stateFile -Force }

if (-not $KeepState -and (Test-Path -LiteralPath $stateDir)) {
    Remove-Item -LiteralPath $stateDir -Recurse -Force
    Write-Host "removed $stateDir" -ForegroundColor DarkGray
}

Write-Host 'close and reopen the browser once so it picks up the change' -ForegroundColor DarkGray