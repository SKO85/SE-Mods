# BUG-064: Debug HUD scan age inflated by disabled/unscanned BaRs
## Status: Fixed
## Severity: Low
## Version: v2.5.0
## Found In: In-game testing / HudHandler.cs
## Description
The debug HUD "Scan Age" metric showed values above 590 seconds. This happened because `_LastTargetsUpdate` defaults to `TimeSpan.Zero` for BaRs that are turned off or haven't completed their first scan. The scan age calculation `ElapsedPlayTime - Zero` equals the entire session uptime, inflating the metric for any disabled BaR.
## Root Cause
`HudHandler.BuildStats` iterates all BaR systems without filtering out those with a zero timestamp. `TimeSpan.Zero` is used as a "force rescan" sentinel, not a valid last-scan time.
## Fix
Skip BaRs where `LastTargetsUpdate == TimeSpan.Zero` when computing the oldest scan age. `Handlers/HudHandler.cs:548`.
