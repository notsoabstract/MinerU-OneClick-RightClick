param([string]$ApplicationDirectory = $PSScriptRoot)
$ErrorActionPreference = 'Stop'
$packageName = 'Local.MinerURightClick'
$recordPath = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'MinerURightClick\menu-registration.json'
$record = $null
if (Test-Path -LiteralPath $recordPath) { $record = Get-Content -LiteralPath $recordPath -Raw | ConvertFrom-Json }
Get-AppxPackage -Name $packageName | Where-Object { $_.Publisher -eq 'CN=MinerURightClick' } |
  ForEach-Object { Remove-AppxPackage -Package $_.PackageFullName -ErrorAction Stop }
if ($record -and $record.Name -eq $packageName -and $record.MachineCertificateAdded -and $record.Thumbprint -eq 'DCEA4519798D5CB3969F5A1CFEEB719C38DA16A5') {
  $helperPath = Join-Path $ApplicationDirectory 'Machine-Certificate.ps1'
  $cerPath = Join-Path $ApplicationDirectory 'MinerURightClick.cer'
  $trust = & $helperPath -Action Verify -CertificatePath $cerPath | ConvertFrom-Json
  if ($trust.Trusted) {
    $powershellPath = Join-Path ([Environment]::GetFolderPath('Windows')) 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $arguments = '-NoLogo -NoProfile -ExecutionPolicy Bypass -File "' + $helperPath + '" -Action Remove -CertificatePath "' + $cerPath + '"'
    try { $process = Start-Process -FilePath $powershellPath -ArgumentList $arguments -Verb RunAs -WindowStyle Hidden -Wait -PassThru }
    catch { throw 'The menu was removed. Windows administrator confirmation is still required to finish removing this tool''s certificate.' }
    if ($process.ExitCode -notin @(0, 20)) { throw ('Certificate cleanup failed with exit code ' + $process.ExitCode + '. The installation record was kept for retry.') }
    $actualTrust = & $helperPath -Action Verify -CertificatePath $cerPath | ConvertFrom-Json
    if ($actualTrust.Trusted) { throw 'The certificate is still installed. The installation record was kept for retry.' }
  }
}
if (Test-Path -LiteralPath $recordPath) { Remove-Item -LiteralPath $recordPath -Force }
Write-Output 'MinerU menu registration removed for the current user.'
