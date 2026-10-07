param([string]$ApplicationDirectory = $PSScriptRoot)
$ErrorActionPreference = 'Stop'
$ApplicationDirectory = [IO.Path]::GetFullPath($ApplicationDirectory)
$packageName = 'Local.MinerURightClick'
$recordDirectory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'MinerURightClick'
$recordPath = Join-Path $recordDirectory 'menu-registration.json'
$identity = Get-Content -LiteralPath (Join-Path $ApplicationDirectory 'package-identity.json') -Raw | ConvertFrom-Json
$cerPath = Join-Path $ApplicationDirectory 'MinerURightClick.cer'
$packagePath = Join-Path $ApplicationDirectory 'MinerURightClick.identity.msix'
$helperPath = Join-Path $ApplicationDirectory 'Machine-Certificate.ps1'
foreach ($file in @($cerPath, $packagePath, $helperPath, (Join-Path $ApplicationDirectory 'MinerURightClick.exe'),(Join-Path $ApplicationDirectory 'MinerUContextMenu.dll'))) {
  if (!(Test-Path -LiteralPath $file -PathType Leaf)) { throw "Missing installed component: $file" }
}
$publicCertificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new($cerPath)
try {
  if ($identity.Name -ne $packageName -or $identity.Publisher -ne 'CN=MinerURightClick' -or
      $publicCertificate.Subject -ne 'CN=MinerURightClick' -or $publicCertificate.Thumbprint -ne $identity.Thumbprint) {
    throw 'Identity package certificate does not match this application.'
  }
  $hashAlgorithm = [Security.Cryptography.SHA256]::Create()
  $packageStream = [IO.File]::OpenRead($packagePath)
  try {
    $packageHash = [BitConverter]::ToString($hashAlgorithm.ComputeHash($packageStream)).Replace('-', '')
  } finally { $packageStream.Dispose(); $hashAlgorithm.Dispose() }
  if ($packageHash -ne $identity.PackageSha256) { throw 'Identity package checksum mismatch.' }
  $previousRecord = $null
  if (Test-Path -LiteralPath $recordPath) { $previousRecord = Get-Content -LiteralPath $recordPath -Raw | ConvertFrom-Json }
  $installed = Get-AppxPackage -Name $packageName
  if ($installed -and $installed.Publisher -ne $identity.Publisher) { throw 'A different publisher owns this package name.' }
  $sameRegistration = $installed -and $previousRecord -and $previousRecord.Name -eq $packageName -and
    $previousRecord.Thumbprint -eq $publicCertificate.Thumbprint -and
    ([string]$previousRecord.ApplicationDirectory).Equals($ApplicationDirectory, [StringComparison]::OrdinalIgnoreCase) -and
    ([string]$installed.Version -eq '1.0.0.0')
  $trust = & $helperPath -Action Verify -CertificatePath $cerPath | ConvertFrom-Json
  $machineCertificateAdded = $false
  if ($previousRecord -and $previousRecord.Name -eq $packageName -and
      $previousRecord.Thumbprint -eq 'DCEA4519798D5CB3969F5A1CFEEB719C38DA16A5' -and $previousRecord.MachineCertificateAdded) { $machineCertificateAdded = $true }
  New-Item -ItemType Directory -Path $recordDirectory -Force | Out-Null
  if (!$trust.Trusted) {
    $powershellPath = Join-Path ([Environment]::GetFolderPath('Windows')) 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $arguments = '-NoLogo -NoProfile -ExecutionPolicy Bypass -File "' + $helperPath + '" -Action Add -CertificatePath "' + $cerPath + '"'
    try { $process = Start-Process -FilePath $powershellPath -ArgumentList $arguments -Verb RunAs -WindowStyle Hidden -Wait -PassThru }
    catch { throw 'Windows administrator confirmation was cancelled or could not be opened. The menu was not installed.' }
    if ($process.ExitCode -notin @(0, 10)) { throw ('The certificate step failed with exit code ' + $process.ExitCode + '. The menu was not installed.') }
    if ($process.ExitCode -eq 10) { $machineCertificateAdded = $true }
  }
  $actualTrust = & $helperPath -Action Verify -CertificatePath $cerPath | ConvertFrom-Json
  if (!$actualTrust.Trusted) { throw 'The machine certificate could not be verified after the administrator step. The menu was not installed.' }
  # Persist ownership before package registration so a failed install can still be cleaned up precisely.
  [ordered]@{ Name=$packageName; PackageFullName=$null; ApplicationDirectory=$ApplicationDirectory;
    Thumbprint=$publicCertificate.Thumbprint; MachineCertificateAdded=$machineCertificateAdded; Status='CertificateReady' } |
    ConvertTo-Json | Set-Content -LiteralPath $recordPath -Encoding utf8
  if (!$sameRegistration) {
    if ($installed) { Remove-AppxPackage -Package $installed.PackageFullName -ErrorAction Stop }
    Add-AppxPackage -Path $packagePath -ExternalLocation $ApplicationDirectory -ErrorAction Stop
  }
  $installed = Get-AppxPackage -Name $packageName
  if (!$installed) { throw 'Menu identity package registration could not be verified.' }
  New-Item -ItemType Directory -Path $recordDirectory -Force | Out-Null
  [ordered]@{ Name = $packageName; PackageFullName = $installed.PackageFullName;
    ApplicationDirectory = $ApplicationDirectory; Thumbprint = $publicCertificate.Thumbprint;
    MachineCertificateAdded = $machineCertificateAdded; Status='Installed' } |
    ConvertTo-Json | Set-Content -LiteralPath $recordPath -Encoding utf8
  Write-Output 'MinerU modern File Explorer command registered for the current user.'
} finally { $publicCertificate.Dispose() }
