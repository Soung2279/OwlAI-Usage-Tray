[CmdletBinding(DefaultParameterSetName = 'Pfx')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Pfx')]
    [string]$CertificatePath,

    [Parameter(ParameterSetName = 'Pfx')]
    [Security.SecureString]$CertificatePassword,

    [Parameter(Mandatory, ParameterSetName = 'Store')]
    [string]$CertificateThumbprint,

    [Parameter(Mandatory)]
    [string]$FilePath,

    [string]$TimestampServer = 'http://timestamp.digicert.com'
)

$ErrorActionPreference = 'Stop'
$resolvedFile = (Resolve-Path -LiteralPath $FilePath).Path

if ($PSCmdlet.ParameterSetName -eq 'Pfx') {
    $resolvedCertificate = (Resolve-Path -LiteralPath $CertificatePath).Path
    if ($null -eq $CertificatePassword) {
        $CertificatePassword = Read-Host '请输入 PFX 证书密码' -AsSecureString
    }

    $flags = [Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet
    $certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new(
        $resolvedCertificate,
        $CertificatePassword,
        $flags)
}
else {
    $normalizedThumbprint = $CertificateThumbprint.Replace(' ', '').ToUpperInvariant()
    $certificate = Get-ChildItem Cert:\CurrentUser\My | Where-Object {
        $_.Thumbprint -eq $normalizedThumbprint
    } | Select-Object -First 1
    if ($null -eq $certificate) {
        throw "当前用户证书库中找不到证书：$normalizedThumbprint"
    }
}

if (-not $certificate.HasPrivateKey) {
    throw '代码签名证书不包含私钥。'
}

$codeSigningOid = '1.3.6.1.5.5.7.3.3'
$supportsCodeSigning = $certificate.Extensions | Where-Object {
    $_.Oid.Value -eq '2.5.29.37' -and $_.EnhancedKeyUsages.ObjectId.Value -contains $codeSigningOid
}
if (-not $supportsCodeSigning) {
    throw '所选证书不包含 Code Signing EKU。'
}

$signature = Set-AuthenticodeSignature `
    -FilePath $resolvedFile `
    -Certificate $certificate `
    -HashAlgorithm SHA256 `
    -TimestampServer $TimestampServer

if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
    throw "签名验证失败：$($signature.Status) $($signature.StatusMessage)"
}

Write-Host "Signed: $resolvedFile"
Write-Host "Publisher: $($certificate.Subject)"
Write-Host "Timestamp: $TimestampServer"
