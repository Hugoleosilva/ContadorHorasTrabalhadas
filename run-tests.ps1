$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$srcDir = Join-Path $root 'src'
$testFile = Join-Path $root 'tests\TrackerTests.cs'
$distDir = Join-Path $root 'dist'
$testExe = Join-Path $distDir 'TrackerTests.exe'
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'

if (-not (Test-Path $distDir)) {
    New-Item -ItemType Directory -Path $distDir | Out-Null
}

$argsList = @(
    '/nologo',
    '/target:exe',
    '/warn:4',
    '/codepage:65001',
    "/out:$testExe",
    '/reference:System.dll',
    (Join-Path $srcDir 'Models.cs'),
    (Join-Path $srcDir 'CsvStorage.cs'),
    (Join-Path $srcDir 'HourBankCalculator.cs'),
    (Join-Path $srcDir 'TimeTracker.cs'),
    (Join-Path $srcDir 'AlertManager.cs'),
    $testFile
)

& $csc @argsList
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

& $testExe
$exitCode = $LASTEXITCODE
Remove-Item $testExe -ErrorAction SilentlyContinue
exit $exitCode
