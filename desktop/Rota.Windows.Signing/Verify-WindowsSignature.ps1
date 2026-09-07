param(
    [Parameter(Mandatory = $true)][string[]]$Files,
    [switch]$RequireTrusted,
    [string]$SignToolPath = ''
)

$ErrorActionPreference = 'Stop'
$signTool = ''
if ($RequireTrusted) {
    $signTool = & (Join-Path $PSScriptRoot 'Find-SignTool.ps1') -ExplicitPath $SignToolPath
}

foreach ($file in $Files) {
    $path = (Resolve-Path -LiteralPath $file).Path
    $signature = Get-AuthenticodeSignature -LiteralPath $path
    if ($RequireTrusted) {
        if ($signature.Status -ne 'Valid') { throw "A assinatura de $path não é confiável: $($signature.Status)." }
        & $signTool verify /pa /all /v $path
        if ($LASTEXITCODE -ne 0) { throw "A política Authenticode recusou $path." }
    }
    elseif ($signature.Status -eq 'HashMismatch' -or
            ($signature.Status -eq 'UnknownError' -and $null -eq $signature.SignerCertificate)) {
        throw "O artefato possui uma assinatura inválida: $path ($($signature.Status))."
    }
    [pscustomobject]@{
        Path = $path
        Status = $signature.Status
        Subject = $signature.SignerCertificate.Subject
        TimestampSubject = $signature.TimeStamperCertificate.Subject
    }
}
