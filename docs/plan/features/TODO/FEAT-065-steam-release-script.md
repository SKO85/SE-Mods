# FEAT-065: Steam Release Script
## Status: Done
## Priority: Medium
## Version: tooling
## Summary
NodeJS script using SteamCMD to push mod versions to Steam Workshop.
## Motivation
Manual Steam Workshop uploads are error-prone. A script with config-driven enabled flags prevents accidental pushes of non-ready versions.
## Design
- NodeJS CLI app in `_Scripts/SteamRelease/`
- JSON config file defining mod versions with Workshop IDs, content paths, and `enabled` flag
- Only the Testing and Script versions are enabled by default
- Uses SteamCMD to upload via generated VDF files (auto-downloads SteamCMD on first run)
- Default mode is dry run; `--confirm` flag required for live upload
- `--login` for one-time Steam authentication (session cached)
- `--name <name>` to target a specific mod, `--all` for all enabled
- `--note`/`--notes` to attach a change note
- Auto-builds the mod before upload (skipped for Script mod)
- `.env` file for STEAM_USERNAME (gitignored)
## Files Affected
- `_Scripts/SteamRelease/` (new directory)
- `_Scripts/BuildAndRepairSystem/build_post.cmd` (fix error handling)
- `SKO-Nanobot-BuildAndRepair-System.csproj` (fix PostBuildEvent for CLI builds)
- `SKO-Nanobot-BuildAndRepair-System/metadata.mod` (version bump to 2.5.0)
- `SKO-Nanobot-BuildAndRepair-System-Script/metadata.mod` (version bump to 1.12)
## Testing
- `node publish.js --name Testing` to verify dry run with build + VDF generation
- `node publish.js --name Testing --confirm` to verify live upload
- Successfully tested upload to Workshop item 3461889745
