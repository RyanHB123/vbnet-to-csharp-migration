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
    $legacyOutput = & ./legacy/LegacyInvoices/bin/Release/LegacyInvoices.exe
    if ($LASTEXITCODE -ne 0) { throw 'Legacy executable failed.' }
    $modernOutput = & dotnet ./src/ModernInvoices.Cli/bin/Release/net8.0/ModernInvoices.Cli.dll
    if ($LASTEXITCODE -ne 0) { throw 'Modern executable failed; build the solution in Release first.' }
    if (($legacyOutput -join "`n") -cne ($modernOutput -join "`n")) {
        throw "Legacy and modern output differ.`nLegacy: $legacyOutput`nModern: $modernOutput"
    }
    $legacyOutput
    Write-Output 'PASS .NET Framework 4.8 and .NET 8 executable outputs match'

    $previousLegacy = $env:LEGACY_DATA_FILE
    $previousModern = $env:MODERN_DATA_FILE
    $runId = [Guid]::NewGuid().ToString('N')
    $testDirectory = Join-Path $repoRoot 'artifacts'
    New-Item -ItemType Directory -Force -Path $testDirectory | Out-Null
    $legacyDataPath = Join-Path $testDirectory "$runId.xml"
    $modernDataPath = Join-Path $testDirectory "$runId.json"
    $env:LEGACY_DATA_FILE = $legacyDataPath
    $env:MODERN_DATA_FILE = $modernDataPath
    function Invoke-LegacyCommand([string[]]$CommandArguments) {
        $result = & ./legacy/LegacyInvoices/bin/Release/LegacyInvoices.exe @CommandArguments
        if ($LASTEXITCODE -ne 0) { throw 'Legacy command failed.' }
        return $result
    }
    function Invoke-ModernCommand([string[]]$CommandArguments) {
        $result = & dotnet ./src/ModernInvoices.Cli/bin/Release/net8.0/ModernInvoices.Cli.dll @CommandArguments
        if ($LASTEXITCODE -ne 0) { throw 'Modern command failed.' }
        return $result
    }
    function Assert-Reports {
        $legacyReport = Invoke-LegacyCommand @('report')
        $modernReport = (Invoke-ModernCommand @('report') | Out-String) | ConvertFrom-Json
        foreach ($line in $legacyReport) {
            $pair = $line.Split(':', 2)
            $value = [decimal]::Parse($pair[1], [Globalization.CultureInfo]::InvariantCulture)
            if ($value -ne $modernReport.($pair[0])) { throw "Report mismatch: $line" }
        }
    }
    try {
        $legacyQuote = Invoke-LegacyCommand @('quote', 'CUST-001', 'MON-001:8', 'DOCK-002:2')
        $modernQuote = Invoke-ModernCommand @('quote', 'CUST-001', 'MON-001:8', 'DOCK-002:2')
        if (($legacyQuote -join "`n") -cne ($modernQuote -join "`n")) { throw 'Catalogue quotes differ.' }
        $legacyPlaced = Invoke-LegacyCommand @('place', 'CUST-001', 'MON-001:8', 'DOCK-002:2')
        $legacyId = $legacyPlaced.Replace('Placed: ', '').Trim()
        $modernPlaced = (Invoke-ModernCommand @('place', 'CUST-001', 'MON-001:8', 'DOCK-002:2') | Out-String) | ConvertFrom-Json
        Assert-Reports
        $legacyProducts = Invoke-LegacyCommand @('products', 'MON-001')
        if (($legacyProducts -join "`n") -notmatch '\| 16\s*$') { throw 'Legacy stock deduction failed.' }
        Invoke-LegacyCommand @('cancel', $legacyId) | Out-Null
        Invoke-ModernCommand @('cancel', $modernPlaced.id) | Out-Null
        Assert-Reports
        $legacyProducts = Invoke-LegacyCommand @('products', 'MON-001')
        if (($legacyProducts -join "`n") -notmatch '\| 24\s*$') { throw 'Legacy restocking failed.' }
        $legacyCsv = Invoke-LegacyCommand @('export')
        if (($legacyCsv -join "`n") -notmatch 'Cancelled.*1186.92') { throw 'Legacy export lost order history.' }
        Write-Output 'PASS actual VB.NET Framework and C# runtimes: catalogue quote, persistent order, reports, cancellation, stock restoration, CSV'
    } finally {
        $env:LEGACY_DATA_FILE = $previousLegacy
        $env:MODERN_DATA_FILE = $previousModern
        foreach ($testFile in @($legacyDataPath, $modernDataPath)) {
            if (Test-Path -LiteralPath $testFile) { Remove-Item -LiteralPath $testFile }
        }
    }
} finally {
    Pop-Location
}
