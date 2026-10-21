param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64'
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'PackageFiles.ps1')
$root = Split-Path $PSScriptRoot -Parent
$artifacts = Join-Path $root 'artifacts'
$null = New-Item -ItemType Directory -Path $artifacts -Force
$stage = Join-Path $artifacts ('.WfloatIA-build-' + $Runtime + '-' + [Guid]::NewGuid().ToString('N'))
$null = Assert-PackagePath -Root $artifacts -Path $stage
$output = Join-Path $artifacts "WfloatIA-$Runtime"
$legacy = @(Get-PreviousPackages -Root $artifacts -Runtime $Runtime)
foreach ($path in @($output) + @($legacy | ForEach-Object { $_.FullName })) { $null = Assert-PackagePath -Root $artifacts -Path $path }
foreach ($process in Get-Process -Name WindowFloatIA -ErrorAction SilentlyContinue) {
    if ($process.Path -and (@($output) + @($legacy | Where-Object PSIsContainer | ForEach-Object { $_.FullName }) | Where-Object { $process.Path.StartsWith($_ + '\', [StringComparison]::OrdinalIgnoreCase) })) {
        throw 'Saia do Wfloat.IA pelo icone da bandeja antes de gerar o pacote novamente. A janela fechada pode continuar em segundo plano.'
    }
}
$package = Join-Path $stage 'package'
$null = New-Item -ItemType Directory -Path $package -Force
$promotionStarted = $false
$promoted = $false
try {
    & dotnet publish (Join-Path $root 'src/WindowFloatIA/WindowFloatIA.csproj') -c Release -r $Runtime --self-contained true --disable-build-servers -o $package
    if ($LASTEXITCODE -ne 0) { throw 'A compilacao falhou. O pacote anterior foi preservado.' }
    if (-not (Test-Path -LiteralPath (Join-Path $package 'WindowFloatIA.exe'))) { throw 'Executavel ausente na saida.' }
    foreach ($document in @('README.md','CONEXOES.md','LICENSE','ATUALIZACAO.md','SECURITY.md','FERRAMENTAS.md')) {
        Copy-Item -LiteralPath (Join-Path $root $document) -Destination $package
    }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'desinstalar.ps1'), (Join-Path $PSScriptRoot 'desinstalar.cmd') -Destination $package
    $files = @(Get-ChildItem -LiteralPath $package -File -Recurse | ForEach-Object { $_.FullName.Substring($package.Length + 1) })
    @{ Product = 'WindowFloatIA'; Format = 1; Files = $files } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $package 'package-manifest.json') -Encoding utf8
    $zip = Join-Path $stage 'package.zip'
    Compress-Archive -Path (Join-Path $package '*') -DestinationPath $zip
    $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  WfloatIA-$Runtime.zip" | Set-Content -LiteralPath "$zip.sha256" -Encoding ascii
    $promotionStarted = $true
    Publish-PackageFiles -Root $artifacts -Stage $stage -Runtime $Runtime
    $promoted = $true
    # Remove legacy timestamp packages only after the replacement ZIP and checksum exist.
    foreach ($old in $legacy) {
        try { Remove-PackagePath -Root $artifacts -Path $old.FullName }
        catch { Write-Warning "Novo pacote pronto, mas nao foi possivel remover $($old.FullName): $($_.Exception.Message)" }
    }
    Write-Host "Executavel: $(Join-Path $output 'WindowFloatIA.exe')"
    Write-Host "Pacote: $artifacts\WfloatIA-$Runtime.zip"
} finally {
    if (-not $promotionStarted -or $promoted) {
        try { Remove-PackagePath -Root $artifacts -Path $stage }
        catch { Write-Warning "Limpeza temporaria pendente: $stage" }
    } else { Write-Warning "Uma troca falhou. Confira o pacote anterior; arquivos de recuperacao, se houver, foram preservados em $stage" }
}
