# BUG-022: BlockPriorityHandling GetItemKey cache ignores `real` parameter
## Severity: Medium
## Version: v2.5.0
## Status: Fixed

## File
`Handlers/BlockPriorityHandling.cs:68,98`

## Description
`GetItemKey` behaves differently when `real=false`: disabled functional blocks are classified as `ArmorBlock`. But the cache key is only `block.EntityId`, not including `real`. If `GetItemKey(block, false)` is called first (caching `ArmorBlock` for a disabled block), then `GetItemKey(block, true)` returns the cached `ArmorBlock` instead of the true block class.

The cache TTL is 5 minutes, so this stale result persists and can cause blocks to be mis-prioritized.

## Fix
Either include `real` in the cache key, or use separate caches for real/non-real lookups.
