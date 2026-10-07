param([string]$ApplicationDirectory = (Join-Path $PSScriptRoot '..\dist'), [switch]$Registered)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$tcc = Join-Path $projectRoot 'tools\tcc\tcc\tcc.exe'
$testExe = Join-Path $PSScriptRoot 'Test-Shell.exe'
& $tcc -o $testExe (Join-Path $PSScriptRoot 'Test-Shell.c') `
  (Join-Path $PSScriptRoot 'ole32.def') (Join-Path $PSScriptRoot 'shell32.def') -lkernel32
if ($LASTEXITCODE) { throw "Shell test build failed: $LASTEXITCODE" }
if ($Registered) {
  & $testExe --registered
  if ($LASTEXITCODE) { throw "Registered shell activation tests failed: $LASTEXITCODE" }
  return
}
$library = [IO.Path]::GetFullPath((Join-Path $ApplicationDirectory 'MinerUContextMenu.dll'))
Add-Type -TypeDefinition 'using System;using System.Text;using System.Runtime.InteropServices; public static class MinerUTestPath { [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] public static extern uint GetShortPathName(string longPath, StringBuilder shortPath, uint buffer); }'
$shortPath = [Text.StringBuilder]::new(32768)
if (![MinerUTestPath]::GetShortPathName($library,$shortPath,32768)) { throw 'Cannot resolve DLL path for native test.' }
& $testExe $shortPath.ToString()
if ($LASTEXITCODE) { throw "Shell tests failed: $LASTEXITCODE" }
