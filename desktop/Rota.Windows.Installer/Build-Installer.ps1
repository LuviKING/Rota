param(
    [Parameter(Mandatory = $true)][string]$PublishDir,
    [Parameter(Mandatory = $true)][string]$OutputDir,
    [string]$Version = '0.4.0',
    [string]$CompilerPath = ''
)

$ErrorActionPreference = 'Stop'
$publish = (Resolve-Path -LiteralPath $PublishDir).Path
$application = Join-Path $publish 'Rota.exe'
if (-not (Test-Path -LiteralPath $application -PathType Leaf)) {
    throw "Rota.exe não foi encontrado em $publish."
}

$output = [IO.Path]::GetFullPath($OutputDir)
[IO.Directory]::CreateDirectory($output) | Out-Null

if ([string]::IsNullOrWhiteSpace($CompilerPath)) {
    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
    )
    $CompilerPath = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($CompilerPath) -or -not (Test-Path -LiteralPath $CompilerPath -PathType Leaf)) {
    throw 'Inno Setup 6.7.3 não foi encontrado. Instale JRSoftware.InnoSetup pelo winget.'
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Versão inválida: $Version"
}

$script = Join-Path $PSScriptRoot 'Rota.Windows.iss'
& $CompilerPath "/DAppVersion=$Version" "/DPublishDir=$publish" "/DOutputDir=$output" $script
if ($LASTEXITCODE -ne 0) {
    throw "O compilador do instalador terminou com o código $LASTEXITCODE."
}

$installer = Join-Path $output "Rota-Windows-v$Version-Setup-x64.exe"
if (-not (Test-Path -LiteralPath $installer -PathType Leaf)) {
    throw "O instalador esperado não foi gerado: $installer"
}
Get-Item -LiteralPath $installer
