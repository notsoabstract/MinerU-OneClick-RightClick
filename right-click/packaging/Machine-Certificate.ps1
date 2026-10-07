param(
  [ValidateSet('Add','Remove','Verify')][string]$Action = 'Verify',
  [string]$CertificatePath = (Join-Path $PSScriptRoot 'MinerURightClick.cer')
)
$ErrorActionPreference = 'Stop'
$expectedThumbprint = 'DCEA4519798D5CB3969F5A1CFEEB719C38DA16A5'
$expectedSha256 = 'AB0F91BA03ED41C9590BA5A29B01ADBA3CBCF2896EDC275AD0F3B5C3FD520040'
$expectedSubject = 'CN=MinerURightClick'
$certificate = $null
$store = $null
try {
  $certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new([IO.Path]::GetFullPath($CertificatePath))
  $hash = [Security.Cryptography.SHA256]::Create()
  try { $sha256 = [BitConverter]::ToString($hash.ComputeHash($certificate.RawData)).Replace('-','') }
  finally { $hash.Dispose() }
  if ($certificate.Thumbprint -ne $expectedThumbprint -or $sha256 -ne $expectedSha256 -or $certificate.Subject -ne $expectedSubject) {
    throw 'The public certificate does not match this tool''s fixed certificate pin.'
  }
  if ($certificate.HasPrivateKey) { throw 'Only this tool''s public leaf certificate is accepted.' }
  $constraints = @($certificate.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.19' })
  if ($constraints.Count -ne 1) { throw 'A non-CA basic constraints extension is required.' }
  $basic = [Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($constraints[0], $constraints[0].Critical)
  if ($basic.CertificateAuthority -or $basic.HasPathLengthConstraint) { throw 'A CA certificate is never accepted.' }
  $usageExtensions = @($certificate.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.37' })
  if ($usageExtensions.Count -ne 1) { throw 'A single code-signing usage extension is required.' }
  $usage = [Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($usageExtensions[0], $usageExtensions[0].Critical)
  if ($usage.EnhancedKeyUsages.Count -ne 1 -or $usage.EnhancedKeyUsages[0].Value -ne '1.3.6.1.5.5.7.3.3') {
    throw 'The certificate must allow code signing only.'
  }
  $keyUsageExtensions = @($certificate.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.15' })
  if ($keyUsageExtensions.Count -ne 1) { throw 'A digital-signature key usage extension is required.' }
  $keyUsage = [Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new($keyUsageExtensions[0], $keyUsageExtensions[0].Critical)
  if ($keyUsage.KeyUsages -ne [Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature) {
    throw 'The certificate must allow digital signatures only.'
  }
  if ($Action -ne 'Verify') {
    $principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
    if (!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Windows administrator confirmation is required for Add or Remove.' }
  }
  $store = [Security.Cryptography.X509Certificates.X509Store]::new('TrustedPeople','LocalMachine')
  $openFlag = [Security.Cryptography.X509Certificates.OpenFlags]::ReadOnly
  if ($Action -ne 'Verify') { $openFlag = [Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite }
  $store.Open($openFlag)
  $existing = $store.Certificates.Find([Security.Cryptography.X509Certificates.X509FindType]::FindByThumbprint, $expectedThumbprint, $false)
  foreach ($item in $existing) {
    if ($item.Subject -ne $expectedSubject -or $item.HasPrivateKey -or
        [Convert]::ToBase64String($item.RawData) -ne [Convert]::ToBase64String($certificate.RawData)) { throw 'The installed certificate does not match the pinned public leaf certificate.' }
  }
  $added = $false
  $removed = $false
  if ($Action -eq 'Add' -and $existing.Count -eq 0) { $store.Add($certificate); $added = $true }
  if ($Action -eq 'Remove' -and $existing.Count -gt 0) {
    foreach ($item in $existing) { $store.Remove($item) }
    $removed = $true
  }
  $trusted = $store.Certificates.Find([Security.Cryptography.X509Certificates.X509FindType]::FindByThumbprint, $expectedThumbprint, $false).Count -gt 0
  $result = [ordered]@{ Success=$true; Action=$Action; Store='LocalMachine\TrustedPeople'; Thumbprint=$expectedThumbprint; Subject=$expectedSubject; Trusted=$trusted; Added=$added; Removed=$removed }
  $json = $result | ConvertTo-Json -Compress
  Write-Output $json
  # Exit codes preserve ownership information without writing elevated result files.
  if ($Action -eq 'Add') { if ($added) { exit 10 } else { exit 0 } }
  if ($Action -eq 'Remove') { if ($removed) { exit 20 } else { exit 0 } }
} finally {
  if ($store) { $store.Close(); $store.Dispose() }
  if ($certificate) { $certificate.Dispose() }
}
