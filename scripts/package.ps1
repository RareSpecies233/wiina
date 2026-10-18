#Requires -Version 7.0
<#
.SYNOPSIS
    Builds the portable Wiina v1 package.

.DESCRIPTION
    Publishes Wiina.App as a self-contained win-x64 Release build into a
    temporary staging directory under %TEMP%, adds the dynamically linked
    libmpv binary plus license/source files, verifies the required payload,
    and writes artifacts/wiina-<version>-win-x64.zip.

    Nothing is committed: artifacts/ and third_party/libmpv/ are gitignored.

.PARAMETER Configuration
    Build configuration; defaults to Release.

.PARAMETER Version
    Product version stamped into the archive name. Must match
    Directory.Build.props / PackageManifest.Version for a release build.

.EXAMPLE
    pwsh -File scripts/package.ps1
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Version = '1.0.0'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$runtime = 'win-x64'
$project = Join-Path $root 'src\Wiina.App\Wiina.App.csproj'
$libmpvSource = Join-Path $root 'third_party\libmpv\libmpv-2.dll'
$artifactsDir = Join-Path $root 'artifacts'
$archivePath = Join-Path $artifactsDir "wiina-$Version-$runtime.zip"
$requiredRootFiles = @('LICENSE', 'NOTICE', 'README.md')
$requiredPayloadFiles = @('Wiina.exe', 'libmpv-2.dll') + $requiredRootFiles + @('SOURCE.txt')

$dotnet = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) {
    $dotnet = 'dotnet'
}

if (-not (Test-Path -LiteralPath $project)) {
    throw "Project not found: $project"
}
if (-not (Test-Path -LiteralPath $libmpvSource)) {
    throw "libmpv-2.dll not found at $libmpvSource. Run tools/fetch-libmpv.ps1 first; the binary is intentionally not committed."
}
foreach ($file in $requiredRootFiles) {
    $path = Join-Path $root $file
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Required release file is missing: $path"
    }
}

$staging = Join-Path $env:TEMP ("wiina-package-" + [guid]::NewGuid().ToString('N'))
$publishDir = Join-Path $staging 'publish'
$payloadDir = Join-Path $staging 'payload'

try {
    New-Item -ItemType Directory -Path $publishDir, $payloadDir -Force | Out-Null

    Write-Host "Publishing $project ($Configuration, $runtime, self-contained)..."
    & $dotnet publish $project -c $Configuration -r $runtime --self-contained true `
        -o $publishDir -p:Version=$Version -p:DebugType=None -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE"
    }

    Copy-Item -Path (Join-Path $publishDir '*') -Destination $payloadDir -Recurse -Force
    Copy-Item -LiteralPath $libmpvSource -Destination (Join-Path $payloadDir 'libmpv-2.dll') -Force
    foreach ($file in $requiredRootFiles) {
        Copy-Item -LiteralPath (Join-Path $root $file) -Destination (Join-Path $payloadDir $file) -Force
    }

    # libmpv needs no separate FFmpeg DLLs; if a future libmpv build adds
    # companions, copy them explicitly and list them here.
    $extraMpv = Get-ChildItem -LiteralPath (Join-Path $root 'third_party\libmpv') -File |
        Where-Object { $_.Extension -in @('.dll', '.exe') -and $_.Name -ne 'libmpv-2.dll' }
    if ($extraMpv) {
        Write-Warning ("Additional mpv binaries were not packaged: " + (($extraMpv | ForEach-Object Name) -join ', '))
    }

    $commit = 'unknown'
    $sourceUrl = 'https://github.com/RareSpecies233/wiina'
    try {
        $commit = (& git -C $root rev-parse HEAD 2>$null).Trim()
        $remote = (& git -C $root remote get-url origin 2>$null)
        if ($remote) { $sourceUrl = ($remote.Trim() -replace '\.git$', '') }
    } catch {
        $commit = 'unknown'
    }
    $source = @"
Wiina $Version ($runtime)
Built: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')
Commit: $commit

Corresponding source:
$sourceUrl

Wiina is licensed under GPL-3.0-only. libmpv/FFmpeg are dynamically linked;
see NOTICE for the license and written-offer details.
"@
    Set-Content -LiteralPath (Join-Path $payloadDir 'SOURCE.txt') -Value $source -Encoding utf8

    $missing = @()
    foreach ($file in $requiredPayloadFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $payloadDir $file))) {
            $missing += $file
        }
    }
    if ($missing.Count -gt 0) {
        throw "Packaged payload is missing required files: $($missing -join ', ')"
    }

    New-Item -ItemType Directory -Path $artifactsDir -Force | Out-Null
    if (Test-Path -LiteralPath $archivePath) {
        Remove-Item -LiteralPath $archivePath -Force
    }

    Compress-Archive -Path (Join-Path $payloadDir '*') -DestinationPath $archivePath -CompressionLevel Optimal

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead($archivePath)
    try {
        $entries = $zip.Entries | ForEach-Object FullName
        $missingInZip = @($requiredPayloadFiles | Where-Object { $_ -notin $entries })
        if ($missingInZip.Count -gt 0) {
            throw "Archive is missing required entries: $($missingInZip -join ', ')"
        }
        $sizeMb = [math]::Round((Get-Item -LiteralPath $archivePath).Length / 1MB, 2)
        Write-Host "Created $archivePath ($sizeMb MB, $($entries.Count) entries)."
    } finally {
        $zip.Dispose()
    }
} finally {
    if (Test-Path -LiteralPath $staging) {
        Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
    }
}
