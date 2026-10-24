# Releasing Wiina

`scripts/release.ps1` is the single entry point for a release.

```powershell
pwsh -File scripts/release.ps1 -Version 1.2.3
```

Use `-NoInstaller` to build only the portable zip, or `-SkipPackage` to check
the version-sync edits without building anything.

The script:

1. Synchronises the version in every source of truth:
   `Directory.Build.props`, `Branding.Version`,
   `PackageManifest.Version`/`ArchiveFileName`, the `assemblyIdentity` in
   `src/Wiina.App/app.manifest`, the `-Version` default in `scripts/package.ps1`
   and `MyAppVersion` in `installer/wiina.iss`.
2. Builds the self-contained portable payload through
   `scripts/package.ps1`, writing
   `artifacts/wiina-<version>-win-x64.zip`.
3. Verifies the **payload** `Wiina.dll` inside the staging directory reports
   `-Version` before zipping (the build uses `Platform=x64 -t:Rebuild`, so a
   stale sibling output cannot be packaged). `release.ps1` also reads the
   managed assembly version from `src/Wiina.App/bin/x64/Release` and fails on a
   mismatch.
4. If Inno Setup 6 (`iscc.exe`) is on `PATH` or in its default install
   directory, unpacks the portable archive to `artifacts/publish` and compiles
   `installer/wiina.iss` to `artifacts/Wiina-Setup-<version>-x64.exe`. Without
   Inno Setup it skips the installer with a warning; the `.iss` is already
   version-stamped.
5. Prints the absolute path, size and SHA-256 of each artifact.

## Portable-only flow (package.ps1)

```powershell
# 1) Build + stage + zip; fails if any required file or the Wiina.dll version is wrong
pwsh -File scripts/package.ps1 -Version 1.2.3

# 2) Confirm the payload version instead of trusting the file name
$dll = '.\artifacts\wiina-1.2.3-win-x64\Wiina.dll'
[System.Diagnostics.FileVersionInfo]::GetVersionInfo($dll).FileVersion   # 1.2.3.0
(Get-Item $dll).VersionInfo.ProductVersion                               # 1.2.3+<commit>

# 3) Recompute the asset hash for the release notes / acceptance record
Get-FileHash .\artifacts\wiina-1.2.3-win-x64.zip -Algorithm SHA256
(Get-Item .\artifacts\wiina-1.2.3-win-x64.zip).Length
```

`package.ps1` refuses to zip when `Wiina.dll` does not report
`<version>.0`, when `libmpv-2.dll` is missing from `third_party/libmpv/`, or
when `LICENSE`, `NOTICE`, `README.md`, `SOURCE.txt` are absent from the
payload. It also rejects extra `third_party/libmpv` DLLs/EXEs so an accidental
dependency cannot slip into the archive.

Artifacts are gitignored. Commit the version source changes (and any changelog)
but never `artifacts/`. The release build is unsigned, so SmartScreen may warn
on first launch.

## Publishing the GitHub release

The portable zip is a release **asset**, not a committed file. After merging
the release commit to `main` and building the archive:

1. Create and push the version tag:

   ```powershell
   git switch main
   git pull --ff-only origin main
   git tag -a v1.2.3 -m "Wiina 1.2.3"
   git push origin main
   git push origin v1.2.3
   ```

2. Publish with GitHub CLI (recommended, reproducible):

   ```powershell
   gh auth login
   gh release create v1.2.3 artifacts/wiina-1.2.3-win-x64.zip `
     --title "Wiina 1.2.3" `
     --notes-file docs/release/notes.md `
     --latest
   ```

   `gh` uploads the zip as a release asset. The file stays ignored by git.

3. Or use the web UI: **Releases → Draft a new release**, choose tag
   `v1.2.3` (target `main`), title `Wiina 1.2.3`, paste
   `docs/release/notes.md` as the description, attach
   `artifacts/wiina-1.2.3-win-x64.zip`, and publish as the latest release.

4. Verify the asset size and SHA-256 printed by `release.ps1` against the
   record in `docs/acceptance/`. Do not copy an older hash: the table below is
   historical, and every release has a different payload.

## Historical artifacts

Use these only to check that an old record was copied correctly — never as the
expected hash for a new build.

| Version | Bytes | SHA-256 |
|---|---|---|
| 1.0.2 | 145,557,990 | `14C1218C09C8EFDD4C98B80BE796902269788514FA085D925F01E2FD3583D8D3` |
| 1.1.0 | 145,561,023 | `8A197A5E22584ED28C29D257E994CB15C976DD3A1B757AE40169C75CE6A445BD` |
| 1.1.2 | 145,561,657 | `E11B87A19C1FEAEDBA9E8997D5305077DAAE78F8C419323EE681C6277EEF80EC` |
| 1.2.0 | 145,566,693 | `ADA7CBE59A3BF091FEED9F9DCB9F3F2266DD72542E9984BCD18B388520193F7B` |
| 1.2.1 | 145,580,027 | `3D9ECFD46D7FC55D0AE180CED78473662C40A6DEEC180AC879B80D9430E4F901` |
| 1.2.2 | 145,582,114 | `E087101B61FA1AE0E09E3EA0415D26BB1AD3987778EE99241073E2E7E1F0ADC3` |

The 1.2.3 archive will not match 1.2.2: Win2D (and the capture/blur code) is
no longer part of the payload and new source/test files were added. Record the
actual size and hash in `docs/acceptance/v1.2.3.md` after building.
