# Release Notes – v2.4.4

- Release date: 1 March 2026
- Notes: N/A

---

## Bug Fixes

- **Performance:** Scanning for target blocks and source containers is now skipped entirely when the Build and Repair block is disabled, reducing unnecessary server load.
- **Performance:** Improved how nearby entities are sorted, which reduces server lag when many systems are active in the same area.
- **Sync:** Block settings now correctly persist after a server restart or when a player relogs, instead of occasionally resetting to defaults.
- **Control Panel:** Removed spurious `(none)` and `(NULL)` entries that appeared in the info panel during welding, grinding, and item collection.
- **Welding List:** Fixed inconsistent sorting and unreliable display of missing items in the welding target list.
- **Sound Effects:** Welding, grinding, and waiting sounds now play at the Build and Repair block's location rather than at the player's position.
- **Welding/Idle State:** Reduced unnecessary switching between Welding and Idle states.

  > **Known limitation:** When welding a projected grid where the first block is placed with only a single component, the block briefly enters an Idle state for 1–2 seconds while waiting to collect the remaining components before resuming. Further improvement is planned.
