$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$artifacts = Join-Path $repo 'artifacts'
. (Join-Path $PSScriptRoot 'PackageFiles.ps1')
$testRoot = Join-Path $artifacts ('package-test-' + [Guid]::NewGuid().ToString('N'))
$null = Assert-PackagePath $artifacts $testRoot
$null = New-Item -ItemType Directory -Path (Join-Path $testRoot 'scripts') -Force
function Check($condition, $message) { if (-not $condition) { throw "FAIL: $message" }; Write-Output "PASS: $message" }
try {
    foreach ($file in @('publish.ps1','PackageFiles.ps1','desinstalar.ps1','desinstalar.cmd')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination (Join-Path $testRoot 'scripts') }
    foreach ($file in @('README.md','CONEXOES.md','LICENSE','ATUALIZACAO.md','SECURITY.md','FERRAMENTAS.md')) { 'fixture' | Set-Content -LiteralPath (Join-Path $testRoot $file) }
    $fixtureArtifacts = Join-Path $testRoot 'artifacts'
    $null = New-Item -ItemType Directory -Path $fixtureArtifacts -Force
    $old = Join-Path $fixtureArtifacts 'WfloatIA-win-x64-20260101-010101-abcdef'
    $other = Join-Path $fixtureArtifacts 'WfloatIA-win-arm64-20260101-010101-abcdef'
    foreach ($directory in @($old,$other,(Join-Path $fixtureArtifacts 'previews'))) { $null = New-Item -ItemType Directory -Path $directory; 'keep' | Set-Content -LiteralPath (Join-Path $directory 'marker.txt') }
    'old' | Set-Content -LiteralPath "$old.zip"
    'old' | Set-Content -LiteralPath "$old.zip.sha256"
    $global:WfloatPackageTestFailBuild = $false
    function dotnet {
        if ($global:WfloatPackageTestFailBuild) { $global:LASTEXITCODE = 1; return }
        $arguments = @($args)
        $destination = $arguments[[Array]::IndexOf($arguments, '-o') + 1]
        'fixture executable' | Set-Content -LiteralPath (Join-Path $destination 'WindowFloatIA.exe')
        '{}' | Set-Content -LiteralPath (Join-Path $destination 'WindowFloatIA.runtimeconfig.json')
        $global:LASTEXITCODE = 0
    }
    & (Join-Path $testRoot 'scripts/publish.ps1')
    $package = Join-Path $fixtureArtifacts 'WfloatIA-win-x64'
    Check ((Test-Path -LiteralPath (Join-Path $package 'desinstalar.cmd')) -and (Test-Path -LiteralPath "$package.zip.sha256")) 'Package includes uninstaller, ZIP and checksum'
    Check (-not (Test-Path -LiteralPath $old) -and -not (Test-Path -LiteralPath "$old.zip")) 'Legacy packages removed after successful publication'
    Check ((Test-Path -LiteralPath $other) -and (Test-Path -LiteralPath (Join-Path $fixtureArtifacts 'previews/marker.txt'))) 'Other architecture and previews preserved'
    'obsolete' | Set-Content -LiteralPath (Join-Path $package 'obsolete.dll')
    $global:WfloatPackageTestFailBuild = $true
    $failed = $false
    try { & (Join-Path $testRoot 'scripts/publish.ps1') } catch { $failed = $true }
    Check ($failed -and (Test-Path -LiteralPath (Join-Path $package 'obsolete.dll'))) 'Build failure preserves previous package'
    $global:WfloatPackageTestFailBuild = $false
    & (Join-Path $testRoot 'scripts/publish.ps1')
    Check (-not (Test-Path -LiteralPath (Join-Path $package 'obsolete.dll'))) 'Second build replaces package without obsolete files'
    Check (@(Get-ChildItem -LiteralPath $fixtureArtifacts -Directory | Where-Object Name -like 'WfloatIA-win-x64*').Count -eq 1) 'Only one x64 package remains'
    $expectedHash = (Get-Content -LiteralPath "$package.zip.sha256").Split(' ')[0]
    Check ($expectedHash -eq (Get-FileHash -LiteralPath "$package.zip" -Algorithm SHA256).Hash.ToLowerInvariant()) 'ZIP checksum matches'
    # Force a failure after the old directory was moved, then confirm rollback.
    $stage = Join-Path $fixtureArtifacts '.rollback-test'
    $null = New-Item -ItemType Directory -Path (Join-Path $stage 'package') -Force
    'replacement' | Set-Content -LiteralPath (Join-Path $stage 'package/new.txt')
    'zip' | Set-Content -LiteralPath (Join-Path $stage 'package.zip')
    'hash' | Set-Content -LiteralPath (Join-Path $stage 'package.zip.sha256')
    $lock = [IO.File]::Open("$package.zip", [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
    $failed = $false
    try { Publish-PackageFiles $fixtureArtifacts $stage 'win-x64' } catch { $failed = $true } finally { $lock.Dispose() }
    Check ($failed -and (Test-Path -LiteralPath (Join-Path $package 'WindowFloatIA.exe')) -and -not (Test-Path -LiteralPath (Join-Path $package 'new.txt'))) 'Failed package swap restores previous directory'
    # Test uninstall against fixture files, with registry calls mocked (no actual registry mutation).
    $global:WfloatPackageTestRemovedStartup = $false
    function Get-ItemPropertyValue { '"' + (Join-Path $package 'WindowFloatIA.exe') + '" --background' }
    function Remove-ItemProperty { $global:WfloatPackageTestRemovedStartup = $true }
    'my personal note' | Set-Content -LiteralPath (Join-Path $package 'my-note.txt')
    & (Join-Path $package 'desinstalar.ps1') -WhatIf | Out-Null
    Check ((Test-Path -LiteralPath (Join-Path $package 'WindowFloatIA.exe')) -and -not $global:WfloatPackageTestRemovedStartup) 'Uninstall WhatIf changes nothing'
    $manifestPath = Join-Path $package 'package-manifest.json'
    $originalManifest = Get-Content -LiteralPath $manifestPath -Raw
    $manifest = $originalManifest | ConvertFrom-Json
    $manifest.Files += '..\escape.txt'
    $manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath
    $failed = $false
    try { & (Join-Path $package 'desinstalar.ps1') -Force } catch { $failed = $true }
    Check ($failed -and (Test-Path -LiteralPath (Join-Path $package 'WindowFloatIA.exe'))) 'Invalid manifest cannot escape package'
    $originalManifest | Set-Content -LiteralPath $manifestPath
    & (Join-Path $package 'desinstalar.ps1') -Force
    Check ($global:WfloatPackageTestRemovedStartup -and -not (Test-Path -LiteralPath (Join-Path $package 'WindowFloatIA.exe'))) 'Uninstaller removes matching startup and package binaries'
    Check ((Test-Path -LiteralPath (Join-Path $package 'my-note.txt')) -and (Test-Path -LiteralPath "$package.zip")) 'Uninstaller preserves personal files and external ZIP'
    $failed = $false
    try { Remove-PackagePath $fixtureArtifacts (Join-Path $testRoot 'README.md') } catch { $failed = $true }
    Check $failed 'Cleanup rejects paths outside artifacts'
} finally {
    Remove-Item Function:\dotnet -ErrorAction SilentlyContinue
    Remove-Item Function:\Get-ItemPropertyValue -ErrorAction SilentlyContinue
    Remove-Item Function:\Remove-ItemProperty -ErrorAction SilentlyContinue
    Remove-PackagePath -Root $artifacts -Path $testRoot
}

