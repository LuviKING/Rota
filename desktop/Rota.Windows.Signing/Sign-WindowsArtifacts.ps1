param(
    [Parameter(Mandatory = $true)][string[]]$Files,
    [Parameter(Mandatory = $true)][string]$CertificateThumbprint,
    [string]$TimestampUrl = 'http://timestamp.digicert.com',
    [string]$SignToolPath = '',
    [switch]$SkipTimestamp
)

$ErrorActionPreference = 'Stop'
$thumbprint = $CertificateThumbprint.Replace(' ', '').ToUpperInvariant()
if ($thumbprint -notmatch '^[0-9A-F]{40,128}$') { throw 'A impressão digital do certificado é inválida.' }
$certificate = Get-Item -LiteralPath "Cert:\CurrentUser\My\$thumbprint" -ErrorAction SilentlyContinue
if ($null -eq $certificate) { throw 'O certificado de assinatura não está instalado em Cert:\CurrentUser\My.' }
if ($certificate.NotAfter -le (Get-Date)) { throw 'O certificado de assinatura está expirado.' }
$codeSigningUsage = $certificate.EnhancedKeyUsageList |
    Where-Object { $_.ObjectId -eq '1.3.6.1.5.5.7.3.3' }
if ($null -eq $codeSigningUsage) {
    throw 'O certificado não possui a finalidade Code Signing.'
}

$signTool = & (Join-Path $PSScriptRoot 'Find-SignTool.ps1') -ExplicitPath $SignToolPath
$targets = foreach ($file in $Files) {
    $path = (Resolve-Path -LiteralPath $file).Path
    if ([IO.Path]::GetExtension($path) -notin @('.exe', '.msi')) { throw "Tipo de artefato não permitido para assinatura: $path" }
    $path
}

foreach ($target in $targets) {
    $arguments = @('sign', '/sha1', $thumbprint, '/s', 'My', '/fd', 'SHA256', '/d', 'Rota')
    if (-not $SkipTimestamp) {
        $timestamp = $null
        if (-not [Uri]::TryCreate($TimestampUrl, [UriKind]::Absolute, [ref]$timestamp) -or $timestamp.Scheme -notin @('http', 'https')) {
            throw 'O endereço do servidor de timestamp é inválido.'
        }
        $arguments += @('/tr', $TimestampUrl, '/td', 'SHA256')
    }
    $arguments += $target
    & $signTool @arguments
    if ($LASTEXITCODE -ne 0) { throw "O SignTool não conseguiu assinar $target." }

    $signature = Get-AuthenticodeSignature -LiteralPath $target
    if ($null -eq $signature.SignerCertificate -or $signature.Status -in @('NotSigned', 'HashMismatch')) {
        throw "A assinatura anexada a $target não pôde ser confirmada."
    }
}

$targets | Get-Item
