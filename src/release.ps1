[CmdletBinding(DefaultParameterSetName = 'Pfx')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Pfx')]
    [string]$CertificatePath,

    [Parameter(ParameterSetName = 'Pfx')]
    [Security.SecureString]$CertificatePassword,

    [Parameter(Mandatory, ParameterSetName = 'Store')]
    [string]$CertificateThumbprint,

    [string]$TimestampServer = 'http://timestamp.digicert.com'
)

$ErrorActionPreference = 'Stop'
$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repositoryDir = Split-Path -Parent $projectDir
$distributionDir = Join-Path $repositoryDir 'dist'
$iconPath = Join-Path $projectDir 'icon.ico'
if (-not (Test-Path -LiteralPath $iconPath)) {
    throw "正式发布缺少图标：$iconPath"
}

& (Join-Path $projectDir 'build.ps1')
$sourceExe = Join-Path $distributionDir 'OwlUsageTray.exe'
$releaseExe = Join-Path $distributionDir 'OwlUsageTray-1.1.0-win-x64.exe'

$signArguments = @{
    FilePath = $sourceExe
    TimestampServer = $TimestampServer
}
if ($PSCmdlet.ParameterSetName -eq 'Pfx') {
    $signArguments.CertificatePath = $CertificatePath
    if ($null -ne $CertificatePassword) {
        $signArguments.CertificatePassword = $CertificatePassword
    }
}
else {
    $signArguments.CertificateThumbprint = $CertificateThumbprint
}

& (Join-Path $projectDir 'sign-release.ps1') @signArguments
Copy-Item -LiteralPath $sourceExe -Destination $releaseExe -Force
$hash = Get-FileHash -LiteralPath $releaseExe -Algorithm SHA256
$hashLine = "$($hash.Hash)  $([IO.Path]::GetFileName($releaseExe))"
Set-Content -LiteralPath "$releaseExe.sha256" -Value $hashLine -Encoding ascii

Write-Host "Release: $releaseExe"
Write-Host "SHA256: $($hash.Hash)"
