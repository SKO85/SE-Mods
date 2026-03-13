using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Handlers;
using SKONanobotBuildAndRepairSystem.Models;
using SKONanobotBuildAndRepairSystem.Profiling;
using System;
using VRage.Game.ModAPI;

namespace SKONanobotBuildAndRepairSystem
{
    public partial class NanobotSystem
    {
        /// <summary>
        /// Try to weld/grind/collect the possible targets
        /// </summary>
        private void ServerTryWeldingGrindingCollecting()
        {
            var profilerTs = MethodProfiler.Start();
            try
            {
            var inventoryFull = State.InventoryFull;
            var limitsExceeded = State.LimitsExceeded;

            var welding = false;
            var needwelding = false;
            var grinding = false;
            var needgrinding = false;
            var collecting = false;
            var needcollecting = false;
            var transporting = false;

            if (_Welder.Closed || _Welder.MarkedForClose)
                return;

            var ready = _Welder.Enabled && _Welder.IsWorking && _Welder.IsFunctional;

            IMySlimBlock currentWeldingBlock = null;
            IMySlimBlock currentGrindingBlock = null;

            var playTime = MyAPIGateway.Session.ElapsedPlayTime;
            var isFullInventoryAndPicking = State.InventoryFull && State.CurrentTransportIsPick;

            if (ready)
            {
                ServerTryPushInventory();

                if (isFullInventoryAndPicking)
                {
                    State.LastTransportTarget = State.CurrentTransportTarget;
                    State.CurrentTransportTarget = null;
                    State.CurrentTransportStartTime = TimeSpan.Zero;
                    transporting = false;
                }
                else
                {
                    transporting = IsTransportRunning(playTime);
                }

                if (transporting && State.CurrentTransportIsPick) needgrinding = true;

                if ((Settings.Flags & SyncBlockSettings.Settings.ComponentCollectIfIdle) == 0 && !transporting)
                    ServerTryCollectingFloatingTargets(out collecting, out needcollecting, out transporting);

                if (!transporting)
                {
                    State.MissingComponents.Clear();
                    State.LimitsExceeded = false;

                    if (!Mod.Settings.DisableLimitSystemsPerTargetGrid)
                        BuildGridSystemCountCache();

                    switch (Settings.WorkMode)
                    {
                        case WorkModes.WeldBeforeGrind:
                            ServerTryWelding(out welding, out needwelding, out transporting, out currentWeldingBlock);
                            if (State.PossibleWeldTargets.CurrentCount == 0 || (((Settings.Flags & SyncBlockSettings.Settings.ScriptControlled) != 0) && Settings.CurrentPickedGrindingBlock != null))
                            {
                                ServerTryGrinding(out grinding, out needgrinding, out transporting, out currentGrindingBlock);
                            }
                            break;

                        case WorkModes.GrindBeforeWeld:
                            ServerTryGrinding(out grinding, out needgrinding, out transporting, out currentGrindingBlock);
                            if (State.PossibleGrindTargets.CurrentCount == 0 || (((Settings.Flags & SyncBlockSettings.Settings.ScriptControlled) != 0) && Settings.CurrentPickedWeldingBlock != null))
                            {
                                ServerTryWelding(out welding, out needwelding, out transporting, out currentWeldingBlock);
                            }
                            break;

                        case WorkModes.GrindIfWeldGetStuck:
                            ServerTryWelding(out welding, out needwelding, out transporting, out currentWeldingBlock);
                            if (!(welding || transporting) || (((Settings.Flags & SyncBlockSettings.Settings.ScriptControlled) != 0) && Settings.CurrentPickedGrindingBlock != null))
                            {
                                ServerTryGrinding(out grinding, out needgrinding, out transporting, out currentGrindingBlock);
                            }
                            break;

                        case WorkModes.WeldOnly:
                            ServerTryWelding(out welding, out needwelding, out transporting, out currentWeldingBlock);
                            break;

                        case WorkModes.GrindOnly:
                            ServerTryGrinding(out grinding, out needgrinding, out transporting, out currentGrindingBlock);
                            break;
                    }
                    State.MissingComponents.RebuildHash();
                }

                if (((Settings.Flags & SyncBlockSettings.Settings.ComponentCollectIfIdle) != 0) && !transporting && !welding && !grinding)
                    ServerTryCollectingFloatingTargets(out collecting, out needcollecting, out transporting);
            }
            else
            {
                if (isFullInventoryAndPicking)
                {
                    State.LastTransportTarget = State.CurrentTransportTarget;
                    State.CurrentTransportTarget = null;
                    State.CurrentTransportStartTime = TimeSpan.Zero;
                    transporting = false;
                }
                else
                {
                    transporting = IsTransportRunning(playTime); //Finish running transport
                }

                State.MissingComponents.Clear();
                State.MissingComponents.RebuildHash();
            }

            if (!(welding || grinding || collecting || transporting) && _TransportInventory.CurrentVolume > 0)
            {
                // Idle but not empty -> empty inventory
                if (!isFullInventoryAndPicking && State.LastTransportTarget.HasValue)
                {
                    State.CurrentTransportIsPick = true;
                    State.CurrentTransportTarget = State.LastTransportTarget;
                    State.CurrentTransportStartTime = playTime;

                    transporting = true;
                }

                if (ready)
                    ServerEmptyTransportInventory(true);
            }

            if (((State.Welding && !welding) || (State.Grinding && !(grinding || collecting))))
            {
                if (!isFullInventoryAndPicking && ready)
                {
                    StartAsyncUpdateSourcesAndTargets(false); //Scan immediately once for new targets
                }
            }

            // When transporting components for welding (delivery, not a pick), preserve the
            // previous NeedWelding state so the "Blocks to Build" panel remains visible and
            // MissingComponents doesn't flicker while the transport timer is ticking.
            if (transporting && !State.CurrentTransportIsPick && !needwelding && State.NeedWelding)
            {
                needwelding = State.NeedWelding;
                currentWeldingBlock = State.CurrentWeldingBlock;
            }

            var readyChanged = State.Ready != ready;
            var weldingChanged = State.Welding != welding;
            var needWeldingChanged = State.NeedWelding != needwelding;
            var grindingChanged = State.Grinding != grinding;
            var needGrindingChanged = State.NeedGrinding != needgrinding;
            State.Ready = ready;
            State.Welding = welding;
            State.NeedWelding = needwelding;
            State.CurrentWeldingBlock = currentWeldingBlock;

            State.Grinding = grinding;
            State.NeedGrinding = needgrinding;
            State.CurrentGrindingBlock = currentGrindingBlock;

            var transportChanged = State.Transporting != transporting;
            State.Transporting = transporting;

            var inventoryFullChanged = State.InventoryFull != inventoryFull;
            var limitsExceededChanged = State.LimitsExceeded != limitsExceeded;

            var missingComponentsChanged = State.MissingComponents.LastHash != State.MissingComponents.CurrentHash;
            State.MissingComponents.LastHash = State.MissingComponents.CurrentHash;

            var possibleWeldTargetsChanged = State.PossibleWeldTargets.LastHash != State.PossibleWeldTargets.CurrentHash;
            State.PossibleWeldTargets.LastHash = State.PossibleWeldTargets.CurrentHash;

            var possibleGrindTargetsChanged = State.PossibleGrindTargets.LastHash != State.PossibleGrindTargets.CurrentHash;
            State.PossibleGrindTargets.LastHash = State.PossibleGrindTargets.CurrentHash;

            var possibleFloatingTargetsChanged = State.PossibleFloatingTargets.LastHash != State.PossibleFloatingTargets.CurrentHash;
            State.PossibleFloatingTargets.LastHash = State.PossibleFloatingTargets.CurrentHash;

            if (missingComponentsChanged || possibleWeldTargetsChanged || possibleGrindTargetsChanged || possibleFloatingTargetsChanged || transportChanged) State.HasChanged();

            if (MyAPIGateway.Session.IsServer)
            {
                if (State.IsTransmitNeeded() && MyAPIGateway.Multiplayer.MultiplayerActive && MyAPIGateway.Session.ElapsedPlayTime.Subtract(_UpdateStateTransmitLast).TotalSeconds >= _UpdateStateTransmitInterval)
                {
                    _UpdateStateTransmitLast = MyAPIGateway.Session.ElapsedPlayTime;
                    _UpdateStateTransmitInterval = _RandomDelay.Next(TransmitStateMinIntervalSeconds, TransmitStateMaxIntervalSeconds + 1);
                    NetworkMessagingHandler.MsgBlockStateSend(0, this);
                }
            }

            UpdateCustomInfo(
                missingComponentsChanged ||
                possibleWeldTargetsChanged ||
                possibleGrindTargetsChanged ||
                possibleFloatingTargetsChanged ||
                readyChanged ||
                weldingChanged ||
                needWeldingChanged ||
                grindingChanged ||
                needGrindingChanged ||
                inventoryFullChanged ||
                limitsExceededChanged ||
                transportChanged);
            }
            finally
            {
                MethodProfiler.StopAndLog("ServerTryWeldingGrindingCollecting", profilerTs, () =>
                    string.Format("entityId={0};workMode={1};welding={2};grinding={3};transporting={4};weldTargets={5};grindTargets={6};floatingTargets={7};inventoryFull={8}",
                        _Welder.EntityId, Settings.WorkMode, State.Welding, State.Grinding, State.Transporting,
                        State.PossibleWeldTargets.CurrentCount, State.PossibleGrindTargets.CurrentCount,
                        State.PossibleFloatingTargets.CurrentCount, State.InventoryFull));
            }
        }

        /// <summary>
        /// Builds a per-tick cache of how many OTHER NanobotSystem instances are actively
        /// welding or grinding on each grid. Called once per tick before the weld/grind loops.
        /// </summary>
        private void BuildGridSystemCountCache()
        {
            var profilerTs = MethodProfiler.Start();
            _gridSystemCountCache.Clear();
            try
            {
            lock (Mod.NanobotSystems)
            {
                foreach (var system in Mod.NanobotSystems.Values)
                {
                    if (system == this) continue;

                    long weldGridId = 0;
                    long grindGridId = 0;

                    var weldBlock = system.State.CurrentWeldingBlock;
                    if (weldBlock != null && weldBlock.CubeGrid != null)
                        weldGridId = weldBlock.CubeGrid.EntityId;

                    var grindBlock = system.State.CurrentGrindingBlock;
                    if (grindBlock != null && grindBlock.CubeGrid != null)
                        grindGridId = grindBlock.CubeGrid.EntityId;

                    if (weldGridId != 0)
                    {
                        int existing;
                        if (_gridSystemCountCache.TryGetValue(weldGridId, out existing))
                            _gridSystemCountCache[weldGridId] = existing + 1;
                        else
                            _gridSystemCountCache[weldGridId] = 1;
                    }

                    if (grindGridId != 0 && grindGridId != weldGridId)
                    {
                        int existing;
                        if (_gridSystemCountCache.TryGetValue(grindGridId, out existing))
                            _gridSystemCountCache[grindGridId] = existing + 1;
                        else
                            _gridSystemCountCache[grindGridId] = 1;
                    }
                }
            }
            }
            finally
            {
                var _cacheSize = _gridSystemCountCache.Count;
                MethodProfiler.StopAndLog("BuildGridSystemCountCache", profilerTs, () =>
                    string.Format("entityId={0};cachedGrids={1};totalSystems={2}",
                        _Welder.EntityId, _cacheSize, Mod.NanobotSystems.Count));
            }
        }

        /// <summary>
        /// Returns the cached count of other systems targeting the given grid.
        /// Must call BuildGridSystemCountCache() first in the same tick.
        /// </summary>
        private int GetCachedSystemCountOnGrid(long gridEntityId)
        {
            int count;
            if (_gridSystemCountCache.TryGetValue(gridEntityId, out count))
                return count;
            return 0;
        }
    }
}
