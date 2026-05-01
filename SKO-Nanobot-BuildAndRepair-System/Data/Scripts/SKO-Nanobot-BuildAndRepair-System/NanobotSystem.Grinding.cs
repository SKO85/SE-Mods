using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Handlers;
using SKONanobotBuildAndRepairSystem.Helpers;
using SKONanobotBuildAndRepairSystem.Models;
using SKONanobotBuildAndRepairSystem.Profiling;
using SKONanobotBuildAndRepairSystem.Utils;
using System;
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
            // BUG-137: ServerDoGrind invokes engine block-damage / block-removal calls that can
            // spike to 28 ms (profile session 20260430211845: ms=28.299, decreaseMs=28.237 on a
            // LargeEnergyModule going to integrity=0). Pre-fix it ran inside lock(State.PossibleGrindTargets),
            // so the spike blocked async scan publish, IsTargetListSaturated, RebuildHash and
            // every other consumer of the same lock. Capture the chosen target inside the lock,
            // run TryClaimGrindSlot and ServerDoGrind outside. Mirrors the welding-side fix
            // (BUG-135) and accepts the same trade-off: only one grind attempt per call (if the
            // first eligible block fails ServerDoGrind, the next tick tries the next one).
            TargetBlockData chosenGrindTarget = null;
            // BUG-144: lock timing + iteration counters. Mirrors welding's instrumentation
            // (BUG-122 / BUG-131). Verifies Fix A pattern (BUG-137) keeps lock duration
            // tiny under load, and surfaces iteration-cost spikes (e.g. many already-destroyed
            // entries to skip on a heavily-ground grid).
            var skippedByNullBlock = 0;
            var skippedByClosedFatBlock = 0;
            var skippedByGridLimit = 0;
            var skippedByAssign = 0;
            var skippedByScript = 0;
            var skippedByDestroyed = 0;
            var iterationsExamined = 0;
            var loopSkipped = false;
            var tsLockAcquire = 0L;
            var tsInLock = 0L;
            try
            {

            if (State.InventoryFull)
                return;

            if (!PowerHelper.HasRequiredElectricPower(this)) return; //No power -> nothing to do

            var tsBeforeLock = profilerTs != 0L ? Stopwatch.GetTimestamp() : 0L;
            lock (State.PossibleGrindTargets)
            {
                var tsAfterAcquire = profilerTs != 0L ? Stopwatch.GetTimestamp() : 0L;
                if (tsBeforeLock != 0L) tsLockAcquire = tsAfterAcquire - tsBeforeLock;
                // FEAT-076: Skip the full 256-entry iteration when the previous run found
                // nothing grindable (all targets grid-limited, assigned, or destroyed).
                // Resets when: target list hash changes (new scan), or the saturated grid
                // set changes (a BaR on the target grid left, freeing a slot).
                if (_grindLoopExhausted
                    && State.PossibleGrindTargets.CurrentHash == _grindExhaustedAtHash
                    && _gridSaturation.Count == _grindExhaustedSaturatedCount)
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

                    if (Settings.CurrentPickedGrindingBlock == null)
                    {
                        var gridId = targetData.Block.CubeGrid.EntityId;
                        if (IsGridOverSystemLimit(gridId, ref lastRejectedGridId))
                        {
                            skippedByGridLimit++;
                            continue;
                        }
                    }

                    if (Mod.Settings.AssignToSystemEnabled && _Welder.IsWorking && _Welder.Enabled && Settings.CurrentPickedGrindingBlock == null && !targetData.Block.AssignToSystem(_Welder.EntityId))
                    {
                        skippedByAssign++;
                        continue;
                    }

                    if (((Settings.Flags & SyncBlockSettings.Settings.ScriptControlled) != 0) && targetData.Block != Settings.CurrentPickedGrindingBlock)
                    {
                        skippedByScript++;
                        continue;
                    }

                    if (!targetData.Block.IsDestroyed)
                    {
                        needGrinding = true;
                        // BUG-137: capture target, exit lock. TryClaimGrindSlot and ServerDoGrind
                        // run outside so the engine damage call (decreaseMs spike) doesn't hold
                        // the target-list lock.
                        chosenGrindTarget = targetData;
                        break;
                    }
                    else
                    {
                        skippedByDestroyed++;
                    }
                }

                // FEAT-076: Mark exhausted when the full iteration found nothing grindable.
                // The loop will be skipped on subsequent ticks until the target list changes
                // (scan swap updates hash) or the saturated grid set changes (a slot frees up).
                // Note: needGrinding is still inside the lock, so this check correctly stays
                // false ("not exhausted") whenever we found a chosenGrindTarget.
                if (!grinding && !needGrinding)
                {
                    _grindLoopExhausted = true;
                    _grindExhaustedAtHash = State.PossibleGrindTargets.CurrentHash;
                    _grindExhaustedSaturatedCount = _gridSaturation.Count;
                }
                if (tsBeforeLock != 0L) tsInLock = Stopwatch.GetTimestamp() - tsAfterAcquire;
            }

            // BUG-137: deferred grind work. Runs OUTSIDE lock(State.PossibleGrindTargets) so
            // the engine damage / block-removal cost (up to ~28 ms on heavy blocks like
            // LargeEnergyModule) no longer blocks scan publish, IsTargetListSaturated,
            // RebuildHash or any other consumer of the same lock.
            if (chosenGrindTarget != null)
            {
                // OPT 3: Global grind budget — cap ServerDoGrind calls per tick (count + time).
                if (!Mod.TryClaimGrindSlot())
                {
                    if (Mod.Settings.AssignToSystemEnabled) chosenGrindTarget.Block.ReleaseFromSystem();
                }
                else
                {
                    var grindTs = Stopwatch.GetTimestamp();
                    grinding = ServerDoGrind(chosenGrindTarget, out transporting);
                    Mod.ReportGrindTime((Stopwatch.GetTimestamp() - grindTs) * 1000.0 / Stopwatch.Frequency);

                    if (grinding)
                    {
                        currentGrindingBlock = chosenGrindTarget.Block;
                        // Record world position for locality-aware sort in next scan cycle.
                        if (chosenGrindTarget.Block != null && chosenGrindTarget.Block.CubeGrid != null)
                        {
                            _LastGrindWorldPosition = chosenGrindTarget.Block.CubeGrid.GridIntegerToWorld(chosenGrindTarget.Block.Position);
                            _HasLastGrindPosition = true;
                        }
                    }
                    else
                    {
                        // Grinding failed — release assignment regardless of reason so other BaRs aren't starved.
                        if (Mod.Settings.AssignToSystemEnabled)
                        {
                            chosenGrindTarget.Block.ReleaseFromSystem();
                        }
                    }
                }
            }

            // Faction reputation when grinding for not owned grids.
            if (Mod.Settings.DecreaseFactionReputationOnGrinding && currentGrindingBlock != null)
            {
                if (currentGrindingBlock.OwnerId != Welder.OwnerId && currentGrindingBlock.CubeGrid.EntityId != Welder.CubeGrid.EntityId)
                {
                    var ownerId = UtilsPlayer.GetOwner(currentGrindingBlock.CubeGrid as MyCubeGrid);
                    if (ownerId > 0 && ownerId != Welder.OwnerId)
                    {
                        UtilsFaction.DamageReputationWithPlayerFaction(Welder.OwnerId, ownerId);
                    }
                }
            }

            }
            finally
            {
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
                    MethodProfiler.StopAndLog("ServerTryGrinding", profilerTs, () =>
                        string.Format("entityId={0};grinding={1};needGrinding={2};transporting={3};targets={4};currentBlock={5};lockAcquireMs={6:F3};inLockMs={7:F3};iterations={8};skipNull={9};skipClosed={10};skipGrid={11};skipAssign={12};skipScript={13};skipDestroyed={14};loopSkipped={15}",
                            _Welder.EntityId, _grinding, _needGrinding, _transporting, _targetCount,
                            State.CurrentGrindingBlock != null ? State.CurrentGrindingBlock.BlockDefinition.Id.SubtypeName : "none",
                            _lockAcquireMs, _inLockMs, _iterationsExamined, _skippedByNullBlock, _skippedByClosedFatBlock, _skippedByGridLimit, _skippedByAssign, _skippedByScript, _skippedByDestroyed, _loopSkipped));
                }
            }
        }

        private bool ServerDoGrind(TargetBlockData targetData, out bool transporting)
        {
            var profilerTs = MethodProfiler.Start();
            var target = targetData.Block;
            var playTime = MyAPIGateway.Session.ElapsedPlayTime;
            // BUG-103: Move any in-flight items but don't gate the grind on the cosmetic timer.
            // ServerEmptyTransportInventory inside IsTransportRunning already drains items each tick;
            // grinding the next block proceeds without waiting for the timer to elapse. Visual
            // particle still plays via State.Transporting being true while the timer runs.
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
                    return false;
                }
                if ((Settings.GrindJanitorOptions & AutoGrindOptions.HackOnly) != 0 && target.FatBlock != null && integrityRatio < ownershipIntegrityRatio)
                {
                    //Block allready hacked -> stop grinding and switch to next
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

            // Sub-timing: measure each phase to identify spike source.
            // BUG-140: split the old tsDecrease into two engine-call timers so we can
            // see which one dominates the grind spike on large blocks:
            //   tsMountLevel — target.DecreaseMountLevel (damage + integrity update + grid topology)
            //   tsMoveItems  — target.MoveItemsFromConstructionStockpile (drain components into transport inv)
            // Combined decreaseMs in the log = mountLevelMs + moveItemsMs for back-compat reading.
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
                // BUG-106: predict full dismount and gate on the global dismount budget BEFORE
                // calling DecreaseMountLevel. The decreaseMs spike (5-12ms on armor blocks) is
                // entirely in the SE engine cascade triggered when integrity hits 0. Spreading
                // these across ticks avoids compounding when many BaRs grind simultaneously.
                if (integrityRatio <= 0f && !Mod.TryClaimDismountSlot())
                {
                    if (profilerTs != 0L)
                    {
                        var _emptyMsR = tsEmpty * 1000.0 / tsFreq;
                        MethodProfiler.StopAndLog("ServerDoGrind", profilerTs, () =>
                            string.Format("entityId={0};block={1};autoGrind={2};transporting={3};dismounted={4};integrity={5:F1};emptyMs={6:F3};friendlyMs={7:F3};friendlyIter={8};decreaseMs={9:F3};mountLevelMs={10:F3};moveItemsMs={11:F3};dismountCheckMs={12:F3};razeMs={13:F3};mechCheckMs={14:F3};transportMs={15:F3};damage={16:F2};distance={17:F1};transportTimeS={18:F3};earlyExit=dismountSlot",
                                _Welder.EntityId,
                                target != null ? target.BlockDefinition.Id.SubtypeName : "null",
                                (targetData.Attributes & TargetBlockData.AttributeFlags.Autogrind) != 0,
                                false, false, target != null ? target.Integrity / target.MaxIntegrity : 0f,
                                _emptyMsR, 0.0, 0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0,
                                damage, targetData.Distance, 0.0));
                    }
                    return false;
                }

                MyDamageInformation damageInfo = new MyDamageInformation(false, damage, MyDamageType.Grind, _Welder.EntityId);

                if (target.UseDamageSystem)
                {
                    // BUG-130: write once per distinct friendly OWNER instead of once per friendly
                    // BaR. The shared Mod._FriendlyDamageByOwner is owner-keyed, so all BaRs sharing
                    // a welder owner share the same entry. In single-faction worlds this collapses
                    // 174 CDict writes into ~1 dict write, eliminating the 21 ms friendlyMs spikes.
                    // Behavior preserved: read-side IsFriendlyDamage still returns true for the
                    // same set of (block, welder) pairs.
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

                // BUG-140: separate timers for the two engine calls. DecreaseMountLevel does
                // the actual damage application + integrity update + (on hitting 0) grid topology
                // mutation. MoveItemsFromConstructionStockpile drains the consumed components into
                // our transport inventory. Cost split varies by block: heavy mechanical / multi-part
                // blocks make DecreaseMountLevel expensive; component-heavy blocks (refineries,
                // assemblers) push more cost into MoveItemsFromConstructionStockpile.
                // Stopwatch calls gated on profilerTs so they're zero-cost in production.
                tsMark = profilerTs != 0L ? Stopwatch.GetTimestamp() : 0L;
                target.DecreaseMountLevel(damageInfo.Amount, _TransportInventory);
                if (tsMark != 0L) tsMountLevel = Stopwatch.GetTimestamp() - tsMark;

                tsMark = profilerTs != 0L ? Stopwatch.GetTimestamp() : 0L;
                target.MoveItemsFromConstructionStockpile(_TransportInventory);
                if (tsMark != 0L) tsMoveItems = Stopwatch.GetTimestamp() - tsMark;

                // BUG-140: instrument IsFullyDismounted — engine field/method that can re-check
                // multi-part block state. Cheap on simple blocks, worth measuring on large/complex.
                tsMark = profilerTs != 0L ? Stopwatch.GetTimestamp() : 0L;
                var fullyDismounted = target.IsFullyDismounted;
                if (tsMark != 0L) tsDismountCheck = Stopwatch.GetTimestamp() - tsMark;

                if (fullyDismounted)
                {
                    // OPT 1: Mechanical blocks (pistons, rotors, hinges) cause 100-380ms spikes
                    // when destroyed because they detach subgrids. Cap to 1 destruction per tick globally.
                    tsMark = Stopwatch.GetTimestamp();
                    if (target.FatBlock is Sandbox.ModAPI.IMyMechanicalConnectionBlock || target.FatBlock is Sandbox.ModAPI.IMyAttachableTopBlock)
                    {
                        if (!Mod.TryClaimMechanicalGrindSlot())
                        {
                            tsMechCheck = Stopwatch.GetTimestamp() - tsMark;
                            // Log even on early-return so the cost shows up in the profile.
                            if (profilerTs != 0L)
                            {
                                var _emptyMsR = tsEmpty * 1000.0 / tsFreq;
                                var _friendlyMsR = tsFriendly * 1000.0 / tsFreq;
                                var _mountLevelMsR = tsMountLevel * 1000.0 / tsFreq;
                                var _moveItemsMsR = tsMoveItems * 1000.0 / tsFreq;
                                var _dismountCheckMsR = tsDismountCheck * 1000.0 / tsFreq;
                                var _decreaseMsR = _mountLevelMsR + _moveItemsMsR; // back-compat sum
                                var _mechCheckMsR = tsMechCheck * 1000.0 / tsFreq;
                                var _friendlyIterR = friendlyIter;
                                MethodProfiler.StopAndLog("ServerDoGrind", profilerTs, () =>
                                    string.Format("entityId={0};block={1};autoGrind={2};transporting={3};dismounted={4};integrity={5:F1};emptyMs={6:F3};friendlyMs={7:F3};friendlyIter={8};decreaseMs={9:F3};mountLevelMs={10:F3};moveItemsMs={11:F3};dismountCheckMs={12:F3};razeMs={13:F3};mechCheckMs={14:F3};transportMs={15:F3};damage={16:F2};distance={17:F1};transportTimeS={18:F3};earlyExit=mechSlot",
                                        _Welder.EntityId,
                                        target != null ? target.BlockDefinition.Id.SubtypeName : "null",
                                        (targetData.Attributes & TargetBlockData.AttributeFlags.Autogrind) != 0,
                                        false, true, 0f,
                                        _emptyMsR, _friendlyMsR, _friendlyIterR, _decreaseMsR, _mountLevelMsR, _moveItemsMsR, _dismountCheckMsR, 0.0, _mechCheckMsR, 0.0,
                                        damage, targetData.Distance, 0.0));
                            }
                            return false;
                        }
                    }
                    tsMechCheck = Stopwatch.GetTimestamp() - tsMark;

                    tsMark = Stopwatch.GetTimestamp();
                    // BUG-127: defer raze to Mod's batched queue. Drains every
                    // RazeProcessIntervalTicks ticks via IMyCubeGrid.RazeBlocks(positions),
                    // collapsing N physics+integrity recalcs into 1 per grid and shifting
                    // the SE-engine cleanup off the grind tick. SetToConstructionSite call
                    // dropped — no longer needed for batch raze. Block sits at
                    // FatBlock.Closed=false / IsDestroyed=true until the queue drains;
                    // ServerTryGrinding's existing IsDestroyed / Closed skips already filter
                    // it from being re-targeted. tsRaze now reports just the enqueue cost
                    // (sub-microsecond).
                    RazeQueueHandler.Enqueue(target);
                    tsRaze = Stopwatch.GetTimestamp() - tsMark;
                }
            }

            // BUG-124: split tsTransport into 4 sub-timers. Profile session 20260429184659 showed
            // transportMs=19.134 ms on a fully-dismounted LargeBlockArmorBlock, but the existing
            // ServerEmptyTransportInventory profiler caps at 0.189 ms across 2 489 calls — so the
            // 19 ms is NOT in that call. Prime suspect: ComputePosition(target) → target.ComputeWorldCenter
            // is being run on a block that was just removed from its grid 5 lines earlier (line 327).
            // Diagnostic-only this ticket; fix follows once the dominant segment is confirmed.
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
                //Transport started
                tsTransportMark = profilerTs != 0L ? Stopwatch.GetTimestamp() : 0L;
                var transportPos = ComputePosition(target);
                if (tsTransportMark != 0L) tsTransportPos = Stopwatch.GetTimestamp() - tsTransportMark;

                tsTransportMark = profilerTs != 0L ? Stopwatch.GetTimestamp() : 0L;
                State.CurrentTransportIsPick = true;
                State.CurrentTransportIsCollecting = false;
                State.CurrentTransportTarget = transportPos;
                State.CurrentTransportStartTime = playTime;
                State.CurrentTransportTime = TimeSpan.FromSeconds(2d * targetData.Distance / Settings.GrindTransportSpeed);
                if (tsTransportMark != 0L) tsTransportSet = Stopwatch.GetTimestamp() - tsTransportMark;

                tsTransportMark = profilerTs != 0L ? Stopwatch.GetTimestamp() : 0L;
                ServerEmptyTransportInventory(true);
                if (tsTransportMark != 0L) tsTransportEmpty = Stopwatch.GetTimestamp() - tsTransportMark;
                transporting = true;
            }
            tsTransport = Stopwatch.GetTimestamp() - tsMark;

            if (profilerTs != 0L)
            {
                var _transporting = transporting;
                var _emptyMs = tsEmpty * 1000.0 / tsFreq;
                var _friendlyMs = tsFriendly * 1000.0 / tsFreq;
                var _mountLevelMs = tsMountLevel * 1000.0 / tsFreq;
                var _moveItemsMs = tsMoveItems * 1000.0 / tsFreq;
                var _decreaseMs = _mountLevelMs + _moveItemsMs; // back-compat sum
                var _dismountCheckMs = tsDismountCheck * 1000.0 / tsFreq;
                var _razeMs = tsRaze * 1000.0 / tsFreq;
                var _mechCheckMs = tsMechCheck * 1000.0 / tsFreq;
                var _transportMs = tsTransport * 1000.0 / tsFreq;
                var _transportGateMs = tsTransportGate * 1000.0 / tsFreq;
                var _transportPosMs = tsTransportPos * 1000.0 / tsFreq;
                var _transportSetMs = tsTransportSet * 1000.0 / tsFreq;
                var _transportEmptyMs = tsTransportEmpty * 1000.0 / tsFreq;
                var _friendlyIter = friendlyIter;
                var _damage = damage;
                var _distance = targetData.Distance;
                var _transportTimeS = transporting ? State.CurrentTransportTime.TotalSeconds : 0.0;
                MethodProfiler.StopAndLog("ServerDoGrind", profilerTs, () =>
                    string.Format("entityId={0};block={1};autoGrind={2};transporting={3};dismounted={4};integrity={5:F1};emptyMs={6:F3};friendlyMs={7:F3};friendlyIter={8};decreaseMs={9:F3};mountLevelMs={10:F3};moveItemsMs={11:F3};dismountCheckMs={12:F3};razeMs={13:F3};mechCheckMs={14:F3};transportMs={15:F3};transportGateMs={16:F3};transportPosMs={17:F3};transportSetMs={18:F3};transportEmptyMs={19:F3};damage={20:F2};distance={21:F1};transportTimeS={22:F3}",
                        _Welder.EntityId,
                        target != null ? target.BlockDefinition.Id.SubtypeName : "null",
                        (targetData.Attributes & TargetBlockData.AttributeFlags.Autogrind) != 0,
                        _transporting,
                        target != null && target.IsFullyDismounted,
                        target != null ? target.Integrity / target.MaxIntegrity : 0f,
                        _emptyMs, _friendlyMs, _friendlyIter, _decreaseMs, _mountLevelMs, _moveItemsMs, _dismountCheckMs, _razeMs, _mechCheckMs, _transportMs,
                        _transportGateMs, _transportPosMs, _transportSetMs, _transportEmptyMs,
                        _damage, _distance, _transportTimeS));
            }
            return true;
        }
    }
}
