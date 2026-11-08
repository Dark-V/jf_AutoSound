# Changelog

## 1.0.2.0

- Match external tracks whose filenames end in a title confirmed by their folder, such as `.StudioBand.mka`, `.Amediateka.ass` and `.signs.ass` under `надписи`.
- Create the matching `.ru.<title>` hardlinks and recognize existing links with those names. Verify the working and automatic layouts through Jellyfin's playback API.
- Add an optional folder limit so a normal scan can process one series inside an enabled library.

## 1.0.1.0

- Attach external tracks to linked versions that Jellyfin skips during an ordinary scan. Each version is matched and probed using its own video path.
- Verify the tracks of both linked episode versions through Jellyfin's playback API.

## 1.0.0.0

- First catalog release for Jellyfin 12.4. Install Sidecar Bridge from the plugin catalog and restart Jellyfin.
- Attach nested audio and subtitles during the normal library scan. Includes library selection, preview, diagnostics and ownership tracking for hardlinks.
- GitHub Actions verifies the installation package on Windows x64, Linux x64 and Linux ARM64 before publication.
