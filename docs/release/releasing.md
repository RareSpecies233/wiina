# Releasing Wiina

`scripts/release.ps1` is the single entry point for a release.

```powershell
pwsh -File scripts/release.ps1 -Version 1.1.0
```

The script:

1. Synchronises the version in every source of truth:
   `Directory.Build.props`, `Branding.Version`,
   `PackageManifest.Version`/`ArchiveFileName`, the `assemblyIdentity` in
   `src/Wiina.App/app.manifest`, the default in `scripts/package.ps1` and
   `MyAppVersion` in `installer/wiina.iss`.
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

Use `-NoInstaller` to build only the portable zip, or `-SkipPackage` to check
the version-sync edits without building anything.

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
   git tag -a v1.0.2 -m "Wiina 1.0.2"
   git push origin main
   git push origin v1.0.2
   ```

2. Publish with GitHub CLI (recommended, reproducible):

   ```powershell
   gh auth login
   gh release create v1.0.2 artifacts/wiina-1.0.2-win-x64.zip `
     --title "Wiina 1.0.2" `
     --notes-file docs/release/notes.md `
     --latest
   ```

   `gh` uploads the zip as a release asset. The file stays ignored by git.

3. Or use the web UI: **Releases → Draft a new release**, choose tag
   `v1.0.2` (target `main`), title `Wiina 1.0.2`, paste
   `docs/release/notes.md` as the description, attach
   `artifacts/wiina-1.0.2-win-x64.zip`, and publish as the latest release.

4. Verify the asset size and SHA-256 printed by `release.ps1`. The current
   v1.0.2 archive is 145,557,990 bytes, SHA-256
   `14C1218C09C8EFDD4C98B80BE796902269788514FA085D925F01E2FD3583D8D3`.

