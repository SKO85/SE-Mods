using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Extensions;
using SKONanobotBuildAndRepairSystem.Handlers;
using SKONanobotBuildAndRepairSystem.Helpers;
using SKONanobotBuildAndRepairSystem.Models;
using SKONanobotBuildAndRepairSystem.Profiling;
using SKONanobotBuildAndRepairSystem.Utils;
using System;
using System.Diagnostics;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;
using static SKONanobotBuildAndRepairSystem.Utils.UtilsInventory;

namespace SKONanobotBuildAndRepairSystem
{
    public partial class NanobotSystem
    {
        private void ServerTryWelding(out bool welding, out bool needWelding, out bool transporting, out IMySlimBlock currentWeldingBlock)
        {
            var profilerTs = MethodProfiler.Start();
            // BUG-120: clear the broken-block caches if the BaR's owner has changed since
            // last check — entitlement-keyed decisions don't carry across owners.
            EnsureBrokenCacheOwnerScope();
            welding = false;
            needWelding = false;
            transporting = false;
            currentWeldingBlock = null;
            var hadLockOn = State.CurrentWeldingBlock != null;
            var skippedByLockOn = 0;
            var checkedByWeldable = 0;
            var skippedByIgnore = 0;
            var skippedByGridLimit = 0;
            var skippedByAssign = 0;
            var skippedByFailCooldown = 0;
            var componentFailures = 0;
            var totalComponentChecks = 0;
            var lookingForNextChecked = 0;
            var lockOnFound = false;
            var weldSkipped = false;
            // BUG-122: measure lock acquisition + in-lock time. Profile session 20260429181044
            // showed a 27 ms ServerTryWelding spike where Weldable (already profiled, max 0.344 ms)
            // and ServerEmptyTransportInventory (max 0.136 ms) account for ~µs, ServerFindMissingComponents
            // and ServerDoWeld for ~12 ms — leaving ~15 ms unaccounted. Background scan publishes
            // into State.PossibleWeldTargets under the same lock (Scanning.cs ApplyClusterResultToSelf
            // ~1472), so contention is the leading suspect. Two non-summing measurements: how long we
            // wait to acquire, then how long we spend inside.
            var tsLockAcquire = 0L;
            var tsInLock = 0L;
            // BUG-131: in-lock sub-timers. Profile session 20260430160958 showed 14-20 ms
            // unaccounted inside the welding lock vs Weldable / ServerFindMissingComponents /
            // ServerDoWeld already profiled. Three suspects on the per-iteration hot path:
            // (1) BlockSystemAssigningHandler TtlCache ops (IsAssignedToOtherSystem,
            // AssignToSystem, ReleaseFromSystem) which call MyAPIGateway.Session.ElapsedPlayTime
            // on every read and allocate a new CacheItem on every write,
            // (2) IsGridOverSystemLimit (153 calls in one observed spike),
            // (3) BlockWeldPriority.GetPriority (called per non-ignored iteration).
            // Sub-timers accumulate across the loop; per-iteration measurement is too noisy.
            var tsAssignOps = 0L;
            var tsGridLimit = 0L;
            var tsPriority = 0L;
            long _opTs;
            // BUG-135: weld work (ServerFindMissingComponents → PullFromSourcesOnePass walks ~80
            // source inventories; ServerDoWeld; ServerEmptyTransportInventory) used to run inside
            // lock(State.PossibleWeldTargets). Profile session welding1 showed that single block
            // holding the lock for 20-23 ms while the inner inventory pull ran, blocking async
            // scan publish, IsTargetListSaturated and RebuildHash on the same lock. Capture the
            // chosen target under the lock and run the expensive work outside it. Lock duration
            // drops to iteration cost (< 1 ms) even when the inventory pull spikes.
            TargetBlockData chosenTarget = null;
            bool chosenIsLockOnBlock = false;
            try
            {

            var hasRequiredPower = PowerHelper.HasRequiredElectricPower(this);
            if (!hasRequiredPower) return; //No power -> nothing to do

            var tsBeforeLock = profilerTs != 0L ? Stopwatch.GetTimestamp() : 0L;
            lock (State.PossibleWeldTargets)
            {
                var tsAfterAcquire = profilerTs != 0L ? Stopwatch.GetTimestamp() : 0L;
                if (tsBeforeLock != 0L) tsLockAcquire = tsAfterAcquire - tsBeforeLock;
                // OPT: When the previous iteration found nothing to weld (all targets grid-limited
                // or assigned), skip the full iteration until the target list changes (new scan).
                // Hash read is under lock so it can't race with background scan updates.
                if (_weldLoopExhausted && State.PossibleWeldTargets.CurrentHash == _weldExhaustedAtHash)
                {
                    weldSkipped = true;
                    return;
                }
                _weldLoopExhausted = false;
                // Set to true once the locked-on block completes this tick so the loop
                // can find the next target immediately, without actually welding it
                // (only one block is welded per tick). The next target is returned as
                // currentWeldingBlock so it starts welding on the very next update cycle.
                var lookingForNext = false;
                long lastRejectedGridId = 0;
                // When lock-on is lost (block vanished from list after scan rebuild),
                // we re-iterate without lock-on so the BaR doesn't waste the tick.
                var lockOnRetry = false;
                // BUG-131: cache the lock-on block's identity once per call. The original
                // IsSameBlock did 4 IMySlimBlock engine accessors per iteration (a.CubeGrid,
                // b.CubeGrid, a.CubeGrid.EntityId, b.CubeGrid.EntityId). The lock-on side
                // (a) is constant across the whole loop, so caching its grid id + position
                // here saves 2 accessors per iteration on the skip-until-lock-on path —
                // which dominates the spike samples (skipLock=70-200). State.CurrentWeldingBlock
                // is reassigned at line 113 on lock-on found but always to a block with the
                // SAME grid id + position, so the cache stays valid. Clears (line 199, 244,
                // 258, 309) set it to null, which the runtime check below short-circuits.
                long lockOnGridId = 0;
                Vector3I lockOnPos = default(Vector3I);
                if (State.CurrentWeldingBlock != null && State.CurrentWeldingBlock.CubeGrid != null)
                {
                    lockOnGridId = State.CurrentWeldingBlock.CubeGrid.EntityId;
                    lockOnPos = State.CurrentWeldingBlock.Position;
                }
                LockOnRetry:
                foreach (var targetData in State.PossibleWeldTargets)
                {
                    // BUG-131: inlined IsSameBlock against cached lock-on identity to halve
                    // the engine-accessor cost on the per-iteration skip path.
                    var isLockOnBlock = State.CurrentWeldingBlock != null
                        && targetData.Block != null
                        && targetData.Block.CubeGrid != null
                        && targetData.Block.CubeGrid.EntityId == lockOnGridId
                        && targetData.Block.Position == lockOnPos;

                    if (!lookingForNext && State.CurrentWeldingBlock != null && !isLockOnBlock)
                    {
                        skippedByLockOn++;
                        continue;
                    }

                    // Cap lookingForNext iteration to avoid 8ms+ spikes scanning 100+ blocks.
                    // If no eligible block is found within the cap, the next stagger tick
                    // will find one through normal iteration.
                    if (lookingForNext)
                    {
                        // Only count non-ignored blocks against the cap.
                        // Ignored blocks are O(1) to skip and don't cause perf issues.
                        if (!targetData.Ignore)
                            lookingForNextChecked++;
                        if (lookingForNextChecked >= 20)
                            break;
                    }

                    if (!lookingForNext && isLockOnBlock)
                    {
                        lockOnFound = true;
                        // Update reference so downstream code uses the current list entry.
                        State.CurrentWeldingBlock = targetData.Block;
                    }

                    if (((Settings.Flags & SyncBlockSettings.Settings.ScriptControlled) != 0) && targetData.Block != Settings.CurrentPickedWeldingBlock) continue;

                    if (Mod.Settings.AssignToSystemEnabled
                        && Settings.CurrentPickedWeldingBlock == null)
                    {
                        _opTs = profilerTs != 0L ? Stopwatch.GetTimestamp() : 0L;
                        var assignedToOther = targetData.Block.IsAssignedToOtherSystem(_Welder.EntityId);
                        if (_opTs != 0L) tsAssignOps += Stopwatch.GetTimestamp() - _opTs;
                        if (assignedToOther)
                        {
                            skippedByAssign++;
                            continue;
                        }
                    }

                    // FEAT-AI: skip blocks that recently failed for any BaR. Lock-on and
                    // lookingForNext are exempt — lock-on is the BaR's committed target
                    // (we want it to resume the moment components arrive), and the
                    // looking-for-next path is already capped at 20 iterations and only
                    // runs once per tick after the previous block finished.
                    if (!isLockOnBlock && !lookingForNext
                        && BlockFailureCooldownHandler.IsOnCooldown(targetData.Block))
                    {
                        skippedByFailCooldown++;
                        needWelding = true;
                        continue;
                    }

                    var isIgnored = targetData.Ignore;

                    // BUG-116 (CLN-2): the BUG-116 starved-priority pre-filter relied on a
                    // bitset (`starvedPriorityBits`) that the in-loop fail handler used to
                    // populate. After BUG-135 moved the welding work outside the lock, the
                    // bitset was never written to within a single call, so this gate (and its
                    // post-Weldable counterpart) ran on a permanently-zero mask and was
                    // unreachable. Removed both gates and the bookkeeping fields.
                    var isWeldable = !isIgnored && Weldable(targetData);
                    if (!isIgnored) checkedByWeldable++;
                    else skippedByIgnore++;

                    if (((Settings.Flags & SyncBlockSettings.Settings.ScriptControlled) != 0) || isWeldable)
                    {
                        if (targetData.Block != null && targetData.Block.FatBlock != null && targetData.Block.FatBlock.Closed)
                        {
                            continue;
                        }

                        if (!isLockOnBlock)
                        {
                            if (Settings.CurrentPickedWeldingBlock == null)
                            {
                                // BUG-164: use the effective (post-materialization) grid for the
                                // limit check on projected blocks — that's the grid the Inc lands on.
                                var gridId = GetEffectiveGridId(targetData.Block);
                                _opTs = profilerTs != 0L ? Stopwatch.GetTimestamp() : 0L;
                                var overLimit = IsGridOverSystemLimit(gridId, ref lastRejectedGridId);
                                if (_opTs != 0L) tsGridLimit += Stopwatch.GetTimestamp() - _opTs;
                                if (overLimit)
                                {
                                    skippedByGridLimit++;
                                    continue;
                                }
                            }

                            if (Mod.Settings.AssignToSystemEnabled && _Welder.IsWorking && _Welder.Enabled && Settings.CurrentPickedWeldingBlock == null)
                            {
                                _opTs = profilerTs != 0L ? Stopwatch.GetTimestamp() : 0L;
                                var assignOk = targetData.Block.AssignToSystem(_Welder.EntityId);
                                if (_opTs != 0L) tsAssignOps += Stopwatch.GetTimestamp() - _opTs;
                                if (!assignOk)
                                {
                                    skippedByAssign++;
                                    continue;
                                }
                            }
                        }
                        else
                        {
                            // Lock-on block: enforce grid limit (live counter is accurate).
                            // If over limit, release and find a target on another grid.
                            if (Settings.CurrentPickedWeldingBlock == null && targetData.Block.CubeGrid != null)
                            {
                                // BUG-164: same effective-grid resolution for projected blocks.
                                var gridId = GetEffectiveGridId(targetData.Block);
                                _opTs = profilerTs != 0L ? Stopwatch.GetTimestamp() : 0L;
                                var overLimit = IsGridOverSystemLimit(gridId, ref lastRejectedGridId);
                                if (_opTs != 0L) tsGridLimit += Stopwatch.GetTimestamp() - _opTs;
                                if (overLimit)
                                {
                                    ReleaseAssignmentIfEnabled(targetData.Block, profilerTs != 0L, ref tsAssignOps);
                                    State.CurrentWeldingBlock = null;
                                    lookingForNext = true;
                                    skippedByGridLimit++;
                                    continue;
                                }
                            }
                            // Refresh assignment for the lock-on block.
                            if (Mod.Settings.AssignToSystemEnabled && _Welder.IsWorking && _Welder.Enabled)
                            {
                                _opTs = profilerTs != 0L ? Stopwatch.GetTimestamp() : 0L;
                                targetData.Block.AssignToSystem(_Welder.EntityId);
                                if (_opTs != 0L) tsAssignOps += Stopwatch.GetTimestamp() - _opTs;
                            }
                        }

                        needWelding = true;

                        if (lookingForNext)
                        {
                            // The previous block just completed this tick. Lock on to this
                            // next eligible block so it starts welding next cycle immediately.
                            currentWeldingBlock = targetData.Block;
                            break;
                        }

                        // BUG-135: capture the target and exit the lock. Expensive welding work
                        // (component pull, ServerDoWeld, transport inventory empty) runs outside.
                        chosenTarget = targetData;
                        chosenIsLockOnBlock = isLockOnBlock;
                        break;
                    }
                    else
                    {
                        ReleaseAssignmentIfEnabled(targetData.Block, profilerTs != 0L, ref tsAssignOps);
                        if (targetData.Ignore)
                        {
                            State.PossibleWeldTargets.ChangeHash();
                        }
                        // Current tracked block is no longer weldable; clear the lock so the
                        // loop can find the next eligible block in this same tick.
                        if (isLockOnBlock)
                        {
                            State.CurrentWeldingBlock = null;
                        }
                    }
                }

                // Lock-on block vanished from the list (e.g., projected grid EntityId changed
                // after projector update). Clear lock-on and re-iterate so this tick isn't wasted.
                if (!lockOnRetry && State.CurrentWeldingBlock != null && !lockOnFound)
                {
                    ReleaseAssignmentIfEnabled(State.CurrentWeldingBlock, profilerTs != 0L, ref tsAssignOps);
                    State.CurrentWeldingBlock = null;
                    // Reset all in-loop counters so the BUG-122 lock-acquire / in-lock
                    // instrumentation reports first-pass-only values; otherwise a retry
                    // double-counts skip / check tallies for the same target list.
                    skippedByLockOn = 0;
                    skippedByGridLimit = 0;
                    skippedByIgnore = 0;
                    skippedByAssign = 0;
                    skippedByFailCooldown = 0;
                    checkedByWeldable = 0;
                    componentFailures = 0;
                    totalComponentChecks = 0;
                    lookingForNextChecked = 0;
                    lockOnRetry = true;
                    goto LockOnRetry;
                }

                // OPT: Mark exhausted when the full iteration found nothing claimable.
                // Hash write under lock so it stays consistent with background scan updates.
                if (!welding && !needWelding && totalComponentChecks == 0 && !hadLockOn)
                {
                    _weldLoopExhausted = true;
                    _weldExhaustedAtHash = State.PossibleWeldTargets.CurrentHash;
                }
                if (tsBeforeLock != 0L) tsInLock = Stopwatch.GetTimestamp() - tsAfterAcquire;
            }

            // BUG-135: deferred weld work. Runs OUTSIDE lock(State.PossibleWeldTargets) so the
            // 20+ ms PullFromSourcesOnePass spike no longer blocks the async scan publish, the
            // saturation check, the welding-loop iteration of other ticks, or RebuildHash.
            // The chosenTarget reference and its Block survive after we exit the lock — even if
            // the background scan publishes a new list, the IMySlimBlock referenced by chosenTarget
            // remains valid (the engine retains it; only the list-membership changes).
            if (chosenTarget != null)
            {
                // BUG-154: Global weld budget — cap ServerDoWeld calls per tick (count + time).
                // When the budget is exhausted, defer this BaR's weld work to the next tick.
                // Lock-on (CurrentWeldingBlock) is preserved so the same BaR resumes the
                // same block; assignment stays so other BaRs don't steal it. Same shape as
                // the grind budget (BUG-137).
                if (!Mod.TryClaimWeldSlot())
                {
                    // Treat as "needs welding but couldn't this tick" so the caller's
                    // outer state stays consistent (NeedWelding remains true downstream).
                    needWelding = true;
                }
                else
                {
                    var weldTs = Stopwatch.GetTimestamp();

                    if (!transporting)
                    {
                        transporting = ServerFindMissingComponents(chosenTarget);
                        totalComponentChecks++;
                    }

                    welding = ServerDoWeld(chosenTarget);

                    Mod.ReportWeldTime((Stopwatch.GetTimestamp() - weldTs) * 1000.0 / Stopwatch.Frequency);

                    ServerEmptyTransportInventory(false);

                if (chosenTarget.Ignore)
                {
                    // BUG-053: Only release assignment when the block was successfully
                    // processed (welding=true). Failed projected builds (safe zone blocked)
                    // keep the assignment so other BaRs in the same tick don't cascade
                    // through the same block. The TTL will release it naturally.
                    if (welding) ReleaseAssignmentIfEnabled(chosenTarget.Block, profilerTs != 0L, ref tsAssignOps);
                    State.PossibleWeldTargets.ChangeHash();
                    // BUG-163: when this BaR actually welded the block this tick (welding=true),
                    // surface its grid contribution to GridSystemCount so MaxSystemsPerTargetGrid
                    // gets enforced for one-shot welds. Without this, projected blocks finalized
                    // in a single tick set chosenTarget.Ignore=true → the original code nulled
                    // State.CurrentWeldingBlock without ever setting currentWeldingBlock, so the
                    // setter never fired Inc(grid). Result: 72+ BaRs all welding the same grid
                    // saw count=0 (because nobody was "locked on") and IsGridOverSystemLimit
                    // always passed. Grinding doesn't have this path, which is why the user
                    // observed grinding respecting the limit but welding ignoring it. Setting
                    // currentWeldingBlock keeps the grid contribution alive for one tick;
                    // next tick the welding loop sees Ignore=true and clears the lock-on
                    // through the existing else-branch (around line 334), Dec'ing the count.
                    if (welding)
                    {
                        currentWeldingBlock = chosenTarget.Block;
                    }
                    else
                    {
                        // Block ignored without welding work (e.g., safe zone blocked, broken proj).
                        // No grid contribution to surface; clear lock-on as before.
                        State.CurrentWeldingBlock = null;
                    }
                }
                else if (welding || transporting)
                {
                    currentWeldingBlock = chosenTarget.Block;
                }
                else
                {
                    // Block can't be welded right now (no components available).
                    if (chosenIsLockOnBlock)
                    {
                        State.CurrentWeldingBlock = null;
                    }

                    ReleaseAssignmentIfEnabled(chosenTarget.Block, profilerTs != 0L, ref tsAssignOps);

                    // FEAT-AI: parking the failed block in the global cooldown so other
                    // BaRs (and this BaR's next tick) skip it instead of re-claiming and
                    // re-failing. TTL is short (a few seconds); components arriving
                    // mid-cooldown only delay welding by that window.
                    BlockFailureCooldownHandler.MarkFailed(chosenTarget.Block);

                    componentFailures++;
                }
                } // end of TryClaimWeldSlot else-branch (BUG-154)
            }

            }
            finally
            {
                if (profilerTs != 0L)
                {
                    var _welding = welding;
                    var _needWelding = needWelding;
                    var _transporting = transporting;
                    var _targetCount = State.PossibleWeldTargets.CurrentCount;
                    var _hadLockOn = hadLockOn;
                    var _lockOnFound = lockOnFound;
                    var _skippedByLockOn = skippedByLockOn;
                    var _checkedByWeldable = checkedByWeldable;
                    var _skippedByIgnore = skippedByIgnore;
                    var _skippedByGridLimit = skippedByGridLimit;
                    var _skippedByAssign = skippedByAssign;
                    var _skippedByFailCooldown = skippedByFailCooldown;
                    var _componentFailures = componentFailures;
                    var _lockOnLost = hadLockOn && !lockOnFound;
                    var _totalComponentChecks = totalComponentChecks;
                    var _lookingForNextChecked = lookingForNextChecked;
                    var _weldSkipped = weldSkipped;
                    var _saturatedGridCount = _gridSaturation.Count;
                    var tsFreq = Stopwatch.Frequency;
                    var _lockAcquireMs = tsLockAcquire * 1000.0 / tsFreq;
                    var _inLockMs = tsInLock * 1000.0 / tsFreq;
                    var _assignOpsMs = tsAssignOps * 1000.0 / tsFreq;
                    var _gridLimitMs = tsGridLimit * 1000.0 / tsFreq;
                    var _priorityMs = tsPriority * 1000.0 / tsFreq;
                    MethodProfiler.StopAndLog("ServerTryWelding", profilerTs, () =>
                        string.Format("entityId={0};welding={1};needWelding={2};transporting={3};targets={4};currentBlock={5};hadLockOn={6};lockOnFound={7};lockOnLost={8};skipLock={9};weldChecked={10};skipIgnore={11};skipGrid={12};skipAssign={13};skipFailCooldown={14};componentFails={15};compChecks={16};nextCap={17};exhaustedSkip={18};saturatedGrids={19};lockAcquireMs={20:F3};inLockMs={21:F3};assignOpsMs={22:F3};gridLimitMs={23:F3};priorityMs={24:F3}",
                            _Welder.EntityId, _welding, _needWelding, _transporting, _targetCount,
                            State.CurrentWeldingBlock != null ? State.CurrentWeldingBlock.BlockDefinition.Id.SubtypeName : "none",
                            _hadLockOn, _lockOnFound, _lockOnLost,
                            _skippedByLockOn, _checkedByWeldable, _skippedByIgnore, _skippedByGridLimit, _skippedByAssign, _skippedByFailCooldown, _componentFailures,
                            _totalComponentChecks, _lookingForNextChecked, _weldSkipped, _saturatedGridCount,
                            _lockAcquireMs, _inLockMs, _assignOpsMs, _gridLimitMs, _priorityMs));
                }
            }
        }

        private bool Weldable(TargetBlockData targetData)
        {
            var profilerTs = MethodProfiler.Start();
            var target = targetData.Block;
            var isProjected = (targetData.Attributes & TargetBlockData.AttributeFlags.Projected) != 0;
            var result = false;
            try
            {
                if (isProjected)
                {
                    // BUG-115: skip projected blocks for which proj.Build() has previously thrown NRE.
                    // The per-TargetBlockData Ignore flag is wiped each scan refresh, so without this
                    // persistent check the BaR re-locks the same broken block tick-after-tick and the
                    // NRE keeps appearing in the mod log every scan cycle.
                    if (target != null && _BrokenProjBuildKeys.Contains(GetBrokenBlockKey(target)))
                    {
                        targetData.Ignore = true;
                        return false;
                    }

                    // Keep this at false, otherwise it will not work with Multigrid Projections.
                    if (target.CanBuild(false))
                    {
                        targetData.Ignore = false;
                        result = true;
                        return true;
                    }

                    targetData.Ignore = true;
                    return false;
                }

                var weld = (!IsWeldIntegrityReached(target) || target.NeedRepair(Settings.WeldOptions)) && !IsFriendlyDamage(target);

                targetData.Ignore = !weld;
                result = weld;
                return weld;
            }
            finally
            {
                if (profilerTs != 0L)
                {
                    var _result = result;
                    MethodProfiler.StopAndLog("Weldable", profilerTs, () =>
                        string.Format("entityId={0};block={1};projected={2};result={3}",
                            _Welder.EntityId,
                            target != null ? target.BlockDefinition.Id.SubtypeName : "null",
                            isProjected, _result));
                }
            }
        }

        // BUG-115: stable key for a projected block, matching the convention used by
        // BlockSystemAssigningHandler ("gridId:position"). Used by _BrokenProjBuildKeys
        // to persist proj.Build NRE failures across scan refreshes.
        private static string GetBrokenBlockKey(IMySlimBlock block)
        {
            if (block == null || block.CubeGrid == null) return null;
            return block.CubeGrid.EntityId.ToString() + ":" + block.Position.ToString();
        }

        // BUG-120: owner-scope guard for the broken-block caches. The persisted skip
        // decisions (NRE set + silent-fail counter) are valid only for the current
        // _Welder.OwnerId — when ownership changes (admin grant, terminal "Take Ownership",
        // faction transfer), the new owner may have different DLC entitlements, so
        // previously-broken blocks may now build successfully. Polling-based: one long
        // comparison per ServerTryWelding entry (codebase has no ownership-change events).
        private void EnsureBrokenCacheOwnerScope()
        {
            var currentOwner = _Welder != null ? _Welder.OwnerId : 0L;
            if (currentOwner != _BrokenCacheOwnerId)
            {
                _BrokenProjBuildKeys.Clear();
                _ProjBuildSilentFailCount.Clear();
                _BrokenCacheOwnerId = currentOwner;
            }
        }

        // BUG-115 diagnostic: cheap "is this IdentityId currently a connected player?" check.
        // Used only on the NRE warning path (cold) so the per-call list allocation is acceptable.
        // SE's GetPlayers signature accepts a predicate filter, so the list is empty unless a match
        // is found. Returns false for IdentityId 0 (no player), factions, or fully resolvable-but-offline players.
        private static bool IsIdentityOnline(long identityId)
        {
            if (identityId == 0L) return false;
            try
            {
                var probe = new System.Collections.Generic.List<VRage.Game.ModAPI.IMyPlayer>();
                MyAPIGateway.Players.GetPlayers(probe, p => p != null && p.IdentityId == identityId);
                return probe.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        internal bool IsWeldIntegrityReached(IMySlimBlock target)
        {
            try
            {
                // FEAT-034: Skeleton mode — always consider integrity reached (only place, don't weld)
                if (Settings.WeldOptions == AutoWeldOptions.WeldSkeleton)
                    return true;

                if (Settings.WeldOptions == AutoWeldOptions.WeldFull)
                    return target.IsFullIntegrity;

                var requiredIntegrity = target.GetRequiredIntegrity(Settings.WeldOptions);
                return target.Integrity >= requiredIntegrity;
            }
            catch (Exception ex)
            {
                // If something goes wrong, treat as fully built to avoid infinite retries.
                Logging.Instance.Error("IsWeldIntegrityReached exception: {0}", ex.Message);
                return true;
            }
        }

        private bool ServerDoWeld(TargetBlockData targetData)
        {
            var profilerTs = MethodProfiler.Start();
            long tsBuild = 0, tsStockpile = 0, tsMount = 0, tsDeform = 0, tsResolve = 0;
            long tsResolveCoord = 0, tsResolveLookup = 0; // BUG-108 sub-timers
            // BUG-113: cover the previously-unprofiled engine/handler calls inside ServerDoWeld
            // so welding spikes can be attributed beyond just proj.Build/IncreaseMountLevel.
            long tsFindItem = 0, tsLimitsCheck = 0, tsCanContinue = 0, tsIntegrityCheck = 0, tsAssign = 0;
            var welderInventory = _Welder.GetInventory(0);
            var welding = false;
            var created = false;
            float appliedWeldAmount = 0f;
            var target = targetData.Block;
            var hasIgnoreColor = ((Settings.Flags & SyncBlockSettings.Settings.UseIgnoreColor) != 0) && IsColorNearlyEquals(Settings.IgnoreColorPacked, target.GetColorMask());

            if ((targetData.Attributes & TargetBlockData.AttributeFlags.Projected) != 0)
            {
                //New Block (Projected)
                var cubeGridProjected = target.CubeGrid as MyCubeGrid;
                var blockDefinition = target.BlockDefinition as MyCubeBlockDefinition;
                tsFindItem = Stopwatch.GetTimestamp();
                var item = _TransportInventory.FindItem(blockDefinition.Components[0].Definition.Id);
                tsFindItem = Stopwatch.GetTimestamp() - tsFindItem;

                if ((CreativeModeActive || (item != null && item.Amount >= 1)) && cubeGridProjected != null && cubeGridProjected.Projector != null)
                {
                    tsLimitsCheck = Stopwatch.GetTimestamp();
                    var withinLimits = _Welder.IsWithinWorldLimits(cubeGridProjected.Projector, blockDefinition.BlockPairName, blockDefinition.PCU);
                    tsLimitsCheck = Stopwatch.GetTimestamp() - tsLimitsCheck;
                    if (withinLimits)
                    {
                        if (!cubeGridProjected.Projector.Closed && !cubeGridProjected.Projector.CubeGrid.Closed && (target.FatBlock == null || !target.FatBlock.Closed))
                        {
                            // BUG-107: gate proj.Build on the global per-tick budget. The 7-9ms
                            // buildMs spikes on projected armor/conveyor blocks come from the SE
                            // engine materialization + grid topology update. Spread across ticks.
                            // Skip both build AND resolve when the slot is exhausted — resolving
                            // a not-yet-built block would null out the target and ignore it.
                            if (!Mod.TryClaimProjBuildSlot())
                            {
                                EmitServerDoWeldProfile(profilerTs,
                                    targetData.Block != null ? targetData.Block.BlockDefinition.Id.SubtypeName : "null",
                                    true, false, false, false,
                                    0.0, 0.0, 0.0, 0.0,
                                    0.0, 0.0, 0.0,
                                    tsFindItem * 1000.0 / Stopwatch.Frequency, tsLimitsCheck * 1000.0 / Stopwatch.Frequency, 0.0, 0.0, 0.0,
                                    0f, 0f, false, targetData.Distance,
                                    "projBuildSlot");
                                return false;
                            }

                            tsBuild = Stopwatch.GetTimestamp();
                            var proj = cubeGridProjected.Projector as Sandbox.ModAPI.IMyProjector;
                            // BUG-115: proj.Build() can throw NullReferenceException from inside SE's
                            // MyProjectorBase.BuildInternal → MySessionComponentGameInventory.ValidateArmor/HasArmor
                            // when the BuiltBy player's Steam ID can't be resolved (offline player, missing DLC
                            // owner data on large worlds with imported grids). Without this guard the exception
                            // bubbles up through ServerTryWelding → ServerTryWeldingGrindingCollecting and is
                            // silently swallowed by UpdateBeforeSimulation10_100's catch — meaning the BaR's
                            // entire weld tick aborts and the lock-on block is retried next tick, ad infinitum.
                            // Mark the block as ignored so the loop moves on; profiler still logs the failure.
                            try
                            {
                                proj.Build(target, _Welder.OwnerId, _Welder.EntityId, true, _Welder.OwnerId);
                            }
                            catch (NullReferenceException ex)
                            {
                                tsBuild = Stopwatch.GetTimestamp() - tsBuild;
                                targetData.Ignore = true;
                                // BUG-115: persist the skip across scan refreshes. Without this, the next
                                // background scan rebuilds TargetBlockData with Ignore=false and the BaR
                                // retries the same broken block, spamming the mod log every scan cycle.
                                var brokenKey = GetBrokenBlockKey(target);
                                var firstFailure = brokenKey != null && _BrokenProjBuildKeys.Add(brokenKey);
                                if (firstFailure && Logging.Instance.ShouldLog(Logging.Level.Error))
                                {
                                    // BUG-115 diagnostic: the SE NRE is inside MySessionComponentGameInventory.HasArmor.
                                    // Both args (MyStringHash armorId, ulong steamId) are value types, so the null
                                    // deref must come from internal state keyed by one of those args. Capture every
                                    // input we control so we can correlate which IDs/blocks/projectors trigger it
                                    // and rule in/out: BuiltBy resolution, modded armor variant missing from the
                                    // entitlement table, owner-online state, etc.
                                    var passedBuiltBy = _Welder.OwnerId; // value passed as proj.Build's 5th arg
                                    var slimBlockBuiltBy = _Welder.SlimBlock != null ? _Welder.SlimBlock.BuiltBy : 0L;
                                    var typeIdName = blockDefinition != null ? blockDefinition.Id.TypeId.ToString() : "null";
                                    var subtypeName = blockDefinition != null ? blockDefinition.Id.SubtypeName : "null";
                                    var pairName = blockDefinition != null ? blockDefinition.BlockPairName : "null";
                                    var pcu = blockDefinition != null ? blockDefinition.PCU : 0;
                                    var fromMod = blockDefinition != null && blockDefinition.Context != null && !blockDefinition.Context.IsBaseGame;
                                    var modName = fromMod ? blockDefinition.Context.ModName : "BaseGame";
                                    var projectorApi = cubeGridProjected != null ? cubeGridProjected.Projector as Sandbox.ModAPI.IMyProjector : null;
                                    var projectorEntityId = projectorApi != null ? projectorApi.EntityId : 0L;
                                    var projectorGridName = projectorApi != null && projectorApi.CubeGrid != null ? projectorApi.CubeGrid.DisplayName : "null";
                                    var ownerOnline = IsIdentityOnline(passedBuiltBy);
                                    var slimOwnerOnline = IsIdentityOnline(slimBlockBuiltBy);
                                    Logging.Instance.Write(Logging.Level.Error,
                                        "BuildAndRepairSystemBlock {0}: proj.Build threw NRE; marking block permanently ignored for this BaR. " +
                                        "block.subtype={1}; block.typeId={2}; block.pairName={3}; block.pcu={4}; block.fromMod={5}; block.modName={6}; " +
                                        "passed.owner={7}; passed.builder={8}; passed.builtBy={9}; passed.builtBy.online={10}; " +
                                        "barSlim.builtBy={11}; barSlim.builtBy.online={12}; " +
                                        "projector.entityId={13}; projector.grid={14}; " +
                                        "ex={15}",
                                        Logging.BlockName(_Welder, Logging.BlockNameOptions.None),
                                        subtypeName, typeIdName, pairName, pcu, fromMod, modName,
                                        _Welder.OwnerId, _Welder.EntityId, passedBuiltBy, ownerOnline,
                                        slimBlockBuiltBy, slimOwnerOnline,
                                        projectorEntityId, projectorGridName,
                                        ex.Message);
                                }
                                EmitServerDoWeldProfile(profilerTs,
                                    targetData.Block != null ? targetData.Block.BlockDefinition.Id.SubtypeName : "null",
                                    true, false, false, false,
                                    tsBuild * 1000.0 / Stopwatch.Frequency, 0.0, 0.0, 0.0,
                                    0.0, 0.0, 0.0,
                                    tsFindItem * 1000.0 / Stopwatch.Frequency, tsLimitsCheck * 1000.0 / Stopwatch.Frequency, 0.0, 0.0, 0.0,
                                    0f, 0f, true, targetData.Distance,
                                    "projBuildNRE");
                                return false;
                            }
                            tsBuild = Stopwatch.GetTimestamp() - tsBuild;
                        }

                        // proj.Build() handles component consumption internally; manual RemoveItems is not needed.

                        // BUG-105/108: instrument the projected→physical block resolution.
                        // tsResolve aggregates everything; tsResolveCoord and tsResolveLookup
                        // split out the coordinate transform vs the GetCubeBlock lookup so the
                        // dominant cost can be identified for a follow-up cache strategy.
                        tsResolve = Stopwatch.GetTimestamp();
                        //After creation we can't welding this projected block, we have to find the 'physical' block instead.
                        var projectorGrid = cubeGridProjected.Projector != null ? cubeGridProjected.Projector.CubeGrid : null;
                        if (projectorGrid == null || projectorGrid.Closed)
                        {
                            targetData.Ignore = true;
                        }
                        else
                        {
                            var tsResolveMark = Stopwatch.GetTimestamp();
                            var blockPos = projectorGrid.WorldToGridInteger(cubeGridProjected.GridIntegerToWorld(target.Position));
                            tsResolveCoord = Stopwatch.GetTimestamp() - tsResolveMark;

                            tsResolveMark = Stopwatch.GetTimestamp();
                            target = projectorGrid.GetCubeBlock(blockPos);
                            tsResolveLookup = Stopwatch.GetTimestamp() - tsResolveMark;

                            if (target != null)
                            {
                                // BUG-113: instrument the assignment release+reassign pair (BlockSystemAssigningHandler
                                // dictionary ops). Cheap individually but worth measuring under contention.
                                var tsAssignMark = Stopwatch.GetTimestamp();
                                // Release the projected block's assignment before switching to the physical block.
                                ReleaseAssignmentIfEnabled(targetData.Block);
                                targetData.Block = target;
                                targetData.Attributes &= ~TargetBlockData.AttributeFlags.Projected;
                                created = true;

                                // Close assignment gap: the projected block's key (ProjGridId:Pos) differs
                                // from the physical block's key (RealGridId:Pos). Assign the physical block
                                // immediately so no other BaR can steal it during our stagger wait.
                                if (Mod.Settings.AssignToSystemEnabled)
                                    target.AssignToSystem(_Welder.EntityId);
                                tsAssign = Stopwatch.GetTimestamp() - tsAssignMark;
                            }
                            else
                            {
                                targetData.Ignore = true;
                                // BUG-120: proj.Build returned cleanly but the physical block didn't
                                // appear (typical signature: online owner lacks the required DLC).
                                // Track per-block; after PROJ_BUILD_MAX_SILENT_FAILS consecutive ticks,
                                // promote to the persistent skip set so background scan refreshes don't
                                // keep re-feeding this block back to the weld loop. Threshold rules out
                                // transient races (another BaR built it the same tick, momentary projector
                                // disable, brief component shortage). Reset by EnsureBrokenCacheOwnerScope
                                // (owner change) and by _onEnabledChanged (player power-cycle).
                                var brokenKey = GetBrokenBlockKey(targetData.Block);
                                if (brokenKey != null)
                                {
                                    int silentFailCount;
                                    _ProjBuildSilentFailCount.TryGetValue(brokenKey, out silentFailCount);
                                    silentFailCount++;
                                    if (silentFailCount >= PROJ_BUILD_MAX_SILENT_FAILS)
                                    {
                                        _ProjBuildSilentFailCount.Remove(brokenKey);
                                        if (_BrokenProjBuildKeys.Add(brokenKey) && Logging.Instance.ShouldLog(Logging.Level.Event))
                                        {
                                            Logging.Instance.Write(Logging.Level.Event,
                                                "BuildAndRepairSystemBlock {0}: proj.Build silently failed {1} times for block at {2}; ignored permanently for owner {3} (likely DLC-missing). Power-cycle the BaR or reassign ownership to retry.",
                                                Logging.BlockName(_Welder, Logging.BlockNameOptions.None),
                                                PROJ_BUILD_MAX_SILENT_FAILS, brokenKey, _Welder.OwnerId);
                                        }
                                    }
                                    else
                                    {
                                        _ProjBuildSilentFailCount[brokenKey] = silentFailCount;
                                    }
                                }
                            }
                        }
                        tsResolve = Stopwatch.GetTimestamp() - tsResolve;

                    }
                    else
                    {
                        State.LimitsExceeded = true;
                        targetData.Ignore = true;
                    }
                }
            }

            // FEAT-034: Skeleton mode — block was placed, skip further welding
            var skipWelding = created && Settings.WeldOptions == AutoWeldOptions.WeldSkeleton;

            if (!skipWelding && !hasIgnoreColor && target != null && (targetData.Attributes & TargetBlockData.AttributeFlags.Projected) == 0)
            {
                // BUG-113: instrument IsWeldIntegrityReached — cheap property comparison but worth
                // measuring to confirm. Aggregates both call sites (entry check + completion check).
                var tsIntMark = Stopwatch.GetTimestamp();
                var integrityReached = IsWeldIntegrityReached(target);
                tsIntegrityCheck = Stopwatch.GetTimestamp() - tsIntMark;
                //No ignore color and allready created
                if (!integrityReached || created)
                {
                    //Move collected/needed items to stockpile.
                    tsStockpile = Stopwatch.GetTimestamp();
                    target.MoveItemsToConstructionStockpile(_TransportInventory);
                    tsStockpile = Stopwatch.GetTimestamp() - tsStockpile;

                    // BUG-113: instrument target.CanContinueBuild — engine call, can be costly when
                    // there are many components to check against the transport inventory.
                    var tsCanMark = Stopwatch.GetTimestamp();
                    welding = target.CanContinueBuild(_TransportInventory) || CreativeModeActive;
                    tsCanContinue = Stopwatch.GetTimestamp() - tsCanMark;

                    if (welding)
                    {
                        var weldAmount = MyAPIGateway.Session.WelderSpeedMultiplier * Mod.Settings.Welder.WeldingMultiplier * WELDER_AMOUNT_PER_SECOND;

                        // Cap weld amount for non-Full modes so we don't overshoot the target integrity.
                        if (Settings.WeldOptions != AutoWeldOptions.WeldFull)
                        {
                            var remaining = target.GetRequiredIntegrity(Settings.WeldOptions) - target.Integrity;
                            if (remaining <= 0f)
                                welding = false;
                            else
                                weldAmount = Math.Min(weldAmount, remaining);
                        }

                        if (welding)
                        {
                            appliedWeldAmount = weldAmount;
                            tsMount = Stopwatch.GetTimestamp();
                            target.IncreaseMountLevel(weldAmount, _Welder.OwnerId, welderInventory, MyAPIGateway.Session.WelderSpeedMultiplier * Mod.Settings.Welder.WeldingMultiplier * WELDER_MAX_REPAIR_BONE_MOVEMENT_SPEED, false);
                            tsMount = Stopwatch.GetTimestamp() - tsMount;
                        }
                    }

                    var tsIntMark2 = Stopwatch.GetTimestamp();
                    if (IsWeldIntegrityReached(target))
                    {
                        targetData.Ignore = true;
                    }
                    tsIntegrityCheck += Stopwatch.GetTimestamp() - tsIntMark2;
                }
                else
                {
                    //Deformation
                    welding = true;
                    // BUG-105: instrument the deformation IncreaseMountLevel — previously
                    // unprofiled and could be a hidden cost during full-integrity repairs.
                    tsDeform = Stopwatch.GetTimestamp();
                    target.IncreaseMountLevel(MyAPIGateway.Session.WelderSpeedMultiplier * Mod.Settings.Welder.WeldingMultiplier * WELDER_AMOUNT_PER_SECOND, _Welder.OwnerId, welderInventory, MyAPIGateway.Session.WelderSpeedMultiplier * Mod.Settings.Welder.WeldingMultiplier * WELDER_MAX_REPAIR_BONE_MOVEMENT_SPEED, false);
                    tsDeform = Stopwatch.GetTimestamp() - tsDeform;
                }
            }

            var result = welding || created;
            EmitServerDoWeldProfile(profilerTs,
                targetData.Block != null ? targetData.Block.BlockDefinition.Id.SubtypeName : "null",
                (targetData.Attributes & TargetBlockData.AttributeFlags.Projected) != 0,
                created, welding, result,
                tsBuild * 1000.0 / Stopwatch.Frequency,
                tsResolve * 1000.0 / Stopwatch.Frequency,
                tsResolveCoord * 1000.0 / Stopwatch.Frequency,
                tsResolveLookup * 1000.0 / Stopwatch.Frequency,
                tsStockpile * 1000.0 / Stopwatch.Frequency,
                tsMount * 1000.0 / Stopwatch.Frequency,
                tsDeform * 1000.0 / Stopwatch.Frequency,
                tsFindItem * 1000.0 / Stopwatch.Frequency,
                tsLimitsCheck * 1000.0 / Stopwatch.Frequency,
                tsCanContinue * 1000.0 / Stopwatch.Frequency,
                tsIntegrityCheck * 1000.0 / Stopwatch.Frequency,
                tsAssign * 1000.0 / Stopwatch.Frequency,
                appliedWeldAmount,
                target != null && target.MaxIntegrity > 0f ? target.Integrity / target.MaxIntegrity : 0f,
                targetData.Ignore,
                targetData.Distance,
                null);
            return result;
        }

        /// <summary>
        /// Try to find an the missing components and moves them into welder inventory
        /// </summary>
        private bool ServerFindMissingComponents(TargetBlockData targetData)
        {
            var profilerTs = MethodProfiler.Start();
            // BUG-122: split internal cost. Profile session 20260429181044 showed 9-10 ms spikes
            // on projected blocks (LargeBlockLargeContainer, SmallHydrogenThrust). tsGetMissing
            // sums the SE engine `GetMissingComponents` walks (1 call non-projected, 1-2 projected);
            // tsPullPick sums the inner overload that runs ServerPickFromWelder + PullComponents.
            var tsGetMissing = 0L;
            var tsPullPick = 0L;
            long tsMark;
            try
            {
                var playTime = MyAPIGateway.Session.ElapsedPlayTime;

                if (IsTransportRunning(playTime))
                    return true;

                var remainingVolume = _MaxWeldTransportVolume;
                _TempMissingComponents.Clear();
                var picked = false;
                var cubeGrid = targetData.Block.CubeGrid as MyCubeGrid;
                if (cubeGrid == null) return false;

                if ((targetData.Attributes & TargetBlockData.AttributeFlags.Projected) != 0)
                {
                    // Single GetMissingComponents call for projected blocks.
                    // Skeleton/ignoreColor: only the creation component.
                    // Otherwise: all components at the target integrity level.
                    var useIgnoreColor = ((Settings.Flags & SyncBlockSettings.Settings.UseIgnoreColor) != 0) && IsColorNearlyEquals(Settings.IgnoreColorPacked, targetData.Block.GetColorMask());
                    if (Settings.WeldOptions == AutoWeldOptions.WeldSkeleton || useIgnoreColor)
                    {
                        tsMark = profilerTs != 0L ? Stopwatch.GetTimestamp() : 0L;
                        targetData.Block.GetMissingComponents(_TempMissingComponents, IntegrityLevel.Create);
                        if (tsMark != 0L) tsGetMissing += Stopwatch.GetTimestamp() - tsMark;

                        if (_TempMissingComponents.Count > 0)
                        {
                            tsMark = profilerTs != 0L ? Stopwatch.GetTimestamp() : 0L;
                            picked = ServerFindMissingComponents(targetData, ref remainingVolume);
                            if (tsMark != 0L) tsPullPick += Stopwatch.GetTimestamp() - tsMark;
                        }
                    }
                    else
                    {
                        // Pick creation component first to guarantee it's in transport
                        // before other components fill the volume.
                        tsMark = profilerTs != 0L ? Stopwatch.GetTimestamp() : 0L;
                        targetData.Block.GetMissingComponents(_TempMissingComponents, IntegrityLevel.Create);
                        if (tsMark != 0L) tsGetMissing += Stopwatch.GetTimestamp() - tsMark;
                        if (_TempMissingComponents.Count > 0)
                        {
                            tsMark = profilerTs != 0L ? Stopwatch.GetTimestamp() : 0L;
                            picked = ServerFindMissingComponents(targetData, ref remainingVolume);
                            if (tsMark != 0L) tsPullPick += Stopwatch.GetTimestamp() - tsMark;
                        }

                        // Then fetch remaining components (full/functional level minus creation)
                        if (picked)
                        {
                            var blockDef = targetData.Block.BlockDefinition as MyCubeBlockDefinition;
                            var createCompName = blockDef.Components[0].Definition.Id.SubtypeName;
                            int createCount;
                            _TempMissingComponents.TryGetValue(createCompName, out createCount);
                            _TempMissingComponents.Clear();

                            tsMark = profilerTs != 0L ? Stopwatch.GetTimestamp() : 0L;
                            targetData.Block.GetMissingComponents(_TempMissingComponents, Settings.WeldOptions == AutoWeldOptions.WeldFunctional ? IntegrityLevel.Functional : IntegrityLevel.Complete);
                            if (tsMark != 0L) tsGetMissing += Stopwatch.GetTimestamp() - tsMark;

                            // Subtract the creation component (already picked)
                            if (createCount > 0 && _TempMissingComponents.ContainsKey(createCompName))
                            {
                                if (_TempMissingComponents[createCompName] <= createCount)
                                    _TempMissingComponents.Remove(createCompName);
                                else
                                    _TempMissingComponents[createCompName] -= createCount;
                            }
                        }
                    }
                }
                else
                {
                    tsMark = profilerTs != 0L ? Stopwatch.GetTimestamp() : 0L;
                    targetData.Block.GetMissingComponents(_TempMissingComponents, Settings.WeldOptions == AutoWeldOptions.WeldFunctional ? IntegrityLevel.Functional : IntegrityLevel.Complete);
                    if (tsMark != 0L) tsGetMissing += Stopwatch.GetTimestamp() - tsMark;
                }

                if (_TempMissingComponents.Count > 0)
                {
                    tsMark = profilerTs != 0L ? Stopwatch.GetTimestamp() : 0L;
                    ServerFindMissingComponents(targetData, ref remainingVolume);
                    if (tsMark != 0L) tsPullPick += Stopwatch.GetTimestamp() - tsMark;
                }

                if (remainingVolume < _MaxWeldTransportVolume || (CreativeModeActive && _TempMissingComponents.Count > 0))
                {
                    //Transport startet
                    State.CurrentTransportIsPick = false;
                    State.CurrentTransportIsCollecting = false;
                    State.CurrentTransportTarget = ComputePosition(targetData.Block);
                    State.CurrentTransportStartTime = playTime;
                    State.CurrentTransportTime = TimeSpan.FromSeconds(2d * targetData.Distance / Settings.WeldTransportSpeed);

                    return true;
                }
                return false;
            }
            finally
            {
                _TempMissingComponents.Clear();
                if (profilerTs != 0L)
                {
                    var _transportStarted = State.CurrentTransportStartTime > TimeSpan.Zero;
                    var _transportTimeS = State.CurrentTransportTime.TotalSeconds;
                    var _distance = targetData.Distance;
                    var _weldTransportSpeed = Settings.WeldTransportSpeed;
                    var tsFreq = Stopwatch.Frequency;
                    var _getMissingMs = tsGetMissing * 1000.0 / tsFreq;
                    var _pullPickMs = tsPullPick * 1000.0 / tsFreq;
                    MethodProfiler.StopAndLog("ServerFindMissingComponents", profilerTs, () =>
                        string.Format("entityId={0};block={1};projected={2};transportStarted={3};transportTimeS={4:F3};distance={5:F1};weldTransportSpeed={6:F1};getMissingMs={7:F3};pullPickMs={8:F3}",
                            _Welder.EntityId,
                            targetData.Block != null ? targetData.Block.BlockDefinition.Id.SubtypeName : "null",
                            (targetData.Attributes & TargetBlockData.AttributeFlags.Projected) != 0,
                            _transportStarted, _transportTimeS, _distance, _weldTransportSpeed,
                            _getMissingMs, _pullPickMs));
                }
            }
        }

        private bool ServerFindMissingComponents(TargetBlockData targetData, ref float remainingVolume)
        {
            // BUG-133: replaced per-component-foreach-over-sources with source-outer iteration.
            // Old path was 18.9 ms on cold cache for projected blocks (10 components × 78
            // sources × FindItem engine call). New path walks each source's items ONCE per
            // call and matches against the remaining-needs dict — O(sources + items) instead
            // of O(components × sources × items). Phase 1 still tries the welder's own
            // inventory per-component (cheap, small inventory).
            var picked = false;
            _TempPullRemaining.Clear();
            _TempPullDefs.Clear();

            // Phase 1: try to satisfy from welder's own inventory before walking sources.
            // ServerPickFromWelder is cheap (single inventory) and may zero out some needs.
            foreach (var keyValue in _TempMissingComponents)
            {
                var componentId = new MyDefinitionId(typeof(MyObjectBuilder_Component), keyValue.Key);
                var definition = MyDefinitionManager.Static.GetPhysicalItemDefinition(componentId);
                int neededAmount = keyValue.Value;

                picked = ServerPickFromWelder(componentId, definition.Volume, ref neededAmount, ref remainingVolume) || picked;

                if (neededAmount > 0)
                {
                    _TempPullRemaining[keyValue.Key] = neededAmount;
                    _TempPullDefs[keyValue.Key] = definition;
                }
                if (remainingVolume <= 0) break;
            }

            // Phase 2: single source walk for everything still missing.
            if (_TempPullRemaining.Count > 0 && remainingVolume > 0)
            {
                picked = PullFromSourcesOnePass(ref remainingVolume) || picked;
            }

            // Phase 3: report whatever we still could not get.
            foreach (var keyValue in _TempPullRemaining)
            {
                if (keyValue.Value > 0)
                {
                    var componentId = new MyDefinitionId(typeof(MyObjectBuilder_Component), keyValue.Key);
                    AddToMissingComponents(componentId, keyValue.Value);
                }
            }

            _TempPullRemaining.Clear();
            _TempPullDefs.Clear();
            return picked;
        }

        /// <summary>
        /// BUG-133: walks each source inventory exactly once, matching items against any
        /// component still in <see cref="_TempPullRemaining"/>. Pulls into the welder, then
        /// stages into the transport inventory via ServerPickFromWelder so remainingVolume
        /// reflects what's actually queued for transport. Updates _TempPullRemaining in place.
        /// BUG-134: empty sources are skipped without paying the GetItems engine call, and
        /// the source that succeeded on the previous call is tried first to exploit temporal
        /// locality (back-to-back welds usually need the same components from the same place).
        /// BUG-136: walk is capped at MaxSourcesPerPullWalk sources per call and resumes from
        /// _NextPullSourceIdx next call (round-robin). On grids with many cargo containers
        /// (~80) the uncapped walk caused 20 ms spikes per weld cycle while every BaR-related
        /// lock was held — see ServerTryWelding's deferred-work block. The cap accepts partial
        /// component pulls (transport starts with whatever was found; the weld cycle re-runs
        /// against the same block on the next tick) in exchange for bounded per-call cost.
        /// BUG-141: per-call detailed instrumentation. Identifies whether spikes come from
        /// the engine GetItems walk (large/many cargos = many items to enumerate),
        /// CanTransferItemTo (conveyor topology re-walk), MaxItemsAddable (volume math),
        /// or TransferItemFrom (the actual item move). Each sub-time is a sum across
        /// the sources visited in this call.
        /// </summary>
        private const int MaxSourcesPerPullWalk = 16;

        // BUG-141: aggregate stats accumulated across all TryPullFromSource calls in one
        // PullFromSourcesOnePass invocation. Pass-by-ref so each TryPullFromSource adds
        // its own contribution without per-call profiler overhead.
        // ProfilerEnabled gate so Stopwatch.GetTimestamp calls are zero-cost when
        // profiling is off (mirrors the profilerTs!=0L pattern used elsewhere).
        private struct PullSourceStats
        {
            public bool ProfilerEnabled;   // gate sub-timers when profiling is off
            public int SourcesEmpty;       // ItemCount==0 short-circuits
            public int ItemsExamined;      // total items walked across all visited sources
            public int MaxItemsAddCalls;   // MaxItemsAddable invocations
            public int TransferAttempts;   // TransferItemFrom invocations
            public int TransferSucceeded;  // TransferItemFrom returns true
            public long TsGetItems;        // sum of srcInventory.GetItems engine ticks
            public long TsMaxItemsAdd;     // sum of MaxItemsAddable engine ticks
            public long TsTransfer;        // sum of TransferItemFrom engine ticks
            public long MaxSourceTicks;    // longest single TryPullFromSource call
        }

        private bool PullFromSourcesOnePass(ref float remainingVolume)
        {
            // BUG-141: per-call profile entry separate from outer ServerFindMissingComponents
            // pullPickMs. Lets us see source-walk patterns directly: how many sources visited,
            // empty-skip rate, time split across engine calls, max single-source cost.
            var profilerTs = MethodProfiler.Start();
            var picked = false;
            var welderInventory = _Welder.GetInventory(0);
            if (welderInventory == null)
            {
                if (profilerTs != 0L)
                {
                    MethodProfiler.StopAndLog("PullFromSourcesOnePass", profilerTs, () =>
                        string.Format("entityId={0};earlyExit=noWelderInv", _Welder.EntityId));
                }
                return false;
            }

            var stats = new PullSourceStats();
            stats.ProfilerEnabled = profilerTs != 0L;  // BUG-141 fix: gate sub-timers
            var sourcesAvailable = 0;
            var sourcesVisited = 0;
            var lastSuccessfulHit = false;
            var lockAcquireTs = profilerTs != 0L ? Stopwatch.GetTimestamp() : 0L;

            lock (_PossibleSources)
            {
                var lockAcquiredTs = profilerTs != 0L ? Stopwatch.GetTimestamp() : 0L;
                var lockWaitTicks = lockAcquiredTs != 0L ? lockAcquiredTs - lockAcquireTs : 0L;
                sourcesAvailable = _PossibleSources.Count;
                if (sourcesAvailable == 0)
                {
                    if (profilerTs != 0L)
                    {
                        var _lockWaitMs = lockWaitTicks * 1000.0 / Stopwatch.Frequency;
                        MethodProfiler.StopAndLog("PullFromSourcesOnePass", profilerTs, () =>
                            string.Format("entityId={0};earlyExit=noSources;lockWaitMs={1:F3}", _Welder.EntityId, _lockWaitMs));
                    }
                    return false;
                }

                // BUG-134: locate the last successful source in the current list. Linear scan
                // (~µs for ~80 entries). Reference invalidates after a scan rebuilds the list,
                // in which case we just fall through to the normal walk.
                var lastSuccessful = _LastSuccessfulSource;
                int lastSuccessfulIdx = -1;
                if (lastSuccessful != null)
                {
                    for (int i = 0; i < sourcesAvailable; i++)
                    {
                        if (ReferenceEquals(_PossibleSources[i], lastSuccessful))
                        {
                            lastSuccessfulIdx = i;
                            break;
                        }
                    }
                }

                bool abort = false;
                if (lastSuccessfulIdx >= 0)
                {
                    var srcStartTs = profilerTs != 0L ? Stopwatch.GetTimestamp() : 0L;
                    abort = TryPullFromSource(_PossibleSources[lastSuccessfulIdx], welderInventory, ref remainingVolume, ref picked, ref stats);
                    if (srcStartTs != 0L)
                    {
                        var srcTicks = Stopwatch.GetTimestamp() - srcStartTs;
                        if (srcTicks > stats.MaxSourceTicks) stats.MaxSourceTicks = srcTicks;
                    }
                    sourcesVisited++;
                    lastSuccessfulHit = true;
                    if (abort || remainingVolume <= 0 || _TempPullRemaining.Count == 0)
                    {
                        if (profilerTs != 0L)
                        {
                            EmitPullStats(profilerTs, sourcesAvailable, sourcesVisited, lastSuccessfulHit, lockWaitTicks, ref stats, "lastSuccessfulSatisfied");
                        }
                        return picked;
                    }
                }

                // BUG-136: capped, round-robin walk. Start where the last call left off so
                // calls that miss _LastSuccessfulSource still eventually visit every source
                // instead of always re-walking the same low-index entries.
                if (_NextPullSourceIdx < 0 || _NextPullSourceIdx >= sourcesAvailable)
                    _NextPullSourceIdx = 0;
                var startIdx = _NextPullSourceIdx;
                var visited = 0;
                string exitReason = "capReached";

                for (var step = 0; step < sourcesAvailable; step++)
                {
                    if (visited >= MaxSourcesPerPullWalk) { exitReason = "capReached"; break; }
                    var srcIdx = (startIdx + step) % sourcesAvailable;
                    if (srcIdx == lastSuccessfulIdx) continue;

                    visited++;
                    if (remainingVolume <= 0) { exitReason = "noVolume"; break; }
                    if (_TempPullRemaining.Count == 0) { exitReason = "needsSatisfied"; break; }

                    var srcStartTs = profilerTs != 0L ? Stopwatch.GetTimestamp() : 0L;
                    var welderFull = TryPullFromSource(_PossibleSources[srcIdx], welderInventory, ref remainingVolume, ref picked, ref stats);
                    if (srcStartTs != 0L)
                    {
                        var srcTicks = Stopwatch.GetTimestamp() - srcStartTs;
                        if (srcTicks > stats.MaxSourceTicks) stats.MaxSourceTicks = srcTicks;
                    }
                    sourcesVisited++;

                    // Advance the cursor past this source even on misses so the next call
                    // resumes further into the list rather than retrying the same ones.
                    var next = srcIdx + 1;
                    if (next >= sourcesAvailable) next = 0;
                    _NextPullSourceIdx = next;

                    if (welderFull) { exitReason = "welderFull"; break; }
                }

                if (profilerTs != 0L)
                {
                    EmitPullStats(profilerTs, sourcesAvailable, sourcesVisited, lastSuccessfulHit, lockWaitTicks, ref stats, exitReason);
                }
            }

            return picked;
        }

        private void EmitPullStats(long profilerTs, int sourcesAvailable, int sourcesVisited, bool lastSuccessfulHit, long lockWaitTicks, ref PullSourceStats stats, string exitReason)
        {
            var tsFreq = Stopwatch.Frequency;
            var _lockWaitMs = lockWaitTicks * 1000.0 / tsFreq;
            var _getItemsMs = stats.TsGetItems * 1000.0 / tsFreq;
            var _maxItemsAddMs = stats.TsMaxItemsAdd * 1000.0 / tsFreq;
            var _transferMs = stats.TsTransfer * 1000.0 / tsFreq;
            var _maxSourceMs = stats.MaxSourceTicks * 1000.0 / tsFreq;
            var _entityId = _Welder.EntityId;
            var _sourcesAvailable = sourcesAvailable;
            var _sourcesVisited = sourcesVisited;
            var _sourcesEmpty = stats.SourcesEmpty;
            var _itemsExamined = stats.ItemsExamined;
            var _maxItemsAddCalls = stats.MaxItemsAddCalls;
            var _transferAttempts = stats.TransferAttempts;
            var _transferSucceeded = stats.TransferSucceeded;
            var _lastSuccessfulHit = lastSuccessfulHit;
            var _exitReason = exitReason;
            MethodProfiler.StopAndLog("PullFromSourcesOnePass", profilerTs, () =>
                string.Format("entityId={0};available={1};visited={2};empty={3};itemsExamined={4};maxItemsAddCalls={5};transferAttempts={6};transferSucceeded={7};lastSuccessfulHit={8};getItemsMs={9:F3};maxItemsAddMs={10:F3};transferMs={11:F3};maxSourceMs={12:F3};lockWaitMs={13:F3};exit={14}",
                    _entityId, _sourcesAvailable, _sourcesVisited, _sourcesEmpty, _itemsExamined, _maxItemsAddCalls, _transferAttempts, _transferSucceeded, _lastSuccessfulHit,
                    _getItemsMs, _maxItemsAddMs, _transferMs, _maxSourceMs, _lockWaitMs, _exitReason));
        }

        /// <summary>
        /// BUG-134: per-source pull body. Returns true if the welder is full and the caller
        /// should abort the entire walk (no further source can yield anything that fits).
        /// Returning false means "ok, continue with next source".
        /// BUG-141: stats ref accumulates engine-call timings + counters across all calls
        /// in one PullFromSourcesOnePass invocation. Whether the spike is in GetItems
        /// (engine inventory enumeration on large cargos), CanTransferItemTo (conveyor
        /// network walk), or TransferItemFrom (the actual move) becomes visible.
        /// </summary>
        private bool TryPullFromSource(IMyInventory srcInventory, IMyInventory welderInventory, ref float remainingVolume, ref bool picked, ref PullSourceStats stats)
        {
            if (srcInventory == null) return false;
            var srcOwner = srcInventory.Owner as IMyEntity;
            if (srcOwner == null || srcOwner.MarkedForClose) return false;
            // BUG-134: skip empty sources without paying the GetItems engine call.
            if (srcInventory.ItemCount == 0)
            {
                stats.SourcesEmpty++;
                return false;
            }

            _TempPullInventoryItems.Clear();
            // BUG-141 fix: gate Stopwatch.GetTimestamp on stats.ProfilerEnabled so the
            // calls are zero-cost when profiling is off (matches BUG-122 pattern).
            var tsMark = stats.ProfilerEnabled ? Stopwatch.GetTimestamp() : 0L;
            srcInventory.GetItems(_TempPullInventoryItems);
            if (tsMark != 0L) stats.TsGetItems += Stopwatch.GetTimestamp() - tsMark;

            for (int i = _TempPullInventoryItems.Count - 1; i >= 0; i--)
            {
                if (remainingVolume <= 0) break;
                var srcItem = _TempPullInventoryItems[i];
                if (srcItem == null || srcItem.Amount <= 0) continue;

                stats.ItemsExamined++;
                var subtypeName = srcItem.Type.SubtypeId;
                int neededAmount;
                if (!_TempPullRemaining.TryGetValue(subtypeName, out neededAmount) || neededAmount <= 0) continue;

                Sandbox.Definitions.MyPhysicalItemDefinition definition;
                if (!_TempPullDefs.TryGetValue(subtypeName, out definition)) continue;
                var volume = definition.Volume;
                var componentId = new MyDefinitionId(typeof(MyObjectBuilder_Component), subtypeName);

                // BUG-148: skip the pre-flight CanTransferItemTo check. The source is in
                // _PossibleSources, which means AddIfConnectedToInventory already proved
                // conveyor reachability via IsConnectedTo (cached in InventoryHelper).
                // The remaining purpose of CanTransferItemTo was to detect per-component
                // sorter filters — but on servers without per-component sorters (the
                // common case) it's pure cost. Profile session 20260430234507 (Torch)
                // showed canTransferMs=4-5ms recurring at the spike rate (~every 5s)
                // because each weld probes 5-7 unique (src,welder,component) combinations
                // for the FIRST time — TTL bumps don't help, only avoiding the call does.
                // Trade-off: if a sorter blocks the component, MaxItemsAddable +
                // TransferItemFrom will both fire and TransferItemFrom returns false →
                // we lose two cheap engine calls per blocked component. Massive net win
                // when sorters are absent or rare.

                var maxByVolume = (int)Math.Floor(remainingVolume / volume);
                if (maxByVolume <= 0) continue;
                var amountPossible = Math.Min(Math.Min(neededAmount, (int)srcItem.Amount), maxByVolume);
                if (amountPossible <= 0) continue;

                stats.MaxItemsAddCalls++;
                tsMark = stats.ProfilerEnabled ? Stopwatch.GetTimestamp() : 0L;
                var amountMoveable = (int)welderInventory.MaxItemsAddable(amountPossible, componentId);
                if (tsMark != 0L) stats.TsMaxItemsAdd += Stopwatch.GetTimestamp() - tsMark;
                if (amountMoveable <= 0)
                {
                    _TempPullInventoryItems.Clear();
                    return true; // welder full → stop the whole walk
                }

                stats.TransferAttempts++;
                tsMark = stats.ProfilerEnabled ? Stopwatch.GetTimestamp() : 0L;
                var transferred = welderInventory.TransferItemFrom(srcInventory, i, null, true, amountMoveable);
                if (tsMark != 0L) stats.TsTransfer += Stopwatch.GetTimestamp() - tsMark;
                if (transferred)
                {
                    stats.TransferSucceeded++;
                    int needed = neededAmount;
                    picked = ServerPickFromWelder(componentId, volume, ref needed, ref remainingVolume) || picked;
                    if (needed > 0) _TempPullRemaining[subtypeName] = needed;
                    else _TempPullRemaining.Remove(subtypeName);
                    // BUG-134: remember this source as the first to try next call.
                    _LastSuccessfulSource = srcInventory;
                }
            }
            _TempPullInventoryItems.Clear();
            return false;
        }

        /// <summary>
        /// Try to pick needed material from own inventory, if successfull material is moved into transport inventory
        /// </summary>
        private bool ServerPickFromWelder(MyDefinitionId componentId, float volume, ref int neededAmount, ref float remainingVolume)
        {
            var profilerTs = MethodProfiler.Start();
            var picked = false;
            var startNeeded = neededAmount;

            var welderInventory = _Welder.GetInventory(0);
            if (welderInventory == null || welderInventory.Empty())
            {
                if (profilerTs != 0L)
                {
                    MethodProfiler.StopAndLog("ServerPickFromWelder", profilerTs, () =>
                        string.Format("entityId={0};component={1};startNeeded={2};picked={3};empty=True",
                            _Welder.EntityId, componentId.SubtypeName, startNeeded, false));
                }
                return picked;
            }

            _TempInventoryItems.Clear();
            welderInventory.GetItems(_TempInventoryItems);
            for (int i1 = _TempInventoryItems.Count - 1; i1 >= 0; i1--)
            {
                var srcItem = _TempInventoryItems[i1];
                if (srcItem != null && (MyDefinitionId)srcItem.Type == componentId && srcItem.Amount > 0)
                {
                    var maxpossibleAmount = Math.Min(neededAmount, (int)Math.Floor(remainingVolume / volume));
                    var pickedAmount = MyFixedPoint.Min(maxpossibleAmount, srcItem.Amount);
                    if (pickedAmount > 0)
                    {
                        welderInventory.RemoveItems(srcItem.ItemId, pickedAmount);
                        var physicalObjBuilder = (MyObjectBuilder_PhysicalObject)MyObjectBuilderSerializer.CreateNewObject((MyDefinitionId)srcItem.Type);
                        _TransportInventory.AddItems(pickedAmount, physicalObjBuilder);

                        neededAmount -= (int)pickedAmount;
                        remainingVolume -= (float)pickedAmount * volume;

                        picked = true;
                    }
                }
                if (neededAmount <= 0 || remainingVolume <= 0) break;
            }
            _TempInventoryItems.Clear();

            if (profilerTs != 0L)
            {
                MethodProfiler.StopAndLog("ServerPickFromWelder", profilerTs, () =>
                    string.Format("entityId={0};component={1};startNeeded={2};picked={3};empty=False",
                        _Welder.EntityId, componentId.SubtypeName, startNeeded, picked));
            }
            return picked;
        }

        private void AddToMissingComponents(MyDefinitionId componentId, int neededAmount)
        {
            int missingAmount;
            if (State.MissingComponents.TryGetValue(componentId, out missingAmount))
            {
                State.MissingComponents[componentId] = missingAmount + neededAmount;
            }
            else
            {
                State.MissingComponents.Add(componentId, neededAmount);
            }
        }

        // REF-5: single source of truth for the ServerDoWeld profile emit format. Pre-fix
        // the format string was duplicated at three call sites (two early exits + the
        // regular exit) and had already drifted — the regular exit grew resolveCoordMs /
        // resolveLookupMs sub-timers (BUG-108) while the two early exits stayed on the
        // older shape. Canonical format here covers all sub-timers plus a trailing
        // earlyExit field. Early-exit callers pass `0.0` for unmeasured timers and a
        // non-empty reason ("projBuildSlot", "projBuildNRE"); the regular exit passes
        // empty string.
        private const string ServerDoWeldProfileFormat =
            "entityId={0};block={1};projected={2};created={3};welding={4};result={5};" +
            "buildMs={6:F3};resolveMs={7:F3};resolveCoordMs={8:F3};resolveLookupMs={9:F3};" +
            "stockpileMs={10:F3};mountMs={11:F3};deformMs={12:F3};" +
            "findItemMs={13:F3};limitsMs={14:F3};canContinueMs={15:F3};integrityCheckMs={16:F3};" +
            "assignMs={17:F3};weldAmount={18:F2};integrityRatio={19:F3};completed={20};" +
            "distance={21:F1};earlyExit={22}";

        private void EmitServerDoWeldProfile(long profilerTs,
            string blockSubtype, bool projected, bool created, bool welding, bool result,
            double buildMs, double resolveMs, double resolveCoordMs, double resolveLookupMs,
            double stockpileMs, double mountMs, double deformMs,
            double findItemMs, double limitsMs, double canContinueMs, double integrityCheckMs, double assignMs,
            float weldAmount, float integrityRatio, bool completed, double distance,
            string earlyExit)
        {
            if (profilerTs == 0L) return;
            var entityId = _Welder.EntityId;
            MethodProfiler.StopAndLog("ServerDoWeld", profilerTs, () =>
                string.Format(ServerDoWeldProfileFormat,
                    entityId, blockSubtype, projected, created, welding, result,
                    buildMs, resolveMs, resolveCoordMs, resolveLookupMs,
                    stockpileMs, mountMs, deformMs,
                    findItemMs, limitsMs, canContinueMs, integrityCheckMs, assignMs,
                    weldAmount, integrityRatio, completed, distance,
                    earlyExit ?? string.Empty));
        }
    }
}
