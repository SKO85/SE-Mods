using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Handlers;
using SKONanobotBuildAndRepairSystem.Helpers;
using SKONanobotBuildAndRepairSystem.Models;
using SKONanobotBuildAndRepairSystem.Profiling;
using SKONanobotBuildAndRepairSystem.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using VRage.Game;
using VRage.Game.ModAPI;

namespace SKONanobotBuildAndRepairSystem
{
    public partial class NanobotSystem
    {
        private void ServerTryGrinding(out bool grinding, out bool needGrinding, out bool transporting, out IMySlimBlock currentGrindingBlock)
        {
            var profilerTs = MethodProfiler.Start();
            grinding = false;
            needGrinding = false;
            transporting = false;
            currentGrindingBlock = null;
            // BUG-137: capture target under lock; expensive grind work runs outside.
            TargetBlockData chosenGrindTarget = null;
            // BUG-144: lock timing + iteration counters (mirrors BUG-122/131 in welding).
            var skippedByNullBlock = 0;
            var skippedByClosedFatBlock = 0;
            var skippedByGridLimit = 0;
            var skippedByAssign = 0;
            var skippedByScript = 0;
            var skippedByDestroyed = 0;
            var skippedByRefused = 0;
            var skippedByOutOfArea = 0;
            var iterationsExamined = 0;
            var deadRemoved = 0;
            var grindBudgetDeferred = false;
            var grindProtected = false;
            var grindNoPower = false;
            var loopSkipped = false;
            // FEAT-261123.4: sub-timers for the work outside the target-list lock.
            long tsMark, tsPower = 0L, tsClaim = 0L, tsArea = 0L, tsProtect = 0L, tsSlot = 0L, tsDoGrind = 0L, tsRemoveAll = 0L, tsRelease = 0L, tsRep = 0L;
            var tsLockAcquire = 0L;
            var tsInLock = 0L;
            try
            {

            if (State.InventoryFull)
                return;

            tsMark = profilerTs != 0L ? Stopwatch.GetTimestamp() : 0L;
            var hasPower = PowerHelper.HasRequiredElectricPower(this);
            if (tsMark != 0L) tsPower += Stopwatch.GetTimestamp() - tsMark;
            if (!hasPower) { grindNoPower = true; return; } //No power -> nothing to do

            var tsBeforeLock = profilerTs != 0L ? Stopwatch.GetTimestamp() : 0L;
            lock (State.PossibleGrindTargets)
            {
                var tsAfterAcquire = profilerTs != 0L ? Stopwatch.GetTimestamp() : 0L;
                if (tsBeforeLock != 0L) tsLockAcquire = tsAfterAcquire - tsBeforeLock;
                // FEAT-076: skip iteration when previous run found nothing grindable;
                // resets on target-list hash change or saturated-grid set change.
                // BUG-260511.16: also retry after ExhaustedRetryInterval so BaRs notice
                // claims expiring in BlockSystemAssigningHandler (invisible to the
                // hash / saturation guards). Without this, exhausted BaRs stay stuck
                // until the next cluster scan bumps the hash, even though slots free
                // up continuously as TTL claims drain.
                // FEAT-261123.6: grids refused by a shield / protection mod, for the terminal (before the exhausted skip).
                State.GrindBlockedGrids = _grindRefusedGrids.Count > 0 ? CountGrindRefused() : 0;

                var grindExhaustedAge = Mod.NowPlayTime.Subtract(_grindExhaustedAtTime);
                if (_grindLoopExhausted
                    && State.PossibleGrindTargets.CurrentHash == _grindExhaustedAtHash
                    && _gridSaturation.Count == _grindExhaustedSaturatedCount
                    && grindExhaustedAge < ExhaustedRetryInterval)
                {
                    loopSkipped = true;
                    if (tsBeforeLock != 0L) tsInLock = Stopwatch.GetTimestamp() - tsAfterAcquire;
                    return;
                }
                _grindLoopExhausted = false;

                long lastRejectedGridId = 0;
                foreach (var targetData in State.PossibleGrindTargets)
                {
                    iterationsExamined++;
                    if (targetData.Block == null) { skippedByNullBlock++; continue; }
                    if (targetData.Block.CubeGrid == null) { skippedByNullBlock++; continue; }
                    if (targetData.Block.FatBlock != null && targetData.Block.FatBlock.Closed) { skippedByClosedFatBlock++; continue; }

                    // BUG-261122.15: grinding this grid was refused (protection plugin / mod) — skip it for a while.
                    if (_grindRefusedGrids.Count > 0 && IsGrindRefused(targetData.Block.CubeGrid.EntityId)) { skippedByRefused++; continue; }

                    if (Settings.CurrentPickedGrindingBlock == null)
                    {
                        var gridId = targetData.Block.CubeGrid.EntityId;
                        if (IsGridOverSystemLimit(gridId, ref lastRejectedGridId))
                        {
                            skippedByGridLimit++;
                            continue;
                        }
                    }

                    // Check-only (no claim) during iteration: skip blocks already
                    // claimed by ANOTHER BaR. We must NOT call AssignToSystem here
                    // because it would claim every block we iterate past on the way
                    // to the chosen one — one BaR's single picker call would leak
                    // claims onto many blocks (skeleton orphans, blocks past the
                    // first valid target, etc.), blocking other BaRs from grabbing
                    // them for the next AssignmentTtlSeconds. Mirrors ServerTryWelding's
                    // two-phase IsAssignedToOtherSystem-then-AssignToSystem pattern.
                    if (Mod.Settings.AssignToSystemEnabled
                        && Settings.CurrentPickedGrindingBlock == null
                        && targetData.Block.IsAssignedToOtherSystem(_Welder.EntityId))
                    {
                        skippedByAssign++;
                        continue;
                    }

                    if (((Settings.Flags & SyncBlockSettings.Settings.ScriptControlled) != 0) && targetData.Block != Settings.CurrentPickedGrindingBlock)
                    {
                        skippedByScript++;
                        continue;
                    }

                    // IsDestroyed is true as soon as Integrity reaches 0, but the
                    // construction stockpile can still contain components — wheels and
                    // other attachable blocks frequently end up in that state. Using
                    // IsFullyDismounted instead means we keep grinding the block until
                    // both integrity AND components are stripped, so we don't orphan
                    // partially-dismounted blocks on the grid forever.
                    if (!targetData.Block.IsFullyDismounted)
                    {
                        needGrinding = true;
                        // BUG-137: capture target; ServerDoGrind runs outside the lock.
                        chosenGrindTarget = targetData;
                        break;
                    }
                    else
                    {
                        skippedByDestroyed++;
                        // Fully-dismounted block (integrity 0 AND stockpile empty) that
                        // is still on the grid — usually a skeleton wheel / attachable
                        // left over from damage events, or a block dismounted by a
                        // previous BaR before this one's first scan picked it up.
                        // Enqueue for raze (queue itself dedups) so the cleanup path
                        // removes it; ServerDoGrind isn't called for these so the
                        // existing post-grind enqueue can't reach them.
                        RazeQueueHandler.Enqueue(targetData.Block);
                    }
                }

                // BUG-261123.3: drop ground / removed entries once instead of re-walking them every tick (one in-place pass,
                // only when the walk met one; razes are queued, the queue dedups).
                if (skippedByNullBlock + skippedByClosedFatBlock + skippedByDestroyed > 0)
                {
                    deadRemoved = RemoveDeadGrindEntries(State.PossibleGrindTargets);
                    if (deadRemoved > 0) State.PossibleGrindTargets.RebuildHash();
                }

                // FEAT-076: mark exhausted when the full iteration found nothing grindable.
                if (!grinding && !needGrinding)
                {
                    _grindLoopExhausted = true;
                    _grindExhaustedAtHash = State.PossibleGrindTargets.CurrentHash;
                    _grindExhaustedSaturatedCount = _gridSaturation.Count;
                    _grindExhaustedAtTime = Mod.NowPlayTime;
                }
                if (tsBeforeLock != 0L) tsInLock = Stopwatch.GetTimestamp() - tsAfterAcquire;
            }

            // BUG-137: deferred grind work runs outside the target-list lock.
            if (chosenGrindTarget != null)
            {
                // Claim the chosen target (the matching read in the picker only
                // checked IsAssignedToOtherSystem). If another BaR raced in between
                // the iteration's read and now, AssignToSystem fails and we drop
                // the target for this cycle — next cycle's picker will try again.
                tsMark = profilerTs != 0L ? Stopwatch.GetTimestamp() : 0L;
                if (Mod.Settings.AssignToSystemEnabled
                    && _Welder.IsWorking
                    && _Welder.Enabled
                    && Settings.CurrentPickedGrindingBlock == null)
                {
                    if (!chosenGrindTarget.Block.AssignToSystem(_Welder.EntityId))
                    {
                        skippedByAssign++;
                        chosenGrindTarget = null;
                    }
                }
                if (tsMark != 0L) tsClaim += Stopwatch.GetTimestamp() - tsMark;
            }
            // BUG-261122.16: the area check is scan-time only — a target whose grid moved (ship flying off, ground-off
            // piece flung away) is dropped from this BaR's list instead of being ground from afar.
            tsMark = profilerTs != 0L ? Stopwatch.GetTimestamp() : 0L;
            var outOfArea = chosenGrindTarget != null && !IsTargetInArea(chosenGrindTarget.Block);
            if (tsMark != 0L) tsArea += Stopwatch.GetTimestamp() - tsMark;
            if (outOfArea)
            {
                ReleaseAssignmentIfEnabled(chosenGrindTarget.Block);
                lock (State.PossibleGrindTargets)
                {
                    if (State.PossibleGrindTargets.Remove(chosenGrindTarget)) State.PossibleGrindTargets.RebuildHash();
                }
                chosenGrindTarget = null;
                skippedByOutOfArea++;
            }
            // BUG-260612.13: zones/shields are otherwise enforced only at scan time —
            // a shield raised or zone enabled around the target mid-operation kept it
            // being ground off the stale list for up to a scan cycle. Both checks are
            // cheap here (DS dictionary lookup; zone verdict cached 15 s per block).
            tsMark = profilerTs != 0L ? Stopwatch.GetTimestamp() : 0L;
            var isProtected = chosenGrindTarget != null
                && (IsShieldProtected(chosenGrindTarget.Block)
                    || SafeZoneHandler.IsProtectedFromGrinding(chosenGrindTarget.Block, _Welder));
            if (tsMark != 0L) tsProtect += Stopwatch.GetTimestamp() - tsMark;
            if (isProtected)
            {
                grindProtected = true;
                ReleaseAssignmentIfEnabled(chosenGrindTarget.Block);
                chosenGrindTarget = null;
            }

            if (chosenGrindTarget != null)
            {
                // OPT 3: Global grind budget — cap ServerDoGrind calls per tick (count + time).
                tsMark = profilerTs != 0L ? Stopwatch.GetTimestamp() : 0L;
                var gotSlot = Mod.TryClaimGrindSlot();
                if (tsMark != 0L) tsSlot += Stopwatch.GetTimestamp() - tsMark;
                if (!gotSlot)
                {
                    grindBudgetDeferred = true;
                    // BUG-261023.8: budget deferral, not a failure — keep the claim and report
                    // the block (like BUG-154 for welding) so the current block / grid count
                    // don't flicker and no other BaR takes it between ticks.
                    needGrinding = true;
                    currentGrindingBlock = chosenGrindTarget.Block;
                }
                else
                {
                    var grindTs = Stopwatch.GetTimestamp();
                    _grindJanitorDone = false;
                    grinding = ServerDoGrind(chosenGrindTarget, out transporting);
                    var grindTicks = Stopwatch.GetTimestamp() - grindTs;
                    Mod.ReportGrindTime(grindTicks * 1000.0 / Stopwatch.Frequency);
                    if (profilerTs != 0L) tsDoGrind += grindTicks;

                    if (grinding)
                    {
                        currentGrindingBlock = chosenGrindTarget.Block;
                        // Record world position for locality-aware sort in next scan cycle.
                        if (chosenGrindTarget.Block != null && chosenGrindTarget.Block.CubeGrid != null)
                        {
                            _LastGrindWorldPosition = chosenGrindTarget.Block.CubeGrid.GridIntegerToWorld(chosenGrindTarget.Block.Position);
                            _HasLastGrindPosition = true;
                        }

                        // BUG-165: drop razed / fully-dismounted blocks immediately so
                        // future ticks skip them. Use IsFullyDismounted (not IsDestroyed)
                        // so partially-dismounted blocks (integrity 0 with components
                        // still in the stockpile) stay in the list for further grinding.
                        var grindBlock = chosenGrindTarget.Block;
                        if (grindBlock != null && grindBlock.IsFullyDismounted)
                        {
                            // BUG-261123.7: own list only; cluster members drop it on their next walk (BUG-261123.3).
                            tsMark = profilerTs != 0L ? Stopwatch.GetTimestamp() : 0L;
                            RemoveGrindTarget(chosenGrindTarget);
                            if (tsMark != 0L) tsRemoveAll += Stopwatch.GetTimestamp() - tsMark;
                        }
                    }
                    else
                    {
                        // BUG-260612.10: a janitor target ground to its Disable/Hack
                        // threshold is FINISHED, not failed — leaving it listed made the
                        // picker re-claim it and burn a shared grind slot every tick
                        // until the next scan evicted it.
                        tsMark = profilerTs != 0L ? Stopwatch.GetTimestamp() : 0L;
                        if (_grindJanitorDone)
                        {
                            RemoveGrindTargetEverywhere(chosenGrindTarget);
                        }

                        // Grinding failed — release assignment regardless of reason so other BaRs aren't starved.
                        ReleaseAssignmentIfEnabled(chosenGrindTarget.Block);
                        if (tsMark != 0L) tsRelease += Stopwatch.GetTimestamp() - tsMark;
                    }
                }
            }

            // Faction reputation when grinding for not owned grids.
            // BUG-260612.34: CubeGrid null-guarded like the same block's check above.
            tsMark = profilerTs != 0L ? Stopwatch.GetTimestamp() : 0L;
            if (Mod.Settings.DecreaseFactionReputationOnGrinding && currentGrindingBlock != null && currentGrindingBlock.CubeGrid != null)
            {
                if (currentGrindingBlock.OwnerId != Welder.OwnerId && currentGrindingBlock.CubeGrid.EntityId != Welder.CubeGrid.EntityId)
                {
                    var ownerId = UtilsPlayer.GetOwner(currentGrindingBlock.CubeGrid as MyCubeGrid);
                    if (ownerId > 0 && ownerId != Welder.OwnerId)
                    {
                        UtilsFaction.QueueReputationDamage(Welder.OwnerId, ownerId); // FEAT-261123.5: applied in batches
                    }
                }
            }
            if (tsMark != 0L) tsRep += Stopwatch.GetTimestamp() - tsMark;

            }
            finally
            {
                // FEAT-261123.4: remember why this attempt did nothing.
                if (!grinding && !transporting)
                    _lastGrindIdle = Handlers.CycleStats.GrindIdleReason(skippedByRefused, grindProtected, grindBudgetDeferred,
                        skippedByGridLimit, skippedByAssign, loopSkipped, grindNoPower, skippedByOutOfArea);
                if (profilerTs != 0L)
                {
                    var _grinding = grinding;
                    var _needGrinding = needGrinding;
                    var _transporting = transporting;
                    var _targetCount = State.PossibleGrindTargets.CurrentCount;
                    var tsFreq = Stopwatch.Frequency;
                    var _lockAcquireMs = tsLockAcquire * 1000.0 / tsFreq;
                    var _inLockMs = tsInLock * 1000.0 / tsFreq;
                    var _iterationsExamined = iterationsExamined;
                    var _skippedByNullBlock = skippedByNullBlock;
                    var _skippedByClosedFatBlock = skippedByClosedFatBlock;
                    var _skippedByGridLimit = skippedByGridLimit;
                    var _skippedByAssign = skippedByAssign;
                    var _skippedByScript = skippedByScript;
                    var _skippedByDestroyed = skippedByDestroyed;
                    var _loopSkipped = loopSkipped;
                    var _skippedByRefused = skippedByRefused;
                    var _skippedByOutOfArea = skippedByOutOfArea;
                    var _deadRemoved = deadRemoved;
                    var _powerMs = tsPower * 1000.0 / tsFreq;
                    var _claimMs = tsClaim * 1000.0 / tsFreq;
                    var _areaMs = tsArea * 1000.0 / tsFreq;
                    var _protectMs = tsProtect * 1000.0 / tsFreq;
                    var _slotMs = tsSlot * 1000.0 / tsFreq;
                    var _doGrindMs = tsDoGrind * 1000.0 / tsFreq;
                    var _removeAllMs = tsRemoveAll * 1000.0 / tsFreq;
                    var _releaseMs = tsRelease * 1000.0 / tsFreq;
                    var _repMs = tsRep * 1000.0 / tsFreq;
                    MethodProfiler.StopAndLog("ServerTryGrinding", profilerTs, () =>
                        string.Format("entityId={0};grinding={1};needGrinding={2};transporting={3};targets={4};currentBlock={5};lockAcquireMs={6:F3};inLockMs={7:F3};iterations={8};skipNull={9};skipClosed={10};skipGrid={11};skipAssign={12};skipScript={13};skipDestroyed={14};loopSkipped={15};skipRefused={16};droppedOutOfArea={17};deadRemoved={18};powerMs={19:F3};claimMs={20:F3};areaMs={21:F3};protectMs={22:F3};slotMs={23:F3};doGrindMs={24:F3};removeEverywhereMs={25:F3};releaseMs={26:F3};reputationMs={27:F3}",
                            _Welder.EntityId, _grinding, _needGrinding, _transporting, _targetCount,
                            State.CurrentGrindingBlock != null ? State.CurrentGrindingBlock.BlockDefinition.Id.SubtypeName : "none",
                            _lockAcquireMs, _inLockMs, _iterationsExamined, _skippedByNullBlock, _skippedByClosedFatBlock, _skippedByGridLimit, _skippedByAssign, _skippedByScript, _skippedByDestroyed, _loopSkipped, _skippedByRefused, _skippedByOutOfArea, _deadRemoved,
                            _powerMs, _claimMs, _areaMs, _protectMs, _slotMs, _doGrindMs, _removeAllMs, _releaseMs, _repMs));
                }
            }
        }

        // BUG-261123.3: in-place compaction (List.RemoveAll needs System.Predicate, prohibited in the SE sandbox).
        internal static int RemoveDeadGrindEntries(List<TargetBlockData> list)
        {
            var write = 0;
            for (var read = 0; read < list.Count; read++)
            {
                var entry = list[read];
                if (IsDeadGrindEntry(entry)) continue;
                if (write != read) list[write] = entry;
                write++;
            }
            var removed = list.Count - write;
            if (removed > 0) list.RemoveRange(write, removed);
            return removed;
        }

        // BUG-261123.3: a grind entry nothing more can be done with (block gone, or fully dismounted: raze queued).
        internal static bool IsDeadGrindEntry(TargetBlockData targetData)
        {
            var block = targetData != null ? targetData.Block : null;
            if (block == null || block.CubeGrid == null) return true;
            if (block.FatBlock != null && block.FatBlock.Closed) return true;
            if (!block.IsFullyDismounted) return false;
            RazeQueueHandler.Enqueue(block);
            return true;
        }

        // BUG-261122.15: grids whose grind a damage handler refused (protection plugin / mod), per BaR, main thread.
        private readonly Dictionary<long, TimeSpan> _grindRefusedGrids = new Dictionary<long, TimeSpan>();
        private static readonly TimeSpan GrindRefusedSkip = TimeSpan.FromSeconds(30);

        /// <summary>
        /// BUG-261122.15: vanilla grinders raise BeforeDamage so plugins / mods can veto or reduce grinding; the
        /// original mod could not ("not available in modding"). False when the damage was zeroed: that grid is
        /// then skipped by this BaR for GrindRefusedSkip (one event per grid per period, not one per tick).
        /// </summary>
        private bool RaiseGrindDamage(IMySlimBlock target, ref MyDamageInformation info)
        {
            var damageSystem = MyAPIGateway.Session.DamageSystem;
            if (damageSystem == null) return true;
            damageSystem.RaiseBeforeDamageApplied(target, ref info);
            if (info.Amount > 0f) return true;
            if (target.CubeGrid != null) _grindRefusedGrids[target.CubeGrid.EntityId] = Mod.NowPlayTime + GrindRefusedSkip;
            return false;
        }

        private int CountGrindRefused()
        {
            var now = Mod.NowPlayTime;
            var count = 0;
            foreach (var until in _grindRefusedGrids.Values) if (now < until) count++;
            return count;
        }

        private bool IsGrindRefused(long gridId)
        {
            TimeSpan until;
            if (!_grindRefusedGrids.TryGetValue(gridId, out until)) return false;
            if (Mod.NowPlayTime < until) return true;
            _grindRefusedGrids.Remove(gridId);
            return false;
        }

        /// <summary>
        /// Drops a finished grind target from this BaR's list and, via
        /// BUG-260511.19's removal propagation, from every cluster member's list so
        /// pickers stop walking it between scans. Used for (BUG-260612.10) janitor
        /// targets that reached their threshold: members cannot tell those are done.
        /// </summary>
        private void RemoveGrindTargetEverywhere(TargetBlockData targetData)
        {
            RemoveGrindTarget(targetData);

            var grindBlock = targetData.Block;
            var cluster = AssignedCluster;
            if (cluster != null && grindBlock != null && grindBlock.CubeGrid != null)
            {
                var gid = grindBlock.CubeGrid.EntityId;
                var pos = grindBlock.Position;
                var members = cluster.Members;
                for (int m = 0; m < members.Count; m++)
                {
                    var other = members[m];
                    if (other != null && other != this)
                    {
                        other.RemoveGrindTargetByPosition(gid, pos);
                    }
                }
            }
        }

        /// <summary>
        /// Drops a finished grind target from this BaR's list only. Enough for fully dismounted blocks: other members
        /// see them as dead and drop them on their next walk (BUG-261123.3), so scanning every member's list per ground
        /// block (cost grows with cluster size) is not needed (BUG-261123.7).
        /// </summary>
        private void RemoveGrindTarget(TargetBlockData targetData)
        {
            lock (State.PossibleGrindTargets)
            {
                if (State.PossibleGrindTargets.Remove(targetData))
                {
                    // BUG-260610.33: RebuildHash so CurrentCount tracks the removal.
                    State.PossibleGrindTargets.RebuildHash();
                }
            }
        }

        private bool ServerDoGrind(TargetBlockData targetData, out bool transporting)
        {
            var profilerTs = MethodProfiler.Start();
            var target = targetData.Block;
            var playTime = MyAPIGateway.Session.ElapsedPlayTime;
            // BUG-103: don't gate grind on the cosmetic transport timer.
            transporting = IsTransportRunning(playTime);

            var targetGrid = target.CubeGrid;
            if (targetGrid == null) return false;

            if (targetGrid.Physics == null || !targetGrid.Physics.Enabled) return false;            

            var criticalIntegrityRatio = ((MyCubeBlockDefinition)target.BlockDefinition).CriticalIntegrityRatio;
            var ownershipIntegrityRatio = ((MyCubeBlockDefinition)target.BlockDefinition).OwnershipIntegrityRatio > 0 ? ((MyCubeBlockDefinition)target.BlockDefinition).OwnershipIntegrityRatio : criticalIntegrityRatio;
            var integrityRatio = target.Integrity / target.MaxIntegrity;

            if ((targetData.Attributes & TargetBlockData.AttributeFlags.Autogrind) != 0)
            {
                if ((Settings.GrindJanitorOptions & AutoGrindOptions.DisableOnly) != 0 && target.FatBlock != null && integrityRatio < criticalIntegrityRatio)
                {
                    //Block allready out of order -> stop grinding and switch to next
                    _grindJanitorDone = true; // BUG-260612.10: finished, not failed.
                    return false;
                }
                if ((Settings.GrindJanitorOptions & AutoGrindOptions.HackOnly) != 0 && target.FatBlock != null && integrityRatio < ownershipIntegrityRatio)
                {
                    //Block allready hacked -> stop grinding and switch to next
                    _grindJanitorDone = true; // BUG-260612.10: finished, not failed.
                    return false;
                }
            }

            var disassembleRatio = target.FatBlock != null ? target.FatBlock.DisassembleRatio : ((MyCubeBlockDefinition)target.BlockDefinition).DisassembleRatio;
            if (disassembleRatio <= 0f) return false;
            var integrityPointsPerSec = ((MyCubeBlockDefinition)target.BlockDefinition).IntegrityPointsPerSec;
            if (target.MaxIntegrity <= 0f) return false;

            float damage = MyAPIGateway.Session.GrinderSpeedMultiplier * Mod.Settings.Welder.GrindingMultiplier * GRINDER_AMOUNT_PER_SECOND;
            var grinderAmount = damage * integrityPointsPerSec / disassembleRatio;
            integrityRatio = (target.Integrity - grinderAmount) / target.MaxIntegrity;

            if ((targetData.Attributes & TargetBlockData.AttributeFlags.Autogrind) != 0)
            {
                if ((Settings.GrindJanitorOptions & AutoGrindOptions.DisableOnly) != 0 && integrityRatio < criticalIntegrityRatio)
                {
                    //Grind only down to critical ratio not further
                    grinderAmount = target.Integrity - (0.9f * criticalIntegrityRatio * target.MaxIntegrity);
                    damage = grinderAmount * disassembleRatio / integrityPointsPerSec;
                    integrityRatio = criticalIntegrityRatio;
                }
                else if ((Settings.GrindJanitorOptions & AutoGrindOptions.HackOnly) != 0 && integrityRatio < ownershipIntegrityRatio)
                {
                    //Grind only down to ownership ratio not further
                    grinderAmount = target.Integrity - (0.9f * ownershipIntegrityRatio * target.MaxIntegrity);
                    damage = grinderAmount * disassembleRatio / integrityPointsPerSec;
                    integrityRatio = ownershipIntegrityRatio;
                }
            }

            var emptying = false;
            bool isEmpty = false;

            // BUG-140: split tsDecrease into MountLevel vs MoveItems sub-timers.
            var tsFreq = Stopwatch.Frequency;
            var tsEmpty = 0L;
            var tsMountLevel = 0L;
            var tsMoveItems = 0L;
            var tsRaze = 0L;
            var tsTransport = 0L;
            var tsFriendly = 0L;
            var tsMechCheck = 0L;
            var tsDismountCheck = 0L;
            var friendlyIter = 0;
            long tsMark;

            tsMark = Stopwatch.GetTimestamp();
            if (integrityRatio <= 0.2)
            {
                //Try to emtpy inventory (if any)
                if (target.FatBlock != null && target.FatBlock.HasInventory)
                {
                    emptying = EmptyBlockInventories(target.FatBlock, _TransportInventory, out isEmpty);
                }
            }
            tsEmpty = Stopwatch.GetTimestamp() - tsMark;

            if (!emptying || isEmpty)
            {
                MyDamageInformation damageInfo = new MyDamageInformation(false, damage, MyDamageType.Grind, _Welder.EntityId);

                // BUG-261122.15: raise BeforeDamage like vanilla grinders, so protection plugins / mods can veto it.
                if (target.UseDamageSystem && !RaiseGrindDamage(target, ref damageInfo))
                {
                    EmitServerDoGrindProfile(profilerTs,
                        target.BlockDefinition.Id.SubtypeName,
                        (targetData.Attributes & TargetBlockData.AttributeFlags.Autogrind) != 0,
                        false, false,
                        target.Integrity / target.MaxIntegrity,
                        tsEmpty * 1000.0 / tsFreq, 0.0, 0,
                        0.0, 0.0, 0.0,
                        0.0, 0.0, 0.0,
                        0.0, 0.0, 0.0, 0.0,
                        damage, targetData.Distance, 0.0,
                        "grindRefused");
                    Mod.RefundGrindSlot();
                    return false;
                }

                // BUG-106: gate full dismount on the global budget before DecreaseMountLevel.
                if (integrityRatio <= 0f && !Mod.TryClaimDismountSlot())
                {
                    EmitServerDoGrindProfile(profilerTs,
                        target != null ? target.BlockDefinition.Id.SubtypeName : "null",
                        (targetData.Attributes & TargetBlockData.AttributeFlags.Autogrind) != 0,
                        false, false,
                        target != null ? target.Integrity / target.MaxIntegrity : 0f,
                        tsEmpty * 1000.0 / tsFreq, 0.0, 0,
                        0.0, 0.0, 0.0,
                        0.0, 0.0, 0.0,
                        0.0, 0.0, 0.0, 0.0,
                        damage, targetData.Distance, 0.0,
                        "dismountSlot");
                    // BUG-260612.11: no grind ran — return the slot claimed by the
                    // caller so dismount-gate bounces don't drain the fleet budget.
                    Mod.RefundGrindSlot();
                    return false;
                }

                if (target.UseDamageSystem)
                {
                    // BUG-130: owner-keyed friendly-damage map (one write per distinct owner).
                    tsMark = Stopwatch.GetTimestamp();
                    System.Collections.Generic.List<long> friendlyOwners;
                    if (Mod.TryGetFriendlyOwnersForOwner(_Welder.OwnerId, out friendlyOwners) && friendlyOwners != null)
                    {
                        var deadline = MyAPIGateway.Session.ElapsedPlayTime + Mod.Settings.FriendlyDamageTimeout;
                        for (var i = 0; i < friendlyOwners.Count; i++)
                        {
                            friendlyIter++;
                            Mod.MarkFriendlyDamage(friendlyOwners[i], target, deadline);
                        }
                    }
                    tsFriendly = Stopwatch.GetTimestamp() - tsMark;
                }

                // BUG-140: timers for DecreaseMountLevel and MoveItemsFromConstructionStockpile.
                tsMark = profilerTs != 0L ? Stopwatch.GetTimestamp() : 0L;
                target.DecreaseMountLevel(damageInfo.Amount, _TransportInventory);
                if (tsMark != 0L) tsMountLevel = Stopwatch.GetTimestamp() - tsMark;

                tsMark = profilerTs != 0L ? Stopwatch.GetTimestamp() : 0L;
                target.MoveItemsFromConstructionStockpile(_TransportInventory);
                if (tsMark != 0L) tsMoveItems = Stopwatch.GetTimestamp() - tsMark;

                // BUG-140: instrument IsFullyDismounted.
                tsMark = profilerTs != 0L ? Stopwatch.GetTimestamp() : 0L;
                var fullyDismounted = target.IsFullyDismounted;
                if (tsMark != 0L) tsDismountCheck = Stopwatch.GetTimestamp() - tsMark;

                if (fullyDismounted)
                {
                    // BUG-260612.20: the mechanical one-per-tick cap (OPT 1) moved into
                    // RazeQueueHandler.Process, where the expensive raze actually runs.
                    // Failing the grind HERE — after DecreaseMountLevel already did the
                    // work — reported a completed grind as failure: state flicker,
                    // assignment release, and the list removal + cluster propagation
                    // were skipped for exactly the expensive block class.
                    tsMark = Stopwatch.GetTimestamp();
                    // BUG-127: defer raze to the batched RazeQueueHandler.
                    RazeQueueHandler.Enqueue(target);
                    tsRaze = Stopwatch.GetTimestamp() - tsMark;
                }
            }

            // BUG-124: split tsTransport into Gate/Pos/Set/Empty sub-timers.
            tsMark = Stopwatch.GetTimestamp();
            var tsTransportGate = 0L;
            var tsTransportPos = 0L;
            var tsTransportSet = 0L;
            var tsTransportEmpty = 0L;
            long tsTransportMark;
            tsTransportMark = profilerTs != 0L ? Stopwatch.GetTimestamp() : 0L;
            var transportGateOpen = (float)_TransportInventory.CurrentVolume >= _MaxGrindTransportVolume || target.IsFullyDismounted;
            if (tsTransportMark != 0L) tsTransportGate = Stopwatch.GetTimestamp() - tsTransportMark;
            if (transportGateOpen)
            {
                // Multi-grind: a previous block's cosmetic transport may still be
                // animating this cycle. Resetting StartTime here would keep the timer
                // pinned to "now" so IsTransportRunning never elapses, and the player
                // sees particles leave the target but never arrive. Only seed a fresh
                // transport when none is in flight.
                var transportAlreadyRunning = State.CurrentTransportStartTime > TimeSpan.Zero;

                if (!transportAlreadyRunning)
                {
                    tsTransportMark = profilerTs != 0L ? Stopwatch.GetTimestamp() : 0L;
                    var transportPos = ComputePosition(target);
                    if (tsTransportMark != 0L) tsTransportPos = Stopwatch.GetTimestamp() - tsTransportMark;

                    tsTransportMark = profilerTs != 0L ? Stopwatch.GetTimestamp() : 0L;
                    State.CurrentTransportIsPick = true;
                    State.CurrentTransportIsCollecting = false;
                    State.CurrentTransportTarget = transportPos;
                    State.CurrentTransportStartTime = playTime;
                    State.CurrentTransportTime = TransportTime(targetData.Distance, Settings.GrindTransportSpeed);
                    if (tsTransportMark != 0L) tsTransportSet = Stopwatch.GetTimestamp() - tsTransportMark;
                }

                // Inventory transfer happens every time the gate opens, independent
                // of the cosmetic timer — otherwise the BaR fills up and stalls.
                tsTransportMark = profilerTs != 0L ? Stopwatch.GetTimestamp() : 0L;
                ServerEmptyTransportInventory(true);
                if (tsTransportMark != 0L) tsTransportEmpty = Stopwatch.GetTimestamp() - tsTransportMark;
                transporting = true;
            }
            tsTransport = Stopwatch.GetTimestamp() - tsMark;

            EmitServerDoGrindProfile(profilerTs,
                target != null ? target.BlockDefinition.Id.SubtypeName : "null",
                (targetData.Attributes & TargetBlockData.AttributeFlags.Autogrind) != 0,
                transporting,
                target != null && target.IsFullyDismounted,
                target != null ? target.Integrity / target.MaxIntegrity : 0f,
                tsEmpty * 1000.0 / tsFreq,
                tsFriendly * 1000.0 / tsFreq,
                friendlyIter,
                tsMountLevel * 1000.0 / tsFreq,
                tsMoveItems * 1000.0 / tsFreq,
                tsDismountCheck * 1000.0 / tsFreq,
                tsRaze * 1000.0 / tsFreq,
                tsMechCheck * 1000.0 / tsFreq,
                tsTransport * 1000.0 / tsFreq,
                tsTransportGate * 1000.0 / tsFreq,
                tsTransportPos * 1000.0 / tsFreq,
                tsTransportSet * 1000.0 / tsFreq,
                tsTransportEmpty * 1000.0 / tsFreq,
                damage,
                targetData.Distance,
                transporting ? State.CurrentTransportTime.TotalSeconds : 0.0,
                null);
            return true;
        }

        // Single source of truth for the ServerDoGrind profile format.
        // Early-exit callers pass 0.0 for unmeasured timers and a non-empty earlyExit reason.
        private const string ServerDoGrindProfileFormat =
            "entityId={0};block={1};autoGrind={2};transporting={3};dismounted={4};integrity={5:F1};" +
            "emptyMs={6:F3};friendlyMs={7:F3};friendlyIter={8};decreaseMs={9:F3};" +
            "mountLevelMs={10:F3};moveItemsMs={11:F3};dismountCheckMs={12:F3};" +
            "razeMs={13:F3};mechCheckMs={14:F3};transportMs={15:F3};" +
            "transportGateMs={16:F3};transportPosMs={17:F3};transportSetMs={18:F3};transportEmptyMs={19:F3};" +
            "damage={20:F2};distance={21:F1};transportTimeS={22:F3};earlyExit={23}";

        private void EmitServerDoGrindProfile(long profilerTs,
            string blockSubtype, bool autoGrind, bool transporting, bool dismounted, float integrity,
            double emptyMs, double friendlyMs, int friendlyIter,
            double mountLevelMs, double moveItemsMs, double dismountCheckMs,
            double razeMs, double mechCheckMs, double transportMs,
            double transportGateMs, double transportPosMs, double transportSetMs, double transportEmptyMs,
            float damage, double distance, double transportTimeS,
            string earlyExit)
        {
            if (profilerTs == 0L) return;
            var entityId = _Welder.EntityId;
            var decreaseMs = mountLevelMs + moveItemsMs; // back-compat sum
            MethodProfiler.StopAndLog("ServerDoGrind", profilerTs, () =>
                string.Format(ServerDoGrindProfileFormat,
                    entityId, blockSubtype, autoGrind, transporting, dismounted, integrity,
                    emptyMs, friendlyMs, friendlyIter, decreaseMs,
                    mountLevelMs, moveItemsMs, dismountCheckMs,
                    razeMs, mechCheckMs, transportMs,
                    transportGateMs, transportPosMs, transportSetMs, transportEmptyMs,
                    damage, distance, transportTimeS,
                    earlyExit ?? string.Empty));
        }
    }
}
