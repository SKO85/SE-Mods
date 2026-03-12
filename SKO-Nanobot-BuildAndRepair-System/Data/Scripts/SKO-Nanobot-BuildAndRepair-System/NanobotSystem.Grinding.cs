using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Handlers;
using SKONanobotBuildAndRepairSystem.Helpers;
using SKONanobotBuildAndRepairSystem.Models;
using SKONanobotBuildAndRepairSystem.Utils;
using System;
using VRage.Game;
using VRage.Game.ModAPI;

namespace SKONanobotBuildAndRepairSystem
{
    public partial class NanobotSystem
    {
        private void ServerTryGrinding(out bool grinding, out bool needgrinding, out bool transporting, out IMySlimBlock currentGrindingBlock)
        {
            grinding = false;
            needgrinding = false;
            transporting = false;
            currentGrindingBlock = null;

            if (State.InventoryFull)
                return;

            if (!PowerHelper.HasRequiredElectricPower(this)) return; //No power -> nothing to do

            lock (State.PossibleGrindTargets)
            {
                foreach (var targetData in State.PossibleGrindTargets)
                {
                    if (targetData.Block != null && targetData.Block.FatBlock != null && targetData.Block.FatBlock.Closed)
                    {
                        continue;
                    }

                    if (!Mod.Settings.DisableLimitSystemsPerTargetGrid && Settings.CurrentPickedGrindingBlock == null &&
                        CountSystemsOnGrid(targetData.Block.CubeGrid.EntityId) >= Mod.Settings.MaxSystemsPerTargetGrid)
                    {
                        continue;
                    }

                    if (Mod.Settings.AssignToSystemEnabled && _Welder.IsWorking && _Welder.Enabled && Settings.CurrentPickedGrindingBlock == null && !targetData.Block.AssignToSystem(_Welder.EntityId))
                    {
                        continue;
                    }

                    if (((Settings.Flags & SyncBlockSettings.Settings.ScriptControlled) != 0) && targetData.Block != Settings.CurrentPickedGrindingBlock)
                    {
                        continue;
                    }

                    if (!targetData.Block.IsDestroyed)
                    {
                        needgrinding = true;

                        grinding = ServerDoGrind(targetData, out transporting);

                        if (grinding)
                        {
                            currentGrindingBlock = targetData.Block;
                            break; //Only grind one block at once
                        }

                        if (Mod.Settings.AssignToSystemEnabled && (targetData.Ignore || targetData.Block.IsFullyDismounted))
                        {
                            // Release the block from this system.
                            targetData.Block.ReleaseFromSystem();
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

        private bool ServerDoGrind(TargetBlockData targetData, out bool transporting)
        {
            var target = targetData.Block;
            var playTime = MyAPIGateway.Session.ElapsedPlayTime;
            transporting = IsTransportRunning(playTime);
            if (transporting) return false;

            var targetGrid = target.CubeGrid;

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
            var integrityPointsPerSec = ((MyCubeBlockDefinition)target.BlockDefinition).IntegrityPointsPerSec;

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

            if (integrityRatio <= 0.2)
            {
                //Try to emtpy inventory (if any)
                if (target.FatBlock != null && target.FatBlock.HasInventory)
                {
                    emptying = EmptyBlockInventories(target.FatBlock, _TransportInventory, out isEmpty);
                }
            }

            if (!emptying || isEmpty)
            {
                MyDamageInformation damageInfo = new MyDamageInformation(false, damage, MyDamageType.Grind, _Welder.EntityId);

                if (target.UseDamageSystem)
                {
                    foreach (var entry in Mod.NanobotSystems)
                    {
                        var relation = entry.Value.Welder.GetUserRelationToOwner(_Welder.OwnerId);
                        if (MyRelationsBetweenPlayerAndBlockExtensions.IsFriendly(relation))
                        {
                            //A 'friendly' damage from grinder -> do not repair (for a while)
                            //I don't check block relation here, because if it is enemy we won't repair it in any case and it just times out
                            entry.Value.FriendlyDamage[target] = MyAPIGateway.Session.ElapsedPlayTime + Mod.Settings.FriendlyDamageTimeout;
                        }
                    }
                }

                target.DecreaseMountLevel(damageInfo.Amount, _TransportInventory);
                target.MoveItemsFromConstructionStockpile(_TransportInventory);

                if (target.IsFullyDismounted)
                {
                    target.SpawnConstructionStockpile();
                    target.CubeGrid.RazeBlock(target.Position);
                }
            }

            if ((float)_TransportInventory.CurrentVolume >= _MaxTransportVolume || target.IsFullyDismounted)
            {
                //Transport started
                State.CurrentTransportIsPick = true;
                State.CurrentTransportTarget = ComputePosition(target);
                State.CurrentTransportStartTime = playTime;
                State.CurrentTransportTime = TimeSpan.FromSeconds(2d * targetData.Distance / Settings.TransportSpeed);

                ServerEmptyTransportInventory(true);
                transporting = true;
            }

            return true;
        }
    }
}
