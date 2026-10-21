[CmdletBinding(SupportsShouldProcess = $true)]
param([switch]$RemoveUserData, [switch]$Force)
$ErrorActionPreference = 'Stop'
# Portable uninstaller: only the current package's manifest files are removed.
function Assert-ContainedPath {
    param([string]$Root, [string]$Path)
    $base = [IO.Path]::GetFullPath($Root).TrimEnd([char[]]'\/')
    $full = [IO.Path]::GetFullPath($Path)
    if (-not $full.StartsWith($base + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "Caminho fora da pasta permitida: $full" }
    $cursor = $full
    while ($cursor -and $cursor.Length -ge $base.Length) {
        if (Test-Path -LiteralPath $cursor) {
            if (((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Link/juncao nao permitido: $cursor" }
        }
        $cursor = Split-Path -Parent $cursor
    }
    return $full
}
function Assert-Tree {
    param([string]$Path)
    $pending = New-Object 'System.Collections.Generic.Stack[string]'
    $pending.Push($Path)
    while ($pending.Count -gt 0) {
        foreach ($entry in Get-ChildItem -LiteralPath $pending.Pop() -Force) {
            if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Link/juncao nao permitido: $($entry.FullName)" }
            if ($entry.PSIsContainer) { $pending.Push($entry.FullName) }
        }
    }
}
$packageRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$manifestPath = Assert-ContainedPath $packageRoot (Join-Path $packageRoot 'package-manifest.json')
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw 'Execute desinstalar.cmd dentro de um pacote gerado do Wfloat.IA, nunca no repositorio.' }
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.Product -ne 'WindowFloatIA' -or $manifest.Format -ne 1 -or 'WindowFloatIA.exe' -notin $manifest.Files -or 'WindowFloatIA.runtimeconfig.json' -notin $manifest.Files) { throw 'Manifesto de pacote invalido.' }
$files = @($manifest.Files | ForEach-Object {
    if ([string]::IsNullOrWhiteSpace($_) -or [IO.Path]::IsPathRooted($_) -or $_ -match '(^|[\\/])\.\.([\\/]|$)|:') { throw 'Caminho invalido no manifesto.' }
    $checked = Assert-ContainedPath $packageRoot (Join-Path $packageRoot $_)
    if (Test-Path -LiteralPath $checked -PathType Container) { throw 'O manifesto so pode listar arquivos.' }
    $checked
})
Assert-Tree $packageRoot
$exe = Join-Path $packageRoot 'WindowFloatIA.exe'
foreach ($process in Get-Process -Name WindowFloatIA -ErrorAction SilentlyContinue) {
    if ($process.Path -and $process.Path.Equals($exe, [StringComparison]::OrdinalIgnoreCase)) { throw 'Escolha Sair no icone do Wfloat.IA na bandeja e execute o desinstalador novamente.' }
}
if (-not $Force -and -not $WhatIfPreference) {
    Write-Host "Remover esta copia: $packageRoot"
    if ((Read-Host 'Continuar? [s/N]') -notmatch '^[sS]$') { Write-Host 'Cancelado.'; return }
    if (-not $RemoveUserData) { $RemoveUserData = (Read-Host 'Apagar tambem contas, preferencias e conversas deste usuario? Afeta todas as copias do Wfloat.IA. [s/N]') -match '^[sS]$' }
}
# Validate the optional data path before making any changes.
$dataRoot = $null
if ($RemoveUserData) {
    $localRoot = [Environment]::GetFolderPath('LocalApplicationData')
    if ([string]::IsNullOrWhiteSpace($localRoot)) { throw 'Pasta de dados do usuario indisponivel.' }
    $dataRoot = Assert-ContainedPath $localRoot (Join-Path $localRoot 'WindowFloatIA')
    if (Test-Path -LiteralPath $dataRoot) { Assert-Tree $dataRoot }
}
$runPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$startup = Get-ItemPropertyValue -LiteralPath $runPath -Name 'WindowFloatIA' -ErrorAction SilentlyContinue
if ($startup -is [string] -and $startup -match ('^"' + [regex]::Escape($exe) + '"(?:\s|$)')) {
    if ($PSCmdlet.ShouldProcess('Inicializacao automatica desta copia', 'Remover')) { Remove-ItemProperty -LiteralPath $runPath -Name 'WindowFloatIA' }
}
if ($dataRoot -and (Test-Path -LiteralPath $dataRoot)) {
    $null = Assert-ContainedPath ([Environment]::GetFolderPath('LocalApplicationData')) $dataRoot
    Assert-Tree $dataRoot
    if ($PSCmdlet.ShouldProcess($dataRoot, 'Apagar contas, preferencias e conversas de todas as copias')) { Remove-Item -LiteralPath $dataRoot -Recurse -Force }
}
# Keep uninstall files until last so a partial failure can be retried.
$files = @($files | Where-Object { [IO.Path]::GetFileName($_) -notin @('desinstalar.ps1','desinstalar.cmd') }) + @((Join-Path $packageRoot 'desinstalar.cmd'), (Join-Path $packageRoot 'desinstalar.ps1'), $manifestPath)
foreach ($file in $files | Select-Object -Unique) {
    $null = Assert-ContainedPath $packageRoot $file
    if ((Test-Path -LiteralPath $file -PathType Leaf) -and $PSCmdlet.ShouldProcess($file, 'Remover arquivo do pacote')) { Remove-Item -LiteralPath $file -Force }
}
# Remove only empty directories; files added by the user are preserved.
foreach ($directory in Get-ChildItem -LiteralPath $packageRoot -Directory -Recurse -Force | Sort-Object { $_.FullName.Length } -Descending) {
    $null = Assert-ContainedPath $packageRoot $directory.FullName
    if (-not (Get-ChildItem -LiteralPath $directory.FullName -Force | Select-Object -First 1)) {
        if ($PSCmdlet.ShouldProcess($directory.FullName, 'Remover pasta vazia')) { Remove-Item -LiteralPath $directory.FullName }
    }
}
if (-not (Get-ChildItem -LiteralPath $packageRoot -Force | Select-Object -First 1)) {
    if ($PSCmdlet.ShouldProcess($packageRoot, 'Remover pasta vazia do pacote')) { Remove-Item -LiteralPath $packageRoot }
}
Write-Host 'Remocao concluida (ou simulada com -WhatIf). Arquivos extras, ZIPs e atalhos criados manualmente nao sao removidos.'
if (-not $RemoveUserData) { Write-Host 'Contas, preferencias e conversas foram preservadas.' }
