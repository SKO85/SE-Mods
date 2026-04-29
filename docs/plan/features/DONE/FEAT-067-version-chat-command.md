# FEAT-067: `/nanobars version` chat command

## Status: Done
## Priority: Medium
## Version: v2.5.2

## Summary

New chat command `/nanobars version` that prints the mod version of the client and — when running on a dedicated server — also the server version, so players can diagnose version mismatches between their client and the server they are connected to.

## Motivation

Users on dedicated servers occasionally run an outdated local copy of the mod (e.g. stale Workshop cache) while the server is already on a newer version. The symptoms are subtle and hard to diagnose: sync glitches, unexpected terminal values, or features the server expects that the client doesn't support. There was no player-facing way to compare the two versions from inside the game — only the mission screen version line, which reflects only the local install.

## Design

- Client-side handler in `ChatHandler.OnMessageEntered` always prints `BaR Mod Client: v{version}` immediately from the local `Constants.ModVersion`.
- If the session is a dedicated-server client (`!MyAPIGateway.Session.IsServer`), the command is also forwarded to the server via the existing `NetworkMessagingHandler.MsgModCommandSend` path. The server-side `VersionCommand.Execute` returns `BaR Mod Server: v{version}`, which reaches the client via the existing `MsgModCommandResponse` round-trip and appears as a second chat line.
- On a local game session (host), `IsServer` is true and there is no separate server, so only the client line is printed.
- The admin gate in `NetworkMessagingHandler.ServerMsgModCommandReceived` is bypassed for `version` so any player — not just admins — can check their session for mismatches.
- Added to the `Player Commands` section of `/nanobars -help` (new section) so the command is discoverable by non-admins.

### Expected output

Local game session:
```
BaR Mod Client: v2.5.2
```

Dedicated server session:
```
BaR Mod Client: v2.5.2
BaR Mod Server: v2.5.2
```

## Files Affected

- `Chat/Commands/VersionCommand.cs` — new file, returns the server version line.
- `Chat/ChatHandler.cs` — new `version` handler in `OnMessageEntered`; new `case "version"` in `ExecuteServerCommand`.
- `Handlers/NetworkMessagingHandler.cs` — whitelist `version` in `ServerMsgModCommandReceived` so it bypasses the admin check.
- `Chat/Commands/HelpCommand.cs` — new `Player Commands` section documenting `version`.
- `SKO-Nanobot-BuildAndRepair-System.csproj` — compile entry for the new file.

## Testing

1. **Local game:** run `/nanobars version` — expect one line `BaR Mod Client: v2.5.2`.
2. **Dedicated server, admin client:** run `/nanobars version` — expect two lines: client line (immediate) and server line (on response).
3. **Dedicated server, non-admin player:** run `/nanobars version` — expect the same two lines, not `Command requires admin permissions`.
4. Intentional mismatch: run a DS on v2.5.1 and a client on v2.5.2, expect the two lines to show the differing versions.
