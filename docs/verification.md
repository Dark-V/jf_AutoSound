# Verification record

Recorded 2026-11-04. Jellyfin v12.4 commit `83d0d0e7f76e23e2e085c8ff67d28b772d35967d`. SDK 10.0.403, runtime 10.0.14, Windows x64, local NTFS.

## Executed locally

- Plugin Release build: succeeded, zero warnings/errors.
- `dotnet test tests/SidecarBridge.Tests -c Release --no-restore`: **54 passed, 2 skipped** (the two Linux-only filesystem tests), zero failed.
- The Windows suite includes a real deny-create ACL test, file identity, source replacement, unowned/user-file conflicts, parallel requests, cancellation, registry restart/retry, traversal and junction refusal, Unicode and episode/edition matching, later arrivals, collisions and preview.
- Minimal real-server A/B prototype: passed before feature expansion.
- Full real-server test: passed for first-scan external audio/subtitles, one-scan later audio on unchanged video, source replacement, stable registry on an unchanged scan, admin preview and rejection of anonymous diagnostic access. The final test also checks reserved titles, flags and multi-stream metadata; see the committed sanitized proof for the final run.
- Original fixture hashes and hardlink identities were checked. The video was not edited to force an update. No per-item refresh endpoint was called.

Raw local paths/tokens are not committed. `tests/evidence/windows-proof.json` contains sanitized stream evidence. The unredacted disposable server log and original proof are development intermediates only. CI uploads fresh per-platform `proof.json`, server logs, TRX results and package artifacts for each run.

## Reproduce

```sh
dotnet restore src/Jellyfin.Plugin.SidecarBridge --locked-mode
dotnet restore tests/SidecarBridge.Tests --locked-mode
dotnet build src/Jellyfin.Plugin.SidecarBridge -c Release --no-restore
dotnet test tests/SidecarBridge.Tests -c Release --no-restore
git clone --depth 1 --branch v12.4 https://github.com/jellyfin/jellyfin.git integration-work/jellyfin
dotnet publish integration-work/jellyfin/Jellyfin.Server/Jellyfin.Server.csproj -c Release -o integration-work/server -p:EnableNETAnalyzers=false -p:RunAnalyzers=false
python tests/integration/one_scan.py --server integration-work/server/jellyfin.dll --plugin src/Jellyfin.Plugin.SidecarBridge/bin/Release/net10.0 --work integration-work/proof
python tools/package.py
```

FFmpeg must be installed **for fixture generation and the disposable Jellyfin server**. It is not invoked by plugin code. `--work` must be a new empty directory; the test refuses to erase existing directories. The test starts only its own loopback server, generates a random temporary admin password, and stops that server on exit.

## Platform claims

| Target | Local execution | CI coverage |
| --- | --- | --- |
| Windows x64 | Core/native tests + full real-server test passed | `windows-2025` |
| Linux x64 | Not available locally | `ubuntu-24.04`, including native permissions/cross-device tests and real-server scan |
| Linux ARM64 | Not available locally | `ubuntu-24.04-arm`, same checks on native ARM64 |
| Docker / network storage / real playback | Not tested | No deployment/playback coverage claimed |

See the repository's **current GitHub Actions run** for remote results. A configured job is not evidence that it passed; only a completed successful run and its artifacts establish that platform's test status.
