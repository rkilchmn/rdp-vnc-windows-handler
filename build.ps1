<#
.SYNOPSIS
    Compiles the rdp:// and vnc:// URL handler with the .NET Framework C# compiler that ships with Windows.

.DESCRIPTION
    Output: dist\RdpVncUrlHandler.exe (a windowed executable, no console window flashes on click).
    No Visual Studio or .NET SDK required.

.EXAMPLE
    .\build.ps1
    .\build.ps1 -Clean
#>
[CmdletBinding()]
param(
    [switch] $Clean,
    [switch] $Verbose2
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$root = $PSScriptRoot
$src = Join-Path $root 'src\RdpVncUrlHandler.cs'
$dist = Join-Path $root 'dist'
$out = Join-Path $dist 'RdpVncUrlHandler.exe'

if (-not (Test-Path -LiteralPath $src)) { throw "source not found: $src" }

if ($Clean -and (Test-Path -LiteralPath $dist)) {
    Write-Host "cleaning $dist" -ForegroundColor DarkGray
    Remove-Item -LiteralPath $dist -Recurse -Force
}

$csc = @(
    (Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
    (Join-Path $env:SystemRoot 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1

if (-not $csc) { throw 'csc.exe not found - .NET Framework 4.x does not appear to be installed.' }

New-Item -ItemType Directory -Path $dist -Force | Out-Null

$references = @('System.dll', 'System.Core.dll', 'System.Web.Extensions.dll')

$arguments = @(
    '/nologo'
    '/target:winexe'
    '/platform:anycpu'
    '/optimize+'
    '/warnaserror-'
    '/utf8output'
    "/out:$out"
)
foreach ($reference in $references) { $arguments += "/reference:$reference" }
$arguments += $src

Write-Host "compiling with $csc" -ForegroundColor DarkGray
& $csc $arguments
if ($LASTEXITCODE -ne 0) { throw "compilation failed with exit code $LASTEXITCODE" }

Copy-Item -LiteralPath (Join-Path $root 'install.ps1')   -Destination $dist -Force
Copy-Item -LiteralPath (Join-Path $root 'uninstall.ps1') -Destination $dist -Force
Copy-Item -LiteralPath (Join-Path $root 'settings.sample.json') -Destination (Join-Path $dist 'settings.json') -Force

$info = Get-Item -LiteralPath $out
Write-Host ''
Write-Host "built $($info.FullName) ($([math]::Round($info.Length / 1KB, 1)) KB)" -ForegroundColor Green
Write-Host "next: powershell -ExecutionPolicy Bypass -File .\dist\install.ps1"
