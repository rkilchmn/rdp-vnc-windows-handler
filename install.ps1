<#
.SYNOPSIS
    Registers RdpVncUrlHandler.exe as the Windows handler for rdp:// and vnc:// links, so
    clicking rdp://127.0.0.1:3390 starts a remote desktop client and clicking
    vnc://127.0.0.1:5900 starts a VNC session.

.DESCRIPTION
    Writes the URL protocol registrations to HKCU\Software\Classes by default (no admin
    needed). Any previous registration is exported to a .reg backup and restored by
    uninstall.ps1. Every registered protocol is tracked in
    %LOCALAPPDATA%\RdpVncUrlHandler\install.json.

.PARAMETER Protocol
    One or more schemes to register. Default: rdp and vnc.

.PARAMETER HandlerPath
    Path to RdpVncUrlHandler.exe. Default: next to this script, otherwise .\dist\RdpVncUrlHandler.exe

.PARAMETER Machine
    Register for all users (HKLM\Software\Classes). Requires an elevated PowerShell.

.PARAMETER TestUrl
    After installing, run the handler in dry-run mode against this url to verify parsing.

.EXAMPLE
    .\install.ps1
    .\install.ps1 -Protocol rdp
    .\install.ps1 -Protocol rdp,vnc -TestUrl 'vnc://127.0.0.1:5900'
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string[]] $Protocol = @('rdp', 'vnc'),
    [string] $HandlerPath,
    [switch] $Machine,
    [string] $TestUrl
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

if (-not $Protocol -or $Protocol.Count -eq 0) { $Protocol = @('rdp') }
foreach ($name in $Protocol) {
    if ($name -notmatch '^[A-Za-z][A-Za-z0-9+.\-]{0,30}$') { throw "invalid protocol name: $name" }
}

function Resolve-HandlerPath {
    param([string] $Requested)

    if ($Requested) {
        if (-not (Test-Path -LiteralPath $Requested)) { throw "handler not found: $Requested" }
        return (Resolve-Path -LiteralPath $Requested).Path
    }

    foreach ($candidate in @((Join-Path $PSScriptRoot 'RdpVncUrlHandler.exe'), (Join-Path $PSScriptRoot 'dist\RdpVncUrlHandler.exe'))) {
        if (Test-Path -LiteralPath $candidate) { return (Resolve-Path -LiteralPath $candidate).Path }
    }

    throw 'RdpVncUrlHandler.exe not found. Run build.ps1 first, or pass -HandlerPath.'
}

function Invoke-ShellChangeNotify {
    if (-not ('Native.ShellNotify' -as [type])) {
        Add-Type -Namespace 'Native' -Name 'ShellNotify' -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("shell32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
public static extern void SHChangeNotify(int wEventId, uint uFlags, System.IntPtr dwItem1, System.IntPtr dwItem2);
'@ | Out-Null
    }
    [Native.ShellNotify]::SHChangeNotify(0x08000000, 0x0000, [IntPtr]::Zero, [IntPtr]::Zero) # SHCNE_ASSOCCHANGED
}

$handler = Resolve-HandlerPath -Requested $HandlerPath

$mstsc = Join-Path $env:SystemRoot 'System32\mstsc.exe'
if (-not (Test-Path -LiteralPath $mstsc)) { Write-Warning "mstsc.exe not found at $mstsc - the handler will look for it at launch time." }

$tvnViewer = @(
    (Join-Path $env:ProgramFiles 'TightVNC\tvnviewer.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'TightVNC\tvnviewer.exe')
) | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1

if ($tvnViewer) {
    Write-Host "using the TightVNC viewer at $tvnViewer" -ForegroundColor DarkGray
} else {
    Write-Warning 'TightVNC viewer not found - set VncViewerPath in settings.json before using vnc:// links'
}

if ($Machine) {
    $classRoot = 'HKLM:\SOFTWARE\Classes'
    $hiveName = 'HKEY_LOCAL_MACHINE'
    $scopeName = 'Machine'
} else {
    $classRoot = 'HKCU:\SOFTWARE\Classes'
    $hiveName = 'HKEY_CURRENT_USER'
    $scopeName = 'User'
}

$stateDir = Join-Path $env:LOCALAPPDATA 'RdpVncUrlHandler'
New-Item -ItemType Directory -Path $stateDir -Force | Out-Null
$stateFile = Join-Path $stateDir 'install.json'

# keep records for protocols registered earlier that are not part of this run
$existing = @()
if (Test-Path -LiteralPath $stateFile) {
    try {
        $previous = Get-Content -LiteralPath $stateFile -Raw | ConvertFrom-Json
        if ($previous.PSObject.Properties.Name -contains 'registrations') { $existing = @($previous.registrations) }
        elseif ($previous.PSObject.Properties.Name -contains 'protocol') { $existing = @($previous) }
    } catch {
        Write-Warning "could not read $stateFile, starting a fresh record"
    }
}

$command = '"{0}" "%1"' -f $handler
$registrations = @()

foreach ($name in $Protocol) {
    $protocolKey = Join-Path $classRoot $name
    $backupFile = $null

    if (Test-Path -LiteralPath $protocolKey) {
        $backupFile = Join-Path $stateDir ("{0}-backup-{1}.reg" -f $name, (Get-Date -Format 'yyyyMMdd-HHmmss'))
        $regExe = Join-Path $env:SystemRoot 'System32\reg.exe'
        & $regExe export ('{0}\SOFTWARE\Classes\{1}' -f $hiveName, $name) $backupFile /y | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "reg export of ${name}: failed (exit $LASTEXITCODE), nothing backed up"
            $backupFile = $null
        } else {
            Write-Host "backed up the previous ${name}: registration to $backupFile" -ForegroundColor DarkGray
        }
    }

    # rdp gets the mstsc icon, vnc the TightVNC viewer icon when it is installed
    $ownerExe = if ($name -eq 'vnc' -and $tvnViewer) { $tvnViewer } else { $mstsc }
    $iconExe = $ownerExe

    if ($PSCmdlet.ShouldProcess($protocolKey, "register ${name}: url protocol")) {
        New-Item -Path $protocolKey -Force | Out-Null
        Set-ItemProperty -Path $protocolKey -Name '(Default)' -Value "URL:${name} Protocol" -Type String
        New-ItemProperty -Path $protocolKey -Name 'URL Protocol' -Value '' -PropertyType String -Force | Out-Null
        Set-ItemProperty -Path $protocolKey -Name 'DefaultIcon' -Value "$iconExe,0" -Type String
        Set-ItemProperty -Path $protocolKey -Name 'PerceivedType' -Value 'application' -Type String

        $openKey = Join-Path $protocolKey 'shell\open\command'
        New-Item -Path $openKey -Force | Out-Null
        Set-ItemProperty -Path $openKey -Name '(Default)' -Value $command -Type String

        $shellOpen = Join-Path $protocolKey 'shell\open'
        New-ItemProperty -Path $shellOpen -Name 'FriendlyName' -Value 'Open with RDP URL Handler' -PropertyType String -Force | Out-Null

        # makes the association show up in "Open with" lists
        $supportedKey = Join-Path $classRoot "Applications\$([IO.Path]::GetFileName($ownerExe))\supportedProtocols\$name"
        New-Item -Path $supportedKey -Force | Out-Null
        Set-ItemProperty -Path $supportedKey -Name '(Default)' -Value '1' -Type String

        Invoke-ShellChangeNotify
    }

    $registrations += [pscustomobject]@{
        protocol    = $name
        scope       = $scopeName
        protocolKey = $protocolKey
        handlerPath = $handler
        command     = $command
        backupFile  = $backupFile
        installedAt = (Get-Date).ToString('o')
    }
}

# merge with what was registered before, newest entry per protocol wins
$merged = @{}
foreach ($record in $existing + $registrations) { $merged[$record.protocol] = $record }
@{ registrations = @($merged.Values) } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $stateFile -Encoding UTF8

Write-Host ''
foreach ($record in $registrations) {
    Write-Host "registered $($record.protocol):// -> $command" -ForegroundColor Green
}
Write-Host "scope: $classRoot" -ForegroundColor DarkGray
Write-Host 'uninstall with: .\uninstall.ps1   (restores any backup it took)' -ForegroundColor DarkGray
Write-Host 'close and reopen the browser once so it picks up the new associations' -ForegroundColor DarkGray

if ($TestUrl) {
    Write-Host ''
    Write-Host "dry run: $TestUrl" -ForegroundColor Cyan
    $dryRunProcess = Start-Process -FilePath $handler -ArgumentList ('"{0}" /dryrun' -f $TestUrl) -NoNewWindow -Wait -PassThru
    Write-Host "handler exit code: $($dryRunProcess.ExitCode)" -ForegroundColor DarkGray
}

Write-Host ''
Write-Host "test links: <a href='rdp://127.0.0.1:3390'>rdp://127.0.0.1:3390</a>" -ForegroundColor Cyan
Write-Host "            <a href='vnc://127.0.0.1:5900'>vnc://127.0.0.1:5900</a>" -ForegroundColor Cyan