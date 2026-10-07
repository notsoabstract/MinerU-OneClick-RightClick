param([string]$OutputDirectory = (Join-Path $PSScriptRoot '..\dist'))
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$tcc = Join-Path $projectRoot 'tools\tcc\tcc\tcc.exe'
if (!(Test-Path -LiteralPath $tcc)) { throw 'Missing isolated TinyCC compiler.' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
& $tcc -shared -o (Join-Path $OutputDirectory 'MinerUContextMenu.dll') `
  (Join-Path $PSScriptRoot 'MinerUContextMenu.c') (Join-Path $PSScriptRoot 'ole32.def') -lkernel32
if ($LASTEXITCODE) { throw "Shell build failed: $LASTEXITCODE" }
Write-Output (Join-Path $OutputDirectory 'MinerUContextMenu.dll')
