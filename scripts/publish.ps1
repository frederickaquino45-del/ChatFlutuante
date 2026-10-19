param([string]$Runtime = 'win-x64')
$ErrorActionPreference = 'Stop'
if ($Runtime -notin @('win-x64','win-arm64')) { throw 'Use win-x64 ou win-arm64.' }
$root = Split-Path $PSScriptRoot -Parent
$output = Join-Path $root "artifacts/WfloatIA-$Runtime"
dotnet publish (Join-Path $root 'src/WindowFloatIA/WindowFloatIA.csproj') -c Release -r $Runtime --self-contained true --disable-build-servers -o $output
if ($LASTEXITCODE -ne 0) { throw 'Falha na publicação.' }
Copy-Item (Join-Path $root 'README.md'),(Join-Path $root 'LICENSE'),(Join-Path $root 'CONEXOES.md') -Destination $output
$zip = Join-Path $root "artifacts/WfloatIA-$Runtime.zip"
Compress-Archive -Path "$output/*" -DestinationPath $zip -Force
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($zip))" | Set-Content "$zip.sha256" -Encoding ascii
Write-Output "Pacote: $zip"
