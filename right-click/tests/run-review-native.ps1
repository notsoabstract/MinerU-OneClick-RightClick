param([switch]$Registered, [string]$LibraryPath)
$ErrorActionPreference = 'Stop'
$reviewRoot = Split-Path -Parent $PSScriptRoot
if (-not $LibraryPath) { $LibraryPath = Join-Path $reviewRoot 'dist\MinerUContextMenu.dll' }
$reviewHarness = Join-Path $PSScriptRoot 'ReviewNativeHarness'
New-Item -ItemType Directory -Path $reviewHarness -Force | Out-Null
$reviewCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$reviewExe = Join-Path $reviewHarness 'ReviewNative.exe'
& $reviewCompiler /nologo /target:exe /platform:x64 /utf8output /codepage:65001 ('/out:' + $reviewExe) (Join-Path $PSScriptRoot 'ReviewNative.cs')
if ($LASTEXITCODE -ne 0) { throw 'Native integration review compilation failed.' }
if ($Registered) { & $reviewExe --registered $LibraryPath (Join-Path $reviewRoot 'reports') }
else { & $reviewExe $LibraryPath (Join-Path $reviewRoot 'reports') }
if ($LASTEXITCODE -ne 0) { throw 'Native integration review failed.' }
