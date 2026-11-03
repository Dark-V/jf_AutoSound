# Disposable real-server test

`one_scan.py` requires Python 3, a built plugin, a published Jellyfin **12.4** server and FFmpeg. It never connects to an existing server and refuses a nonempty `--work` directory.

Follow [verification.md](../../docs/verification.md) to build the server. Optional `--dotnet` and `--ffmpeg` arguments select development executables.

Use `--plugin-zip artifacts/SidecarBridge_0.1.0.0.zip` to test the actual installer manifest and both assemblies. `--plugin` instead accepts a build directory for prototype development; CI tests the ZIP.

The test generates its own one-second media. It disables realtime monitoring and interval refresh in the test library, then issues one ordinary scheduled library scan per scenario. Assertions inspect API `MediaStreams`, hardlink identities and original hashes, rather than just checking that filenames exist.

Server state, generated fixture files, logs and `proof.json` stay under the work directory. The random admin credential is never printed or committed. FFmpeg and Python are test/build tools only; neither is launched by the plugin.
