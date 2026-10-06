# SCN-CHAT-002: Dedicated server: admin command forwarding and response routing
## Area: CHAT
## Tier: 4
## Preconditions
- Torch / vanilla DS with the mod; one admin client and one non-admin client connected.
## Steps
1. Admin client: `/nanobars config list`, `/nanobars systems count`, `/nanobars profile start 30`.
2. Non-admin client: `/nanobars config list`.
3. Admin client: `/nanobars profile stop`; wait for the auto-stop of a second `profile start 10`.
4. Admin client: `/nanobars debug show` (local) and `/nanobars debug on` (server).
## Expected outcome
- Admin commands are forwarded via `NetworkMessagingHandler.MsgModCommandSend`, executed server-side,
  and the reply arrives as `MsgModCommandResponse` — long outputs open the mission screen.
- Non-admin gets "Command requires admin permissions" locally; nothing reaches the server.
- Profiler auto-stop message is delivered to the initiating SteamId only (`TickAutoStop` path).
- `debug show/hide/left/right/cluster-area/targets` stay client-local; `debug on/off` is rewritten to
  `config set DebugMode …` and forwarded.
## Perf envelope
n/a
## Coverage
manual
## Last verified
never
## Related
FEAT-054 (admin welcome), BUG-059 (isDedicated detection), BUG-260610.41 (send failures logged)
