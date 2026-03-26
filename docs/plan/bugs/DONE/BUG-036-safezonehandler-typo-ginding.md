# BUG-036: SafeZoneHandler typo "ProtectedFromGindingCache"
## Status: Fixed
## Severity: Low
## Version: v2.5.0
## Found In: Handlers/SafeZoneHandler.cs:36

## Description

The variable `ProtectedFromGindingCache` is missing the 'r' — should be `ProtectedFromGrindingCache`. The typo propagates to every reference of this variable throughout SafeZoneHandler.cs.

## Root Cause

Typo in original field declaration.

## Fix

Rename `ProtectedFromGindingCache` to `ProtectedFromGrindingCache` across all references in SafeZoneHandler.cs.
