# Changelog

## 1.0.3.0

- Match the longest complete video basename before a dot-separated track suffix. Read the suffix as track metadata and use the folder to fill in missing information.
- Keep the existing checks for episode type, numbering, edition, and ambiguous matches when a complete basename is unavailable.

## 1.0.2.0

- Support track labels when a filename suffix agrees with its containing directory.
- Reuse existing hardlinks when their destination name matches the planned link and both paths point to the same file.
- Add an optional folder limit so a normal scan can process one series inside an enabled library.

## 1.0.1.0

- Attach external tracks to linked versions that Jellyfin skips during an ordinary scan. Each version is matched and probed using its own video path.
- Verify the tracks of both linked episode versions through Jellyfin's playback API.

## 1.0.0.0

- First catalog release for Jellyfin 12.4. Install Sidecar Bridge from the plugin catalog and restart Jellyfin.
- Attach nested audio and subtitles during the normal library scan. Includes library selection, preview, diagnostics and ownership tracking for hardlinks.
- GitHub Actions verifies the installation package on Windows x64, Linux x64 and Linux ARM64 before publication.
