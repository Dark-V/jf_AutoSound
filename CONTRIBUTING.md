# Contributing

Pin the Jellyfin tag and SDK, restore using the committed NuGet lockfiles and run the core and real-server tests before changing scan integration. A filename-only test is insufficient for provider ordering/cache changes: verify persisted `MediaStreams` after one ordinary scan of an unchanged video with a newly added track.

Use generated or owned test media. Do not attach copyrighted releases, real library paths, credentials or private server logs. Keep conservative matching and conflict refusal. Changes must not add copy/remux fallbacks, external runtime scripts or follow-up refresh loops.

## Releases

1. Update `<Version>` in the plugin project and add the matching section in `CHANGELOG.md`. Jellyfin uses four numeric components, such as `1.0.0.0`.
2. Commit the release source to `main` and push a matching tag, such as `v1.0.0.0`.
3. The **Publish Sidecar Bridge** workflow builds and tests the package on all three platforms. It publishes the tested Linux x64 job's ZIP and SHA-256 file, downloads both public assets to verify them, then appends the version to the catalog on `main`. The catalog entry uses the controller package's ABI, the real ZIP URL, its MD5 and GitHub's publication timestamp.

The workflow checks the public raw catalog URL after pushing. Re-running publication reuses matching assets and catalog entries. A version's existing assets and catalog entry are kept intact. Use a new version and tag to distribute a changed ZIP.
