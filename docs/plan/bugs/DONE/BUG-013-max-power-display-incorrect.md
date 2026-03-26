# BUG-013: Max power shown in custom info panel is incorrect
## Status: Fixed
## Severity: Low
## Version: v2.5.0
## Found In: NanobotSystem.Init.cs / PowerHelper.cs
## Description
The custom info panel shows 350 kW as max power, but the actual maximum is 200 kW. Power states are mutually exclusive (never combined):
- Disabled: 0 kW
- Idle: 50 kW (standby)
- Transporting/Collecting: 100 kW
- Welding: 200 kW
- Grinding: 200 kW

When welding/grinding and transporting simultaneously, the power is only the welding/grinding power (200 kW), not additive.
## Root Cause
Two issues:

1. **Max power registration** (`NanobotSystem.Init.cs:61`): Formula `MAX(Welding, Grinding) + Transport + Standby` incorrectly adds all power levels. Should be `MAX(Welding, Grinding, Transport)` since the states are not additive.

2. **Dynamic power calculation** (`PowerHelper.cs:52-56`): Transport power is always added on top of welding/grinding power. Should only apply when NOT welding and NOT grinding.
## Fix
- `NanobotSystem.Init.cs:61`: Change max power to `MAX(MAX(Welding, Grinding), Transport)`.
- `PowerHelper.cs:52-56`: Only add transport power when not welding and not grinding.
