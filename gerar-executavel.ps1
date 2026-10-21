param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64'
)
$ErrorActionPreference = 'Stop'
try {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Instale o SDK .NET 10: https://dotnet.microsoft.com/download/dotnet/10.0' }
    $sdks = & dotnet --list-sdks
    if ($LASTEXITCODE -ne 0 -or -not ($sdks -match '^10\.')) { throw 'O SDK .NET 10 e necessario para compilar este projeto.' }
    Write-Host 'Gerando pacote com .NET incluido. A primeira compilacao precisa de internet.'
    & (Join-Path $PSScriptRoot 'scripts/publish.ps1') -Runtime $Runtime
    Write-Host "`nPronto! O pacote anterior desta arquitetura foi substituido." -ForegroundColor Green
    Write-Host 'Extraia o ZIP inteiro. Para remover esta copia, execute desinstalar.cmd na pasta extraida.'
    exit 0
} catch {
    Write-Host "`nErro: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
