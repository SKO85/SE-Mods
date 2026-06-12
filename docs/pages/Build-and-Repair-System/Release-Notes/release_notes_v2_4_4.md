---
layout: default
title: "Release Notes – v2.4.4"
parent: Release Notes
grand_parent: Build and Repair System
nav_order: 7
---

# Release Notes – v2.4.4

- Release date: 1 March 2026
- Notes: Bug fix release — less server load, block settings that actually stick, and a tidier info panel.

---

## Bug Fixes

- **Less server load:** Turned-off BaR blocks no longer scan for targets and containers at all, and sorting nearby objects is cheaper — noticeably less lag when many systems are active in the same area.
- **Settings that stick:** Block settings no longer occasionally reset to defaults after a server restart or a relog.
- **Cleaner info panel:** Removed the stray `(none)` and `(NULL)` entries that showed up during welding, grinding and item collection.
- **Welding list:** Fixed inconsistent sorting and unreliable display of missing items in the welding target list.
- **Sounds in the right place:** Welding, grinding and waiting sounds now play at the BaR block instead of following you around.
- **Less status flicker:** Reduced unnecessary switching between Welding and Idle states.

  > **Known limitation:** When welding a projected grid where the first block is placed with only a single component, the block briefly shows Idle for 1–2 seconds while waiting to collect the remaining components, then resumes. Further improvement is planned.
