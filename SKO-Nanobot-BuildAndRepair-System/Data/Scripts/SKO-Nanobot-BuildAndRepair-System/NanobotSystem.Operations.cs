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

            // Fast path: block was already off and still off, no transport in progress, nothing to drain.
            // The transition tick (State.Ready -> false) has already run, so the panel is up to date.
            if (!ready && !State.Ready && !State.Transporting && _TransportInventory.CurrentVolume == 0)
            {
                return;
            }

            IMySlimBlock currentWeldingBlock = null;
            IMySlimBlock currentGrindingBlock = null;

            var playTime = MyAPIGateway.Session.ElapsedPlayTime;
            var isFullInventoryAndPicking = State.InventoryFull && State.CurrentTransportIsPick;

            if (ready)
            {
                // BUG-014: On first ready tick (or after re-enable), trigger an immediate
                // scan WITH sources so the BaR doesn't operate with empty source/push lists.
                // Skip operations this tick — they'll start once the scan completes.
                if (!_InitialScanCompleted)
                {
                    _LastSourceUpdate = -Mod.Settings.SourcesUpdateInterval;
                    _LastTargetsUpdate = TimeSpan.Zero;
                    UpdateSourcesAndTargetsTimer();
                }
                else
                {
                    ServerTryPushInventory();

                    // BUG-015: Proactively detect full welder inventory after push attempt.
                    // If welder is full and we couldn't push, mark inventory full early
                    // so grinding/collecting are blocked before wasting a cycle.
                    if (!State.InventoryFull && !CreativeModeActive)
                    {
                        var welderInventory = _Welder.GetInventory(0);
                        if (welderInventory != null && (float)welderInventory.CurrentVolume >= (float)welderInventory.MaxVolume)
                        {
                            State.InventoryFull = true;
                        }
                    }

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

                    if (transporting && State.CurrentTransportIsPick)
                    {
                        if (State.CurrentTransportIsCollecting) needcollecting = true;
                        else needgrinding = true;
                    }

                    if ((Settings.Flags & SyncBlockSettings.Settings.ComponentCollectIfIdle) == 0 && !transporting)
                        ServerTryCollectingFloatingTargets(out collecting, out needcollecting, out transporting);

                    if (!transporting)
                    {
                        State.MissingComponents.Clear();
                        State.LimitsExceeded = false;

                        if (!Mod.Settings.DisableLimitSystemsPerTargetGrid)
                            Mod.BuildGridSystemCountCache();

                        switch (Settings.WorkMode)
                        {
                            case WorkModes.WeldBeforeGrind:
                                ServerTryWelding(out welding, out needwelding, out transporting, out currentWeldingBlock);
                                if (!needwelding || (((Settings.Flags & SyncBlockSettings.Settings.ScriptControlled) != 0) && Settings.CurrentPickedGrindingBlock != null))
                                {
                                    ServerTryGrinding(out grinding, out needgrinding, out transporting, out currentGrindingBlock);
                                }
                                break;

                            case WorkModes.GrindBeforeWeld:
                                ServerTryGrinding(out grinding, out needgrinding, out transporting, out currentGrindingBlock);
                                if (!needgrinding || (((Settings.Flags & SyncBlockSettings.Settings.ScriptControlled) != 0) && Settings.CurrentPickedWeldingBlock != null))
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
                    State.CurrentTransportIsCollecting = false;
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
                    _LastTargetsUpdate = TimeSpan.Zero;
                    UpdateSourcesAndTargetsTimer(); //Scan immediately once for new targets
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
            var needCollectingChanged = State.NeedCollecting != needcollecting;
            State.Ready = ready;
            State.Welding = welding;
            State.NeedWelding = needwelding;
            State.CurrentWeldingBlock = currentWeldingBlock;

            State.Grinding = grinding;
            State.NeedGrinding = needgrinding;
            State.NeedCollecting = needcollecting;
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
                needCollectingChanged ||
                inventoryFullChanged ||
                limitsExceededChanged ||
                transportChanged);
            }
            finally
            {
                MethodProfiler.StopAndLog("ServerTryWeldingGrindingCollecting", profilerTs, () =>
                    string.Format("entityId={0};workMode={1};welding={2};grinding={3};needCollecting={4};transporting={5};transportIsCollecting={6};weldTargets={7};grindTargets={8};floatingTargets={9};inventoryFull={10};scanReady={11};pushFull={12}",
                        _Welder.EntityId, Settings.WorkMode, State.Welding, State.Grinding,
                        State.NeedCollecting, State.Transporting, State.CurrentTransportIsCollecting,
                        State.PossibleWeldTargets.CurrentCount, State.PossibleGrindTargets.CurrentCount,
                        State.PossibleFloatingTargets.CurrentCount, State.InventoryFull,
                        _InitialScanCompleted, _PushTargetsFull));
            }
        }

        /// <summary>
        /// Returns the cached count of OTHER systems targeting the given grid.
        /// Reads from the centralized Mod.GridSystemCountCache and subtracts this BaR's own
        /// contribution to match the old per-BaR "skip self" behavior.
        /// </summary>
        private int GetCachedSystemCountOnGrid(long gridEntityId)
        {
            int count;
            if (!Mod.GridSystemCountCache.TryGetValue(gridEntityId, out count))
                return 0;

            // Subtract this system's contribution (mirrors the old "if (system == this) continue" logic).
            long myWeldGridId = 0;
            long myGrindGridId = 0;
            var myWeldBlock = State.CurrentWeldingBlock;
            if (myWeldBlock != null && myWeldBlock.CubeGrid != null)
                myWeldGridId = myWeldBlock.CubeGrid.EntityId;
            var myGrindBlock = State.CurrentGrindingBlock;
            if (myGrindBlock != null && myGrindBlock.CubeGrid != null)
                myGrindGridId = myGrindBlock.CubeGrid.EntityId;

            if (myWeldGridId == gridEntityId)
                count--;
            if (myGrindGridId == gridEntityId && myGrindGridId != myWeldGridId)
                count--;

            return count;
        }
    }
}
