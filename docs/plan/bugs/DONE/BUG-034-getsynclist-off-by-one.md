# BUG-034: GetSyncList off-by-one — sends MaxSyncItems+1 items
## Status: Fixed
## Severity: Medium
## Version: v2.5.0
## Found In: Collections/DefinitionIdHashDictionary.cs:18, Collections/TargetBlockDataHashList.cs:20, Collections/TargetEntityDataHashList.cs:20

## Description

All three `GetSyncList()` implementations use `if (idx > SyncBlockState.MaxSyncItems) break;` which allows MaxSyncItems+1 items to be added before breaking. In contrast, the corresponding `RebuildHash()` methods use `if (idx >= SyncBlockState.MaxSyncItems) break;` which processes exactly MaxSyncItems items.

This means:
- `GetSyncList()` serializes up to MaxSyncItems+1 items
- `RebuildHash()` hashes only MaxSyncItems items

The hash and the sync list cover different item counts, which could cause unnecessary state transmissions (hash doesn't match what was sent).

## Root Cause

Inconsistent comparison operator: `>` in GetSyncList vs `>=` in RebuildHash.

## Fix

Change GetSyncList to use `>=` to match RebuildHash:

```csharp
if (idx >= SyncBlockState.MaxSyncItems) break;
```

Apply to all three files: DefinitionIdHashDictionary.cs, TargetBlockDataHashList.cs, TargetEntityDataHashList.cs.
