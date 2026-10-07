param([string]$OutputDirectory = (Join-Path $PSScriptRoot '..\dist'))
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$sdkBin = Join-Path $projectRoot 'tools\sdk-buildtools\bin\10.0.26100.0\x64'
$makeappx = Join-Path $sdkBin 'makeappx.exe'
$signtool = Join-Path $sdkBin 'signtool.exe'
foreach ($tool in @($makeappx, $signtool)) {
  if (!(Test-Path -LiteralPath $tool)) { throw "Missing isolated build tool: $tool" }
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$stage = Join-Path $PSScriptRoot 'identity-stage'
New-Item -ItemType Directory -Path (Join-Path $stage 'Assets') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'AppxManifest.xml') -Destination (Join-Path $stage 'AppxManifest.xml') -Force

# Small static logos are packaging assets, so no image editor is needed.
Add-Type -AssemblyName System.Drawing
foreach ($asset in @(@('StoreLogo.png', 50), @('Square44x44Logo.png', 44), @('Square150x150Logo.png', 150))) {
  $size = [int]$asset[1]
  $bitmap = [Drawing.Bitmap]::new($size, $size)
  $graphics = [Drawing.Graphics]::FromImage($bitmap)
  $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
  $graphics.Clear([Drawing.Color]::FromArgb(23,32,51))
  $brush = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(72,219,205))
  $font = [Drawing.Font]::new('Segoe UI', [single]($size * 0.55), [Drawing.FontStyle]::Bold, [Drawing.GraphicsUnit]::Pixel)
  $format = [Drawing.StringFormat]::new()
  $format.Alignment = [Drawing.StringAlignment]::Center
  $format.LineAlignment = [Drawing.StringAlignment]::Center
  $graphics.DrawString('M', $font, $brush, [Drawing.RectangleF]::new(0,0,$size,$size), $format)
  $bitmap.Save((Join-Path $stage ('Assets\' + $asset[0])), [Drawing.Imaging.ImageFormat]::Png)
  $format.Dispose(); $font.Dispose(); $brush.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
}
New-Item -ItemType Directory -Path (Join-Path $OutputDirectory 'Assets') -Force | Out-Null
foreach ($assetFile in @('StoreLogo.png','Square44x44Logo.png','Square150x150Logo.png')) {
  Copy-Item -LiteralPath (Join-Path $stage ('Assets\' + $assetFile)) -Destination (Join-Path $OutputDirectory ('Assets\' + $assetFile)) -Force
}

# Generate signing material as offline files, without adding a certificate to any store.
$privateDirectory = Join-Path $projectRoot 'tools\signing-private'
New-Item -ItemType Directory -Path $privateDirectory -Force | Out-Null
$owner = [Security.Principal.WindowsIdentity]::GetCurrent().User
$acl = [Security.AccessControl.DirectorySecurity]::new()
$acl.SetAccessRuleProtection($true, $false)
$acl.SetOwner($owner)
$acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($owner, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow'))
Set-Acl -LiteralPath $privateDirectory -AclObject $acl
$pfxPath = Join-Path $privateDirectory 'MinerURightClick.pfx'
$passwordPath = Join-Path $privateDirectory 'signing-password.dpapi'
if (!(Test-Path -LiteralPath $pfxPath)) {
  $random = [byte[]]::new(32)
  [Security.Cryptography.RandomNumberGenerator]::Fill($random)
  $password = [Convert]::ToBase64String($random)
  $securePassword = ConvertTo-SecureString -String $password -AsPlainText -Force
  $securePassword | ConvertFrom-SecureString | Set-Content -LiteralPath $passwordPath -Encoding ascii
  $rsa = [Security.Cryptography.RSA]::Create(3072)
  $request = [Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=MinerURightClick', $rsa,
    [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
  $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($false,$false,0,$true))
  $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new([Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature,$true))
  $eku = [Security.Cryptography.OidCollection]::new()
  $eku.Add([Security.Cryptography.Oid]::new('1.3.6.1.5.5.7.3.3')) | Out-Null
  $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($eku,$false))
  $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509SubjectKeyIdentifierExtension]::new($request.PublicKey,$false))
  $certificate = $request.CreateSelfSigned([DateTimeOffset]::Now.AddDays(-1),[DateTimeOffset]::Now.AddYears(10))
  [IO.File]::WriteAllBytes($pfxPath,$certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Pfx,$password))
  $certificate.Dispose(); $rsa.Dispose()
} else {
  $securePassword = Get-Content -LiteralPath $passwordPath -Raw | ConvertTo-SecureString
  $password = [Net.NetworkCredential]::new('', $securePassword).Password
}
$certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new($pfxPath,$password,[Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet)
$cerPath = Join-Path $OutputDirectory 'MinerURightClick.cer'
[IO.File]::WriteAllBytes($cerPath,$certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Cert))
$thumbprint = $certificate.Thumbprint
$certificate.Dispose()
$msixPath = Join-Path $OutputDirectory 'MinerURightClick.identity.msix'
& $makeappx pack /o /d $stage /nv /p $msixPath
if ($LASTEXITCODE) { throw "Identity package build failed: $LASTEXITCODE" }
& $signtool sign /fd SHA256 /f $pfxPath /p $password $msixPath
if ($LASTEXITCODE) { throw "Identity package signing failed: $LASTEXITCODE" }
$password = $null; $securePassword = $null
[ordered]@{ Name = 'Local.MinerURightClick'; Publisher = 'CN=MinerURightClick'; Thumbprint = $thumbprint;
  PackageSha256 = (Get-FileHash -LiteralPath $msixPath -Algorithm SHA256).Hash } |
  ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'package-identity.json') -Encoding utf8
foreach ($file in @('Register-Menu.ps1','Unregister-Menu.ps1','Machine-Certificate.ps1','uninstall.ps1')) {
  Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination (Join-Path $OutputDirectory $file) -Force
}
Write-Output "Signed identity package built. Public certificate: $thumbprint. No certificate store or package registration was changed."
