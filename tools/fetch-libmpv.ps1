<#
.SYNOPSIS
    Download and unpack a pinned libmpv (mpv-dev) build for Wiina.

.DESCRIPTION
    Fetches the exact shinchiro mpv-dev-x86_64 archive, verifies its SHA256,
    and extracts include/mpv/*.h + libmpv-2.dll into third_party/libmpv/.
    The binaries are intentionally NOT committed to git (GPL compliance and
    repository size); this script is the reproducible way to obtain them.

.PARAMETER Destination
    Target directory. Default: <repo>/third_party/libmpv

.PARAMETER Force
    Re-download even if the destination already looks complete.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools/fetch-libmpv.ps1
#>
[CmdletBinding()]
param(
    [string]$Destination = '',
    [switch]$Force
)

if (-not $Destination) {
    $Destination = Join-Path $PSScriptRoot '..\third_party\libmpv'
}

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

# --- Pinned build (update these three values together) ---------------------
$MpvRelease  = '20261018'
$MpvTag      = '20261018-git-01e2533f31'
$MpvArchive  = "mpv-dev-x86_64-$MpvTag.7z"
$MpvUrl      = "https://github.com/shinchiro/mpv-winbuild-cmake/releases/download/$MpvRelease/$MpvArchive"
$MpvSha256   = 'D7F5621F42B6E161FE032DF914A10BDECE28A89DF3E57C6C3E16A5A250929B5E'
$MpvSize     = 31595598

# 7zr.exe (7-Zip standalone, public domain) used only to unpack the archive.
$SevenZipUrl    = 'https://www.7-zip.org/a/7zr.exe'
$SevenZipSha256 = '256FECA8E274E5DA655E2A284FABAFD9F554365EB164862089DACD4E8276D282'
# --------------------------------------------------------------------------

function Get-FileWithHash {
    param(
        [Parameter(Mandatory)] [string]$Uri,
        [Parameter(Mandatory)] [string]$OutFile,
        [Parameter(Mandatory)] [string]$Sha256,
        [long]$Size = 0
    )

    $needDownload = $true
    if (Test-Path $OutFile) {
        $existing = Get-Item $OutFile
        if (($Size -eq 0 -or $existing.Length -eq $Size) -and
            (Get-FileHash $OutFile -Algorithm SHA256).Hash -eq $Sha256) {
            $needDownload = $false
        }
    }

    if ($needDownload) {
        Write-Host "Downloading $Uri"
        $tmp = "$OutFile.part"
        if (Test-Path $tmp) { Remove-Item $tmp -Force }
        Invoke-WebRequest -Uri $Uri -OutFile $tmp -UseBasicParsing
        $hash = (Get-FileHash $tmp -Algorithm SHA256).Hash
        if ($hash -ne $Sha256) {
            Remove-Item $tmp -Force
            throw "SHA256 mismatch for $Uri`n  expected: $Sha256`n  actual:   $hash"
        }
        Move-Item $tmp $OutFile -Force
    }
}

$Destination = [IO.Path]::GetFullPath($Destination)
New-Item -ItemType Directory -Force -Path $Destination | Out-Null

$dll = Join-Path $Destination 'libmpv-2.dll'
$headers = Join-Path $Destination 'include\mpv\client.h'
if (-not $Force -and (Test-Path $dll) -and (Test-Path $headers)) {
    Write-Host "libmpv already present at $Destination (use -Force to refresh)."
    exit 0
}

$cache = Join-Path $env:TEMP 'wiina-libmpv'
New-Item -ItemType Directory -Force -Path $cache | Out-Null
$archivePath = Join-Path $cache $MpvArchive
$sevenZip = Join-Path $cache '7zr.exe'

Get-FileWithHash -Uri $MpvUrl -OutFile $archivePath -Sha256 $MpvSha256 -Size $MpvSize
Get-FileWithHash -Uri $SevenZipUrl -OutFile $sevenZip -Sha256 $SevenZipSha256

Write-Host "Extracting to $Destination"
# Extract only what we need: headers + the runtime DLL.
& $sevenZip x $archivePath "-o$Destination" 'include\mpv\*' 'libmpv-2.dll' -y | Out-Null
if ($LASTEXITCODE -ne 0) { throw "7zr failed with exit code $LASTEXITCODE" }

if (-not (Test-Path $dll)) { throw "libmpv-2.dll was not extracted to $Destination" }
if (-not (Test-Path $headers)) { throw "include/mpv/client.h was not extracted to $Destination" }

@"
Wiina libmpv runtime
====================
source  : $MpvUrl
release : $MpvRelease
tag     : $MpvTag
sha256  : $MpvSha256
license : mpv is GPL-2.0-or-later by default (see https://github.com/mpv-player/mpv/blob/master/Copyright)
note    : This directory is gitignored. Do not redistribute without the
          corresponding license texts and source offer (see docs/licensing/AUDIT.md).
"@ | Set-Content -Path (Join-Path $Destination 'VERSION.txt') -Encoding UTF8

$ver = (Get-Item $dll).VersionInfo
Write-Host ""
Write-Host "Done: $dll"
Write-Host ("File version: " + ($(if ($ver.FileVersion) { $ver.FileVersion } else { 'n/a' })))
Write-Host "Docs: $Destination\VERSION.txt"
