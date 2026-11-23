# SCN-BUDGET-010: Network sync load stays bounded on a dedicated server

## Area: BUDGET
## Tier: 2
## Preconditions
- Dedicated server (Torch) with the -Testing mod and an admin client connected (the client receives the traffic).
## Steps
1. `/nanobars test run @sync` (S43, S70, S106, S111, S116-S123), optionally with `detail`.
## Expected outcome
- Settings broadcasts at most one per BaR per second, even when scripts / timers change settings faster (S116).
- State messages at most one per BaR per second; the largest (full target list) <= 64 KB (S117).
- Every scenario records `net.*`: messages / KB sent and received, wire KB/s incl. fan-out, largest message and its
  type, state sends per BaR per second, average / largest state message.
## Perf envelope
State sends ~0.37 per BaR per s while working (Torch, S43 / S70, 261123.42); send cost ~0.2 ms avg.
## Coverage
unit:NetStatsTests
ingame:S116
ingame:S117
ingame:S118
ingame:S119
ingame:S120
ingame:S121
ingame:S122
ingame:S123
## Last verified
never
## Related
FEAT-261123.7, BUG-261123.4 (closed: sync rate measured)
