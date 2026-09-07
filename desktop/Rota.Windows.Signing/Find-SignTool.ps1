param([string]$ExplicitPath = '')

$ErrorActionPreference = 'Stop'
if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
    $resolved = (Resolve-Path -LiteralPath $ExplicitPath).Path
    if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) { throw "SignTool não encontrado: $resolved" }
    return $resolved
}

$kitsRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
if (-not (Test-Path -LiteralPath $kitsRoot -PathType Container)) {
    throw 'O Windows SDK não foi encontrado. Instale o Windows 10/11 SDK para obter o SignTool.'
}

$signTool = Get-ChildItem -LiteralPath $kitsRoot -Directory |
    Sort-Object Name -Descending |
    ForEach-Object { Join-Path $_.FullName 'x64\signtool.exe' } |
    Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
    Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($signTool)) {
    throw 'O executável x64 do SignTool não foi encontrado no Windows SDK.'
}
return $signTool
