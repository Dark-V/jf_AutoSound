# Installation: Linux, Docker and Windows

Target **Jellyfin 12.4.0**, ABI `12.4.0.0`, .NET 10. Other versions need their own source review and integration run. Build with SDK `10.0.403` (or a later patch in that feature band).

## Obtain the ZIP

Download `SidecarBridge_0.1.0.0.zip` from a successful GitHub Actions artifact, or build from source using the README commands. Verify the adjacent SHA-256 file. A public GitHub Release has not been published. The root `manifest.json` is a valid empty catalog; it intentionally advertises no nonexistent download URL.

Packaging a future authorized publication:

```sh
python tools/package.py --source-url https://YOUR-ACTUAL-HOST/ACTUAL-PACKAGE.zip
```

That command only writes local files. Use it with the actual ZIP URL after publication is separately authorized. The generated catalog uses Jellyfin's MD5 package checksum field; SHA-256 is also supplied separately. This example URL is a placeholder, not a published download.

## Linux

1. Stop the Jellyfin service.
2. Find its configured plugins directory. A standard package commonly uses `/var/lib/jellyfin/plugins`; custom data paths change this location.
3. Create `SidecarBridge_0.1.0.0` beneath that directory. Extract the ZIP there so the two DLLs, `meta.json` and `icon.png` are directly inside it.
4. Ensure the Jellyfin service account can read plugin files and sidecar sources, create directory entries beside videos, and write its own plugin data directory. Use the storage owner's normal group/ACL policy.
5. Start Jellyfin and enable libraries in the plugin settings.

The plugin never asks for root, alters permissions, invokes a shell or runs FFmpeg. Jellyfin still uses its own normal FFmpeg/ffprobe installation for media analysis.

## Docker

Install the same plugin under the container's configured plugins directory (commonly `/config/plugins`). Keep it on persistent configuration storage.

Sources and destinations must be hardlink-compatible **inside the container's mount namespace**. Prefer one common media bind mount, for example:

```yaml
volumes:
  - /srv/jellyfin/config:/config
  - /srv/media:/media
```

This is only a mount illustration, not a complete deployment stack. Video, audio and subtitles may occupy different subfolders under `/media`, but their underlying filesystem still needs to be the same. Separate bind mounts, datasets, mergerfs branches and Docker Desktop shared drives can reject a hardlink even when path names look adjacent. Read-only mounts cannot accept new links. The plugin reports the native error and does not copy data instead.

Use paths **as Jellyfin sees them**, such as `/media/Film/Video/Film.mkv`, in the preview. No container sidecar is required. Deployment on a user's Docker server is not included in the test evidence.

## Windows

1. Stop Jellyfin.
2. Locate the plugins folder under the server's actual data directory. A service and an interactive installation can use different data directories.
3. Extract the package into a new `SidecarBridge_0.1.0.0` subfolder.
4. Start Jellyfin, enable the desired libraries and preview one video.

Local NTFS hardlinks do not require symlink privileges or Developer Mode. The Jellyfin account needs read access to sources and permission to create files in the video directory. Both paths must be on the same volume. Junctions and symlinks in the media path are refused.

## First use and removal

Choose Dashboard → Plugins → Sidecar Bridge, enable libraries, save and preview. The plugin starts with no enabled libraries. Newly written files are deferred by the default 30-second age guard; the next ordinary scheduled/manual scan retries them.

To stop new links, disable libraries or uninstall the plugin while Jellyfin is stopped. Existing hardlinks remain and Jellyfin can continue reading them. Automatic missing-source cleanup is off by default. Deleting an original path does not free the media's bytes while another hardlink still exists.

Ownership and retry state are stored under Jellyfin's data path in `sidecar-bridge/links.json`. Back up this file with server configuration. Losing it makes old links unowned; the plugin will not adopt or delete them. A corrupt registry fails closed and needs administrator review.
