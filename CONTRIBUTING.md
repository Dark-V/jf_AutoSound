# Contributing

Pin the Jellyfin tag and SDK, restore using the committed NuGet lockfiles and run the core and real-server tests before changing scan integration. A filename-only test is insufficient for provider ordering/cache changes: verify persisted `MediaStreams` after one ordinary scan of an unchanged video with a newly added track.

Use generated or owned test media. Do not attach copyrighted releases, real library paths, credentials or private server logs. Keep conservative matching and conflict refusal. Changes must not add copy/remux fallbacks, external runtime scripts or follow-up refresh loops.

The CI workflow builds installation artifacts without publishing a GitHub Release. Release publication and deployments need separate authorization from the repository owner.
