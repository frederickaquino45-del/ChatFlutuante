# Shared path checks for package generation. Never follow junctions/symbolic links.
function Assert-PackagePath {
    param([Parameter(Mandatory)][string]$Root, [Parameter(Mandatory)][string]$Path)
    $rootPath = [IO.Path]::GetFullPath($Root).TrimEnd([char[]]'\/')
    $fullPath = [IO.Path]::GetFullPath($Path)
    if (-not $fullPath.StartsWith($rootPath + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Caminho fora da pasta de artefatos: $fullPath"
    }
    $cursor = $fullPath
    while ($cursor -and $cursor.Length -ge $rootPath.Length) {
        if (Test-Path -LiteralPath $cursor) {
            $item = Get-Item -LiteralPath $cursor -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Link/juncao nao permitido: $cursor" }
        }
        $cursor = Split-Path -Parent $cursor
    }
    if (Test-Path -LiteralPath $fullPath -PathType Container) {
        $pending = New-Object 'System.Collections.Generic.Stack[string]'
        $pending.Push($fullPath)
        while ($pending.Count -gt 0) {
            foreach ($entry in Get-ChildItem -LiteralPath $pending.Pop() -Force) {
                if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Link/juncao nao permitido: $($entry.FullName)" }
                if ($entry.PSIsContainer) { $pending.Push($entry.FullName) }
            }
        }
    }
    return $fullPath
}
function Remove-PackagePath {
    param([string]$Root, [string]$Path)
    $checkedPath = Assert-PackagePath -Root $Root -Path $Path
    if (Test-Path -LiteralPath $checkedPath) { Remove-Item -LiteralPath $checkedPath -Recurse -Force -ErrorAction Stop }
}
function Get-PreviousPackages {
    param([string]$Root, [string]$Runtime)
    $pattern = '^WfloatIA-' + [regex]::Escape($Runtime) + '-\d{8}-\d{6}-[0-9a-f]{6}(\.zip(\.sha256)?)?$'
    Get-ChildItem -LiteralPath $Root -Force | Where-Object { $_.Name -match $pattern }
}
function Publish-PackageFiles {
    param([string]$Root, [string]$Stage, [string]$Runtime)
    $entries = @(
        @{ Source = 'package'; Target = "WfloatIA-$Runtime"; Backup = 'previous-package'; MovedOld = $false; MovedNew = $false },
        @{ Source = 'package.zip'; Target = "WfloatIA-$Runtime.zip"; Backup = 'previous.zip'; MovedOld = $false; MovedNew = $false },
        @{ Source = 'package.zip.sha256'; Target = "WfloatIA-$Runtime.zip.sha256"; Backup = 'previous.zip.sha256'; MovedOld = $false; MovedNew = $false }
    )
    # Validate every source and destination before the first move.
    foreach ($entry in $entries) {
        foreach ($path in @((Join-Path $Stage $entry.Source), (Join-Path $Root $entry.Target), (Join-Path $Stage $entry.Backup))) {
            $null = Assert-PackagePath -Root $Root -Path $path
        }
        if (-not (Test-Path -LiteralPath (Join-Path $Stage $entry.Source))) { throw "Pacote incompleto: $($entry.Source)" }
    }
    try {
        foreach ($entry in $entries) {
            $target = Join-Path $Root $entry.Target
            if (Test-Path -LiteralPath $target) { Move-Item -LiteralPath $target -Destination (Join-Path $Stage $entry.Backup); $entry.MovedOld = $true }
            Move-Item -LiteralPath (Join-Path $Stage $entry.Source) -Destination $target; $entry.MovedNew = $true
        }
    } catch {
        $originalError = $_
        [array]::Reverse($entries)
        foreach ($entry in $entries) {
            $target = Join-Path $Root $entry.Target
            if ($entry.MovedNew) { Remove-PackagePath -Root $Root -Path $target }
            if ($entry.MovedOld) { Move-Item -LiteralPath (Join-Path $Stage $entry.Backup) -Destination $target }
        }
        throw $originalError
    }
}
