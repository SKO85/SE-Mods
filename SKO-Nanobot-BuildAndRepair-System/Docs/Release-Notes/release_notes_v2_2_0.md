# Release Notes – v2.2.0

- Release date: 1 October 2025
- Notes: N/A

---

## New Features

### Multigrid Projection Support
Fixed welding of projected grids when using the multigrid-projection plugin.

### `DeleteBotsWhenDead` Config Option (server setting)
A new `DeleteBotsWhenDead` option is available in `ModSettings.xml`. When set to `true` (default), bots such as Wolves and Spiders are deleted after their inventory has been emptied, matching the behaviour of the original mod. Set to `false` to keep them in the world.

### Block Scan Cap Increased
The maximum number of welding and grinding targets scanned per Build and Repair block has been increased to **256**. Once the cap is reached, scanning for new targets stops, which improves performance and reduces server lag.

---

## Bug Fixes

- Updated the `/nanobars -help` command to show only essential information and links to the GitHub help page, issue tracker, and Discord.
- Removed the `-cpsf` command. Use `-cwsf` to generate a local `ModSettings.xml` file. To use custom settings on a server, copy the file to the server, edit it, and restart.
- Fixed incorrect sorting for **Farthest/Nearest** target modes and priority sorting when scanning for target blocks by type and distance.
- Fixed resetting of deformations on damaged blocks when detected.
- Added a warning message in the block's info panel when the mod has not been fully downloaded on a server. This can happen on Dedicated Servers when Steam only partially downloads a mod update, causing unexpected behaviour.
- Fixed the typo _further_ → _farther_ in several UI texts.

---

## Performance

- Added caching for several heavy API calls to reduce server load. Safe-Zone detection for grids entering or leaving a zone may be delayed by up to 10–15 seconds as a result.
- Added a cache for block lookups on grids to reduce unnecessary repeated calls.
