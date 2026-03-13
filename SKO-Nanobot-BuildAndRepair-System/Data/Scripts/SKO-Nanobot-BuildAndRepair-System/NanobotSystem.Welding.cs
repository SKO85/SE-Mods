using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Handlers;
using SKONanobotBuildAndRepairSystem.Helpers;
using SKONanobotBuildAndRepairSystem.Models;
using SKONanobotBuildAndRepairSystem.Profiling;
using SKONanobotBuildAndRepairSystem.Utils;
using System;
using System.Linq;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;
using static SKONanobotBuildAndRepairSystem.Utils.UtilsInventory;

namespace SKONanobotBuildAndRepairSystem
{
    public partial class NanobotSystem
    {
        private void ServerTryWelding(out bool welding, out bool needwelding, out bool transporting, out IMySlimBlock currentWeldingBlock)
        {
            var profilerTs = MethodProfiler.Start();
            welding = false;
            needwelding = false;
            transporting = false;
            currentWeldingBlock = null;
            var hadLockOn = State.CurrentWeldingBlock != null;
            var skippedByLockOn = 0;
            var checkedByWeldable = 0;
            var skippedByIgnore = 0;
            var skippedByGridLimit = 0;
            var skippedByAssign = 0;
            var lockOnFound = false;
            try
            {

            var hasRequiredPower = PowerHelper.HasRequiredElectricPower(this);
            if (!hasRequiredPower) return; //No power -> nothing to do

            lock (State.PossibleWeldTargets)
            {
                // Set to true once the locked-on block completes this tick so the loop
                // can find the next target immediately, without actually welding it
                // (only one block is welded per tick). The next target is returned as
                // currentWeldingBlock so it starts welding on the very next update cycle.
                var lookingForNext = false;
                long lastRejectedGridId = 0;
                foreach (var targetData in State.PossibleWeldTargets)
                {
                    if (!lookingForNext && State.CurrentWeldingBlock != null && State.CurrentWeldingBlock != targetData.Block)
                    {
                        skippedByLockOn++;
                        continue;
                    }

                    if (!lookingForNext && State.CurrentWeldingBlock != null && State.CurrentWeldingBlock == targetData.Block)
                    {
                        lockOnFound = true;
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

                        if (!Mod.Settings.DisableLimitSystemsPerTargetGrid && Settings.CurrentPickedWeldingBlock == null)
                        {
                            var gridId = targetData.Block.CubeGrid.EntityId;
                            if (gridId == lastRejectedGridId || GetCachedSystemCountOnGrid(gridId) >= Mod.Settings.MaxSystemsPerTargetGrid)
                            {
                                lastRejectedGridId = gridId;
                                skippedByGridLimit++;
                                continue;
                            }
                        }

                        if (Mod.Settings.AssignToSystemEnabled && _Welder.IsWorking && _Welder.Enabled && !_Welder.HelpOthers && Settings.CurrentPickedWeldingBlock == null && !targetData.Block.AssignToSystem(_Welder.EntityId))
                        {
                            skippedByAssign++;
                            continue;
                        }

                        needwelding = true;

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
                        if (State.CurrentWeldingBlock == targetData.Block)
                        {
                            State.CurrentWeldingBlock = null;
                        }
                        // Note: Could add a cooldown timer here so non-weldable blocks are skipped
                        // for a period instead of re-evaluated every tick. (Phase 4 feature candidate)
                    }
                }
            }

            }
            finally
            {
                var _welding = welding;
                var _needwelding = needwelding;
                var _transporting = transporting;
                var _targetCount = State.PossibleWeldTargets.CurrentCount;
                var _hadLockOn = hadLockOn;
                var _lockOnFound = lockOnFound;
                var _skippedByLockOn = skippedByLockOn;
                var _checkedByWeldable = checkedByWeldable;
                var _skippedByIgnore = skippedByIgnore;
                var _skippedByGridLimit = skippedByGridLimit;
                var _skippedByAssign = skippedByAssign;
                MethodProfiler.StopAndLog("ServerTryWelding", profilerTs, () =>
                    string.Format("entityId={0};welding={1};needWelding={2};transporting={3};targets={4};currentBlock={5};hadLockOn={6};lockOnFound={7};skipLock={8};weldChecked={9};skipIgnore={10};skipGrid={11};skipAssign={12}",
                        _Welder.EntityId, _welding, _needwelding, _transporting, _targetCount,
                        State.CurrentWeldingBlock != null ? State.CurrentWeldingBlock.BlockDefinition.Id.SubtypeName : "none",
                        _hadLockOn, _lockOnFound, _skippedByLockOn, _checkedByWeldable, _skippedByIgnore, _skippedByGridLimit, _skippedByAssign));
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

                var isFunctionalOnly = (Settings.WeldOptions & AutoWeldOptions.FunctionalOnly) != 0;
                var weld = (!IsWeldIntegrityReached(target) || target.NeedRepair(isFunctionalOnly)) && !IsFriendlyDamage(target);

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
                var isFunctionalOnly = (Settings.WeldOptions & AutoWeldOptions.FunctionalOnly) != 0;
                if (!isFunctionalOnly)
                {
                    return target.IsFullIntegrity;
                }

                var requiredIntegrity = target.GetRequiredIntegrity(isFunctionalOnly);
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
                            var proj = cubeGridProjected.Projector as Sandbox.ModAPI.IMyProjector;
                            proj.Build(target, _Welder.OwnerId, _Welder.EntityId, true, _Welder.SlimBlock.BuiltBy);
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

            if (!hasIgnoreColor && target != null && (targetData.Attributes & TargetBlockData.AttributeFlags.Projected) == 0)
            {
                //No ignore color and allready created
                if (!target.IsFullIntegrity || created)
                {
                    //Move collected/needed items to stockpile.
                    target.MoveItemsToConstructionStockpile(_TransportInventory);

                    //Incomplete
                    welding = target.CanContinueBuild(_TransportInventory) || CreativeModeActive;

                    if (welding)
                    {
                        target.IncreaseMountLevel(MyAPIGateway.Session.WelderSpeedMultiplier * Mod.Settings.Welder.WeldingMultiplier * WELDER_AMOUNT_PER_SECOND, _Welder.OwnerId, welderInventory, MyAPIGateway.Session.WelderSpeedMultiplier * Mod.Settings.Welder.WeldingMultiplier * WELDER_MAX_REPAIR_BONE_MOVEMENT_SPEED, _Welder.HelpOthers);
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
            MethodProfiler.StopAndLog("ServerDoWeld", profilerTs, () =>
                string.Format("entityId={0};block={1};projected={2};created={3};welding={4};result={5}",
                    _Welder.EntityId,
                    targetData.Block != null ? targetData.Block.BlockDefinition.Id.SubtypeName : "null",
                    (targetData.Attributes & TargetBlockData.AttributeFlags.Projected) != 0,
                    created, welding, result));
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
                var picked = false; ;
                var cubeGrid = targetData.Block.CubeGrid as MyCubeGrid;

                if ((targetData.Attributes & TargetBlockData.AttributeFlags.Projected) != 0)
                {
                    targetData.Block.GetMissingComponents(_TempMissingComponents, UtilsInventory.IntegrityLevel.Create);
                    if (_TempMissingComponents.Count > 0)
                    {
                        picked = ServerFindMissingComponents(targetData, ref remainingVolume);

                        if (picked)
                        {
                            if (((Settings.Flags & SyncBlockSettings.Settings.UseIgnoreColor) == 0) || !IsColorNearlyEquals(Settings.IgnoreColorPacked, targetData.Block.GetColorMask()))
                            {
                                //Block could be created and should be welded -> so retrieve the remaining material also
                                var keyValue = _TempMissingComponents.ElementAt(0);
                                _TempMissingComponents.Clear();

                                targetData.Block.GetMissingComponents(_TempMissingComponents, ((Settings.WeldOptions & AutoWeldOptions.FunctionalOnly) == 0) ? UtilsInventory.IntegrityLevel.Complete : UtilsInventory.IntegrityLevel.Functional);

                                if (_TempMissingComponents.ContainsKey(keyValue.Key))
                                {
                                    if (_TempMissingComponents[keyValue.Key] <= keyValue.Value)
                                    {
                                        _TempMissingComponents.Remove(keyValue.Key);
                                    }
                                    else
                                    {
                                        _TempMissingComponents[keyValue.Key] -= keyValue.Value;
                                    }
                                }
                            }
                        }
                    }
                }
                else
                {
                    targetData.Block.GetMissingComponents(_TempMissingComponents, ((Settings.WeldOptions & AutoWeldOptions.FunctionalOnly) == 0) ? UtilsInventory.IntegrityLevel.Complete : UtilsInventory.IntegrityLevel.Functional);
                }

                if (_TempMissingComponents.Count > 0)
                {
                    ServerFindMissingComponents(targetData, ref remainingVolume);
                }

                if (remainingVolume < _MaxTransportVolume || (CreativeModeActive && _TempMissingComponents.Count > 0))
                {
                    //Transport startet
                    State.CurrentTransportIsPick = false;
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
            var picked = false;

            var welderInventory = _Welder.GetInventory(0);
            if (welderInventory == null || welderInventory.Empty())
            {
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
    }
}
