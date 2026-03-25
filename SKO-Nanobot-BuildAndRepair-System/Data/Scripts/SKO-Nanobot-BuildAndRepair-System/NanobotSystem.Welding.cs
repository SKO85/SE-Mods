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
using VRage;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;
using static SKONanobotBuildAndRepairSystem.Utils.UtilsInventory;

namespace SKONanobotBuildAndRepairSystem
{
    public partial class NanobotSystem
    {
        private void ServerTryWelding(out bool welding, out bool needWelding, out bool transporting, out IMySlimBlock currentWeldingBlock)
        {
            var profilerTs = MethodProfiler.Start();
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
            var componentFailures = 0;
            var starvedPriorityBits = 0L;
            var lastFailPriority = -1;
            var consecutiveAtPriority = 0;
            var starvedSkipped = 0;
            var totalComponentChecks = 0;
            var lookingForNextChecked = 0;
            var lockOnFound = false;
            var weldSkipped = false;
            try
            {

            var hasRequiredPower = PowerHelper.HasRequiredElectricPower(this);
            if (!hasRequiredPower) return; //No power -> nothing to do

            // OPT: When the previous iteration found nothing to weld (all targets grid-limited
            // or assigned), skip the full iteration until the target list changes (new scan).
            // CurrentHash is a uint (atomic read); stale reads are benign (skip one extra tick).
            if (_weldLoopExhausted && State.PossibleWeldTargets.CurrentHash == _weldExhaustedAtHash)
            {
                weldSkipped = true;
                return;
            }
            _weldLoopExhausted = false;

            lock (State.PossibleWeldTargets)
            {
                // Set to true once the locked-on block completes this tick so the loop
                // can find the next target immediately, without actually welding it
                // (only one block is welded per tick). The next target is returned as
                // currentWeldingBlock so it starts welding on the very next update cycle.
                var lookingForNext = false;
                long lastRejectedGridId = 0;
                // When lock-on is lost (block vanished from list after scan rebuild),
                // we re-iterate without lock-on so the BaR doesn't waste the tick.
                var lockOnRetry = false;
                LockOnRetry:
                foreach (var targetData in State.PossibleWeldTargets)
                {
                    var isLockOnBlock = State.CurrentWeldingBlock != null
                        && IsSameBlock(State.CurrentWeldingBlock, targetData.Block);

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

                    if (Mod.Settings.AssignToSystemEnabled && !_Welder.HelpOthers
                        && Settings.CurrentPickedWeldingBlock == null
                        && targetData.Block.IsAssignedToOtherSystem(_Welder.EntityId))
                    {
                        skippedByAssign++;
                        continue;
                    }

                    var isIgnored = targetData.Ignore;
                    var isWeldable = !isIgnored && Weldable(targetData);
                    if (!isIgnored) checkedByWeldable++;
                    else skippedByIgnore++;

                    if (((Settings.Flags & SyncBlockSettings.Settings.ScriptControlled) != 0) || isWeldable)
                    {
                        if (targetData.Block != null && targetData.Block.FatBlock != null && targetData.Block.FatBlock.Closed)
                        {
                            continue;
                        }

                        // Skip grid limit and assignment checks for the lock-on block —
                        // this BaR was already working on it, don't let stale counts or
                        // assignment races cause it to be abandoned mid-weld.
                        if (!isLockOnBlock)
                        {
                            // Skip blocks at starved priority levels — zero-cost after initial failures.
                            // lookingForNext is exempt: just picking next lock-on, no component check.
                            if (!lookingForNext && starvedPriorityBits != 0)
                            {
                                var blockPriority = BlockWeldPriority.GetPriority(targetData.Block);
                                if (blockPriority > 0 && blockPriority < 64 && (starvedPriorityBits & (1L << blockPriority)) != 0)
                                {
                                    needWelding = true;
                                    starvedSkipped++;
                                    continue;
                                }
                            }

                            if (Settings.CurrentPickedWeldingBlock == null)
                            {
                                var gridId = targetData.Block.CubeGrid.EntityId;
                                if (IsGridOverSystemLimit(gridId, ref lastRejectedGridId))
                                {
                                    skippedByGridLimit++;
                                    continue;
                                }
                            }

                            if (Mod.Settings.AssignToSystemEnabled && _Welder.IsWorking && _Welder.Enabled && !_Welder.HelpOthers && Settings.CurrentPickedWeldingBlock == null && !targetData.Block.AssignToSystem(_Welder.EntityId))
                            {
                                skippedByAssign++;
                                continue;
                            }
                        }
                        else if (Mod.Settings.AssignToSystemEnabled && _Welder.IsWorking && _Welder.Enabled && !_Welder.HelpOthers)
                        {
                            // Refresh assignment for the lock-on block.
                            targetData.Block.AssignToSystem(_Welder.EntityId);
                        }

                        needWelding = true;

                        if (lookingForNext)
                        {
                            // The previous block just completed this tick. Lock on to this
                            // next eligible block so it starts welding next cycle immediately.
                            currentWeldingBlock = targetData.Block;
                            break;
                        }

                        if (!transporting) //Transport needs to be weld afterwards
                        {
                            transporting = ServerFindMissingComponents(targetData);
                            totalComponentChecks++;
                        }

                        welding = ServerDoWeld(targetData);

                        ServerEmptyTransportInventory(false);

                        if (targetData.Ignore)
                        {
                            if (Mod.Settings.AssignToSystemEnabled) targetData.Block.ReleaseFromSystem();
                            State.PossibleWeldTargets.ChangeHash();
                            // Block completed this tick. Clear lock-on and search for the
                            // next target in the same iteration. welding stays true so
                            // state and effects remain correct for this tick.
                            State.CurrentWeldingBlock = null;
                            lookingForNext = true;
                            // Do NOT break — fall through to find the next target.
                        }
                        else if (welding || transporting)
                        {
                            currentWeldingBlock = targetData.Block;
                            break; //Only weld one block at once (do not split over all blocks as the base shipwelder does)
                        }
                        else
                        {
                            // Block can't be welded right now (no components available).
                            if (isLockOnBlock)
                            {
                                State.CurrentWeldingBlock = null;
                            }

                            if (Mod.Settings.AssignToSystemEnabled) targetData.Block.ReleaseFromSystem();

                            // Track consecutive failures at the same priority level.
                            var failPriority = BlockWeldPriority.GetPriority(targetData.Block);
                            if (failPriority == lastFailPriority)
                            {
                                consecutiveAtPriority++;
                            }
                            else
                            {
                                lastFailPriority = failPriority;
                                consecutiveAtPriority = 1;
                            }

                            // After 3 failures at one priority, mark it starved — remaining
                            // blocks at this level share component needs and will also fail.
                            if (consecutiveAtPriority >= 3 && failPriority > 0 && failPriority < 64)
                            {
                                starvedPriorityBits |= (1L << failPriority);
                            }

                            componentFailures++;
                            // Global safety cap: bound main-thread cost regardless of priority diversity.
                            if (totalComponentChecks >= 10)
                                break;
                        }
                    }
                    else
                    {
                        if (targetData.Ignore)
                        {
                            if (Mod.Settings.AssignToSystemEnabled) targetData.Block.ReleaseFromSystem();
                            State.PossibleWeldTargets.ChangeHash();
                        }
                        // Current tracked block is no longer weldable; clear the lock so the
                        // loop can find the next eligible block in this same tick.
                        if (isLockOnBlock)
                        {
                            State.CurrentWeldingBlock = null;
                        }
                        // Note: Could add a cooldown timer here so non-weldable blocks are skipped
                        // for a period instead of re-evaluated every tick. (Phase 4 feature candidate)
                    }
                }

                // Lock-on block vanished from the list (e.g., projected grid EntityId changed
                // after projector update). Clear lock-on and re-iterate so this tick isn't wasted.
                if (!lockOnRetry && State.CurrentWeldingBlock != null && !lockOnFound)
                {
                    State.CurrentWeldingBlock = null;
                    skippedByLockOn = 0;
                    componentFailures = 0;
                    starvedPriorityBits = 0L;
                    lastFailPriority = -1;
                    consecutiveAtPriority = 0;
                    starvedSkipped = 0;
                    totalComponentChecks = 0;
                    lookingForNextChecked = 0;
                    lockOnRetry = true;
                    goto LockOnRetry;
                }
            }

            // OPT: Mark exhausted when the full iteration found nothing claimable.
            // Skip condition: not welding, not needing welding, no component checks attempted,
            // and no lock-on (lock-on BaRs must always re-check for their block).
            if (!welding && !needWelding && totalComponentChecks == 0 && !hadLockOn)
            {
                _weldLoopExhausted = true;
                _weldExhaustedAtHash = State.PossibleWeldTargets.CurrentHash;
            }

            }
            finally
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
                var _componentFailures = componentFailures;
                var _lockOnLost = hadLockOn && !lockOnFound;
                var _starvedSkipped = starvedSkipped;
                var _totalComponentChecks = totalComponentChecks;
                var _lookingForNextChecked = lookingForNextChecked;
                var _weldSkipped = weldSkipped;
                var _saturatedGridCount = _saturatedGridIds.Count;
                MethodProfiler.StopAndLog("ServerTryWelding", profilerTs, () =>
                    string.Format("entityId={0};welding={1};needWelding={2};transporting={3};targets={4};currentBlock={5};hadLockOn={6};lockOnFound={7};lockOnLost={8};skipLock={9};weldChecked={10};skipIgnore={11};skipGrid={12};skipAssign={13};componentFails={14};starvedSkip={15};compChecks={16};nextCap={17};exhaustedSkip={18};saturatedGrids={19}",
                        _Welder.EntityId, _welding, _needWelding, _transporting, _targetCount,
                        State.CurrentWeldingBlock != null ? State.CurrentWeldingBlock.BlockDefinition.Id.SubtypeName : "none",
                        _hadLockOn, _lockOnFound, _lockOnLost,
                        _skippedByLockOn, _checkedByWeldable, _skippedByIgnore, _skippedByGridLimit, _skippedByAssign, _componentFailures,
                        _starvedSkipped, _totalComponentChecks, _lookingForNextChecked, _weldSkipped, _saturatedGridCount));
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
                var _result = result;
                MethodProfiler.StopAndLog("Weldable", profilerTs, () =>
                    string.Format("entityId={0};block={1};projected={2};result={3}",
                        _Welder.EntityId,
                        target != null ? target.BlockDefinition.Id.SubtypeName : "null",
                        isProjected, _result));
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
            catch
            {
                // If something goes wrong, lets say its all built to avoid issues!
                return true;
            }
        }

        private bool ServerDoWeld(TargetBlockData targetData)
        {
            var profilerTs = MethodProfiler.Start();
            long tsBuild = 0, tsStockpile = 0, tsMount = 0;
            var welderInventory = _Welder.GetInventory(0);
            var welding = false;
            var created = false;
            var target = targetData.Block;
            var hasIgnoreColor = ((Settings.Flags & SyncBlockSettings.Settings.UseIgnoreColor) != 0) && IsColorNearlyEquals(Settings.IgnoreColorPacked, target.GetColorMask());

            if ((targetData.Attributes & TargetBlockData.AttributeFlags.Projected) != 0)
            {
                //New Block (Projected)
                var cubeGridProjected = target.CubeGrid as MyCubeGrid;
                var blockDefinition = target.BlockDefinition as MyCubeBlockDefinition;
                var item = _TransportInventory.FindItem(blockDefinition.Components[0].Definition.Id);

                if ((CreativeModeActive || (item != null && item.Amount >= 1)) && cubeGridProjected != null && cubeGridProjected.Projector != null)
                {
                    if (_Welder.IsWithinWorldLimits(cubeGridProjected.Projector, blockDefinition.BlockPairName, blockDefinition.PCU))
                    {
                        if (!cubeGridProjected.Projector.Closed && !cubeGridProjected.Projector.CubeGrid.Closed && (target.FatBlock == null || !target.FatBlock.Closed))
                        {
                            tsBuild = Stopwatch.GetTimestamp();
                            var proj = cubeGridProjected.Projector as Sandbox.ModAPI.IMyProjector;
                            proj.Build(target, _Welder.OwnerId, _Welder.EntityId, Settings.WeldOptions == AutoWeldOptions.WeldFull, _Welder.SlimBlock.BuiltBy);
                            tsBuild = Stopwatch.GetTimestamp() - tsBuild;
                        }

                        // proj.Build() handles component consumption internally; manual RemoveItems is not needed.

                        //After creation we can't welding this projected block, we have to find the 'physical' block instead.
                        var cubeGrid = cubeGridProjected.Projector.CubeGrid;
                        var blockPos = cubeGrid.WorldToGridInteger(cubeGridProjected.GridIntegerToWorld(target.Position));
                        target = cubeGrid.GetCubeBlock(blockPos);

                        if (target != null)
                        {
                            targetData.Block = target;
                            targetData.Attributes &= ~TargetBlockData.AttributeFlags.Projected;
                            created = true;

                            // Close assignment gap: the projected block's key (ProjGridId:Pos) differs
                            // from the physical block's key (RealGridId:Pos). Assign the physical block
                            // immediately so no other BaR can steal it during our stagger wait.
                            if (Mod.Settings.AssignToSystemEnabled && !_Welder.HelpOthers)
                                target.AssignToSystem(_Welder.EntityId);
                        }
                        else
                        {
                            targetData.Ignore = true;
                        }

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
                //No ignore color and allready created
                if (!IsWeldIntegrityReached(target) || created)
                {
                    //Move collected/needed items to stockpile.
                    tsStockpile = Stopwatch.GetTimestamp();
                    target.MoveItemsToConstructionStockpile(_TransportInventory);
                    tsStockpile = Stopwatch.GetTimestamp() - tsStockpile;

                    //Incomplete
                    welding = target.CanContinueBuild(_TransportInventory) || CreativeModeActive;

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
                            tsMount = Stopwatch.GetTimestamp();
                            target.IncreaseMountLevel(weldAmount, _Welder.OwnerId, welderInventory, MyAPIGateway.Session.WelderSpeedMultiplier * Mod.Settings.Welder.WeldingMultiplier * WELDER_MAX_REPAIR_BONE_MOVEMENT_SPEED, _Welder.HelpOthers);
                            tsMount = Stopwatch.GetTimestamp() - tsMount;
                        }
                    }

                    if (IsWeldIntegrityReached(target))
                    {
                        targetData.Ignore = true;
                    }
                }
                else
                {
                    //Deformation
                    welding = true;
                    target.IncreaseMountLevel(MyAPIGateway.Session.WelderSpeedMultiplier * Mod.Settings.Welder.WeldingMultiplier * WELDER_AMOUNT_PER_SECOND, _Welder.OwnerId, welderInventory, MyAPIGateway.Session.WelderSpeedMultiplier * Mod.Settings.Welder.WeldingMultiplier * WELDER_MAX_REPAIR_BONE_MOVEMENT_SPEED, _Welder.HelpOthers);
                }
            }

            var result = welding || created;
            var _tsBuild = tsBuild;
            var _tsStockpile = tsStockpile;
            var _tsMount = tsMount;
            MethodProfiler.StopAndLog("ServerDoWeld", profilerTs, () =>
                string.Format("entityId={0};block={1};projected={2};created={3};welding={4};result={5};buildMs={6:F3};stockpileMs={7:F3};mountMs={8:F3}",
                    _Welder.EntityId,
                    targetData.Block != null ? targetData.Block.BlockDefinition.Id.SubtypeName : "null",
                    (targetData.Attributes & TargetBlockData.AttributeFlags.Projected) != 0,
                    created, welding, result,
                    _tsBuild * 1000.0 / Stopwatch.Frequency,
                    _tsStockpile * 1000.0 / Stopwatch.Frequency,
                    _tsMount * 1000.0 / Stopwatch.Frequency));
            return result;
        }

        /// <summary>
        /// Try to find an the missing components and moves them into welder inventory
        /// </summary>
        private bool ServerFindMissingComponents(TargetBlockData targetData)
        {
            var profilerTs = MethodProfiler.Start();
            try
            {
                var playTime = MyAPIGateway.Session.ElapsedPlayTime;

                if (IsTransportRunning(playTime))
                    return true;

                var remainingVolume = _MaxTransportVolume;
                _TempMissingComponents.Clear();
                var picked = false;
                var cubeGrid = targetData.Block.CubeGrid as MyCubeGrid;

                if ((targetData.Attributes & TargetBlockData.AttributeFlags.Projected) != 0)
                {
                    // Single GetMissingComponents call for projected blocks.
                    // Skeleton: only the creation component. Otherwise: all components
                    // at the target integrity level (includes creation component).
                    // Avoids the previous double API call + subtraction logic.
                    var useIgnoreColor = ((Settings.Flags & SyncBlockSettings.Settings.UseIgnoreColor) != 0) && IsColorNearlyEquals(Settings.IgnoreColorPacked, targetData.Block.GetColorMask());
                    if (Settings.WeldOptions == AutoWeldOptions.WeldSkeleton || useIgnoreColor)
                    {
                        targetData.Block.GetMissingComponents(_TempMissingComponents, UtilsInventory.IntegrityLevel.Create);
                    }
                    else
                    {
                        targetData.Block.GetMissingComponents(_TempMissingComponents, Settings.WeldOptions == AutoWeldOptions.WeldFunctional ? UtilsInventory.IntegrityLevel.Functional : UtilsInventory.IntegrityLevel.Complete);
                    }

                    if (_TempMissingComponents.Count > 0)
                    {
                        picked = ServerFindMissingComponents(targetData, ref remainingVolume);
                    }
                }
                else
                {
                    targetData.Block.GetMissingComponents(_TempMissingComponents, Settings.WeldOptions == AutoWeldOptions.WeldFunctional ? UtilsInventory.IntegrityLevel.Functional : UtilsInventory.IntegrityLevel.Complete);
                }

                if (_TempMissingComponents.Count > 0)
                {
                    ServerFindMissingComponents(targetData, ref remainingVolume);
                }

                if (remainingVolume < _MaxTransportVolume || (CreativeModeActive && _TempMissingComponents.Count > 0))
                {
                    //Transport startet
                    State.CurrentTransportIsPick = false;
                    State.CurrentTransportIsCollecting = false;
                    State.CurrentTransportTarget = ComputePosition(targetData.Block);
                    State.CurrentTransportStartTime = playTime;
                    State.CurrentTransportTime = TimeSpan.FromSeconds(2d * targetData.Distance / Settings.TransportSpeed);

                    return true;
                }
                return false;
            }
            finally
            {
                _TempMissingComponents.Clear();
                MethodProfiler.StopAndLog("ServerFindMissingComponents", profilerTs, () =>
                    string.Format("entityId={0};block={1};missingTypes={2};projected={3}",
                        _Welder.EntityId,
                        targetData.Block != null ? targetData.Block.BlockDefinition.Id.SubtypeName : "null",
                        _TempMissingComponents.Count,
                        (targetData.Attributes & TargetBlockData.AttributeFlags.Projected) != 0));
            }
        }

        private bool ServerFindMissingComponents(TargetBlockData targetData, ref float remainingVolume)
        {
            var picked = false;
            foreach (var keyValue in _TempMissingComponents)
            {
                var componentId = new MyDefinitionId(typeof(MyObjectBuilder_Component), keyValue.Key);
                var definition = MyDefinitionManager.Static.GetPhysicalItemDefinition(componentId);
                int neededAmount = keyValue.Value;

                picked = ServerPickFromWelder(componentId, definition.Volume, ref neededAmount, ref remainingVolume) || picked;

                if (neededAmount > 0 && remainingVolume > 0)
                {
                    picked = PullComponents(componentId, definition.Volume, ref neededAmount, ref remainingVolume) || picked;
                }

                if (neededAmount > 0 && remainingVolume > 0)
                {
                    AddToMissingComponents(componentId, neededAmount);
                }

                if (remainingVolume <= 0)
                {
                    break;
                }
            }

            return picked;
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
                MethodProfiler.StopAndLog("ServerPickFromWelder", profilerTs, () =>
                    string.Format("entityId={0};component={1};startNeeded={2};picked={3};empty=True",
                        _Welder.EntityId, componentId.SubtypeName, startNeeded, false));
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

            MethodProfiler.StopAndLog("ServerPickFromWelder", profilerTs, () =>
                string.Format("entityId={0};component={1};startNeeded={2};picked={3};empty=False",
                    _Welder.EntityId, componentId.SubtypeName, startNeeded, picked));
            return picked;
        }

        /// <summary>
        /// Compares two slim blocks by identity rather than reference equality.
        /// Background scans create new IMySlimBlock references for the same physical block;
        /// ReferenceEquals short-circuits the common case, grid+position handles the rest.
        /// </summary>
        private static bool IsSameBlock(IMySlimBlock a, IMySlimBlock b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            return a.CubeGrid.EntityId == b.CubeGrid.EntityId && a.Position == b.Position;
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
    }
}
