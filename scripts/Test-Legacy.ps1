#Requires -Version 7.0
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
Push-Location $repoRoot
try {
    $msbuildCommand = Get-Command msbuild -ErrorAction SilentlyContinue
    if ($msbuildCommand) {
        $msbuildPath = $msbuildCommand.Source
    } else {
        $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
        if (!(Test-Path $vswhere)) { throw 'Install Visual Studio Build Tools with .NET Framework 4.8 targeting tools.' }
        $msbuildPath = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
        if (!$msbuildPath) { throw 'MSBuild was not found.' }
    }
    & $msbuildPath LegacyInvoices.sln /p:Configuration=Debug /verbosity:minimal /nologo
    if ($LASTEXITCODE -ne 0) { throw 'Legacy Debug build failed.' }
    if (!(Test-Path './legacy/LegacyInvoices.Desktop/bin/Debug/LegacyInvoices.Desktop.exe') -or !(Test-Path './legacy/LegacyInvoices.Desktop/bin/Debug/LegacyInvoices.Desktop.pdb')) { throw 'Desktop Debug executable or symbols were not produced.' }
    & $msbuildPath LegacyInvoices.sln /p:Configuration=Release /verbosity:minimal /nologo
    if ($LASTEXITCODE -ne 0) { throw 'Legacy build failed.' }
    if (!(Test-Path './legacy/LegacyInvoices.Desktop/bin/Release/LegacyInvoices.Desktop.exe') -or
        !(Test-Path './legacy/LegacyInvoices.Desktop/bin/Release/LegacyInvoices.Business.dll')) {
        throw 'Legacy desktop executable or business library was not produced.'
    }
    Write-Output 'PASS legacy Windows Forms desktop builds in Debug and Release with symbols and business library'
} finally {
    Pop-Location
}
