$ErrorActionPreference = 'Stop'
$projectDirectory = Split-Path -Parent $PSScriptRoot
$outputPath = Join-Path $PSScriptRoot 'CoreTests.exe'
$compilerPath = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compilerPath /nologo /target:exe /define:CORE_TEST_STUB /out:$outputPath /reference:System.Net.Http.dll /reference:System.Web.Extensions.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll /reference:System.Security.dll (Join-Path $projectDirectory 'src\Recognition.cs') (Join-Path $PSScriptRoot 'CoreTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Core tests did not compile.' }
& $outputPath
if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
