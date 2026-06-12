using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Helpers;
using SKONanobotBuildAndRepairSystem.Models;
using SKONanobotBuildAndRepairSystem.Profiling;
using System;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI;

namespace SKONanobotBuildAndRepairSystem
{
    public partial class NanobotSystem
    {
        private void ServerTryCollectingFloatingTargets(out bool collecting, out bool needCollecting, out bool transporting)
        {
            var profilerTs = MethodProfiler.Start();
            collecting = false;
            needCollecting = false;
            transporting = false;
            try
            {
            if (!PowerHelper.HasRequiredElectricPower(this)) return; //-> Not enough power

            // BUG-018: refresh InventoryFull before collecting (may be stale on reload).
            CheckAndUpdateInventoryFull();
            if (State.InventoryFull) return;

            // BUG-260610.22: pick targets under the lock, run the engine work
            // (inventory transfers, entity deletion — ms-scale) OUTSIDE it. The
            // background scan publish blocks on this lock, so a slow collect tick
            // used to stall a scan worker — the same contention BUG-135/BUG-137
            // removed for the weld and grind loops.
            _TempCollectTargets.Clear();
            lock (State.PossibleFloatingTargets)
            {
                foreach (var targetData in State.PossibleFloatingTargets)
                {
                    if (targetData.Entity != null && !targetData.Ignore)
                    {
                        _TempCollectTargets.Add(targetData);
                    }
                }
            }

            var anyIgnored = false;
            TargetEntityData collectingFirstTarget = null;
            for (var i = 0; i < _TempCollectTargets.Count; i++)
            {
                var targetData = _TempCollectTargets[i];
                // BUG-260612.21: a scan swap mid-loop can orphan snapshot entries —
                // skip entities that closed since the pick.
                if (targetData.Entity == null || targetData.Entity.Closed || targetData.Entity.MarkedForClose)
                    continue;
                needCollecting = true;
                var added = ServerDoCollectFloating(targetData, out transporting, ref collectingFirstTarget);
                if (targetData.Ignore) anyIgnored = true;
                collecting |= added;
                // BUG-260612.23: the old 50-target cap was unreachable (the floating
                // list is capped at 16 at scan-apply) — the volume gate is the real cap.
                if (transporting)
                {
                    break; //Max Inventorysize reached
                }
            }
            if (collecting && !transporting) ServerDoCollectFloating(null, out transporting, ref collectingFirstTarget); //Starttransport if pending

            if (anyIgnored)
            {
                lock (State.PossibleFloatingTargets)
                {
                    State.PossibleFloatingTargets.ChangeHash();
                }
            }

            }
            finally
            {
                // BUG-260612.22: exception-safe — the buffer must not pin entity refs.
                _TempCollectTargets.Clear();
                if (profilerTs != 0L)
                {
                    var _collecting = collecting;
                    var _needCollecting = needCollecting;
                    var _transporting = transporting;
                    var _targetCount = State.PossibleFloatingTargets.CurrentCount;
                    MethodProfiler.StopAndLog("ServerTryCollectingFloatingTargets", profilerTs, () =>
                        string.Format("entityId={0};collecting={1};needCollecting={2};transporting={3};targets={4}",
                            _Welder.EntityId, _collecting, _needCollecting, _transporting, _targetCount));
                }
            }
        }

        private bool ServerDoCollectFloating(TargetEntityData targetData, out bool transporting, ref TargetEntityData collectingFirstTarget)
        {
            transporting = false;
            var collecting = false;
            var canAdd = false;
            var isEmpty = true;

            if (State.InventoryFull)
                return false;

            var playTime = MyAPIGateway.Session.ElapsedPlayTime;
            transporting = IsTransportRunning(playTime);

            if (transporting)
                return false;

            if (targetData != null)
            {
                var target = targetData.Entity;
                var floating = target as MyFloatingObject;
                var floatingFirstTarget = collectingFirstTarget != null ? collectingFirstTarget.Entity as MyFloatingObject : null;

                canAdd = collectingFirstTarget == null || (floatingFirstTarget != null && floating != null);
                if (canAdd)
                {
                    if (floating != null) collecting = EmptyFloatingObject(floating, _TransportInventory, out isEmpty);
                    else
                    {
                        collecting = EmptyBlockInventories(target, _TransportInventory, out isEmpty);

                        if (isEmpty)
                        {
                            var character = target as IMyCharacter;
                            if (character != null && character.IsBot && Mod.Settings.DeleteBotsWhenDead)
                            {
                                //Wolf, Spider, ...
                                target.Delete();
                            }
                        }
                    }

                    if (collecting && collectingFirstTarget == null) collectingFirstTarget = targetData;

                    targetData.Ignore = isEmpty;
                }
            }

            if (collectingFirstTarget != null && collectingFirstTarget.Entity != null && ((float)_TransportInventory.CurrentVolume >= _MaxTransportVolume || (!canAdd && _TransportInventory.CurrentVolume > 0)))
            {
                //Transport started
                State.CurrentTransportIsPick = true;
                State.CurrentTransportIsCollecting = true;
                State.CurrentTransportTarget = ComputePosition(collectingFirstTarget.Entity);
                State.CurrentTransportStartTime = playTime;
                State.CurrentTransportTime = TimeSpan.FromSeconds(2d * collectingFirstTarget.Distance / Settings.TransportSpeed);

                ServerEmptyTransportInventory(true);
                transporting = true;
                collectingFirstTarget = null;
            }

            return collecting;
        }

        private bool EmptyFloatingObject(MyFloatingObject floating, IMyInventory dstInventory, out bool isEmpty)
        {
            var running = false;
            isEmpty = floating.WasRemovedFromWorld || floating.MarkedForClose;
            if (!isEmpty)
            {
                var remainingVolume = _MaxTransportVolume - (double)dstInventory.CurrentVolume;

                var definition = MyDefinitionManager.Static.GetPhysicalItemDefinition(floating.Item.Content.GetId());
                var startAmount = floating.Item.Amount;

                var maxremainAmount = (MyFixedPoint)(remainingVolume / definition.Volume);
                var maxpossibleAmount = maxremainAmount > floating.Item.Amount ? floating.Item.Amount : maxremainAmount; //Do not use MyFixedPoint.Min !Wrong Implementation could cause overflow!
                if (definition.HasIntegralAmounts) maxpossibleAmount = MyFixedPoint.Floor(maxpossibleAmount);
                if (maxpossibleAmount > 0)
                {
                    // BUG-260612.24: verify the stack fits BEFORE removing the floating
                    // object — unreachable with the current volume math, but a silent
                    // AddItems failure after RemoveFloatingObject would destroy items.
                    if (!dstInventory.CanItemsBeAdded(maxpossibleAmount, floating.Item.Content.GetId()))
                    {
                        return running;
                    }

                    if (maxpossibleAmount >= floating.Item.Amount)
                    {
                        MyFloatingObjects.RemoveFloatingObject(floating);
                        isEmpty = true;
                    }
                    else
                    {
                        floating.Item.Amount = floating.Item.Amount - maxpossibleAmount;
                        floating.RefreshDisplayName();
                    }

                    dstInventory.AddItems(maxpossibleAmount, floating.Item.Content);
                    remainingVolume -= (float)maxpossibleAmount * definition.Volume;

                    running = true;
                }
            }
            return running;
        }
    }
}
