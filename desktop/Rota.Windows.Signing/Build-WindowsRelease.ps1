param(
    [Parameter(Mandatory = $true)][string]$PublishDir,
    [Parameter(Mandatory = $true)][string]$OutputDir,
    [string]$Version = '0.4.0',
    [string]$PfxPath = '',
    [string]$CertificatePassword = '',
    [string]$TimestampUrl = 'http://timestamp.digicert.com',
    [string]$SignToolPath = '',
    [switch]$SkipTimestamp,
    [switch]$AllowUntrustedCertificate
)

$ErrorActionPreference = 'Stop'
$publish = (Resolve-Path -LiteralPath $PublishDir).Path
$application = Join-Path $publish 'Rota.exe'
if (-not (Test-Path -LiteralPath $application -PathType Leaf)) { throw 'Rota.exe não foi publicado.' }
$installerScript = Join-Path $PSScriptRoot '..\Rota.Windows.Installer\Build-Installer.ps1'
$signScript = Join-Path $PSScriptRoot 'Sign-WindowsArtifacts.ps1'
$verifyScript = Join-Path $PSScriptRoot 'Verify-WindowsSignature.ps1'
$importedThumbprint = ''
$certificateWasPresent = $false

try {
    if ([string]::IsNullOrWhiteSpace($PfxPath)) {
        & $installerScript -PublishDir $publish -OutputDir $OutputDir -Version $Version
    }
    else {
        $pfx = (Resolve-Path -LiteralPath $PfxPath).Path
        if ([string]::IsNullOrEmpty($CertificatePassword)) { throw 'A senha do certificado não foi informada.' }
        $securePassword = ConvertTo-SecureString $CertificatePassword -AsPlainText -Force
        $preview = [Security.Cryptography.X509Certificates.X509Certificate2]::new(
            $pfx,
            $CertificatePassword,
            [Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet)
        $certificateWasPresent = Test-Path -LiteralPath "Cert:\CurrentUser\My\$($preview.Thumbprint)"
        $certificate = Import-PfxCertificate -FilePath $pfx -CertStoreLocation 'Cert:\CurrentUser\My' -Password $securePassword -Exportable:$false
        $importedThumbprint = $certificate.Thumbprint
        $signTool = & (Join-Path $PSScriptRoot 'Find-SignTool.ps1') -ExplicitPath $SignToolPath

        & $signScript -Files $application -CertificateThumbprint $importedThumbprint -TimestampUrl $TimestampUrl -SignToolPath $signTool -SkipTimestamp:$SkipTimestamp
        & $installerScript -PublishDir $publish -OutputDir $OutputDir -Version $Version -SignToolPath $signTool -CertificateThumbprint $importedThumbprint -TimestampUrl $TimestampUrl -SkipTimestamp:$SkipTimestamp
    }

    $installer = Join-Path ([IO.Path]::GetFullPath($OutputDir)) "Rota-Windows-v$Version-Setup-x64.exe"
    $releaseFiles = @($application, $installer)
    & $verifyScript -Files $releaseFiles -RequireTrusted:((-not [string]::IsNullOrWhiteSpace($PfxPath)) -and -not $AllowUntrustedCertificate) -SignToolPath $SignToolPath
}
finally {
    if (-not [string]::IsNullOrWhiteSpace($importedThumbprint) -and -not $certificateWasPresent) {
        Remove-Item -LiteralPath "Cert:\CurrentUser\My\$importedThumbprint" -Force -ErrorAction SilentlyContinue
    }
}
