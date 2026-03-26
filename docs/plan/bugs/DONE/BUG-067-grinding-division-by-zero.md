# BUG-067: Division by zero in ServerDoGrind on modded blocks
## Status: Fixed
## Severity: High
## Version: v2.5.0
## Found In: Code review round 5 — NanobotSystem.Grinding.cs:149, 133
## Description
`ServerDoGrind` divides by `disassembleRatio` (line 153) and `target.MaxIntegrity` (line 133) without guarding against zero. Modded blocks or corrupted block definitions can have zero values, causing `DivideByZeroException` which crashes the game loop for all BaRs.
## Root Cause
No defensive zero-check before division.
## Fix
Added `if (disassembleRatio <= 0f) return false;` after computing `disassembleRatio`, and `if (target.MaxIntegrity <= 0f) return false;` after the definition cast:
- `NanobotSystem.Grinding.cs:150,152`
