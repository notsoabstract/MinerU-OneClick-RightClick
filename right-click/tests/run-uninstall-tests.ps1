$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$taskExe = Join-Path $PSScriptRoot 'UninstallTests.exe'
& $taskCompiler /nologo /target:exe /platform:x64 /utf8output /codepage:65001 /reference:System.Management.dll ('/out:' + $taskExe) (Join-Path $PSScriptRoot 'UninstallTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Uninstall tests did not compile.' }
& $taskExe (Join-Path $taskRoot 'dist')
if ($LASTEXITCODE -ne 0) { throw 'Uninstall cancellation tests failed.' }
