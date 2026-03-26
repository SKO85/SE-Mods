# BUG-059: isDedicated detection wrong in HelpCommand — DS note shown to all clients
## Status: Fixed
## Severity: Low
## Version: v2.5.0
## Found In: Review / ChatHandler.cs, HelpCommand.cs
## Description
`isDedicated` was computed as `!MyAPIGateway.Session.IsServer`, which is true for ALL non-host clients — including those on listen servers. This caused the "Note: You are on a dedicated server" message to appear for listen-server clients as well, which is incorrect. There is no reliable client-side API to distinguish DS from listen-server connections.
## Root Cause
`IsServer` is false for all clients regardless of server type. `IsDedicated` is only true on the DS process itself, not on clients connected to a DS.
## Fix
Removed the `isDedicated` parameter from `HelpCommand.Execute()` and dropped the DS-specific note entirely. `ChatHandler.cs:68-70`, `HelpCommand.cs:7`.
