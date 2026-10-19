param([switch]$Test)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$compiler = Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'The Windows x64 .NET Framework C# compiler was not found.' }
$distPath = Join-Path $projectRoot 'dist'
$testPath = Join-Path $projectRoot 'build'
[void][IO.Directory]::CreateDirectory($distPath)
$sources = @('Core.cs','ServiceSettings.cs','ReconnectForm.cs','SettingsTests.cs','DpiLayoutTests.cs') | ForEach-Object { Join-Path $projectRoot $_ }
$common = @('/nologo','/target:winexe','/platform:x64','/optimize+', '/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.ServiceProcess.dll',('/win32icon:' + (Join-Path $projectRoot 'XboxReconnect.ico')))
$appPath = Join-Path $distPath 'Xbox-Reconnect-v2.2.exe'
& $compiler @common ('/win32manifest:' + (Join-Path $projectRoot 'XboxReconnect.manifest')) ('/out:' + $appPath) @sources
if ($LASTEXITCODE -ne 0) { throw 'App compilation failed.' }
if ($Test) {
    [void][IO.Directory]::CreateDirectory($testPath)
    $testExe = Join-Path $testPath 'XboxReconnect-Tests.exe'
    & $compiler @common ('/out:' + $testExe) @sources
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    foreach ($testArgument in @('--self-test','--ui-test')) {
        $testProcess = Start-Process -FilePath $testExe -ArgumentList $testArgument -WindowStyle Hidden -Wait -PassThru
        if ($testProcess.ExitCode -ne 0) { throw ('Checks failed: ' + $testArgument + ' (exit ' + $testProcess.ExitCode + ')') }
    }
    $dpiOutput = Join-Path $testPath 'dpi-layout'
    $dpiProcess = Start-Process -FilePath $testExe -ArgumentList @('--dpi-test', ('"' + $dpiOutput + '"')) -WindowStyle Hidden -Wait -PassThru
    if ($dpiProcess.ExitCode -ne 0) { throw 'DPI layout checks failed. See build/dpi-layout/layout-checks.txt.' }
    Write-Host 'Logic, UI and DPI layout checks passed. No real services were changed by the tests.'
}
Write-Host ('Built: ' + $appPath)
