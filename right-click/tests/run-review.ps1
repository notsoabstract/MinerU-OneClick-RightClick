$ErrorActionPreference = 'Stop'
$reviewRoot = Split-Path -Parent $PSScriptRoot
$reviewDist = Join-Path $reviewRoot 'dist'
$reviewCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$reviewHarness = Join-Path $PSScriptRoot 'ReviewHarness'
New-Item -ItemType Directory -Path $reviewHarness -Force | Out-Null
$reviewExe = Join-Path $reviewHarness 'ReviewTests.exe'
$reviewArguments = @('/nologo', '/target:exe', '/platform:x64', '/utf8output', '/codepage:65001', ('/out:' + $reviewExe),
  ('/reference:' + (Join-Path $reviewDist 'MinerURightClick.exe')), '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll', ('/win32manifest:' + (Join-Path $reviewRoot 'src\app.manifest')), (Join-Path $PSScriptRoot 'ReviewTests.cs'))
& $reviewCompiler @reviewArguments
if ($LASTEXITCODE -ne 0) { throw 'Review test compilation failed.' }
Copy-Item -LiteralPath (Join-Path $reviewDist 'MinerURightClick.exe') -Destination (Join-Path $reviewHarness 'MinerURightClick.exe') -Force
& $reviewExe (Join-Path $reviewRoot 'reports')
if ($LASTEXITCODE -ne 0) { throw 'Review tests failed.' }
