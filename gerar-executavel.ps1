param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64'
)
$ErrorActionPreference = 'Stop'
try {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw 'Instale o SDK .NET 10: https://dotnet.microsoft.com/download/dotnet/10.0'
    }
    $sdks = & dotnet --list-sdks
    if ($LASTEXITCODE -ne 0 -or -not ($sdks -match '^10\.')) {
        throw 'O SDK .NET 10 e necessario para compilar este projeto.'
    }
    $project = Join-Path $PSScriptRoot 'src/WindowFloatIA/WindowFloatIA.csproj'
    if (-not (Test-Path -LiteralPath $project)) { throw "Projeto nao encontrado: $project" }
    # Cada compilacao usa uma pasta nova para nao misturar arquivos antigos.
    $stamp = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,6)
    $output = Join-Path $PSScriptRoot "artifacts/WfloatIA-$Runtime-$stamp"
    Write-Host 'Gerando executavel com .NET incluido. A primeira compilacao precisa de internet.'
    & dotnet publish $project -c Release -r $Runtime --self-contained true --disable-build-servers -o $output
    if ($LASTEXITCODE -ne 0) { throw 'A compilacao falhou. Confira os erros acima.' }
    $exe = Join-Path $output 'WindowFloatIA.exe'
    if (-not (Test-Path -LiteralPath $exe)) { throw 'Executavel nao encontrado na saida.' }
    foreach ($document in @('README.md','CONEXOES.md','LICENSE','ATUALIZACAO.md','SECURITY.md')) {
        $source = Join-Path $PSScriptRoot $document
        if (Test-Path -LiteralPath $source) { Copy-Item -LiteralPath $source -Destination $output }
    }
    $zip = "$output.zip"
    Compress-Archive -Path (Join-Path $output '*') -DestinationPath $zip
    $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([IO.Path]::GetFileName($zip))" | Set-Content -LiteralPath "$zip.sha256" -Encoding ascii
    Write-Host "`nPronto!" -ForegroundColor Green
    Write-Host "Executavel: $exe"
    Write-Host "Pacote para compartilhar: $zip"
    Write-Host 'Extraia o ZIP inteiro e mantenha todos os arquivos juntos.'
    exit 0
} catch {
    Write-Host "`nErro: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
