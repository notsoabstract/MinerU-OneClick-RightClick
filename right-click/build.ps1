param([string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $taskCompiler)) { throw '未找到 Windows 内置 C# 编译器。' }
$taskDist = Join-Path $taskRoot 'dist'
New-Item -ItemType Directory -Force -Path $taskDist | Out-Null
$taskArguments = @('/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/utf8output', '/codepage:65001', ('/out:' + (Join-Path $taskDist 'MinerURightClick.exe')),
  '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll', '/reference:System.Net.Http.dll', '/reference:System.Web.Extensions.dll', '/reference:System.IO.Compression.dll', '/reference:System.IO.Compression.FileSystem.dll', '/reference:System.Security.dll')
$taskManifest = Join-Path $taskRoot 'src\app.manifest'
if (Test-Path -LiteralPath $taskManifest) { $taskArguments += '/win32manifest:' + $taskManifest }
$taskArguments += @(Get-ChildItem -LiteralPath (Join-Path $taskRoot 'src') -Filter '*.cs' | ForEach-Object { $_.FullName })
& $taskCompiler @taskArguments
if ($LASTEXITCODE -ne 0) { throw '程序编译失败。' }
Copy-Item -LiteralPath (Join-Path $taskRoot 'packaging\uninstall.ps1') -Destination (Join-Path $taskDist 'uninstall.ps1') -Force
Write-Output ('已生成：' + (Join-Path $taskDist 'MinerURightClick.exe'))
