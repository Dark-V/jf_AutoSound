# Installation and updates

Sidecar Bridge targets Jellyfin 12.4.0 and .NET 10. Its catalog ABI is `12.4.0.0`. Jellyfin hides entries whose ABI is higher than its server version. The ABI field sets a minimum. Jellyfin 12.4.0 is the version verified by this project. [Jellyfin version selection](https://github.com/jellyfin/jellyfin/blob/v12.4/Emby.Server.Implementations/Updates/InstallationManager.cs#L104)

## Install from the catalog

1. Open **Dashboard → Plugins → Repositories**.
2. Add a repository named **Sidecar Bridge** with this URL.

   ```text
   https://raw.githubusercontent.com/Dark-V/jf_AutoSound/refs/heads/main/manifest.json
   ```

3. Open **Catalog**, select **Sidecar Bridge** and install the latest compatible version.
4. Restart Jellyfin and open **Dashboard → Plugins → Sidecar Bridge**. Select libraries and save.

The installer downloads the ZIP and verifies the catalog's MD5 before extracting it. Releases include the ZIP and a SHA-256 checksum. [GitHub Releases](https://github.com/Dark-V/jf_AutoSound/releases)

## Update

Keep the repository enabled. Jellyfin checks for a newer compatible release in the **Update Plugins** scheduled task. Run that task from the dashboard to check immediately. Restart Jellyfin after it downloads an update.

If you installed the development build `0.1.0.0` by hand, select the current version in the catalog and install it once. Automatic updates were disabled in that build. Sidecar Bridge keeps its GUID and uses the same configuration file and ownership registry across versions.

## Filesystem permissions

On Linux, Docker and Windows, the Jellyfin service account needs read access to the sources, permission to create directory entries next to the videos, and write access to its own data path. Source and destination must share a filesystem that supports hardlinks. On Windows this means the same volume.

In Docker, place both sides of the link inside a common media bind mount. For example, mount `/srv/media` at `/media` and keep the video's sidecars below the same underlying filesystem. Use Jellyfin's container paths in Preview. Separate bind mounts, datasets and union filesystem branches can prevent hardlink creation.

## First use and removal

Choose Dashboard → Plugins → Sidecar Bridge, enable libraries, save and preview. The plugin starts with no enabled libraries. Newly written files are deferred by the default 30-second age guard; the next ordinary scheduled/manual scan retries them.

To stop new links, disable libraries or uninstall the plugin while Jellyfin is stopped. Existing hardlinks remain and Jellyfin can continue reading them. Automatic missing-source cleanup is off by default. Deleting an original path does not free the media's bytes while another hardlink still exists.

Ownership and retry state are stored under Jellyfin's data path in `sidecar-bridge/links.json`. Back up this file with server configuration. Losing it makes old links unowned; the plugin will not adopt or delete them. A corrupt registry fails closed and needs administrator review.
