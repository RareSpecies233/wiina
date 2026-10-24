#Requires -Version 7.0
<#
.SYNOPSIS
    Builds the portable Wiina v1 package.

.DESCRIPTION
    Publishes Wiina.App as a self-contained win-x64 Release build and the small
    trimmed, single-file portable uninstaller (src/Wiina.Uninstall) into a
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
    [string]$Version = '1.3.0'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$runtime = 'win-x64'
$project = Join-Path $root 'src\Wiina.App\Wiina.App.csproj'
$uninstallerProject = Join-Path $root 'src\Wiina.Uninstall\Wiina.Uninstall.csproj'
$uninstallerExeName = 'Uninstall Wiina.exe'
$libmpvSource = Join-Path $root 'third_party\libmpv\libmpv-2.dll'
$artifactsDir = Join-Path $root 'artifacts'
$archivePath = Join-Path $artifactsDir "wiina-$Version-$runtime.zip"
$requiredRootFiles = @('LICENSE', 'NOTICE', 'README.md')
$requiredPayloadFiles = @('Wiina.exe', 'libmpv-2.dll', $uninstallerExeName) + $requiredRootFiles + @('SOURCE.txt')

$dotnet = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) {
    $dotnet = 'dotnet'
}

if (-not (Test-Path -LiteralPath $project)) {
    throw "Project not found: $project"
}
if (-not (Test-Path -LiteralPath $uninstallerProject)) {
    throw "Portable uninstaller project not found: $uninstallerProject"
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
$payloadDir = Join-Path $staging 'payload'

try {
    New-Item -ItemType Directory -Path $payloadDir -Force | Out-Null

    # The portable uninstaller is built first and in isolation: it is tiny,
    # independent of the WinUI build, and fails fast if src/Wiina.Uninstall is
    # broken. It is published single-file/trimmed (see its csproj).
    Write-Host "Publishing $uninstallerProject ($Configuration, $runtime, single-file trimmed)..."
    $uninstallerDir = Join-Path $staging 'uninstaller'
    & $dotnet publish $uninstallerProject -c $Configuration -r $runtime --self-contained true `
        -p:Version=$Version -p:DebugType=None -p:DebugSymbols=false -o $uninstallerDir
    if ($LASTEXITCODE -ne 0) {
        throw "Uninstaller publish failed with exit code $LASTEXITCODE"
    }

    $uninstallerExe = Join-Path $uninstallerDir 'Wiina.Uninstall.exe'
    if (-not (Test-Path -LiteralPath $uninstallerExe)) {
        throw "Published uninstaller not found: $uninstallerExe"
    }

    Write-Host "Building $project ($Configuration, $runtime, self-contained)..."
    # Build the exact x64 target we copy below. Without Platform=x64 the build
    # lands in a different output tree than the TargetDir query, which can
    # silently package a stale assembly under a new version number.
    & $dotnet build $project -c $Configuration -r $runtime --self-contained true `
        -p:Version=$Version -p:Platform=x64 -p:DebugType=None -p:DebugSymbols=false -t:Rebuild
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed with exit code $LASTEXITCODE"
    }

    # Use the normal build output rather than `dotnet publish`: publishing
    # emits loose XBF/PRI files for an unpackaged WinUI app and the packaged
    # executable then fails in MainWindow.InitializeComponent with a
    # XamlParseException. The build output embeds the XAML resources and has
    # been verified to launch from the portable zip.
    $targetDir = (& $dotnet msbuild $project -nologo -getProperty:TargetDir `
        -p:Configuration=$Configuration -p:RuntimeIdentifier=$runtime -p:Platform=x64 |
        Select-Object -Last 1).Trim()
    if (-not [System.IO.Path]::IsPathRooted($targetDir)) {
        $targetDir = Join-Path $root $targetDir
    }
    $targetDir = [System.IO.Path]::GetFullPath($targetDir)
    if (-not (Test-Path -LiteralPath (Join-Path $targetDir 'Wiina.exe'))) {
        throw "Build output does not contain Wiina.exe: $targetDir"
    }

    Copy-Item -Path (Join-Path $targetDir '*') -Destination $payloadDir -Recurse -Force
    Copy-Item -LiteralPath $libmpvSource -Destination (Join-Path $payloadDir 'libmpv-2.dll') -Force
    foreach ($file in $requiredRootFiles) {
        Copy-Item -LiteralPath (Join-Path $root $file) -Destination (Join-Path $payloadDir $file) -Force
    }

    # The portable uninstall entry point sits next to Wiina.exe under a
    # user-facing name; the build output assembly name stays Wiina.Uninstall.
    Copy-Item -LiteralPath $uninstallerExe -Destination (Join-Path $payloadDir $uninstallerExeName) -Force
    $uninstallerSize = (Get-Item -LiteralPath (Join-Path $payloadDir $uninstallerExeName)).Length
    if ($uninstallerSize -lt 1MB) {
        throw "Packaged $uninstallerExeName looks truncated ($uninstallerSize bytes)."
    }

    # Verify the payload we are about to zip, not a sibling build output: an
    # incremental build can otherwise ship the previous version under the new
    # archive name.
    $payloadAssembly = Join-Path $payloadDir 'Wiina.dll'
    $payloadVersion = [System.Reflection.AssemblyName]::GetAssemblyName($payloadAssembly).Version
    if ($payloadVersion -ne [version]"$Version.0") {
        throw "Payload Wiina.dll is $payloadVersion but the requested release is $Version."
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
